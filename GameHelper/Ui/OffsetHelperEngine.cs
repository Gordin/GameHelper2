// <copyright file="OffsetHelperEngine.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Ui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using GameHelper.Utils;
    using GameOffsets.Natives;
    using GameOffsets.Objects.Components;
    using GameOffsets.Objects.States;
    using GameOffsets.Objects.States.InGameState;
    using GameOffsets.Objects.UiElement;

    /// <summary>
    ///     The verification engine behind <see cref="OffsetHelper" />. It walks the
    ///     <c>[StructLayout(LayoutKind.Explicit)]</c> offset structs in <c>GameOffsets</c> via
    ///     reflection, reads a live instance ("root") of each out of the game, and runs
    ///     value-based sanity checks on every field so a human can tell — without Ghidra —
    ///     whether a struct's field offsets survived a game patch.
    ///     <para>
    ///     The decisive, game-state-independent checks ("anchors"):
    ///     <list type="bullet">
    ///     <item>a component's <see cref="ComponentHeader.EntityPtr" /> must equal the owning
    ///     entity's address (the same back-pointer <c>ComponentBase.IsParentValid</c> uses);</item>
    ///     <item>a <see cref="StdVector" /> must satisfy <c>First &lt;= Last &lt;= End</c> with all
    ///     three in valid address range;</item>
    ///     <item>entity maps must have a valid MSVC sentinel/root shape, and the local-player
    ///     pointer must resolve to an entity whose path starts with <c>Metadata/</c>;</item>
    ///     <item>a non-null pointer field must be in range and actually readable;</item>
    ///     <item>a <see cref="UiElementBaseOffset.Self" /> pointer must equal its own address.</item>
    ///     </list>
    ///     Primitive fields only get a weak plausibility note (they never fail a struct), and a
    ///     struct with zero anchorable fields is reported Unverifiable rather than Intact.
    ///     Verdicts aggregate across several roots so a single torn read doesn't read as breakage.
    ///     </para>
    /// </summary>
    internal static class OffsetHelperEngine
    {
        /// <summary>Max live instances verified per struct type in a sweep.</summary>
        internal const int MaxRootsPerType = 6;

        /// <summary>Upper bound on awake entities walked while gathering roots.</summary>
        internal const int MaxEntitiesScanned = 256;

        private const int MaxRecursionDepth = 4;
        private const int RecoveryScanRadius = 0x200;
        private const int MaxMapSize = 1_000_000;

        // Main-module address range, resolved lazily once the game is attached (-1 = not yet known).
        // A pointer landing in this range is a real vtable / static, a strong positive signal.
        private static long moduleBase = -1;
        private static long moduleEnd = -1;

        /// <summary>
        ///     Maps a game component name (as read from entity memory, == the managed component
        ///     class name) to the <c>GameOffsets</c> struct that describes its byte layout. Only
        ///     components with a dedicated offset struct can be fully field-verified; component
        ///     names seen on entities but absent here are reported as "unmapped" (header-only).
        /// </summary>
        internal static readonly Dictionary<string, Type> ComponentOffsetTypes = new(StringComparer.Ordinal)
        {
            ["Actor"] = typeof(ActorOffset),
            ["Animated"] = typeof(AnimatedOffsets),
            ["Base"] = typeof(BaseOffsets),
            ["Buffs"] = typeof(BuffsOffsets),
            ["Charges"] = typeof(ChargesOffsets),
            ["Chest"] = typeof(ChestOffsets),
            ["Life"] = typeof(LifeOffset),
            ["Mods"] = typeof(ModsOffsets),
            ["ObjectMagicProperties"] = typeof(ObjectMagicPropertiesOffsets),
            ["Player"] = typeof(PlayerOffsets),
            ["Positioned"] = typeof(PositionedOffsets),
            ["Render"] = typeof(RenderOffsets),
            ["RenderItem"] = typeof(RenderItemOffsets),
            ["Shrine"] = typeof(ShrineOffsets),
            ["Stack"] = typeof(StackOffsets),
            ["Stats"] = typeof(StatsOffsets),
            ["StateMachine"] = typeof(StateMachineComponentOffsets),
            ["Targetable"] = typeof(TargetableOffsets),
            ["Transitionable"] = typeof(TransitionableOffsets),
            ["TriggerableBlockage"] = typeof(TriggerableBlockageOffsets),
            ["WorldItem"] = typeof(WorldItemOffsets),
        };

        private static readonly HashSet<string> StringStructNames = new(StringComparer.Ordinal)
        {
            "StdString", "StdWString",
        };

        /// <summary>Per-field verification status.</summary>
        internal enum FieldStatus
        {
            /// <summary>Field carries no useful signal (padding / string blob).</summary>
            Skip,

            /// <summary>Plausible but low-signal (a primitive / null pointer).</summary>
            Weak,

            /// <summary>A strong anchor check passed.</summary>
            Pass,

            /// <summary>A strong anchor check failed.</summary>
            Fail,
        }

        /// <summary>How a field's raw bytes are interpreted.</summary>
        internal enum FieldKind
        {
            /// <summary>An <see cref="IntPtr" /> field.</summary>
            Pointer,

            /// <summary>An owner/self back-pointer that must equal a known address.</summary>
            OwnerPtr,

            /// <summary>A <see cref="StdVector" /> (First/Last/End).</summary>
            Vector,

            /// <summary>An MSVC <see cref="StdMap" /> header and sentinel/root shape.</summary>
            Map,

            /// <summary>A pointer that must resolve to an entity with a Metadata/ path.</summary>
            EntityPointer,

            /// <summary>A Life-component vital block checked against an optional expected total.</summary>
            Vital,

            /// <summary>An inline std::string / std::wstring blob.</summary>
            String,

            /// <summary>An integer / float / enum.</summary>
            Primitive,
        }

        /// <summary>Overall verdict for a struct type (aggregated over its roots).</summary>
        internal enum ProbeVerdict
        {
            /// <summary>All anchor checks passed on the sampled roots.</summary>
            Intact,

            /// <summary>An anchor check failed structurally (across roots).</summary>
            Degraded,

            /// <summary>No anchorable field, or no layout registered.</summary>
            Unverifiable,

            /// <summary>No live instance existed this run.</summary>
            NoRoot,
        }

        /// <summary>
        ///     Runs a full sweep: gathers live roots and verifies every registered offset struct.
        ///     Must be called on the render thread (it reads game memory synchronously).
        /// </summary>
        /// <returns>the sweep result, never null.</returns>
        internal static SweepResult RunSweep(OffsetRecoveryHints? hints = null)
        {
            var result = new SweepResult { WhenLocal = DateTime.Now };
            var state = Core.States.InGameStateObject;
            var area = state.CurrentAreaInstance;
            if (area == null || area.Address == IntPtr.Zero)
            {
                result.InGame = false;
                return result;
            }

            result.InGame = true;

            var componentRoots = new Dictionary<string, List<Root>>(StringComparer.Ordinal);
            var entityRoots = new List<Root>();

            void AddEntity(Entity? e, string tag)
            {
                if (e == null || !e.IsValid || e.Address == IntPtr.Zero)
                {
                    return;
                }

                var shortName = $"{tag}#{e.Id}";
                if (entityRoots.Count < MaxRootsPerType * 4)
                {
                    entityRoots.Add(new Root(shortName, e.Address, e.Address));
                }

                foreach (var kv in e.GetComponentAddressPairs())
                {
                    if (kv.Value == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!componentRoots.TryGetValue(kv.Key, out var list))
                    {
                        list = new List<Root>();
                        componentRoots[kv.Key] = list;
                    }

                    if (list.Count < MaxRootsPerType * 2)
                    {
                        list.Add(new Root($"{shortName}.{kv.Key}", kv.Value, e.Address));
                    }
                }
            }

            AddEntity(area.Player, "Player");
            AddEntity(state.MouseOverEntity, "MouseOver");
            var scanned = 0;
            foreach (var kv in area.AwakeEntities)
            {
                if (scanned++ >= MaxEntitiesScanned)
                {
                    break;
                }

                AddEntity(kv.Value, kv.Value.EntityType.ToString());
            }

            // Registered component structs (full field verification via the header back-pointer).
            foreach (var (componentName, structType) in ComponentOffsetTypes)
            {
                var roots = componentRoots.TryGetValue(componentName, out var l) ? l : new List<Root>();
                result.Probes.Add(VerifyProbe($"{structType.Name}  ·  {componentName}", structType, roots, "EntityPtr", false, hints));
            }

            // Components present on entities but with no registered layout (markers: DiesAfterTime, NPC, ...).
            foreach (var kv in componentRoots)
            {
                if (!ComponentOffsetTypes.ContainsKey(kv.Key))
                {
                    result.Probes.Add(VerifyProbe($"{kv.Key}  ·  (unmapped)", null, kv.Value, "EntityPtr", true, hints));
                }
            }

            // Non-component root structs reachable straight off RemoteObject addresses.
            result.Probes.Add(VerifyProbe("EntityOffsets", typeof(EntityOffsets), entityRoots, string.Empty, false, hints));
            result.Probes.Add(VerifyProbe("InGameStateOffset", typeof(InGameStateOffset), RootOne(state.Address), string.Empty, false, hints));
            result.Probes.Add(VerifyProbe("AreaInstanceOffsets", typeof(AreaInstanceOffsets), RootOne(area.Address), string.Empty, false, hints));
            result.Probes.Add(VerifyProbe("UiElementBaseOffset", typeof(UiElementBaseOffset), GatherUiRoots(), "Self", false, hints));

            result.Recount();
            return result;
        }

        /// <summary>
        ///     Verifies a single struct instance at <paramref name="addr" /> and returns a flat
        ///     field-by-field breakdown. Public so the per-entity inspector cards can reuse it.
        /// </summary>
        /// <param name="structType">the offset struct to interpret the bytes as.</param>
        /// <param name="label">human label for the root.</param>
        /// <param name="addr">address of the struct instance.</param>
        /// <param name="owner">owner address for the owner/self anchor (or Zero).</param>
        /// <param name="ownerAnchorField">leaf field name that must equal <paramref name="owner" />.</param>
        /// <param name="hints">optional user-supplied values for semantic field validation.</param>
        /// <returns>the per-root result.</returns>
        internal static RootResult VerifyRoot(
            Type structType,
            string label,
            IntPtr addr,
            IntPtr owner,
            string ownerAnchorField,
            OffsetRecoveryHints? hints = null)
        {
            var r = new RootResult { Label = label, Address = addr, Owner = owner, Fields = new List<FieldRow>() };
            if (addr == IntPtr.Zero || !SafeMemoryHandle.IsValidAddress(addr))
            {
                r.ReadOk = false;
                r.Verdict = ProbeVerdict.Degraded;
                r.Note = "address invalid";
                return r;
            }

            var size = SizeOf(structType);
            var buf = Core.Process.Handle.ReadMemoryArray<byte>(addr, size);
            if (buf.Length < size)
            {
                r.ReadOk = false;
                r.Verdict = ProbeVerdict.Degraded;
                r.Note = "struct unreadable";
                return r;
            }

            r.ReadOk = true;
            try
            {
                WalkStruct(structType, buf, 0, owner.ToInt64(), ownerAnchorField, string.Empty, 0, r.Fields, hints);
            }
            catch (Exception ex)
            {
                r.Note = $"walk error: {ex.Message}";
            }

            r.Verdict = VerdictForFields(r.Fields);
            return r;
        }

        private const int UiMaxNodes = 100000;
        private const int UiMaxDepth = 40;
        private const int UiMaxChildrenPerNode = 2000;

        private static readonly HashSet<string> NoExclusions = new();

        /// <summary>
        ///     Walks the whole UiElement tree under the GameUi pointer and records every node's
        ///     child-index path (e.g. "22.0.6"), its address, and its raw per-element visibility
        ///     bit. Used by the panel-finder to diff two snapshots and reveal which element(s)
        ///     toggled when a panel was opened/closed. Runs synchronously on the caller's thread.
        /// </summary>
        /// <returns>the snapshot, keyed by child-index path (root = "").</returns>
        internal static UiCapture CaptureUiTree()
        {
            var cap = new UiCapture { WhenLocal = DateTime.Now };
            var root = Core.States.InGameStateObject.GameUi?.Address ?? IntPtr.Zero;
            if (root != IntPtr.Zero)
            {
                WalkUi(Core.Process.Handle, root, string.Empty, 0, true, cap);
            }

            return cap;
        }

        /// <summary>
        ///     Diffs two UI snapshots by per-element visibility, keyed by child-index path. A path
        ///     is "changed" when its effective visibility differs (treating an absent node as not
        ///     visible, so panels that allocate/free elements are caught alongside plain bit flips).
        ///     The returned rows are the changed nodes plus their ancestors, in pre-order, so they
        ///     render as a tree.
        /// </summary>
        /// <param name="a">baseline snapshot.</param>
        /// <param name="b">current snapshot.</param>
        /// <param name="excluded">paths to ignore entirely (unstable elements filtered during settling).</param>
        /// <returns>the visibility diff.</returns>
        internal static UiDiff DiffVisibility(UiCapture a, UiCapture b, HashSet<string>? excluded = null)
        {
            excluded ??= NoExclusions;
            var diff = new UiDiff();
            var all = new HashSet<string>(a.Nodes.Keys);
            all.UnionWith(b.Nodes.Keys);

            var changed = new HashSet<string>();
            foreach (var p in all)
            {
                if (excluded.Contains(p))
                {
                    continue;
                }

                var visA = a.Nodes.TryGetValue(p, out var va) && va.Vis;
                var visB = b.Nodes.TryGetValue(p, out var vb) && vb.Vis;
                if (visA != visB)
                {
                    changed.Add(p);
                }
            }

            diff.ChangedCount = changed.Count;

            // With effective visibility, opening a panel flips the whole subtree; report only the
            // "origin" — a changed node whose parent did NOT also change. That's the panel root
            // (the element whose own visibility actually toggled), which is what you want to find.
            var origins = new List<string>();
            foreach (var p in changed)
            {
                var parent = ParentPath(p);
                if (parent == null || !changed.Contains(parent))
                {
                    origins.Add(p);
                }
            }

            origins.Sort(ComparePaths);
            diff.OriginCount = origins.Count;
            foreach (var p in origins)
            {
                var ea = a.Nodes.TryGetValue(p, out var va);
                var eb = b.Nodes.TryGetValue(p, out var vb);
                var aStr = !ea ? "absent" : va.Vis ? "visible" : "hidden";
                var bStr = !eb ? "absent" : vb.Vis ? "visible" : "hidden";
                diff.Rows.Add(new UiDiffRow
                {
                    Path = p,
                    Depth = p.Length == 0 ? 0 : p.Split('.').Length,
                    Addr = eb ? vb.Addr : va.Addr,
                    Change = $"{aStr} → {bStr}",
                    IsChanged = true,
                    BecameVisible = eb && vb.Vis,
                });
            }

            return diff;
        }

        private static string? ParentPath(string p)
        {
            if (p.Length == 0)
            {
                return null;
            }

            var i = p.LastIndexOf('.');
            return i < 0 ? string.Empty : p[..i];
        }

        private static void WalkUi(SafeMemoryHandle reader, IntPtr addr, string path, int depth, bool parentVisible, UiCapture cap)
        {
            if (cap.Nodes.Count >= UiMaxNodes || depth > UiMaxDepth)
            {
                return;
            }

            if (!reader.TryReadMemory<UiElementBaseOffset>(addr, out var o))
            {
                return;
            }

            // Reject anything that isn't actually a UiElement (self-pointer must match), except the
            // root which we always trust. Same guard ImportantUiElements uses.
            if (depth > 0 && o.Self != IntPtr.Zero && o.Self != addr)
            {
                return;
            }

            // Effective (on-screen) visibility = own bit AND every ancestor's — the same recursive
            // rule UiElementBase.IsVisible uses. This is what a human perceives as "the panel is
            // shown", so the diff direction matches what you see when you open/close a panel.
            var visible = parentVisible && UiElementBaseFuncs.IsVisibleChecker(o.Flags);
            cap.Nodes[path] = (addr, visible);

            var count = o.ChildrensPtr.TotalElements(IntPtr.Size);
            if (count <= 0 || count > UiMaxChildrenPerNode || o.ChildrensPtr.First == IntPtr.Zero)
            {
                return;
            }

            var children = reader.ReadMemoryArray<IntPtr>(o.ChildrensPtr.First, (int)count);
            for (var i = 0; i < children.Length; i++)
            {
                if (children[i] != IntPtr.Zero)
                {
                    WalkUi(reader, children[i], depth == 0 ? i.ToString() : path + "." + i, depth + 1, visible, cap);
                }
            }
        }

        /// <summary>
        ///     Orders child-index paths in tree pre-order: segment-by-segment numeric compare, with
        ///     a shorter (ancestor) path sorting before its descendants. Root ("") sorts first.
        /// </summary>
        private static int ComparePaths(string a, string b)
        {
            if (a == b)
            {
                return 0;
            }

            var sa = a.Length == 0 ? Array.Empty<string>() : a.Split('.');
            var sb = b.Length == 0 ? Array.Empty<string>() : b.Split('.');
            var n = Math.Min(sa.Length, sb.Length);
            for (var i = 0; i < n; i++)
            {
                var d = int.Parse(sa[i]).CompareTo(int.Parse(sb[i]));
                if (d != 0)
                {
                    return d;
                }
            }

            return sa.Length.CompareTo(sb.Length);
        }

        private static List<Root> RootOne(IntPtr addr)
        {
            return addr == IntPtr.Zero ? new List<Root>() : new List<Root> { new("root", addr, addr) };
        }

        private static List<Root> GatherUiRoots()
        {
            var roots = new List<Root>();
            var ui = Core.States.InGameStateObject.GameUi;
            if (ui == null)
            {
                return roots;
            }

            void Add(string name, IntPtr addr)
            {
                if (addr != IntPtr.Zero && roots.Count < MaxRootsPerType)
                {
                    roots.Add(new Root(name, addr, addr));
                }
            }

            Add("LargeMap", ui.LargeMap.Address);
            Add("MiniMap", ui.MiniMap.Address);
            Add("WorldMapPanel", ui.WorldMapPanel.Address);
            Add("Atlas", ui.Atlas.Address);
            Add("LeftPanel", ui.LeftPanel.Address);
            Add("RightPanel", ui.RightPanel.Address);
            Add("ChatParent", ui.ChatParent.Address);
            return roots;
        }

        private static ProbeResult VerifyProbe(
            string displayName,
            Type? structType,
            List<Root> roots,
            string ownerAnchorField,
            bool unmapped,
            OffsetRecoveryHints? hints)
        {
            var res = new ProbeResult
            {
                Name = displayName,
                StructType = structType,
                Unmapped = unmapped,
                RootCount = roots.Count,
                Roots = new List<RootResult>(),
            };

            if (roots.Count == 0)
            {
                res.Verdict = ProbeVerdict.NoRoot;
                res.Detail = "no live instance this run";
                return res;
            }

            // Unmapped markers have no layout — we can still confirm the component header
            // back-points to the owner, which proves the component pointer itself is real.
            var probeType = structType ?? typeof(ComponentHeader);
            var take = Math.Min(roots.Count, MaxRootsPerType);
            for (var i = 0; i < take; i++)
            {
                res.Roots.Add(VerifyRoot(probeType, roots[i].Label, roots[i].Address, roots[i].Owner, ownerAnchorField, hints));
            }

            res.SampleLabel = roots[0].Label;
            var readable = res.Roots.Where(x => x.ReadOk).ToList();

            if (unmapped)
            {
                res.Verdict = ProbeVerdict.Unverifiable;
                var headerOk = readable.Count(x => x.Fields.Any(f => f.Kind == FieldKind.OwnerPtr && f.Status == FieldStatus.Pass));
                res.Detail = $"no layout registered; header owner-match {headerOk}/{readable.Count}";
                return res;
            }

            if (readable.Count == 0)
            {
                res.Verdict = ProbeVerdict.Degraded;
                res.Detail = "all roots unreadable";
                return res;
            }

            if (!readable.Any(x => x.Fields.Any(f => IsAnchor(f))))
            {
                res.Verdict = ProbeVerdict.Unverifiable;
                res.Detail = "no anchorable fields";
                return res;
            }

            // Aggregate anchor failures across roots so one torn read isn't called breakage.
            var failSummaries = new List<string>();
            var anchorNames = readable
                .SelectMany(x => x.Fields.Where(IsAnchor).Select(f => (f.Name, f.Offset)))
                .Distinct()
                .ToList();

            foreach (var (name, offset) in anchorNames)
            {
                var present = 0;
                var fails = 0;
                string reason = string.Empty;
                foreach (var root in readable)
                {
                    var f = root.Fields.FirstOrDefault(x => x.Name == name && x.Offset == offset);
                    if (f == null)
                    {
                        continue;
                    }

                    present++;
                    if (f.Status == FieldStatus.Fail)
                    {
                        fails++;
                        reason = f.Reason;
                    }
                }

                if (present == 0)
                {
                    continue;
                }

                var threshold = Math.Max(present == 1 ? 1 : 2, (int)Math.Ceiling(present / 2.0));
                if (fails >= threshold)
                {
                    var suffix = present == 1 ? " (1 root — verify)" : string.Empty;
                    failSummaries.Add($"{name} [{reason}] {fails}/{present}{suffix}");
                }
            }

            if (failSummaries.Count > 0)
            {
                res.Verdict = ProbeVerdict.Degraded;
                res.Detail = string.Join("; ", failSummaries);
                res.RecoveryAttempted = true;
                res.Recoveries.AddRange(FindOffsetRecoveries(probeType, roots, ownerAnchorField, hints));
            }
            else
            {
                res.Verdict = ProbeVerdict.Intact;
                res.Detail = $"{readable.Count} root(s) verified";
            }

            return res;
        }

        /// <summary>
        ///     For each top-level field whose decisive anchors failed, slides that field as one
        ///     block through a bounded, pointer-aligned neighborhood. A candidate is reported only
        ///     when the shifted field validates on a majority of the sampled roots. Keeping nested
        ///     fields together is important: game patches commonly insert bytes between logical
        ///     blocks while leaving each block's internal layout unchanged.
        /// </summary>
        private static List<OffsetRecoverySuggestion> FindOffsetRecoveries(
            Type structType,
            List<Root> roots,
            string ownerAnchorField,
            OffsetRecoveryHints? hints)
        {
            var suggestions = new List<OffsetRecoverySuggestion>();
            var sampledRoots = roots.Take(MaxRootsPerType).ToList();
            foreach (var (field, configuredOffset) in LayoutFields(structType))
            {
                if (field.Name.StartsWith("PAD", StringComparison.OrdinalIgnoreCase) ||
                    field.Name.StartsWith("Pad", StringComparison.Ordinal))
                {
                    continue;
                }

                var fieldSize = SizeOf(field.FieldType);
                var scanStart = Math.Max(0, configuredOffset - RecoveryScanRadius);
                var scanEnd = configuredOffset + fieldSize + RecoveryScanRadius;
                var windows = new List<(Root Root, byte[] Bytes)>();
                foreach (var root in sampledRoots)
                {
                    var bytes = Core.Process.Handle.ReadMemoryArray<byte>(root.Address + scanStart, scanEnd - scanStart);
                    if (bytes.Length == scanEnd - scanStart)
                    {
                        windows.Add((root, bytes));
                    }
                }

                if (windows.Count == 0)
                {
                    continue;
                }

                var baseline = EvaluateShiftedField(field, configuredOffset, scanStart, windows, ownerAnchorField, hints);
                if (baseline.PresentRoots == 0 || baseline.FailedRoots < MajorityThreshold(baseline.PresentRoots))
                {
                    continue;
                }

                var candidates = new List<(int Offset, RecoveryEvaluation Evaluation)>();
                for (var delta = -RecoveryScanRadius; delta <= RecoveryScanRadius; delta += IntPtr.Size)
                {
                    if (delta == 0)
                    {
                        continue;
                    }

                    var candidateOffset = configuredOffset + delta;
                    if (candidateOffset < scanStart || candidateOffset + fieldSize > scanEnd)
                    {
                        continue;
                    }

                    var evaluation = EvaluateShiftedField(field, candidateOffset, scanStart, windows, ownerAnchorField, hints);
                    if (evaluation.PresentRoots == 0 ||
                        evaluation.VerifiedRoots < MajorityThreshold(evaluation.PresentRoots) ||
                        evaluation.FailedRoots >= MajorityThreshold(evaluation.PresentRoots) ||
                        evaluation.EvidencePasses == 0)
                    {
                        continue;
                    }

                    candidates.Add((candidateOffset, evaluation));
                }

                var ranked = candidates
                    .OrderByDescending(x => x.Evaluation.VerifiedRoots)
                    .ThenByDescending(x => x.Evaluation.EvidencePasses)
                    .ThenBy(x => Math.Abs(x.Offset - configuredOffset))
                    .ToList();
                if (ranked.Count == 0)
                {
                    continue;
                }

                var best = ranked[0];
                var tied = ranked.Skip(1)
                    .Where(x => x.Evaluation.VerifiedRoots == best.Evaluation.VerifiedRoots &&
                                x.Evaluation.EvidencePasses == best.Evaluation.EvidencePasses)
                    .Take(3)
                    .Select(x => x.Offset)
                    .ToList();
                suggestions.Add(new OffsetRecoverySuggestion
                {
                    FieldName = field.Name,
                    ConfiguredOffset = configuredOffset,
                    CandidateOffset = best.Offset,
                    VerifiedRoots = best.Evaluation.VerifiedRoots,
                    RootCount = best.Evaluation.PresentRoots,
                    EvidencePasses = best.Evaluation.EvidencePasses,
                    AlternativeOffsets = tied,
                });
            }

            ApplyShiftConsensus(structType, sampledRoots, ownerAnchorField, suggestions, hints);
            return suggestions;
        }

        /// <summary>
        ///     Uses agreement between independently strong fields to resolve weak fields. This is
        ///     the common patch-day case where one member was inserted into a native class and all
        ///     later blocks moved by the same amount. An empty vector is not unique enough to find
        ///     by itself, but it is valid corroboration at a shift already established by two or
        ///     more semantic fields.
        /// </summary>
        private static void ApplyShiftConsensus(
            Type structType,
            List<Root> roots,
            string ownerAnchorField,
            List<OffsetRecoverySuggestion> suggestions,
            OffsetRecoveryHints? hints)
        {
            var dominant = suggestions
                .GroupBy(x => x.Shift)
                .Select(x => (Shift: x.Key, Support: x.Count()))
                .OrderByDescending(x => x.Support)
                .ThenBy(x => Math.Abs(x.Shift))
                .FirstOrDefault();
            if (dominant.Support < 2)
            {
                return;
            }

            foreach (var (field, configuredOffset) in LayoutFields(structType))
            {
                var fieldSize = SizeOf(field.FieldType);
                var candidateOffset = configuredOffset + dominant.Shift;
                if (candidateOffset < 0)
                {
                    continue;
                }

                var scanStart = Math.Max(0, configuredOffset - RecoveryScanRadius);
                var scanEnd = configuredOffset + fieldSize + RecoveryScanRadius;
                if (candidateOffset < scanStart || candidateOffset + fieldSize > scanEnd)
                {
                    continue;
                }

                var windows = new List<(Root Root, byte[] Bytes)>();
                foreach (var root in roots)
                {
                    var bytes = Core.Process.Handle.ReadMemoryArray<byte>(root.Address + scanStart, scanEnd - scanStart);
                    if (bytes.Length == scanEnd - scanStart)
                    {
                        windows.Add((root, bytes));
                    }
                }

                if (windows.Count == 0)
                {
                    continue;
                }

                var baseline = EvaluateShiftedField(field, configuredOffset, scanStart, windows, ownerAnchorField, hints);
                if (baseline.PresentRoots == 0 || baseline.FailedRoots < MajorityThreshold(baseline.PresentRoots))
                {
                    continue;
                }

                var consensus = EvaluateShiftedField(field, candidateOffset, scanStart, windows, ownerAnchorField, hints);
                if (consensus.PresentRoots == 0 ||
                    consensus.StructurallyValidRoots < MajorityThreshold(consensus.PresentRoots) ||
                    consensus.FailedRoots >= MajorityThreshold(consensus.PresentRoots))
                {
                    continue;
                }

                var suggestion = suggestions.FirstOrDefault(x => x.FieldName == field.Name);
                if (suggestion == null)
                {
                    suggestion = new OffsetRecoverySuggestion
                    {
                        FieldName = field.Name,
                        ConfiguredOffset = configuredOffset,
                    };
                    suggestions.Add(suggestion);
                }
                else if (suggestion.CandidateOffset != candidateOffset &&
                         !suggestion.AlternativeOffsets.Contains(suggestion.CandidateOffset))
                {
                    suggestion.AlternativeOffsets.Add(suggestion.CandidateOffset);
                }

                suggestion.CandidateOffset = candidateOffset;
                suggestion.AlternativeOffsets.Remove(candidateOffset);
                suggestion.VerifiedRoots = consensus.StructurallyValidRoots;
                suggestion.RootCount = consensus.PresentRoots;
                suggestion.EvidencePasses = consensus.EvidencePasses;
                suggestion.ConsensusSupport = dominant.Support;
            }
        }

        private static RecoveryEvaluation EvaluateShiftedField(
            FieldInfo field,
            int candidateOffset,
            int scanStart,
            List<(Root Root, byte[] Bytes)> windows,
            string ownerAnchorField,
            OffsetRecoveryHints? hints)
        {
            var result = new RecoveryEvaluation();
            foreach (var (root, bytes) in windows)
            {
                var rows = new List<FieldRow>();
                EvaluateFieldAt(field, bytes, candidateOffset - scanStart, root.Owner.ToInt64(),
                    ownerAnchorField, rows, hints);
                var anchors = rows.Where(IsRecoveryAnchor).ToList();
                if (anchors.Count == 0)
                {
                    continue;
                }

                result.PresentRoots++;
                if (anchors.Any(x => x.Status == FieldStatus.Fail))
                {
                    result.FailedRoots++;
                    continue;
                }

                result.StructurallyValidRoots++;
                var evidence = anchors.Count(IsRecoveryEvidence);
                if (evidence > 0)
                {
                    result.VerifiedRoots++;
                    result.EvidencePasses += evidence;
                }
            }

            return result;
        }

        /// <summary>
        ///     Evaluates a top-level field at a trial offset. Calling <see cref="WalkStruct" />
        ///     directly is insufficient for native containers because it would expand a
        ///     <see cref="StdVector" /> into three ordinary pointers and lose the vector-shape
        ///     invariant that made the field recoverable in the first place.
        /// </summary>
        private static void EvaluateFieldAt(
            FieldInfo field,
            byte[] bytes,
            int offset,
            long owner,
            string ownerAnchorField,
            List<FieldRow> rows,
            OffsetRecoveryHints? hints)
        {
            var fieldType = field.FieldType;
            var fieldSize = SizeOf(fieldType);
            var isPad = field.Name.StartsWith("PAD", StringComparison.OrdinalIgnoreCase) ||
                        field.Name.StartsWith("Pad", StringComparison.Ordinal);
            if (offset < 0 || offset + fieldSize > bytes.Length)
            {
                rows.Add(new FieldRow(offset, field.Name, FieldKind.Primitive, "<beyond read>", FieldStatus.Skip, "out of buffer"));
                return;
            }

            if (fieldType == typeof(VitalStruct))
            {
                rows.Add(VitalRow(field.Name, bytes, offset, hints));
            }
            else if (fieldType == typeof(StdVector))
            {
                rows.Add(VectorRow(field.Name, bytes, offset));
            }
            else if (fieldType == typeof(StdMap))
            {
                rows.Add(MapRow(field.Name, bytes, offset));
            }
            else if (StringStructNames.Contains(fieldType.Name))
            {
                rows.Add(new FieldRow(offset, field.Name, FieldKind.String, "std::string blob",
                    isPad ? FieldStatus.Skip : FieldStatus.Weak, "not an anchor"));
            }
            else if (fieldType == typeof(IntPtr) || fieldType == typeof(UIntPtr))
            {
                var value = BitConverter.ToInt64(bytes, offset);
                var isOwner = !string.IsNullOrEmpty(ownerAnchorField) && field.Name == ownerAnchorField;
                rows.Add(field.Name == "LocalPlayerPtr"
                    ? EntityPointerRow(field.Name, value, isPad, offset)
                    : PointerRow(field.Name, value, isOwner, owner, isPad, bytes, offset));
            }
            else if (fieldType.IsValueType && !fieldType.IsPrimitive && !fieldType.IsEnum)
            {
                WalkStruct(fieldType, bytes, offset, owner, ownerAnchorField, field.Name, 0, rows, hints);
            }
            else
            {
                rows.Add(PrimitiveRow(offset, field.Name, fieldType, bytes, offset, isPad));
            }
        }

        private static bool IsRecoveryAnchor(FieldRow field)
        {
            return field.Kind is FieldKind.Vector or FieldKind.Map or FieldKind.OwnerPtr or FieldKind.EntityPointer ||
                   (field.Kind == FieldKind.Vital && field.Status is FieldStatus.Pass or FieldStatus.Fail);
        }

        private static bool IsRecoveryEvidence(FieldRow field)
        {
            return field.Status == FieldStatus.Pass &&
                   !((field.Kind is FieldKind.Vector or FieldKind.Map) && field.Reason.StartsWith("empty", StringComparison.Ordinal));
        }

        private static int MajorityThreshold(int count)
        {
            return Math.Max(count == 1 ? 1 : 2, (int)Math.Ceiling(count / 2.0));
        }

        private static IEnumerable<(FieldInfo Field, int Offset)> LayoutFields(Type type)
        {
            var explicitLayout = (type.StructLayoutAttribute?.Value ?? LayoutKind.Sequential) == LayoutKind.Explicit;
            var sequentialOffset = 0;
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (field.IsStatic)
                {
                    continue;
                }

                var offset = explicitLayout
                    ? field.GetCustomAttribute<FieldOffsetAttribute>()?.Value ?? sequentialOffset
                    : sequentialOffset;
                yield return (field, offset);
                if (!explicitLayout)
                {
                    sequentialOffset += SizeOf(field.FieldType);
                }
            }
        }

        private sealed class RecoveryEvaluation
        {
            public int PresentRoots { get; set; }

            public int VerifiedRoots { get; set; }

            public int StructurallyValidRoots { get; set; }

            public int FailedRoots { get; set; }

            public int EvidencePasses { get; set; }
        }

        private static bool IsAnchor(FieldRow f)
        {
            // Vectors and the owner/self back-pointer always carry a hard invariant. A generic
            // pointer only counts once it has positively resolved (readable / in-module); an
            // ambiguous "weak" pointer is not treated as breakage.
            return f.Kind is FieldKind.Vector or FieldKind.Map or FieldKind.OwnerPtr or FieldKind.EntityPointer ||
                   (f.Kind == FieldKind.Vital && f.Status is FieldStatus.Pass or FieldStatus.Fail) ||
                   (f.Kind == FieldKind.Pointer && f.Status == FieldStatus.Pass);
        }

        private static ProbeVerdict VerdictForFields(List<FieldRow> fields)
        {
            var anchors = fields.Where(IsAnchor).ToList();
            if (anchors.Count == 0)
            {
                return ProbeVerdict.Unverifiable;
            }

            return anchors.Any(f => f.Status == FieldStatus.Fail) ? ProbeVerdict.Degraded : ProbeVerdict.Intact;
        }

        private static void WalkStruct(
            Type t,
            byte[] buf,
            int baseOff,
            long owner,
            string ownerAnchorField,
            string prefix,
            int depth,
            List<FieldRow> rows,
            OffsetRecoveryHints? hints)
        {
            if (depth > MaxRecursionDepth)
            {
                return;
            }

            var explicitLayout = (t.StructLayoutAttribute?.Value ?? LayoutKind.Sequential) == LayoutKind.Explicit;
            var seq = 0;
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (f.IsStatic)
                {
                    continue;
                }

                var size = SizeOf(f.FieldType);
                var off = explicitLayout ? (f.GetCustomAttribute<FieldOffsetAttribute>()?.Value ?? seq) : seq;
                if (!explicitLayout)
                {
                    seq += size;
                }

                var abs = baseOff + off;
                var ft = f.FieldType;
                var name = string.IsNullOrEmpty(prefix) ? f.Name : $"{prefix}.{f.Name}";
                var isPad = f.Name.StartsWith("PAD", StringComparison.OrdinalIgnoreCase) ||
                            f.Name.StartsWith("Pad", StringComparison.Ordinal);

                if (abs < 0 || abs + size > buf.Length)
                {
                    rows.Add(new FieldRow(abs, name, FieldKind.Primitive, "<beyond read>", FieldStatus.Skip, "out of buffer"));
                    continue;
                }

                if (ft == typeof(VitalStruct))
                {
                    rows.Add(VitalRow(name, buf, abs, hints));
                }
                else if (ft == typeof(StdVector))
                {
                    rows.Add(VectorRow(name, buf, abs));
                }
                else if (ft == typeof(StdMap))
                {
                    rows.Add(MapRow(name, buf, abs));
                }
                else if (StringStructNames.Contains(ft.Name))
                {
                    rows.Add(new FieldRow(abs, name, FieldKind.String, "std::string blob", isPad ? FieldStatus.Skip : FieldStatus.Weak, "not an anchor"));
                }
                else if (ft == typeof(IntPtr) || ft == typeof(UIntPtr))
                {
                    var v = BitConverter.ToInt64(buf, abs);
                    var isOwner = !string.IsNullOrEmpty(ownerAnchorField) && f.Name == ownerAnchorField;
                    rows.Add(f.Name == "LocalPlayerPtr"
                        ? EntityPointerRow(name, v, isPad, abs)
                        : PointerRow(name, v, isOwner, owner, isPad, buf, abs));
                }
                else if (ft.IsValueType && !ft.IsPrimitive && !ft.IsEnum)
                {
                    WalkStruct(ft, buf, abs, owner, ownerAnchorField, name, depth + 1, rows, hints);
                }
                else
                {
                    rows.Add(PrimitiveRow(abs, name, ft, buf, abs, isPad));
                }
            }
        }

        private static FieldRow VitalRow(string name, byte[] buf, int abs, OffsetRecoveryHints? hints)
        {
            var vtable = BitConverter.ToInt64(buf, abs);
            var lifeComponent = BitConverter.ToInt64(buf, abs + 0x08);
            var reservedFlat = BitConverter.ToInt32(buf, abs + 0x10);
            var reservedPercent = BitConverter.ToInt32(buf, abs + 0x14);
            var regeneration = BitConverter.ToSingle(buf, abs + 0x28);
            var total = BitConverter.ToInt32(buf, abs + 0x2C);
            var current = BitConverter.ToInt32(buf, abs + 0x30);
            var expected = hints?.ExpectedVitalTotal(name) ?? 0;
            var shapeOk = InModule(vtable) && IsValidPtr(lifeComponent) && TestRead(lifeComponent) &&
                          reservedFlat >= 0 && reservedFlat <= 10_000_000 &&
                          reservedPercent is >= 0 and <= 10_000 &&
                          float.IsFinite(regeneration) && Math.Abs(regeneration) < 10_000_000f &&
                          total is >= 0 and <= 10_000_000 && current >= 0 && current <= total;
            var value = $"current={current} total={total} reserved={reservedFlat}+{reservedPercent / 100f:0.##}%";
            if (expected <= 0)
            {
                return new FieldRow(abs, name, FieldKind.Vital, value, FieldStatus.Weak,
                    shapeOk ? "plausible vital; no expected total" : "not checked (no expected total)");
            }

            if (shapeOk && total == expected)
            {
                return new FieldRow(abs, name, FieldKind.Vital, value, FieldStatus.Pass, $"total == supplied {expected}");
            }

            var reason = !shapeOk ? "vital shape violated" : $"total {total} != supplied {expected}";
            return new FieldRow(abs, name, FieldKind.Vital, value, FieldStatus.Fail, reason);
        }

        private static FieldRow VectorRow(string name, byte[] buf, int abs)
        {
            var first = BitConverter.ToInt64(buf, abs);
            var last = BitConverter.ToInt64(buf, abs + 8);
            var end = BitConverter.ToInt64(buf, abs + 16);

            if (first == 0 && last == 0 && end == 0)
            {
                return new FieldRow(abs, name, FieldKind.Vector, "empty", FieldStatus.Pass, "empty");
            }

            var ordered = first <= last && last <= end;
            var rangeOk = IsValidPtr(first) && IsValidPtr(last) && (end == 0 || IsValidPtr(end));
            if (ordered && rangeOk)
            {
                return new FieldRow(abs, name, FieldKind.Vector, $"span=0x{last - first:X} @0x{first:X}", FieldStatus.Pass, "shape ok");
            }

            // A vector's byte offset is a hard invariant, so when it breaks we can point at where
            // a well-formed vector actually sits — the offset probably just shifted this patch.
            return new FieldRow(abs, name, FieldKind.Vector, $"first=0x{first:X} last=0x{last:X} end=0x{end:X}", FieldStatus.Fail, "vector shape violated" + ScanForVector(buf, abs));
        }

        private static FieldRow MapRow(string name, byte[] buf, int abs)
        {
            var head = BitConverter.ToInt64(buf, abs);
            var size = BitConverter.ToInt32(buf, abs + 8);
            if (TryValidateMap(head, size, out var detail))
            {
                return new FieldRow(abs, name, FieldKind.Map, $"size={size} head=0x{head:X}", FieldStatus.Pass, detail);
            }

            return new FieldRow(abs, name, FieldKind.Map, $"size={size} head=0x{head:X}", FieldStatus.Fail,
                detail + ScanForMap(buf, abs));
        }

        private static FieldRow EntityPointerRow(string name, long value, bool isPad, int abs)
        {
            if (isPad)
            {
                return new FieldRow(abs, name, FieldKind.EntityPointer, Hex(value), FieldStatus.Skip, "pad");
            }

            if (LooksLikeEntity(value, out var path))
            {
                return new FieldRow(abs, name, FieldKind.EntityPointer, Hex(value), FieldStatus.Pass, path);
            }

            return new FieldRow(abs, name, FieldKind.EntityPointer, Hex(value), FieldStatus.Fail,
                value == 0 ? "entity ptr null" : "does not resolve to Metadata/ entity");
        }

        private static FieldRow PointerRow(string name, long v, bool isOwner, long owner, bool isPad, byte[] buf, int abs)
        {
            if (isPad)
            {
                return new FieldRow(abs, name, FieldKind.Pointer, Hex(v), FieldStatus.Skip, "pad");
            }

            if (isOwner)
            {
                // The owner/self back-pointer is the one pointer we KNOW must hold a specific value,
                // so a mismatch is a real FAIL — and we scan for where that value actually lives.
                if (v == owner && v != 0)
                {
                    return new FieldRow(abs, name, FieldKind.OwnerPtr, Hex(v), FieldStatus.Pass, "== owner");
                }

                var reason = v == 0 ? "owner ptr null" : (TestRead(v) ? "!= owner" : "!= owner (unreadable)");
                return new FieldRow(abs, name, FieldKind.OwnerPtr, Hex(v), FieldStatus.Fail, reason + ScanForValue(buf, owner));
            }

            if (v == 0)
            {
                return new FieldRow(abs, name, FieldKind.Pointer, "0x0", FieldStatus.Weak, "null");
            }

            if (InModule(v))
            {
                return new FieldRow(abs, name, FieldKind.Pointer, Hex(v), FieldStatus.Pass, "module (vtable/static)");
            }

            if (IsValidPtr(v) && TestRead(v))
            {
                return new FieldRow(abs, name, FieldKind.Pointer, Hex(v), FieldStatus.Pass, "readable");
            }

            // In-range-but-unreadable (or out-of-range) 8 bytes: we can't tell a broken pointer from
            // a field that was never a pointer (e.g. packed data mislabeled "VtablePtr"). Report it as
            // low-signal rather than a hard FAIL so it doesn't drag the whole struct to Degraded — the
            // owner back-pointer and vectors are the checks that actually decide breakage.
            return new FieldRow(abs, name, FieldKind.Pointer, Hex(v), FieldStatus.Weak, "not a live pointer (data or moved?)");
        }

        /// <summary>
        ///     Scans a struct's bytes for 8-aligned offsets whose value equals <paramref name="target" />
        ///     (used to relocate a broken owner/self back-pointer). Returns a " → found at +0xNN" hint.
        /// </summary>
        private static string ScanForValue(byte[] buf, long target)
        {
            if (target == 0)
            {
                return string.Empty;
            }

            var hits = new List<int>();
            for (var o = 0; o + 8 <= buf.Length && hits.Count < 6; o += 8)
            {
                if (BitConverter.ToInt64(buf, o) == target)
                {
                    hits.Add(o);
                }
            }

            return hits.Count > 0 ? "  → value at " + string.Join(", ", hits.Select(h => $"+0x{h:X}")) : "  → value not found in struct";
        }

        /// <summary>
        ///     Scans a struct's bytes for 8-aligned offsets (other than <paramref name="skip" />) that
        ///     hold a well-formed non-empty <see cref="StdVector" />, to suggest where one may have moved.
        /// </summary>
        private static string ScanForVector(byte[] buf, int skip)
        {
            var hits = new List<int>();
            for (var o = 0; o + 24 <= buf.Length && hits.Count < 6; o += 8)
            {
                if (o == skip)
                {
                    continue;
                }

                var first = BitConverter.ToInt64(buf, o);
                var last = BitConverter.ToInt64(buf, o + 8);
                var end = BitConverter.ToInt64(buf, o + 16);
                if (first != 0 && first < last && last <= end && IsValidPtr(first) && IsValidPtr(last) && IsValidPtr(end))
                {
                    hits.Add(o);
                }
            }

            return hits.Count > 0 ? "  → vector-shaped at " + string.Join(", ", hits.Select(h => $"+0x{h:X}")) : string.Empty;
        }

        private static string ScanForMap(byte[] buf, int skip)
        {
            var hits = new List<int>();
            for (var o = 0; o + 16 <= buf.Length && hits.Count < 6; o += IntPtr.Size)
            {
                if (o == skip)
                {
                    continue;
                }

                var head = BitConverter.ToInt64(buf, o);
                var size = BitConverter.ToInt32(buf, o + 8);
                if (TryValidateMap(head, size, out _))
                {
                    hits.Add(o);
                }
            }

            return hits.Count > 0 ? "  → map-shaped at " + string.Join(", ", hits.Select(h => $"+0x{h:X}")) : string.Empty;
        }

        private static bool TryValidateMap(long head, int size, out string detail)
        {
            detail = "map shape violated";
            if (size < 0 || size > MaxMapSize || !IsValidPtr(head) ||
                !Core.Process.Handle.TryReadMemory<MapNodeHeader>(new IntPtr(head), out var sentinel) ||
                sentinel.IsNil == 0 || sentinel.Color > 1)
            {
                return false;
            }

            if (size == 0)
            {
                detail = "empty map";
                return true;
            }

            var root = sentinel.Parent.ToInt64();
            if (!IsValidPtr(root) ||
                !Core.Process.Handle.TryReadMemory<MapNodeHeader>(sentinel.Parent, out var rootNode) ||
                rootNode.IsNil != 0 || rootNode.Color > 1)
            {
                return false;
            }

            detail = "sentinel/root shape ok";
            return true;
        }

        private static bool LooksLikeEntity(long value, out string path)
        {
            path = string.Empty;
            if (!IsValidPtr(value) ||
                !Core.Process.Handle.TryReadMemory<ItemStruct>(new IntPtr(value), out var item) ||
                !InModule(item.VTablePtr.ToInt64()) ||
                !Core.Process.Handle.TryReadMemory<EntityDetails>(item.EntityDetailsPtr, out var details))
            {
                return false;
            }

            path = Core.Process.Handle.ReadStdWString(details.name);
            return path.StartsWith("Metadata/", StringComparison.Ordinal);
        }

        private static bool InModule(long v)
        {
            EnsureModuleRange();
            return moduleBase > 0 && v >= moduleBase && v < moduleEnd;
        }

        private static void EnsureModuleRange()
        {
            if (moduleBase != -1)
            {
                return;
            }

            try
            {
                var b = Core.Process.Address.ToInt64();
                var size = Core.Process.Information?.MainModule?.ModuleMemorySize ?? 0;
                if (b > 0 && size > 0)
                {
                    moduleBase = b;
                    moduleEnd = b + size;
                }
            }
            catch
            {
                // Leave the sentinel so we retry once the process is attached.
            }
        }

        private static FieldRow PrimitiveRow(int off, string name, Type t, byte[] buf, int abs, bool isPad)
        {
            var underlying = t.IsEnum ? Enum.GetUnderlyingType(t) : t;
            string val;
            try
            {
                if (underlying == typeof(byte)) { val = buf[abs].ToString(); }
                else if (underlying == typeof(sbyte)) { val = ((sbyte)buf[abs]).ToString(); }
                else if (underlying == typeof(bool)) { val = (buf[abs] != 0).ToString(); }
                else if (underlying == typeof(short)) { val = BitConverter.ToInt16(buf, abs).ToString(); }
                else if (underlying == typeof(ushort)) { val = BitConverter.ToUInt16(buf, abs).ToString(); }
                else if (underlying == typeof(char)) { val = ((int)BitConverter.ToChar(buf, abs)).ToString(); }
                else if (underlying == typeof(int)) { val = BitConverter.ToInt32(buf, abs).ToString(); }
                else if (underlying == typeof(uint)) { val = "0x" + BitConverter.ToUInt32(buf, abs).ToString("X"); }
                else if (underlying == typeof(float)) { val = BitConverter.ToSingle(buf, abs).ToString("g6"); }
                else if (underlying == typeof(long)) { val = BitConverter.ToInt64(buf, abs).ToString(); }
                else if (underlying == typeof(ulong)) { val = "0x" + BitConverter.ToUInt64(buf, abs).ToString("X"); }
                else if (underlying == typeof(double)) { val = BitConverter.ToDouble(buf, abs).ToString("g6"); }
                else { val = "?"; }
            }
            catch
            {
                val = "?";
            }

            return new FieldRow(off, name, FieldKind.Primitive, val, isPad ? FieldStatus.Skip : FieldStatus.Weak, isPad ? "pad" : "weak");
        }

        private static bool IsValidPtr(long v)
        {
            return SafeMemoryHandle.IsValidAddress(new IntPtr(v));
        }

        private static bool TestRead(long addr)
        {
            return Core.Process.Handle.TryReadMemory<int>(new IntPtr(addr), out _);
        }

        private static string Hex(long v)
        {
            return "0x" + v.ToString("X");
        }

        [StructLayout(LayoutKind.Explicit, Pack = 1, Size = 0x20)]
        private struct MapNodeHeader
        {
            [FieldOffset(0x00)] public IntPtr Left;
            [FieldOffset(0x08)] public IntPtr Parent;
            [FieldOffset(0x10)] public IntPtr Right;
            [FieldOffset(0x18)] public byte Color;
            [FieldOffset(0x19)] public byte IsNil;
        }

        /// <summary>
        ///     Unmanaged byte size of a type (bool == 1, IntPtr == 8, structs by explicit/sequential
        ///     extent), matching how <c>ReadProcessMemory</c> actually lays bytes out — deliberately
        ///     not <see cref="Marshal.SizeOf(Type)" />, which inflates bool to 4 bytes.
        /// </summary>
        /// <param name="t">the type to size.</param>
        /// <returns>the byte size.</returns>
        private static int SizeOf(Type t)
        {
            if (t == typeof(IntPtr) || t == typeof(UIntPtr) || t == typeof(long) || t == typeof(ulong) || t == typeof(double))
            {
                return 8;
            }

            if (t == typeof(int) || t == typeof(uint) || t == typeof(float))
            {
                return 4;
            }

            if (t == typeof(short) || t == typeof(ushort) || t == typeof(char))
            {
                return 2;
            }

            if (t == typeof(byte) || t == typeof(sbyte) || t == typeof(bool))
            {
                return 1;
            }

            if (t.IsEnum)
            {
                return SizeOf(Enum.GetUnderlyingType(t));
            }

            if (t.IsPrimitive)
            {
                return Marshal.SizeOf(t);
            }

            var explicitLayout = (t.StructLayoutAttribute?.Value ?? LayoutKind.Sequential) == LayoutKind.Explicit;
            var seq = 0;
            var max = 0;
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (f.IsStatic)
                {
                    continue;
                }

                var sz = SizeOf(f.FieldType);
                var off = explicitLayout ? (f.GetCustomAttribute<FieldOffsetAttribute>()?.Value ?? seq) : seq;
                if (!explicitLayout)
                {
                    seq += sz;
                }

                max = Math.Max(max, off + sz);
            }

            var declaredSize = t.StructLayoutAttribute?.Size ?? 0;
            return Math.Max(max, declaredSize);
        }

        /// <summary>A live instance of a struct to verify.</summary>
        internal readonly struct Root
        {
            public Root(string label, IntPtr address, IntPtr owner)
            {
                this.Label = label;
                this.Address = address;
                this.Owner = owner;
            }

            public string Label { get; }

            public IntPtr Address { get; }

            public IntPtr Owner { get; }
        }
    }

    /// <summary>A snapshot of the UiElement tree: child-index path -> (address, visibility bit).</summary>
    internal sealed class UiCapture
    {
        public DateTime WhenLocal { get; set; }

        public Dictionary<string, (IntPtr Addr, bool Vis)> Nodes { get; } = new();

        public int Count => this.Nodes.Count;
    }

    /// <summary>One row of a UI visibility diff (a changed node or an ancestor for context).</summary>
    internal sealed class UiDiffRow
    {
        public string Path { get; set; } = string.Empty;

        public int Depth { get; set; }

        public IntPtr Addr { get; set; }

        public string Change { get; set; } = string.Empty;

        public bool IsChanged { get; set; }

        public bool BecameVisible { get; set; }
    }

    /// <summary>The visibility diff between two UI snapshots.</summary>
    internal sealed class UiDiff
    {
        public List<UiDiffRow> Rows { get; } = new();

        /// <summary>Total elements whose (effective) visibility differed.</summary>
        public int ChangedCount { get; set; }

        /// <summary>Origin elements (a changed node whose parent didn't also change).</summary>
        public int OriginCount { get; set; }
    }

    /// <summary>One verified field of a struct instance.</summary>
    internal sealed class FieldRow
    {
        public FieldRow(int offset, string name, OffsetHelperEngine.FieldKind kind, string value, OffsetHelperEngine.FieldStatus status, string reason)
        {
            this.Offset = offset;
            this.Name = name;
            this.Kind = kind;
            this.Value = value;
            this.Status = status;
            this.Reason = reason;
        }

        public int Offset { get; }

        public string Name { get; }

        public OffsetHelperEngine.FieldKind Kind { get; }

        public string Value { get; }

        public OffsetHelperEngine.FieldStatus Status { get; }

        public string Reason { get; }
    }

    /// <summary>Verification result for one struct instance (root).</summary>
    internal sealed class RootResult
    {
        public string Label { get; set; } = string.Empty;

        public IntPtr Address { get; set; }

        public IntPtr Owner { get; set; }

        public bool ReadOk { get; set; }

        public string Note { get; set; } = string.Empty;

        public OffsetHelperEngine.ProbeVerdict Verdict { get; set; }

        public List<FieldRow> Fields { get; set; } = new();
    }

    /// <summary>Optional values supplied by the user to turn otherwise ambiguous fields into exact anchors.</summary>
    internal sealed class OffsetRecoveryHints
    {
        public int MaxHealth { get; set; }

        public int MaxMana { get; set; }

        public int MaxEnergyShield { get; set; }

        public int ExpectedVitalTotal(string fieldName)
        {
            var leaf = fieldName[(fieldName.LastIndexOf('.') + 1)..];
            return leaf switch
            {
                "Health" => this.MaxHealth,
                "Mana" => this.MaxMana,
                "EnergyShield" => this.MaxEnergyShield,
                _ => 0,
            };
        }
    }

    /// <summary>A patch-ready top-level FieldOffset candidate found by semantic neighborhood scanning.</summary>
    internal sealed class OffsetRecoverySuggestion
    {
        public string FieldName { get; set; } = string.Empty;

        public int ConfiguredOffset { get; set; }

        public int CandidateOffset { get; set; }

        public int Shift => this.CandidateOffset - this.ConfiguredOffset;

        public int VerifiedRoots { get; set; }

        public int RootCount { get; set; }

        public int EvidencePasses { get; set; }

        public int ConsensusSupport { get; set; }

        public List<int> AlternativeOffsets { get; set; } = new();

        public bool IsAmbiguous => this.ConsensusSupport < 2 && this.AlternativeOffsets.Count > 0;
    }

    /// <summary>Aggregated verification result for one struct type.</summary>
    internal sealed class ProbeResult
    {
        public string Name { get; set; } = string.Empty;

        public Type? StructType { get; set; }

        public bool Unmapped { get; set; }

        public int RootCount { get; set; }

        public string SampleLabel { get; set; } = string.Empty;

        public OffsetHelperEngine.ProbeVerdict Verdict { get; set; }

        public string Detail { get; set; } = string.Empty;

        public List<RootResult> Roots { get; set; } = new();

        public bool RecoveryAttempted { get; set; }

        public List<OffsetRecoverySuggestion> Recoveries { get; } = new();
    }

    /// <summary>The result of a full sweep.</summary>
    internal sealed class SweepResult
    {
        public DateTime WhenLocal { get; set; }

        public bool InGame { get; set; }

        public List<ProbeResult> Probes { get; } = new();

        public int Intact { get; private set; }

        public int Degraded { get; private set; }

        public int Unverifiable { get; private set; }

        public int NoRoot { get; private set; }

        /// <summary>Recomputes the summary counts from <see cref="Probes" />.</summary>
        public void Recount()
        {
            this.Intact = this.Degraded = this.Unverifiable = this.NoRoot = 0;
            foreach (var p in this.Probes)
            {
                switch (p.Verdict)
                {
                    case OffsetHelperEngine.ProbeVerdict.Intact: this.Intact++; break;
                    case OffsetHelperEngine.ProbeVerdict.Degraded: this.Degraded++; break;
                    case OffsetHelperEngine.ProbeVerdict.Unverifiable: this.Unverifiable++; break;
                    case OffsetHelperEngine.ProbeVerdict.NoRoot: this.NoRoot++; break;
                }
            }
        }
    }
}
