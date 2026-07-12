namespace Atlas2
{
    using GameHelper;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using GameHelper.RemoteObjects.UiElement;
    using GameHelper.Utils;
    using GameOffsets.Natives;
    using GameOffsets.Objects.UiElement;
    using ImGuiNET;
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using System.Runtime.InteropServices;
    using System.Text;

    public sealed partial class Atlas2
    {
        private const uint IsVisibleMask = 0x800;
        private const int RitualModsChildOffset = 0x3B8;
        private const int TextElementTextOffset = 0x4C0;
        private const int PanelLineModeOffset = 0x637;
        private const int PanelLineIdOffset = 0x63C;
        private const int PanelPendingVecOffset = 0x648;
        private const int PanelCommittedVecOffset = 0x660;
        private const int PanelCandTableBeginOffset = 0x590;
        private const int PanelCandTableEndOffset = 0x598;
        private const int CandTableEntryStride = 0x44;
        private const int CandTableMaxCandidates = 5;
        private string RitualRollLogPathname => Path.Join(DllDirectory, "config", "ritual_roll_log.jsonl");
        private static IntPtr Handle;
        private static int handlePid;
        private static void EnsureProcessHandle()
        {
            int pid = (int)Core.Process.Pid;
            if (Handle != IntPtr.Zero && handlePid == pid)
                return;
            if (Handle != IntPtr.Zero)
                CloseHandle(Handle);
            Handle = ProcessMemoryUtilities.Managed.NativeWrapper.OpenProcess(
                ProcessMemoryUtilities.Native.ProcessAccessFlags.Read, pid);
            handlePid = pid;
        }

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);
        private string L(string key, string fallback) => PluginText.T(key, fallback);
        private GameHelper.Localization.PluginLocalization Loc => PluginText;

        private static string ReadWideString(nint address, int stringLength)
        {
            if (address == IntPtr.Zero || stringLength <= 0)
                return string.Empty;
            var bytes = new byte[stringLength * 2];
            ProcessMemoryUtilities.Managed.NativeWrapper.ReadProcessMemoryArray(Handle, address, bytes);
            return Encoding.Unicode.GetString(bytes).Split('\0')[0];
        }

        public static T Read<T>(IntPtr address) where T : unmanaged
        {
            if (address == IntPtr.Zero)
                return default;

            EnsureProcessHandle();
            T result = default;
            ProcessMemoryUtilities.Managed.NativeWrapper.ReadProcessMemory(Handle, address, ref result);

            return result;
        }

        private static bool TryVectorCount<T>(in StdVector vector, out int count)
            where T : unmanaged
        {
            count = 0;
            if (vector.First == IntPtr.Zero || vector.Last == IntPtr.Zero)
                return false;

            long bytes = vector.Last.ToInt64() - vector.First.ToInt64();
            if (bytes <= 0)
                return false;

            int stride = Marshal.SizeOf<T>();
            if (stride <= 0 || (bytes % stride) != 0)
                return false;

            long c = bytes / stride;
            if (c <= 0 || c > 10000)
                return false;

            count = (int)c;

            return true;
        }

        private static T ReadVectorAt<T>(in StdVector vector, int index)
            where T : unmanaged
        {
            int stride = Marshal.SizeOf<T>();
            var addr = IntPtr.Add(vector.First, index * stride);

            return Read<T>(addr);
        }

        // MSVC std::wstring (SSO): length @ +0x10, capacity @ +0x18; chars inline @ +0x00 while
        // capacity < 8, otherwise +0x00 is the heap buffer pointer. Same layout the game uses for
        // UI label text (see docs/uitree-guide.md).
        private static string ReadGameWString(IntPtr address)
        {
            if (address == IntPtr.Zero)
                return string.Empty;

            long len = Read<long>(IntPtr.Add(address, 0x10));
            long cap = Read<long>(IntPtr.Add(address, 0x18));
            if (len <= 0 || len > 2048 || cap < len)
                return string.Empty;

            var src = cap >= 8 ? Read<IntPtr>(address) : address;
            return src == IntPtr.Zero ? string.Empty : ReadWideString(src, (int)len);
        }

        // Session-dedup of ritual snapshots already written (signature → skip re-append).
        private readonly HashSet<string> ritualLogSeen = new();
        private bool ritualLogHeaderDone;

        // grid -> predicted first Rite mod for the current line's next candidates (see BuildRitualPredictions).
        private static readonly Dictionary<StdTuple2D<int>, string> EmptyRitualPredictions = new();
        private Dictionary<StdTuple2D<int>, string> ritualPredictions = EmptyRitualPredictions;
        // Node under the cursor (accessible/completed only) — the hypothetical START for the
        // pre-click ritual chain while no node is committed yet. One-frame lag by design:
        // predictions build before the node pass hit-tests the cursor.
        private StdTuple2D<int>? ritualHoverGrid;

        // Reads the committed line grids (panel+0x660) as (x,y) int pairs.
        private static List<StdTuple2D<int>> ReadGridVector(IntPtr vecAddr)
        {
            var result = new List<StdTuple2D<int>>();
            var vec = Read<StdVector>(vecAddr);
            if (vec.First == IntPtr.Zero || vec.Last == IntPtr.Zero)
                return result;
            long bytes = vec.Last.ToInt64() - vec.First.ToInt64();
            if (bytes <= 0 || bytes % 8 != 0 || bytes > 8 * 64)
                return result;
            int n = (int)(bytes / 8);
            for (int i = 0; i < n; i++)
                result.Add(Read<StdTuple2D<int>>(IntPtr.Add(vec.First, i * 8)));
            return result;
        }

        // Read the panel's precomputed next-candidate table (panel+0x590) into a map
        // node(x,y) -> its raw candidate list (up to 5, (0,0) sentinels dropped). The table is what
        // AtlasPanel_ritualLineNextCandidates looks up; the roll's candIdx is a node's rank among the
        // frontier's candidates. We read the whole span in one cross-process read and parse locally.
        // Cache: the neighbour table is static per atlas instance, so re-read only when its backing
        // vector changes (atlas reload). Keyed by the (begin, end) pointer pair — begin alone can
        // collide when the allocator reuses the base address for a different atlas's table.
        private static IntPtr candTableCacheBegin = IntPtr.Zero;
        private static IntPtr candTableCacheEnd = IntPtr.Zero;
        // Span already re-read once because a frontier lookup missed (see below) — a legit
        // dead-end frontier must not force a full re-read every frame.
        private static IntPtr candTableHealedBegin = IntPtr.Zero;
        private static IntPtr candTableHealedEnd = IntPtr.Zero;
        private static Dictionary<StdTuple2D<int>, List<StdTuple2D<int>>> candTableCache;

        private static Dictionary<StdTuple2D<int>, List<StdTuple2D<int>>> ReadCandidateTable(
            IntPtr panel, StdTuple2D<int>? frontier = null)
        {
            var map = new Dictionary<StdTuple2D<int>, List<StdTuple2D<int>>>();
            if (panel == IntPtr.Zero)
                return map;
            var begin = Read<IntPtr>(IntPtr.Add(panel, PanelCandTableBeginOffset));
            var end = Read<IntPtr>(IntPtr.Add(panel, PanelCandTableEndOffset));
            if (begin == IntPtr.Zero || end == IntPtr.Zero)
                return map;
            if (begin == candTableCacheBegin && end == candTableCacheEnd && candTableCache != null)
            {
                // Self-heal a stale cache: the line's frontier node always belongs to its own
                // atlas's table, so a miss means the cache was filled from another atlas instance
                // (pointer reuse) or from a not-yet-populated load frame. Drop it and re-read —
                // at most once per table span.
                bool healedThisSpan = begin == candTableHealedBegin && end == candTableHealedEnd;
                if (frontier == null || healedThisSpan || candTableCache.ContainsKey(frontier.Value))
                    return candTableCache;
                candTableCache = null;
                candTableHealedBegin = begin;
                candTableHealedEnd = end;
            }
            long bytes = end.ToInt64() - begin.ToInt64();
            // The live table is ~4k nodes; allow up to 64k entries. Must be a whole number of entries.
            if (bytes <= 0 || bytes % CandTableEntryStride != 0 || bytes > CandTableEntryStride * 65536L)
                return map;
            int n = (int)(bytes / CandTableEntryStride);

            EnsureProcessHandle();
            byte[] buf = new byte[bytes];
            if (!ProcessMemoryUtilities.Managed.NativeWrapper.ReadProcessMemoryArray(Handle, begin, buf))
                return map;

            bool anyCands = false;
            for (int e = 0; e < n; e++)
            {
                int o = e * CandTableEntryStride;
                int nx = BitConverter.ToInt32(buf, o + 0);
                int ny = BitConverter.ToInt32(buf, o + 4);
                var cands = new List<StdTuple2D<int>>(CandTableMaxCandidates);
                for (int c = 0; c < CandTableMaxCandidates; c++)
                {
                    int co = o + 8 + c * 12;      // ints [2..], 3 per candidate (x,y,extra)
                    int cx = BitConverter.ToInt32(buf, co + 0);
                    int cy = BitConverter.ToInt32(buf, co + 4);
                    if (cx == 0 && cy == 0)
                        continue;                 // empty slot sentinel
                    cands.Add(new StdTuple2D<int> { X = cx, Y = cy });
                }
                if (cands.Count > 0)
                    anyCands = true;
                map[new StdTuple2D<int> { X = nx, Y = ny }] = cands;
            }
            // On an atlas-load frame the vector can exist while its entries are still zero-filled —
            // every slot parses as the (0,0) sentinel. Never cache that: the begin/end pointers
            // won't change afterwards, so the garbage would stick until a GH restart.
            if (anyCands)
            {
                candTableCacheBegin = begin;
                candTableCacheEnd = end;
                candTableCache = map;
            }
            return map;
        }

        // ── Ritual Rite-mod PREDICTION (reversed client-side roll). See obsidian poe2/Ritual.md ──
        // The game rolls each line node's Rite mods CLIENT-SIDE and deterministically. We reproduce
        // the roll so a candidate's mods can be shown BEFORE it is committed. Validated exact for the
        // first mod (single-mod nodes 6/6).

        // TinyMT32 exactly as the game uses it (mat1/mat2/tmat below). init_by_array over 4 u32 seed
        // words + an 8-step jump; then a tempered draw. Bit-exact vs TinyMT_seedAndJump (14156b620),
        // next32 (1404e16d0) and randBelow (1404e17a0). NOTE the state transition is the binary's form
        // (x>>1 / s3<<1), which differs from reference TinyMT (x<<1 / y>>1).
        private static class TinyMt32
        {
            private const uint MAT1 = 0x8f7011eeu, MAT2 = 0xfc78ff1fu, TMAT = 0x3793fdffu;

            // seed+jump; returns the 4-word state [s0,s1,s2,s3] (the binary's counter is unused here).
            public static uint[] Seed(uint w0, uint w1, uint w2, uint w3)
            {
                uint[] s = { 0x40336052u, 0xCFA3723Cu, 0x3CAC5F71u, 0x3793FDFFu }; // post-pre-step consts
                uint[] w = { w0, w1, w2, w3 };
                int r = 1;
                for (int i = 0; i < 4; i++)              // absorb 4 words (ini_func1)
                {
                    int a = (r + 1) & 3, b = r & 3, c = (r + 3) & 3;
                    uint x = s[a] ^ s[c] ^ s[b];
                    uint h = ((x >> 27) ^ x) * 0x19660Du;
                    s[a] += h;
                    uint h2 = h + w[i] + (uint)r;
                    s[(r + 2) & 3] += h2;
                    s[b] = h2;
                    r = a;
                }
                for (int k = 0; k < 3; k++)              // 3 mix rounds (ini_func1, no input)
                {
                    int a = (r + 1) & 3, b = r & 3, c = (r + 3) & 3;
                    uint x = s[a] ^ s[c] ^ s[b];
                    uint h = ((x >> 27) ^ x) * 0x19660Du;
                    uint h2 = h + (uint)r;
                    s[a] += h;
                    s[(r + 2) & 3] += h2;
                    s[b] = h2;
                    r = a;
                }
                for (int k = 0; k < 4; k++)              // 4 finalization blocks (ini_func2)
                {
                    int a = (r + 1) & 3, b = r & 3, c = (r + 3) & 3;
                    uint x = s[c] + s[a] + s[b];
                    x = ((x >> 27) ^ x) * 0x5D588B65u;
                    uint y = x - (uint)r;
                    s[a] ^= x;
                    s[(r + 2) & 3] ^= y;
                    s[b] = y;
                    r = a;
                }
                for (int k = 0; k < 8; k++) NextState(s); // jump
                return s;
            }

            private static void NextState(uint[] s)
            {
                uint x = (s[0] & 0x7fffffffu) ^ s[1] ^ s[2];
                uint t = s[3] ^ (s[3] << 1);
                x = (x >> 1) ^ x ^ t;
                uint mag = (x & 1) != 0 ? 0xffffffffu : 0u;
                uint oldS1 = s[1], oldS2 = s[2];
                s[0] = oldS1;
                s[1] = (mag & MAT1) ^ oldS2;
                s[2] = (mag & MAT2) ^ (x << 10) ^ t;
                s[3] = x;
            }

            // one tempered 32-bit output; advances the state (== next32 inner body).
            public static uint Draw(uint[] s)
            {
                uint oldS1 = s[1], oldS2 = s[2];
                uint x = (s[0] & 0x7fffffffu) ^ s[1] ^ s[2];
                uint t = s[3] ^ (s[3] << 1);
                x = (x >> 1) ^ x ^ t;
                uint mag = (x & 1) != 0 ? 0xffffffffu : 0u;
                uint newS2 = (mag & MAT2) ^ (x << 10) ^ t;
                s[0] = oldS1;
                s[1] = (mag & MAT1) ^ oldS2;
                s[2] = newS2;
                s[3] = x;
                uint v = (newS2 >> 8) + oldS1;
                uint magt = (v & 1) != 0 ? 0xffffffffu : 0u;
                return (magt & TMAT) ^ v ^ x;
            }

            // unbiased r in [0,n) with the binary's rejection (bits=32, mask=0xffffffff).
            public static uint RandBelow(uint[] s, uint n)
            {
                if (n <= 1) return 0;
                const uint M = 0xffffffffu;
                while (true)
                {
                    uint r = Draw(s);
                    if (M / n <= r / n && M % n != n - 1) continue;
                    return r % n;
                }
            }
        }

        private sealed class RitualRow
        {
            public int Row { get; set; }
            public int W { get; set; }       // weighting
            public int Cond { get; set; }    // ConditionStat FK (0 = none); binary id = Cond-1
            public int Stat { get; set; }    // granted Stat1 FK — 2nd-pick dup exclusion (0 = none)
            public string Text { get; set; }
        }
        private sealed class RitualPoolFile { public List<RitualRow> Rows { get; set; } }
        private static List<RitualRow> ritualPool;

        private void EnsureRitualPool()
        {
            if (ritualPool != null) return;
            try
            {
                var path = Path.Join(DllDirectory, "json", "ritualmods.json");
                ritualPool = File.Exists(path)
                    ? (JsonConvert.DeserializeObject<RitualPoolFile>(File.ReadAllText(path))?.Rows ?? new())
                    : new();
            }
            catch { ritualPool = new(); }
        }

        // Read the panel's active atlas stats (id -> value, value!=0 only). Chain from
        // ritualLineToggleNode: panel+0x320 -> +0x1b0 -> +0x3a20 -> vector [+0x408 begin, +0x410 end],
        // stride 0x28 (10 int32): stat id @ +0x00, value @ +0x08. Gates the reservoir pool and gives
        // the line length (5 + map_ritual_rite_additional_maps, binary id 0x670b).
        private static Dictionary<int, int> ReadRitualStats(IntPtr panel)
        {
            var stats = new Dictionary<int, int>();
            var o1 = Read<IntPtr>(IntPtr.Add(panel, 0x320));
            if (o1 == IntPtr.Zero) return stats;
            var o2 = Read<IntPtr>(IntPtr.Add(o1, 0x1b0));
            if (o2 == IntPtr.Zero) return stats;
            var holder = Read<IntPtr>(IntPtr.Add(o2, 0x3a20));
            if (holder == IntPtr.Zero) return stats;
            var begin = Read<IntPtr>(IntPtr.Add(holder, 0x408));
            var end = Read<IntPtr>(IntPtr.Add(holder, 0x410));
            if (begin == IntPtr.Zero || end == IntPtr.Zero) return stats;
            long bytes = end.ToInt64() - begin.ToInt64();
            if (bytes <= 0 || bytes % 0x28 != 0 || bytes > 0x28 * 8192L) return stats;
            int n = (int)(bytes / 0x28);
            EnsureProcessHandle();
            byte[] buf = new byte[bytes];
            if (!ProcessMemoryUtilities.Managed.NativeWrapper.ReadProcessMemoryArray(Handle, begin, buf))
                return stats;
            for (int e = 0; e < n; e++)
            {
                int id = BitConverter.ToInt32(buf, e * 0x28 + 0);
                int val = BitConverter.ToInt32(buf, e * 0x28 + 8);
                if (val != 0) stats[id] = val;
            }
            return stats;
        }

        // Whether a line node ALSO gets a second Rite mod: rand(100) < chance stat 0x670C
        // (map_ritual_rite_additional_modifier_chance_%), on a separate deterministic stream
        // seeded [lineId, committedCount, candIdx, salt] — the salt appears ONLY in this coin
        // flip, never in the mod-pick seed.
        private const int StatSecondModChance = 0x670c;
        private const uint SecondModCoinSalt = 0x91DA3AD9;
        private const string TwoModFilterOption = "[2 mods]";  // pseudo-entry in the reward dropdown

        private static bool PredictSecondModFlip(uint lineId, uint committedCount, uint candIdx, int chance)
        {
            if (chance <= 0)
                return false;
            if (chance >= 100)
                return true;
            var s = TinyMt32.Seed(lineId, committedCount, candIdx, SecondModCoinSalt);
            return TinyMt32.RandBelow(s, 100) < (uint)chance;
        }

        // One reservoir pass (seed modCount = 0 first mod / 1 second). The 2nd pass SKIPS —
        // no weight added, no draw — every row whose granted Stat the 1st pick already granted
        // (binary dup check FUN_14064cdc0 on the out-vector; currency trios share one stat so a
        // currency 1st mod blocks its whole trio). Validated 6/6 on logged two-mod nodes.
        private static RitualRow PredictModPass(uint lineId, uint committedCount, uint candIdx,
            uint modCount, List<RitualRow> pool, int grantedStat)
        {
            var s = TinyMt32.Seed(lineId, committedCount, candIdx, modCount);
            long total = 0; RitualRow sel = null;
            foreach (var row in pool)
            {
                if (grantedStat != 0 && row.Stat == grantedStat)
                    continue;
                total += row.W;
                if (TinyMt32.RandBelow(s, (uint)total) < (uint)row.W)
                    sel = row;
            }
            return sel;
        }

        // Game's AtlasPanel_ritualLineReachCheck (140b775f0), ported: a node may join the line
        // only if FROM it the line can still be extended to its full length through eligible
        // nodes (not committed / not already on the path / not blocked). The game refuses the
        // click otherwise — so dead-end branches must never be offered or rolled. `need` =
        // picks still required AFTER taking the node; first success short-circuits.
        private static bool RitualCanComplete(
            Dictionary<StdTuple2D<int>, List<StdTuple2D<int>>> candTable,
            HashSet<StdTuple2D<int>> blocked,
            StdTuple2D<int> node,
            HashSet<StdTuple2D<int>> visited,
            int need)
        {
            if (need <= 0)
                return true;
            if (!candTable.TryGetValue(node, out var raw))
                return false;
            foreach (var c in raw)
            {
                if (blocked.Contains(c) || visited.Contains(c))
                    continue;
                visited.Add(c);
                bool ok = RitualCanComplete(candTable, blocked, c, visited, need - 1);
                visited.Remove(c);
                if (ok)
                    return true;
            }

            return false;
        }

        // Both Rite mods for a candidate: first pick, then the deterministic coin flip, then the
        // second pick with the stat-dup exclusion. Second is null on single-mod nodes.
        private static (string First, string Second) PredictMods(uint lineId, uint committedCount,
            uint candIdx, List<RitualRow> pool, int secondChance)
        {
            var first = PredictModPass(lineId, committedCount, candIdx, 0, pool, 0);
            if (first == null)
                return (null, null);
            if (!PredictSecondModFlip(lineId, committedCount, candIdx, secondChance))
                return (first.Text, null);
            var second = PredictModPass(lineId, committedCount, candIdx, 1, pool, first.Stat);
            return (first.Text, second?.Text);
        }

        // ── "Select N maps" pick counter ─────────────────────────────────────────────────
        // While drawing the ritual line the game shows a header with how many maps can still
        // be picked (the first pick — the start node — consumes one). Authoritative live value,
        // so it overrides the computed 5 + additional-maps stat when readable.
        // GameUi → [22] → [2] → [0], leaf fp 0x502EE1, wstring at +0x4C0 (found via UiExplorer).
        private static readonly int[] RitualPickCounterPath = { 22, 2, 0 };
        private const uint RitualPickCounterFp = 0x502EE1;

        // Reads the counter as the first integer in the label text (locale-independent).
        // False when the element is absent/hidden/moved (index path or fp drifted) or the
        // number is implausible — callers fall back to the stat-derived line length.
        private static bool TryReadRitualPickCounter(out int remaining)
        {
            remaining = 0;
            var addr = Core.States.InGameStateObject.GameUi.Address;
            if (addr == IntPtr.Zero)
                return false;
            foreach (var idx in RitualPickCounterPath)
            {
                addr = Read<UiElement>(addr).GetChildAddress(idx);
                if (addr == IntPtr.Zero)
                    return false;
            }

            var leaf = Read<UiElement>(addr);
            if ((leaf.Flags & ~IsVisibleMask) != (RitualPickCounterFp & ~IsVisibleMask)
                || (leaf.Flags & IsVisibleMask) == 0)
                return false;

            var text = ReadGameWString(IntPtr.Add(addr, TextElementTextOffset));
            if (string.IsNullOrEmpty(text))
                return false;
            int n = 0;
            bool seen = false;
            foreach (var ch in text)
            {
                if (ch >= '0' && ch <= '9') { n = (n * 10) + (ch - '0'); seen = true; }
                else if (seen) break;
            }

            if (!seen || n <= 0 || n > 30)
                return false;
            remaining = n;
            return true;
        }

        // Line-length atlas stat (binary id = tsv id - 1). map_ritual_rite_additional_maps.
        private const int StatAdditionalMaps = 0x670b;
        private const int RitualBaseLineLength = 5;   // AtlasPanel_ritualLineToggleNode: stat + 5
        private const int RitualMaxLookaheadDepth = 16;
        private const int RitualMaxPredictNodes = 4000;

        // Cache: predictions only change when the line state (id + committed set) changes.
        private string ritualPredSig;
        private Dictionary<StdTuple2D<int>, string> ritualPredCache = EmptyRitualPredictions;

        // Predict the Rite mods for EVERY node the ritual line can still reach from its current
        // frontier, up to the line's max length.
        // Each node's mod is rolled for the path that reaches it (committedCount = base + depth;
        // candIdx = its rank among the frontier's candidates minus the committed path). Returns
        // grid -> predicted first-mod text. Cached per line-state.
        private Dictionary<StdTuple2D<int>, string> BuildRitualPredictions(IntPtr panel)
        {
            if (panel == IntPtr.Zero) return EmptyRitualPredictions;
            EnsureRitualPool();
            if (ritualPool == null || ritualPool.Count == 0) return EmptyRitualPredictions;

            // Hover preview ONLY, and only BEFORE the first node is picked: once the line has a
            // start (committed, or clicked-but-unconfirmed pending), the planner window owns the
            // route display and the always-on green chain would just be noise on the atlas.
            var committed = ReadGridVector(IntPtr.Add(panel, PanelCommittedVecOffset));
            int committedReal = committed.Count;   // before a hypothetical start is inserted
            if (committed.Count > 0)
                return EmptyRitualPredictions;
            // Pre-click chain. The first click adds no randomness: lineId and the candidate
            // table exist before the line starts, and the start node itself is never rolled
            // (ritualLineToggleNode's empty-committed branch just adds it to pending). So the
            // whole chain from a hypothetical (hovered) start is already determined. Only while
            // the game is actually in ritual line mode.
            if (Read<byte>(IntPtr.Add(panel, PanelLineModeOffset)) == 0)
                return EmptyRitualPredictions;
            if (ReadGridVector(IntPtr.Add(panel, PanelPendingVecOffset)).Count > 0)
                return EmptyRitualPredictions;
            if (this.ritualHoverGrid is { } start)
                committed.Add(start);
            else
                return EmptyRitualPredictions;

            uint lineId = Read<uint>(IntPtr.Add(panel, PanelLineIdOffset));

            // Signature — reuse the cached chain unless the line changed.
            var sb = new StringBuilder();
            sb.Append(lineId);
            foreach (var g in committed) sb.Append(';').Append(g.X).Append(',').Append(g.Y);
            var sig = sb.ToString();
            if (sig == ritualPredSig && ritualPredCache != null)
                return ritualPredCache;

            var result = new Dictionary<StdTuple2D<int>, string>();
            var candTable = ReadCandidateTable(panel, committed[committed.Count - 1]);
            var stats = ReadRitualStats(panel);

            var pool = new List<RitualRow>(ritualPool.Count);
            foreach (var row in ritualPool)
            {
                if (row.W <= 0) continue;
                if (row.Cond == 0 || stats.ContainsKey(row.Cond) || stats.ContainsKey(row.Cond - 1))
                    pool.Add(row);
            }

            int addlMaps = stats.TryGetValue(StatAdditionalMaps, out var am) ? am
                         : stats.TryGetValue(StatAdditionalMaps + 1, out var am2) ? am2 : 0;
            int lineLen = RitualBaseLineLength + Math.Max(0, addlMaps);
            int secondChance = stats.TryGetValue(StatSecondModChance, out var sc) ? sc
                             : stats.TryGetValue(StatSecondModChance + 1, out var sc2) ? sc2 : 0;
            // The in-game "Select N maps" header is the authoritative remaining-picks count
            // (assumed to decrement as nodes commit — the roll log records it for verification);
            // when readable it overrides the stat-derived length.
            if (TryReadRitualPickCounter(out var picksLeft))
                lineLen = committedReal + picksLeft;
            int maxDepth = Math.Min(RitualMaxLookaheadDepth, Math.Max(0, lineLen - committed.Count));

            // Nodes the line can never be drawn onto — the game's own reach-check rule:
            // completed (widget state ∉ {0,1}) or special-category map (RitualSpecial: the
            // dat-row field the game tests; uniques/towers/hideouts/citadels/bosses). The
            // maps.json tags stay as a fallback for a cache built before the toggle came on.
            // Blocked nodes KEEP their slot in the candidate rank space — the validated candIdx
            // model ranks the raw table minus committed only — but they get no predicted label
            // and the chain is not extended through them.
            var blocked = new HashSet<StdTuple2D<int>>();
            foreach (var nd in nodeCache)
            {
                if (nd.State == AtlasNodeState.CompletedBase
                    || nd.RitualSpecial
                    || string.Equals(nd.Type, "unique", StringComparison.OrdinalIgnoreCase)
                    || nd.Tags.Contains("tower", StringComparer.OrdinalIgnoreCase)
                    || nd.Tags.Contains("hideout", StringComparer.OrdinalIgnoreCase))
                    blocked.Add(nd.GridPosition);
            }

            if (pool.Count > 0 && maxDepth > 0)
            {
                var frontier = committed[committed.Count - 1];
                var visited = new HashSet<StdTuple2D<int>>(committed);   // never revisit committed
                int budget = RitualMaxPredictNodes;
                // BFS by depth so each node is reached via its shallowest (most direct) path, giving
                // the committedCount/candIdx of that path. Recomputed live as the line is drawn, so the
                // chosen path stays exact; branches are a guide.
                var queue = new Queue<(StdTuple2D<int> node, HashSet<StdTuple2D<int>> cset, int depth)>();
                queue.Enqueue((frontier, new HashSet<StdTuple2D<int>>(committed), 0));
                while (queue.Count > 0 && budget > 0)
                {
                    var (node, cset, depth) = queue.Dequeue();
                    if (depth >= maxDepth) continue;
                    if (!candTable.TryGetValue(node, out var raw) || raw.Count == 0) continue;
                    var cands = raw.Where(c => !cset.Contains(c))
                                   .OrderBy(c => c.X).ThenBy(c => c.Y).ToList();
                    uint cc = (uint)cset.Count;   // = committed.Count + depth
                    for (int i = 0; i < cands.Count; i++)
                    {
                        if (budget <= 0) break;
                        var cand = cands[i];
                        if (visited.Contains(cand)) continue;   // reached already via a shallower path
                        if (blocked.Contains(cand)) { visited.Add(cand); continue; }  // holds rank i, can't join
                        // Game reach check (ritualLineReachCheck 140b775f0): a node is clickable
                        // only if the line can still be COMPLETED through it — a dead-end branch
                        // is refused by the game and must not be labeled (its roll never happens).
                        var reachSet = new HashSet<StdTuple2D<int>>(cset) { cand };
                        if (!RitualCanComplete(candTable, blocked, cand, reachSet, maxDepth - depth - 1))
                            continue;
                        visited.Add(cand);
                        var (first, second) = PredictMods(lineId, cc, (uint)i, pool, secondChance);
                        if (!string.IsNullOrEmpty(first))
                        {
                            result[cand] = second == null ? first : first + "\n" + second;
                            budget--;
                        }
                        var childSet = new HashSet<StdTuple2D<int>>(cset) { cand };
                        queue.Enqueue((cand, childSet, depth + 1));
                    }
                }
            }

            ritualPredSig = sig;
            ritualPredCache = result;
            return result;
        }

        // ── "Head of the King Rewards" planner (ritual line mode, page mode 6) ────────────────────────
        // Enumerates every chain the ritual line can take from EVERY eligible start node at once
        // (or only from the committed frontier while a line is being drawn), with each node's
        // predicted first Rite mod (exact, same roll as BuildRitualPredictions but per-path).
        // Shown as a window with a persisted multi-select reward filter (a chain matches when ANY
        // selected reward is in it); a ticked chain draws a ray from the player to its start plus
        // the highlighted route with reward labels — that's how you find WHERE the rewards you
        // filtered for are. See obsidian poe2/Ritual.md.
        private sealed class PlannerChain
        {
            public string Key;                    // stable id: root-onward grids joined
            public List<StdTuple2D<int>> Nodes;   // root (start/frontier) + picked nodes
            public List<string> ShortMods;        // 1st mod per picked node (aligned with Nodes[i+1])
            public List<string> ShortMods2;       // 2nd mod per picked node (null = single-mod)
            public string PathLine;               // "Bastille  >  Headland  >  …"
            public string ModsLine;               // "+25% Tribute   -   Exalted Orbs x2 + Omen: … "
            public int Weight;                    // sum of user reward weights over the chain's mods
        }

        private readonly List<PlannerChain> plannerChains = new();
        private string plannerSig;
        private int plannerStartCount;                 // eligible start nodes in the last rebuild
        private bool plannerLineActive;                // committed non-empty: root = the line's frontier
        private readonly Dictionary<string, int> plannerSelected = new();  // chain key -> palette slot
        private int plannerEnumerated;                 // paths found (incl. beyond the caps)
        private bool plannerCapped;
        private static List<string> plannerRewardOptions;  // distinct ShortModLabel values of the pool
        // Reward-weight edits bump the version; the planner re-weighs + re-sorts its cached
        // chains when the versions diverge (so edits apply live without a full re-enumeration).
        private int plannerWeightsVersion;
        private int plannerChainsWeightsVersion = -1;

        private static readonly Vector4[] PlannerPalette =
        {
            new(1f, 0.85f, 0.2f, 1f),   // yellow
            new(1f, 0.5f, 0.15f, 1f),   // orange
            new(1f, 0.3f, 0.3f, 1f),    // red
            new(0.3f, 0.85f, 1f, 1f),   // cyan
            new(0.45f, 1f, 0.45f, 1f),  // green
            new(0.9f, 0.45f, 1f, 1f),   // violet
        };
        private const int PlannerMaxPaths = 8192;  // global enumeration cap, fair-shared across starts
        private const int PlannerMaxRows = 200;    // rows drawn per frame (matches beyond it still counted)

        // Compact reward label for chain rows / route pills ("4 Exalted Orbs" → "Exalted Orbs x4").
        // Pattern-based over the known RitualAtlasLineMods texts; unknown texts pass through.
        private static readonly Dictionary<string, string> shortModCache = new();

        private static string ShortModLabel(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            if (shortModCache.TryGetValue(text, out var cached))
                return cached;

            string r;
            var m = System.Text.RegularExpressions.Regex.Match(text, @"^(\d+) (.+?Orbs?.*)$");
            if (m.Success)
                r = $"{m.Groups[2].Value} x{m.Groups[1].Value}";
            else if (text.StartsWith("Omen of ", StringComparison.OrdinalIgnoreCase))
                r = "Omen: " + text["Omen of ".Length..];
            else if (text.StartsWith("Contains a very rare Unique", StringComparison.OrdinalIgnoreCase))
                r = "Very Rare Unique";
            else if (text.StartsWith("Contains ", StringComparison.OrdinalIgnoreCase))
                r = text["Contains ".Length..];
            else if (text.Contains("additional pack", StringComparison.OrdinalIgnoreCase))
                r = "+Monster Packs";
            else if (text.Contains("no Cost the first time", StringComparison.OrdinalIgnoreCase))
                r = "+Free Reroll";
            else if (text.Contains("additional Favour reroll", StringComparison.OrdinalIgnoreCase))
                r = "+1 Reroll";
            else if (text.Contains("reduced Tribute", StringComparison.OrdinalIgnoreCase))
                r = "-Reroll Cost";
            else if (text.Contains("increased Tribute", StringComparison.OrdinalIgnoreCase))
                r = "+25% Tribute";
            else if (text.Contains("increased number of Favours", StringComparison.OrdinalIgnoreCase))
                r = "+Favours";
            else
                r = text;
            shortModCache[text] = r;
            return r;
        }

        private string GridDisplayName(StdTuple2D<int> g)
        {
            foreach (var nd in nodeCache)
                if (nd.GridPosition.Equals(g))
                    return nd.Drawable ? nd.MapName : $"({g.X},{g.Y})";
            return $"({g.X},{g.Y})";
        }

        // Every distinct reward the pool can roll, as the short labels the rows display —
        // the option list for the filter dropdown. Built once (the json pool is static).
        private void EnsureRewardOptions()
        {
            if (plannerRewardOptions != null)
                return;
            EnsureRitualPool();
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in ritualPool)
                if (row.W > 0 && !string.IsNullOrEmpty(row.Text))
                    set.Add(ShortModLabel(row.Text));
            plannerRewardOptions = set.ToList();
            // Pseudo-entry: match chains containing a two-mod node (both mods are predicted).
            plannerRewardOptions.Insert(0, TwoModFilterOption);
        }

        // Settings-window table of per-reward weights (shown while the planner toggle is on).
        // The planner sorts its route list by the summed weight of each chain's mods, highest
        // first, so weighted rewards float the best routes to the top. 0 (the default) keeps a
        // reward neutral; negatives push routes down. Stored sparsely (only nonzero).
        private void DrawRewardWeightsTable()
        {
            EnsureRewardOptions();
            ImGui.Indent();
            ImGui.TextUnformatted(this.L("atlas.ritual_weights", "Reward weights"));
            ImGuiHelper.ToolTip(this.L("atlas.ritual_weights_hint",
                "Planner routes are sorted by the sum of these weights over the route's predicted " +
                "rewards, highest first. 0 = neutral; negative pushes a route down the list."));
            if (ImGui.BeginChild("##ritualWeights", new Vector2(0, 240), ImGuiChildFlags.Borders))
            {
                if (ImGui.BeginTable("##ritualWeightsTable", 2,
                    ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
                {
                    ImGui.TableSetupColumn(this.L("atlas.weights_reward_col", "Reward"), ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn(this.L("atlas.weights_weight_col", "Weight"), ImGuiTableColumnFlags.WidthFixed, 220f);
                    ImGui.TableHeadersRow();
                    foreach (var opt in plannerRewardOptions)
                    {
                        if (opt == TwoModFilterOption)
                            continue;   // filter pseudo-entry, not a rollable reward
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted(opt);
                        ImGui.TableNextColumn();
                        int w = Settings.RitualRewardWeights.TryGetValue(opt, out var cur) ? cur : 0;
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.InputInt($"##rw_{opt}", ref w))
                        {
                            if (w == 0)
                                Settings.RitualRewardWeights.Remove(opt);
                            else
                                Settings.RitualRewardWeights[opt] = w;
                            plannerWeightsVersion++;
                        }
                    }

                    ImGui.EndTable();
                }
            }

            ImGui.EndChild();
            ImGui.Unindent();
        }

        // Chain weight = sum of the user's reward weights over every predicted mod on the chain
        // (both mods of a two-mod node count). Recomputed + re-sorted only when the weights or
        // the chain set change; ordering is weight DESC, then the path text for stability.
        private void SortPlannerChains()
        {
            var weights = Settings.RitualRewardWeights;
            foreach (var c in plannerChains)
            {
                int w = 0;
                for (int k = 0; k < c.ShortMods.Count; k++)
                {
                    if (weights.TryGetValue(c.ShortMods[k], out var w1))
                        w += w1;
                    if (c.ShortMods2[k] != null && weights.TryGetValue(c.ShortMods2[k], out var w2))
                        w += w2;
                }

                c.Weight = w;
            }

            plannerChains.Sort((a, b) => a.Weight != b.Weight
                ? b.Weight.CompareTo(a.Weight)
                : string.Compare(a.PathLine, b.PathLine, StringComparison.OrdinalIgnoreCase));
            plannerChainsWeightsVersion = plannerWeightsVersion;
        }

        // Enumerate all chains from every root. Cached by (lineId, depth, committed, roots);
        // rebuilt when the committed line or the eligible-start set changes.
        private void BuildPlannerChains(IntPtr panel)
        {
            if (panel == IntPtr.Zero)
                return;
            EnsureRitualPool();
            if (ritualPool == null || ritualPool.Count == 0)
                return;

            // Roots: while a line is being drawn its committed frontier is the only root; before
            // the first pick EVERY node the line could start from (accessible, not blocked) is a
            // root, so the window lists the whole atlas worth of options at once — no hover
            // needed, the selected row's ray shows where that start is.
            var committed = ReadGridVector(IntPtr.Add(panel, PanelCommittedVecOffset));
            int committedReal = committed.Count;
            plannerLineActive = committedReal > 0;

            // Ineligible nodes (same game-rule set as BuildRitualPredictions — completed state
            // or special-category dat row): they keep their candIdx rank but can't join the
            // line — nor start it. Also grid → display name.
            var blocked = new HashSet<StdTuple2D<int>>();
            var gridName = new Dictionary<StdTuple2D<int>, string>(nodeCache.Count);
            var roots = new List<StdTuple2D<int>>();
            foreach (var nd in nodeCache)
            {
                gridName[nd.GridPosition] = nd.Drawable ? nd.MapName : "???";
                if (nd.State == AtlasNodeState.CompletedBase
                    || nd.RitualSpecial
                    || string.Equals(nd.Type, "unique", StringComparison.OrdinalIgnoreCase)
                    || nd.Tags.Contains("tower", StringComparer.OrdinalIgnoreCase)
                    || nd.Tags.Contains("hideout", StringComparer.OrdinalIgnoreCase))
                    blocked.Add(nd.GridPosition);
                else if (!plannerLineActive && nd.State == AtlasNodeState.AccessibleNow)
                    roots.Add(nd.GridPosition);
            }

            if (plannerLineActive)
                roots.Add(committed[^1]);
            roots.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
            plannerStartCount = roots.Count;
            if (roots.Count == 0)
            {
                plannerChains.Clear();
                plannerSig = null;
                return;
            }

            int prefixCount = plannerLineActive ? committedReal : 1;
            uint lineId = Read<uint>(IntPtr.Add(panel, PanelLineIdOffset));

            var stats = ReadRitualStats(panel);
            int addl = stats.TryGetValue(StatAdditionalMaps, out var am) ? am
                     : stats.TryGetValue(StatAdditionalMaps + 1, out var am2) ? am2 : 0;
            int lineLen = RitualBaseLineLength + Math.Max(0, addl);
            if (TryReadRitualPickCounter(out var picksLeft))
                lineLen = committedReal + picksLeft;
            int maxDepth = Math.Max(0, lineLen - prefixCount);
            int secondChance = stats.TryGetValue(StatSecondModChance, out var sc) ? sc
                             : stats.TryGetValue(StatSecondModChance + 1, out var sc2) ? sc2 : 0;

            var sigSb = new StringBuilder();
            sigSb.Append(lineId).Append('#').Append(maxDepth);
            foreach (var g in committed)
                sigSb.Append(';').Append(g.X).Append(',').Append(g.Y);
            foreach (var g in roots)
                sigSb.Append('|').Append(g.X).Append(',').Append(g.Y);
            var sig = sigSb.ToString();
            if (sig == plannerSig)
                return;
            plannerSig = sig;
            plannerChains.Clear();
            plannerEnumerated = 0;
            plannerCapped = false;

            var candTable = ReadCandidateTable(panel,
                plannerLineActive ? committed[^1] : (StdTuple2D<int>?)null);

            var pool = new List<RitualRow>(ritualPool.Count);
            foreach (var row in ritualPool)
            {
                if (row.W <= 0) continue;
                if (row.Cond == 0 || stats.ContainsKey(row.Cond) || stats.ContainsKey(row.Cond - 1))
                    pool.Add(row);
            }

            if (pool.Count == 0 || maxDepth <= 0)
            {
                PrunePlannerSelection();
                return;
            }

            // Every start shares the same lineId + pool, and a roll depends only on
            // (committedCount, candIdx) — memoized, the whole enumeration rolls ≤ ~40 times.
            var rollMemo = new Dictionary<(uint cc, uint ci), (string First, string Second)>();
            (string First, string Second) Roll(uint cc, uint ci)
            {
                if (!rollMemo.TryGetValue((cc, ci), out var t))
                    rollMemo[(cc, ci)] = t = PredictMods(lineId, cc, ci, pool, secondChance);
                return t;
            }

            // Fair share of the global cap per start, so a branchy early start can't starve
            // the rest of the atlas out of the list.
            int perStart = Math.Max(32, PlannerMaxPaths / roots.Count);
            int startEmitted = 0;

            var path = new List<StdTuple2D<int>>(prefixCount + maxDepth);
            var mods = new List<(string First, string Second)>();
            var visited = new HashSet<StdTuple2D<int>>();

            void Emit()
            {
                // Game reach check (ritualLineReachCheck 140b775f0): a node is clickable only if
                // the line can still be completed through it — so a branch that dead-ends short
                // of full length can never be walked in-game and must not be listed.
                if (mods.Count < maxDepth)
                    return;
                plannerEnumerated++;
                if (plannerChains.Count >= PlannerMaxPaths || startEmitted >= perStart)
                {
                    plannerCapped = true;
                    return;
                }

                startEmitted++;

                var nodes = path.GetRange(prefixCount - 1, path.Count - prefixCount + 1);
                var keySb = new StringBuilder();
                var nameSb = new StringBuilder();
                foreach (var g in nodes)
                {
                    keySb.Append(g.X).Append(',').Append(g.Y).Append('|');
                    if (nameSb.Length > 0) nameSb.Append("  >  ");
                    nameSb.Append(gridName.TryGetValue(g, out var nm) ? nm : "???");
                }

                var shorts = new List<string>(mods.Count);
                var shorts2 = new List<string>(mods.Count);
                var modSb = new StringBuilder();
                for (int k = 0; k < mods.Count; k++)
                {
                    var s = ShortModLabel(mods[k].First);
                    var s2 = mods[k].Second == null ? null : ShortModLabel(mods[k].Second);
                    shorts.Add(s);
                    shorts2.Add(s2);
                    if (modSb.Length > 0) modSb.Append("   -   ");
                    modSb.Append(s);
                    if (s2 != null) modSb.Append(" + ").Append(s2);
                }

                plannerChains.Add(new PlannerChain
                {
                    Key = keySb.ToString(),
                    Nodes = nodes,
                    ShortMods = shorts,
                    ShortMods2 = shorts2,
                    PathLine = nameSb.ToString(),
                    ModsLine = modSb.ToString(),
                });
            }

            void Dfs(StdTuple2D<int> node, int depth)
            {
                if (depth >= maxDepth || plannerChains.Count >= PlannerMaxPaths
                    || startEmitted >= perStart)
                {
                    Emit();
                    return;
                }

                if (!candTable.TryGetValue(node, out var raw) || raw.Count == 0)
                {
                    Emit();
                    return;
                }

                var cands = raw.Where(c => !visited.Contains(c))
                               .OrderBy(c => c.X).ThenBy(c => c.Y).ToList();
                uint cc = (uint)(prefixCount + depth);
                bool any = false;
                for (int i = 0; i < cands.Count && plannerChains.Count < PlannerMaxPaths
                    && startEmitted < perStart; i++)
                {
                    var cand = cands[i];
                    if (blocked.Contains(cand))
                        continue;                 // holds rank i, but can't join the line
                    var roll = Roll(cc, (uint)i);
                    if (string.IsNullOrEmpty(roll.First))
                        continue;
                    any = true;
                    path.Add(cand);
                    mods.Add(roll);
                    visited.Add(cand);
                    Dfs(cand, depth + 1);
                    visited.Remove(cand);
                    mods.RemoveAt(mods.Count - 1);
                    path.RemoveAt(path.Count - 1);
                }

                if (!any)
                    Emit();
            }

            foreach (var root in roots)
            {
                path.Clear();
                mods.Clear();
                visited.Clear();
                if (plannerLineActive)
                {
                    path.AddRange(committed);
                    visited.UnionWith(committed);
                }
                else
                {
                    path.Add(root);
                    visited.Add(root);
                }

                startEmitted = 0;
                Dfs(root, 0);
                if (plannerChains.Count >= PlannerMaxPaths)
                    break;
            }

            this.SortPlannerChains();
            PrunePlannerSelection();
        }

        // Re-home selections whose chain key vanished. When the line advances ALONG a selected
        // route, the re-enumeration roots at the new frontier so the same remaining route gets a
        // shorter key (the old key minus its walked prefix) — carry the palette slot onto that
        // suffix chain instead of dropping it, so the highlight survives drawing the line.
        // Selections with no suffix heir (different start / route broken) are dropped.
        private void PrunePlannerSelection()
        {
            if (plannerSelected.Count == 0)
                return;
            var alive = new HashSet<string>(plannerChains.Count);
            foreach (var c in plannerChains)
                alive.Add(c.Key);
            var dead = plannerSelected.Keys.Where(k => !alive.Contains(k)).ToList();
            foreach (var k in dead)
            {
                var slot = plannerSelected[k];
                plannerSelected.Remove(k);

                string heir = null;
                foreach (var c in plannerChains)
                {
                    // Suffix match on whole "x,y|" tokens (guard against "12,3|" vs "2,3|").
                    if (c.Key.Length >= k.Length || !k.EndsWith(c.Key, StringComparison.Ordinal))
                        continue;
                    if (k[k.Length - c.Key.Length - 1] != '|')
                        continue;
                    if (plannerSelected.ContainsKey(c.Key))
                        continue;
                    if (heir == null || c.Key.Length > heir.Length)
                        heir = c.Key;
                }

                if (heir != null)
                    plannerSelected[heir] = slot;
            }
        }

        private int NextPaletteSlot()
        {
            var used = new HashSet<int>(plannerSelected.Values);
            for (int s = 0; ; s++)
                if (!used.Contains(s))
                    return s;
        }

        // Map overlay for the selected chains: a ray from the player marker to the chain's start,
        // the route polyline, and a reward pill at every picked node.
        private void DrawPlannerOverlay(ImDrawListPtr drawList, Vector2 playerLocation, float uiScale)
        {
            if (plannerSelected.Count == 0 || plannerChains.Count == 0)
                return;

            var needed = new HashSet<StdTuple2D<int>>();
            foreach (var c in plannerChains)
                if (plannerSelected.ContainsKey(c.Key))
                    foreach (var g in c.Nodes)
                        needed.Add(g);
            if (needed.Count == 0)
                return;

            var centers = new Dictionary<StdTuple2D<int>, Vector2>(needed.Count);
            foreach (var nd in nodeCache)
            {
                if (!needed.Contains(nd.GridPosition))
                    continue;
                var ui = Core.States.InGameStateObject.GameUi.Atlas[nd.Index];
                if (ui != null)
                    centers[nd.GridPosition] = ui.Position + ui.Size * 0.5f;
            }

            drawList.ChannelsSetCurrent(ChannelLines);
            float th = MathF.Max(2f, 2.5f * uiScale);
            // The ray is a "where to go" pointer for far-off starts; once the start is near
            // (< 70% of the screen away — the route lines themselves are already in view)
            // it is just clutter, so only long rays draw.
            var disp = ImGui.GetIO().DisplaySize;
            float rayMinLen = 0.7f * MathF.Min(disp.X, disp.Y);
            foreach (var c in plannerChains)
            {
                if (!plannerSelected.TryGetValue(c.Key, out var slot))
                    continue;
                var col = ImGuiHelper.Color(PlannerPalette[slot % PlannerPalette.Length]);
                if (!centers.TryGetValue(c.Nodes[0], out var startC))
                    continue;
                if (Vector2.Distance(playerLocation, startC) >= rayMinLen)
                    drawList.AddLine(playerLocation, startC, col, th);   // ray to the chain's start

                // Ring on the start node — otherwise the route polyline has no readable direction.
                drawList.AddCircle(startC, 16f * uiScale, col, 0, th);
                drawList.AddCircle(startC, 20f * uiScale, col, 0, MathF.Max(1f, th * 0.5f));
                var prev = startC;
                for (int i = 1; i < c.Nodes.Count; i++)
                {
                    if (!centers.TryGetValue(c.Nodes[i], out var pc))
                        continue;
                    drawList.AddLine(prev, pc, col, th);
                    prev = pc;
                }
            }

            drawList.ChannelsSetCurrent(ChannelLabels);
            var pillBg = ImGuiHelper.Color(new Vector4(0.05f, 0.05f, 0.05f, 0.92f));
            foreach (var c in plannerChains)
            {
                if (!plannerSelected.TryGetValue(c.Key, out var slot))
                    continue;
                var colV = PlannerPalette[slot % PlannerPalette.Length];
                var col = ImGuiHelper.Color(colV);
                for (int i = 1; i < c.Nodes.Count; i++)
                {
                    if (!centers.TryGetValue(c.Nodes[i], out var pc))
                        continue;
                    var label = c.ShortMods2[i - 1] == null
                        ? c.ShortMods[i - 1]
                        : c.ShortMods[i - 1] + " + " + c.ShortMods2[i - 1];
                    var ts = ImGui.CalcTextSize(label);
                    var pad = new Vector2(4, 2) * uiScale;
                    var pos = new Vector2(pc.X - ts.X * 0.5f, pc.Y - ts.Y - 12f * uiScale);
                    drawList.AddRectFilled(pos - pad, pos + ts + pad, pillBg, 3f * uiScale);
                    drawList.AddRect(pos - pad, pos + ts + pad, col, 3f * uiScale);
                    drawList.AddText(pos, col, label);
                }
            }
        }

        // The planner window itself (normal-sized font — drawn outside the overlay FontScaleScope;
        // its own text scale is the user-set RitualPlannerFontScale).
        private void DrawPlannerWindow()
        {
            using var _ = new FontScaleScope(Math.Clamp(Settings.RitualPlannerFontScale, 0.5f, 3.0f));

            ImGui.SetNextWindowSize(new Vector2(760, 500), ImGuiCond.FirstUseEver);
            bool open = true;
            if (!ImGui.Begin(this.Loc.Title("atlas.planner_title", "Head of the king Rewards", "AtlasRitualPlanner"), ref open))
            {
                ImGui.End();
                return;
            }

            if (!open)
                Settings.ShowRitualPlanner = false;   // X closes until re-enabled in settings

            // Persisted reward filter: a multi-select dropdown over every reward the pool can
            // roll; a chain matches when ANY selected reward is in it. Stored as '|'-joined
            // short labels so it survives restarts.
            EnsureRewardOptions();
            if (plannerChainsWeightsVersion != plannerWeightsVersion)
                this.SortPlannerChains();   // weights edited in settings — re-rank the cached chains
            var selected = new HashSet<string>(
                (Settings.RitualRewardFilter ?? string.Empty)
                    .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);
            string preview = selected.Count == 0
                ? this.L("atlas.planner_filter_hint", "filter by desired rewards (any match shows the path)…")
                : string.Join(", ", plannerRewardOptions.Where(selected.Contains));
            ImGui.SetNextItemWidth(MathF.Max(120f, ImGui.GetContentRegionAvail().X - 70f));
            bool filterChanged = false;
            if (ImGui.BeginCombo("##plannerFilter", preview, ImGuiComboFlags.HeightLargest))
            {
                foreach (var opt in plannerRewardOptions)
                {
                    bool on = selected.Contains(opt);
                    if (ImGui.Checkbox(opt, ref on))
                    {
                        if (on) selected.Add(opt);
                        else selected.Remove(opt);
                        filterChanged = true;
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.SameLine();
            if (ImGui.Button(this.L("atlas.planner_clear", "Clear")) && selected.Count > 0)
            {
                selected.Clear();
                filterChanged = true;
            }

            if (filterChanged)
                Settings.RitualRewardFilter = string.Join("|", plannerRewardOptions.Where(selected.Contains));

            // Root summary: the drawn line's frontier, or how many possible starts are listed.
            if (plannerLineActive && plannerChains.Count > 0)
                ImGui.TextUnformatted($"{this.L("atlas.planner_start", "Start:")} {GridDisplayName(plannerChains[0].Nodes[0])}");
            else
                ImGui.TextDisabled($"{this.L("atlas.planner_starts", "Possible starts:")} {plannerStartCount}");

            // Filter + count, then rows.
            var visible = new List<PlannerChain>(Math.Min(plannerChains.Count, PlannerMaxRows));
            int matchTotal = 0;
            foreach (var c in plannerChains)
            {
                // Ticked chains always stay listed — a route being walked must not drop out when
                // the reward that matched the filter was on an already-committed node.
                bool isSelected = plannerSelected.ContainsKey(c.Key);
                if (!isSelected && selected.Count > 0)
                {
                    bool wantTwo = selected.Contains(TwoModFilterOption);
                    bool ok = false;
                    for (int k = 0; k < c.ShortMods.Count && !ok; k++)
                        ok = selected.Contains(c.ShortMods[k])
                            || (c.ShortMods2[k] != null
                                && (wantTwo || selected.Contains(c.ShortMods2[k])));

                    if (!ok)
                        continue;
                }

                matchTotal++;
                if (visible.Count < PlannerMaxRows)
                    visible.Add(c);
                else if (isSelected)
                    visible.Add(c);   // never row-cap a ticked chain out of sight
            }

            var counts = $"{this.L("atlas.planner_shown", "Shown:")} {visible.Count}"
                + (matchTotal > visible.Count ? $" ({this.L("atlas.planner_of", "of")} {matchTotal})" : string.Empty)
                + $"  |  {this.L("atlas.planner_chains", "chains:")} {plannerChains.Count}"
                + (plannerCapped ? $" ({this.L("atlas.planner_capped", "capped")})" : string.Empty);
            ImGui.TextDisabled(counts);
            ImGui.Separator();

            ImGui.BeginChild("##plannerRows");
            var modColor = new Vector4(0.45f, 0.75f, 1f, 1f);
            for (int i = 0; i < visible.Count; i++)
            {
                var c = visible[i];
                bool sel = plannerSelected.ContainsKey(c.Key);
                if (ImGui.Checkbox($"##plannerSel{i}", ref sel))
                {
                    if (sel)
                        plannerSelected[c.Key] = NextPaletteSlot();
                    else
                        plannerSelected.Remove(c.Key);
                }

                ImGui.SameLine();
                if (plannerSelected.TryGetValue(c.Key, out var slot))
                {
                    ImGui.ColorButton($"##plannerCol{i}", PlannerPalette[slot % PlannerPalette.Length],
                        ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop, new Vector2(14, 14));
                    ImGui.SameLine();
                }

                ImGui.BeginGroup();
                ImGui.TextUnformatted(c.PathLine);
                if (c.Weight != 0)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled($"[{c.Weight:+0;-0}]");
                }

                ImGui.TextColored(modColor, c.ModsLine);
                ImGui.EndGroup();
                ImGui.Separator();
            }

            ImGui.EndChild();
            ImGui.End();
        }

        // RE ground-truth collector: snapshot the ritual atlas line to config/ritual_roll_log.jsonl.
        // One JSON line per distinct (lineId + committed grids + each line-node's rolled mod text)
        // state. Committed nodes carry posIdx = their index; the pending set are the next candidates
        // (posIdx = committed count). Only runs while a line exists, so idle frames cost one read.
        private void LogRitualSnapshot(IntPtr panel)
        {
            if (panel == IntPtr.Zero)
                return;

            var committed = ReadGridVector(IntPtr.Add(panel, PanelCommittedVecOffset));
            var pending = ReadGridVector(IntPtr.Add(panel, PanelPendingVecOffset));
            if (committed.Count == 0 && pending.Count == 0)
                return; // no active line — nothing to log

            uint lineId = Read<uint>(IntPtr.Add(panel, PanelLineIdOffset));

            // Precomputed next-candidate table: node(x,y) -> its raw ≤5 candidates. Lets the offline
            // solver reconstruct the exact candIdx (rank among a frontier's candidates), which the
            // clicked-only pending set can't (an unclicked candidate still shifts every rank).
            var candTable = ReadCandidateTable(panel,
                committed.Count > 0 ? committed[committed.Count - 1] : (StdTuple2D<int>?)null);
            List<int[]> CandsOf(StdTuple2D<int> g) =>
                candTable.TryGetValue(g, out var cs)
                    ? cs.Select(c => new[] { c.X, c.Y }).ToList()
                    : new List<int[]>();

            // grid → node address (from the already-built cache).
            var gridToAddr = new Dictionary<StdTuple2D<int>, IntPtr>(nodeCache.Count);
            foreach (var nd in nodeCache)
                gridToAddr[nd.GridPosition] = nd.Address;

            var entries = new List<object>();
            void Collect(List<StdTuple2D<int>> grids, string vecName, int basePos)
            {
                for (int i = 0; i < grids.Count; i++)
                {
                    var g = grids[i];
                    string text = null;
                    if (gridToAddr.TryGetValue(g, out var addr) && addr != IntPtr.Zero)
                    {
                        var child = Read<IntPtr>(IntPtr.Add(addr, RitualModsChildOffset));
                        if (child != IntPtr.Zero)
                            text = ReadGameWString(IntPtr.Add(child, TextElementTextOffset));
                    }
                    entries.Add(new
                    {
                        vec = vecName,
                        idx = i,
                        posIdx = basePos + i,
                        x = g.X,
                        y = g.Y,
                        text = string.IsNullOrWhiteSpace(text) ? null : text,
                        cands = CandsOf(g),
                    });
                }
            }

            Collect(committed, "committed", 0);
            Collect(pending, "pending", committed.Count);

            // Only snapshots where at least one node has rolled text are useful ground truth.
            if (!entries.Any(e => ((dynamic)e).text != null))
                return;

            // The frontier ritualLineToggleNode enumerates from = the LAST committed node; its raw
            // candidate set is what the next clicked node is ranked within.
            var frontierCands = committed.Count > 0 ? CandsOf(committed[committed.Count - 1])
                                                    : new List<int[]>();

            // "Select N maps" header — logged to verify it really decrements per committed pick
            // (the prediction depth override assumes it does).
            int? pickCounter = TryReadRitualPickCounter(out var pc) ? pc : null;

            var snapshot = new
            {
                lineId,
                committedCount = committed.Count,
                pendingCount = pending.Count,
                pickCounter,
                frontierCands,
                entries,
            };

            // Signature = lineId + every (posIdx,x,y,text): dedup identical states across frames.
            var sig = new StringBuilder();
            sig.Append(lineId).Append('|').Append(committed.Count).Append('|').Append(pickCounter ?? -1);
            foreach (dynamic e in entries)
                sig.Append(';').Append(e.posIdx).Append(',').Append(e.x).Append(',').Append(e.y)
                   .Append('=').Append((string)e.text ?? "");
            if (!ritualLogSeen.Add(sig.ToString()))
                return;

            try
            {
                var dir = Path.GetDirectoryName(RitualRollLogPathname);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                if (!ritualLogHeaderDone && !File.Exists(RitualRollLogPathname))
                    File.AppendAllText(RitualRollLogPathname,
                        "// Ritual Rite-mod roll ground-truth. One JSON snapshot per line state.\n");
                ritualLogHeaderDone = true;
                File.AppendAllText(RitualRollLogPathname,
                    JsonConvert.SerializeObject(snapshot) + "\n");
            }
            catch { /* logging must never break the overlay */ }
        }

        // RITUAL_PORT_INSERT
    }
}
