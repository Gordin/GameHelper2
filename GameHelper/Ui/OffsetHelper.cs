// <copyright file="OffsetHelper.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Ui
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using System.Threading.Tasks;
    using Coroutine;
    using CoroutineEvents;
    using GameHelper.Cache;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using GameHelper.RemoteObjects.UiElement;
    using GameHelper.Utils;
    using GameOffsets;
    using GameOffsets.Natives;
    using ImGuiNET;
    using Eng = OffsetHelperEngine;

    /// <summary>
    ///     A diagnostic window that tells you, live and without Ghidra, whether the game's memory
    ///     offsets still hold after a patch. It reads live instances of every mapped offset struct
    ///     and runs value-based sanity checks on each field (see <see cref="OffsetHelperEngine" />),
    ///     bucketing each struct into Intact / Degraded / Unverifiable / Not-covered. It also
    ///     re-scans the static-address signature patterns (the other thing patches break) and tracks
    ///     which UI panels you still need to open so their offset chains can be exercised.
    /// </summary>
    public static class OffsetHelper
    {
        private static readonly Vector4 Green = new(0.40f, 0.90f, 0.40f, 1f);
        private static readonly Vector4 Red = new(1.00f, 0.40f, 0.40f, 1f);
        private static readonly Vector4 Yellow = new(1.00f, 0.85f, 0.40f, 1f);
        private static readonly Vector4 Grey = new(0.60f, 0.60f, 0.60f, 1f);
        private static readonly Vector4 Blue = new(0.60f, 0.80f, 1.00f, 1f);

        private static SweepResult? lastSweep;
        private static bool autoRefresh;
        private static int autoRefreshFrameCounter;

        // Static-address re-scan ("Self-test") state, mutated from a background Task.
        private static volatile bool selfTestRunning;
        private static string selfTestSummary = "not run";
        private static Vector4 selfTestColor = Grey;
        private static Dictionary<string, string> selfTestPerPattern = new();

        // Latched "have we ever seen this UI element resolved / visible this session" flags.
        private static readonly Dictionary<string, (bool Resolved, bool Visible)> InteractSeen = new();

        // UI panel finder (visibility-diff) state machine.
        private enum UiScanPhase
        {
            Idle,
            Settling,
            Frozen,
            Comparing,
            Done,
        }

        private static UiScanPhase uiPhase = UiScanPhase.Idle;
        private static double uiLastScanTime;
        private static UiCapture? uiPrevScan;       // last periodic scan (rolling, to detect flicker)
        private static UiCapture? uiBaselineStable; // settled baseline captured at the compare press
        private static readonly HashSet<string> UiExcluded = new(); // paths that ever flickered
        private static int uiStableCount;
        private static UiDiff? uiDiff;
        private const double UiScanIntervalSeconds = 0.5;
        private static readonly UiElementParents UiParents =
            new(null, GameStateTypes.InGameState, GameStateTypes.EscapeState, "OffsetHelper");

        // Per-entity inspector cards the user pinned open.
        private static readonly List<Entity> PinnedEntities = new();

        // In-world entity-box overlay state.
        private static bool liveBoxes;
        private static int maxBoxes = 100;
        private static float boxScale = 1.0f;
        private static readonly HashSet<uint> HiddenBoxEntityIds = new();

        // Aggregate box verdict cache (recomputing every entity's components each frame is too
        // expensive), keyed by entity Id, with a frame stamp and a per-frame recompute budget.
        private static readonly Dictionary<uint, (Eng.ProbeVerdict Verdict, long Frame)> BoxVerdicts = new();
        private static long boxFrame;
        private static IntPtr lastBoxArea;

        // Screen rects of our own ImGui windows this frame; the world boxes ignore clicks/hovers
        // that land on them so interacting with the panel/cards never mis-selects a box.
        private static readonly List<(Vector2 Min, Vector2 Max)> OccludeRects = new();

        private const int AutoRefreshEveryFrames = 60;
        private const int MaxPinnedCards = 24;
        private const int BoxVerdictTtlFrames = 30;
        private const int BoxRecomputeBudgetPerFrame = 16;

        private static readonly uint BoxYellow = ImGuiHelper.Color(255, 220, 60, 255);
        private static readonly uint BoxRed = ImGuiHelper.Color(255, 70, 70, 255);
        private static readonly uint BoxGreen = ImGuiHelper.Color(90, 230, 90, 255);
        private static readonly uint BoxGrey = ImGuiHelper.Color(150, 150, 150, 255);
        private static readonly uint BoxLabelBg = ImGuiHelper.Color(0, 0, 0, 190);

        /// <summary>Initializes the co-routines.</summary>
        internal static void InitializeCoroutines()
        {
            CoroutineHandler.Start(RenderCoroutine());
        }

        private static IEnumerator<Wait> RenderCoroutine()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnRender);
                if (!Core.GHSettings.ShowOffsetHelper)
                {
                    continue;
                }

                UpdateInteractLatches();
                UiScanTick();

                if (autoRefresh && ++autoRefreshFrameCounter >= AutoRefreshEveryFrames)
                {
                    autoRefreshFrameCounter = 0;
                    SafeSweep();
                }

                OccludeRects.Clear();
                ImGui.SetNextWindowSize(new Vector2(720, 640), ImGuiCond.FirstUseEver);
                if (ImGui.Begin("OffsetHelper (OH)", ref Core.GHSettings.ShowOffsetHelper))
                {
                    RecordOcclusion();
                    DrawToolbar();
                    ImGui.Separator();
                    DrawSummary();
                    ImGui.Separator();

                    DrawEntityInspector();
                    DrawStaticAddresses();
                    DrawInteractChecklist();
                    DrawUiPanelFinder();

                    if (lastSweep is { InGame: true })
                    {
                        DrawBucket("Degraded", Red, Eng.ProbeVerdict.Degraded, showDetail: true, defaultOpen: true);
                        DrawBucket("Unverifiable (no anchor field — needs manual / Ghidra)", Yellow, Eng.ProbeVerdict.Unverifiable, showDetail: true, defaultOpen: false);
                        DrawBucket("Not covered this run (no live root — get in-world / near the entity)", Grey, Eng.ProbeVerdict.NoRoot, showDetail: false, defaultOpen: false);
                        DrawBucket("Verified intact", Green, Eng.ProbeVerdict.Intact, showDetail: false, defaultOpen: false);
                    }
                }

                ImGui.End();

                // Cards first so their rects are recorded before the world boxes test occlusion.
                // (Boxes render on the background draw list, so they stay visually behind windows
                // regardless of call order.)
                DrawPinnedCards();

                if (liveBoxes)
                {
                    DrawEntityBoxes();
                }
            }
        }

        private static void RecordOcclusion()
        {
            OccludeRects.Add((ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize()));
        }

        private static bool MouseOccluded(Vector2 m)
        {
            foreach (var (min, max) in OccludeRects)
            {
                if (m.X >= min.X && m.X <= max.X && m.Y >= min.Y && m.Y <= max.Y)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsIgnored(Entity e)
        {
            var ignore = Core.GHSettings.MonstersPathsToIgnore;
            for (var i = 0; i < ignore.Count; i++)
            {
                if (!string.IsNullOrEmpty(ignore[i]) && e.Path.StartsWith(ignore[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void DrawToolbar()
        {
            if (ImGui.Button("Run sweep"))
            {
                SafeSweep();
            }

            ImGui.SameLine();
            if (selfTestRunning)
            {
                ImGui.TextDisabled("Self-test…");
            }
            else if (ImGui.Button("Self-test"))
            {
                StartSelfTest();
            }

            ImGui.SameLine();
            ImGui.Checkbox("Auto", ref autoRefresh);
            ImGui.SameLine();
            ImGui.TextColored(selfTestColor, selfTestSummary);
        }

        private static void DrawSummary()
        {
            if (lastSweep == null)
            {
                ImGui.TextColored(Grey, "No sweep yet — press \"Run sweep\" while in-game.");
                return;
            }

            if (!lastSweep.InGame)
            {
                ImGui.TextColored(Grey, $"Not in game (no area instance). Last attempt {lastSweep.WhenLocal:HH:mm:ss}.");
                return;
            }

            ImGui.TextColored(Green, $"{lastSweep.Intact} intact");
            ImGui.SameLine();
            ImGui.TextDisabled("·");
            ImGui.SameLine();
            ImGui.TextColored(lastSweep.Degraded > 0 ? Red : Grey, $"{lastSweep.Degraded} degraded");
            ImGui.SameLine();
            ImGui.TextDisabled("·");
            ImGui.SameLine();
            ImGui.TextColored(Yellow, $"{lastSweep.Unverifiable} unverifiable");
            ImGui.SameLine();
            ImGui.TextDisabled("·");
            ImGui.SameLine();
            ImGui.TextColored(Grey, $"{lastSweep.NoRoot} no-root");
            ImGui.SameLine();
            ImGui.TextDisabled($"  ({lastSweep.WhenLocal:HH:mm:ss})");
        }

        private static void DrawBucket(string title, Vector4 color, Eng.ProbeVerdict verdict, bool showDetail, bool defaultOpen)
        {
            if (lastSweep == null)
            {
                return;
            }

            var items = lastSweep.Probes.Where(p => p.Verdict == verdict).OrderBy(p => p.Name).ToList();
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            var open = ImGui.CollapsingHeader($"{title} — {items.Count}###odbucket_{verdict}",
                defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
            ImGui.PopStyleColor();
            if (!open)
            {
                return;
            }

            foreach (var p in items)
            {
                DrawProbe(p, color, showDetail);
            }
        }

        private static void DrawProbe(ProbeResult p, Vector4 color, bool showDetail)
        {
            var rootInfo = p.RootCount > 0 ? $" ({p.RootCount} root{(p.RootCount == 1 ? string.Empty : "s")})" : string.Empty;
            var sample = string.IsNullOrEmpty(p.SampleLabel) ? string.Empty : $"   ·   {p.SampleLabel}";
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            var open = ImGui.TreeNode($"{p.Name}{rootInfo}{sample}###odprobe_{p.Name}");
            ImGui.PopStyleColor();

            if (!open)
            {
                return;
            }

            if (showDetail && !string.IsNullOrEmpty(p.Detail))
            {
                ImGui.TextWrapped(p.Detail);
            }

            for (var i = 0; i < p.Roots.Count; i++)
            {
                var root = p.Roots[i];
                var rootColor = VerdictColor(root.Verdict);
                ImGui.PushStyleColor(ImGuiCol.Text, rootColor);
                var ropen = ImGui.TreeNode($"{root.Label}  ·  0x{root.Address.ToInt64():X}###odroot_{p.Name}_{i}");
                ImGui.PopStyleColor();
                if (ropen)
                {
                    if (!root.ReadOk)
                    {
                        ImGui.TextColored(Red, $"unreadable: {root.Note}");
                    }
                    else
                    {
                        DrawFieldTable(p.Name + i, root.Fields);
                    }

                    ImGui.TreePop();
                }
            }

            ImGui.TreePop();
        }

        private static void DrawFieldTable(string id, List<FieldRow> fields)
        {
            if (!ImGui.BeginTable($"odfields_{id}", 5,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp))
            {
                return;
            }

            ImGui.TableSetupColumn("Offset", ImGuiTableColumnFlags.WidthFixed, 62);
            ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Kind", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableHeadersRow();

            foreach (var f in fields)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextDisabled($"0x{f.Offset:X}");
                ImGui.TableNextColumn();
                ImGui.Text(f.Name);
                ImGui.TableNextColumn();
                ImGui.TextDisabled(f.Kind.ToString());
                ImGui.TableNextColumn();
                ImGui.Text(f.Value);
                ImGui.TableNextColumn();
                ImGui.TextColored(StatusColor(f.Status), $"{StatusWord(f.Status)}{(string.IsNullOrEmpty(f.Reason) ? string.Empty : " · " + f.Reason)}");
            }

            ImGui.EndTable();
        }

        private static void DrawStaticAddresses()
        {
            if (!ImGui.CollapsingHeader("Static addresses (scan patterns)"))
            {
                return;
            }

            if (Core.Process.Address == IntPtr.Zero)
            {
                ImGui.TextColored(Grey, "Game not attached.");
                return;
            }

            ImGui.TextDisabled("Resolved once per attach from GameOffsets/StaticOffsetsPatterns.cs. \"Self-test\" re-scans them.");
            if (!ImGui.BeginTable("odstatic", 3,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            {
                return;
            }

            ImGui.TableSetupColumn("Pattern", ImGuiTableColumnFlags.WidthFixed, 180);
            ImGui.TableSetupColumn("Resolved address", ImGuiTableColumnFlags.WidthFixed, 170);
            ImGui.TableSetupColumn("Re-scan (Self-test)", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            var perPattern = selfTestPerPattern;
            foreach (var pattern in StaticOffsetsPatterns.Patterns)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(pattern.Name);

                ImGui.TableNextColumn();
                if (Core.Process.StaticAddresses.TryGetValue(pattern.Name, out var addr) && addr != IntPtr.Zero)
                {
                    ImGuiHelper.IntPtrToImGui("##od" + pattern.Name, addr);
                }
                else
                {
                    ImGui.TextColored(Red, "NOT FOUND (pattern broke)");
                }

                ImGui.TableNextColumn();
                if (perPattern.TryGetValue(pattern.Name, out var note))
                {
                    ImGui.TextColored(note.StartsWith("relocated", StringComparison.Ordinal) ? Yellow : Green, note);
                }
                else
                {
                    ImGui.TextDisabled(selfTestRunning ? "scanning…" : "—");
                }
            }

            ImGui.EndTable();
        }

        private static void DrawInteractChecklist()
        {
            var pending = InteractSeen.Values.Count(v => !(v.Resolved && v.Visible));
            if (!ImGui.CollapsingHeader($"Open/close these to verify — {pending} pending###odinteract"))
            {
                return;
            }

            ImGui.TextDisabled("These UI-element offset chains can only be exercised while the panel is open. " +
                               "Open each one once; a row goes green when it has been both resolved and seen visible.");

            if (!ImGui.BeginTable("odinteract_tbl", 4,
                    ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
            {
                return;
            }

            ImGui.TableSetupColumn("Panel", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Resolved", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Seen open", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("How", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            foreach (var (name, hint) in InteractHints)
            {
                InteractSeen.TryGetValue(name, out var seen);
                var done = seen.Resolved && seen.Visible;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextColored(done ? Green : Grey, $"{(done ? "✓" : "…")} {name}");
                ImGui.TableNextColumn();
                ImGui.TextColored(seen.Resolved ? Green : Grey, seen.Resolved ? "yes" : "no");
                ImGui.TableNextColumn();
                ImGui.TextColored(seen.Visible ? Green : Grey, seen.Visible ? "yes" : "no");
                ImGui.TableNextColumn();
                ImGui.TextDisabled(hint);
            }

            ImGui.EndTable();
            if (ImGui.SmallButton("Reset interact tracking"))
            {
                InteractSeen.Clear();
            }
        }

        /// <summary>
        ///     Periodic (every 0.5s) re-scan driving the panel-finder's "settle" logic. While a scan
        ///     is active it walks the whole UI tree, and any element whose visibility/presence changed
        ///     since the previous scan is permanently excluded — during settling that filters out the
        ///     churn (elements shifting around with nothing really happening); during comparing it
        ///     prunes result entries that keep flickering. Runs regardless of whether the section is
        ///     expanded, so the tree keeps settling in the background.
        /// </summary>
        private static void UiScanTick()
        {
            // Only Settling and Comparing rescan. Frozen deliberately stops scanning so the panel
            // toggle isn't mistaken for churn and filtered out.
            if (uiPhase is not (UiScanPhase.Settling or UiScanPhase.Comparing))
            {
                return;
            }

            var now = ImGui.GetTime();
            if (now - uiLastScanTime < UiScanIntervalSeconds)
            {
                return;
            }

            uiLastScanTime = now;
            if (Core.States.InGameStateObject.GameUi.Address == IntPtr.Zero)
            {
                return;
            }

            var cur = Eng.CaptureUiTree();
            if (uiPrevScan != null)
            {
                var keys = new HashSet<string>(uiPrevScan.Nodes.Keys);
                keys.UnionWith(cur.Nodes.Keys);
                foreach (var p in keys)
                {
                    if (UiExcluded.Contains(p))
                    {
                        continue;
                    }

                    var pa = uiPrevScan.Nodes.TryGetValue(p, out var va);
                    var pb = cur.Nodes.TryGetValue(p, out var vb);
                    // Presence flip or visibility flip between consecutive scans => unstable.
                    if (pa != pb || (pa && pb && va.Vis != vb.Vis))
                    {
                        UiExcluded.Add(p);
                    }
                }
            }

            uiPrevScan = cur;

            if (uiPhase == UiScanPhase.Settling)
            {
                uiStableCount = cur.Nodes.Keys.Count(k => !UiExcluded.Contains(k));
            }
            else if (uiPhase == UiScanPhase.Comparing && uiDiff != null)
            {
                // Drop any result element that has since become unstable.
                uiDiff.Rows.RemoveAll(r => UiExcluded.Contains(r.Path));
                uiDiff.OriginCount = uiDiff.Rows.Count;
            }
        }

        private static void ResetUiScan()
        {
            uiPhase = UiScanPhase.Idle;
            uiPrevScan = null;
            uiBaselineStable = null;
            uiDiff = null;
            uiStableCount = 0;
            UiExcluded.Clear();
        }

        private static void DrawUiPanelFinder()
        {
            var header = uiPhase switch
            {
                UiScanPhase.Settling => $" — settling, {uiStableCount} stable",
                UiScanPhase.Frozen => $" — frozen at {uiStableCount}, toggle now",
                UiScanPhase.Comparing => $" — comparing, {uiDiff?.OriginCount ?? 0} left",
                UiScanPhase.Done => $" — {uiDiff?.OriginCount ?? 0} found",
                _ => string.Empty,
            };
            if (!ImGui.CollapsingHeader($"UI panel finder (visibility diff){header}###oduifinder"))
            {
                return;
            }

            if (Core.States.InGameStateObject.GameUi.Address == IntPtr.Zero)
            {
                ImGui.TextColored(Grey, "Not in game.");
                return;
            }

            ImGui.TextWrapped("1) Start baseline scan and WAIT until \"stable\" stops dropping (the UI has settled). " +
                              "2) click \"Baseline settled\" to freeze it. 3) open or close the panel, then Compare. " +
                              "4) watch it prune flicker, then Finish scanning. Unstable elements (things that shift on " +
                              "their own) are filtered out automatically — freezing before you toggle is what keeps your " +
                              "toggle from being filtered too.");

            switch (uiPhase)
            {
                case UiScanPhase.Idle:
                    if (ImGui.Button("1. Start baseline scan"))
                    {
                        ResetUiScan();
                        uiPrevScan = Eng.CaptureUiTree();
                        uiStableCount = uiPrevScan.Count;
                        uiLastScanTime = ImGui.GetTime();
                        uiPhase = UiScanPhase.Settling;
                    }

                    break;

                case UiScanPhase.Settling:
                    ImGui.TextColored(Yellow, $"Settling…  stable elements: {uiStableCount}   (excluded {UiExcluded.Count})");
                    ImGui.TextDisabled("Wait for \"stable\" to stop dropping, then freeze.");
                    if (ImGui.Button("2. Baseline settled (freeze & stop scanning)"))
                    {
                        uiBaselineStable = uiPrevScan;
                        uiPhase = UiScanPhase.Frozen;
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Reset##uifinder"))
                    {
                        ResetUiScan();
                    }

                    break;

                case UiScanPhase.Frozen:
                    ImGui.TextColored(Green, $"Baseline frozen at {uiStableCount} stable elements. Scanning stopped.");
                    ImGui.TextDisabled("Now open or close the panel in-game, THEN press Compare.");
                    if (ImGui.Button("3. Compare (I've toggled the panel)"))
                    {
                        var cur = Eng.CaptureUiTree();
                        uiDiff = uiBaselineStable != null
                            ? Eng.DiffVisibility(uiBaselineStable, cur, UiExcluded)
                            : new UiDiff();
                        uiPrevScan = cur;
                        uiLastScanTime = ImGui.GetTime();
                        uiPhase = UiScanPhase.Comparing;
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Back to settling"))
                    {
                        uiLastScanTime = ImGui.GetTime();
                        uiPhase = UiScanPhase.Settling;
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Reset##uifinder"))
                    {
                        ResetUiScan();
                    }

                    break;

                case UiScanPhase.Comparing:
                    ImGui.TextColored(Yellow, $"Comparing… pruning flicker. Result elements: {uiDiff?.OriginCount ?? 0}");
                    if (ImGui.Button("Finish scanning"))
                    {
                        uiPhase = UiScanPhase.Done;
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Reset##uifinder"))
                    {
                        ResetUiScan();
                    }

                    RenderDiffTree();
                    break;

                case UiScanPhase.Done:
                    ImGui.TextColored(Green, $"Done. {uiDiff?.OriginCount ?? 0} element(s) toggled.");
                    if (ImGui.Button("Start over"))
                    {
                        ResetUiScan();
                    }

                    RenderDiffTree();
                    break;
            }
        }

        private static void RenderDiffTree()
        {
            if (uiDiff == null || uiDiff.Rows.Count == 0)
            {
                ImGui.TextColored(Grey, "No stable visibility changes (yet). Toggle the panel if you haven't.");
                return;
            }

            ImGui.Separator();
            ImGui.TextDisabled("Hover a node to outline it in-game; expand to walk its live children. Path is click-to-copy.");

            // Keep the live UiElementBase parent chain (positions/scale) fresh, like GameUiExplorer.
            UiParents.UpdateAllParentsParallel();

            foreach (var row in uiDiff.Rows)
            {
                var el = ResolveUiByPath(row.Path);
                var pretty = FormatPath(row.Path);
                var color = row.BecameVisible ? Green : Red;

                if (el == null)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, color);
                    ImGuiHelper.DisplayTextAndCopyOnClick($"{pretty}  {row.Change}  — element moved (not live right now)", pretty);
                    ImGui.PopStyleColor();
                    continue;
                }

                var dims = $"[{el.Position.X:F0},{el.Position.Y:F0} {el.Size.X:F0}x{el.Size.Y:F0}]";
                var label = $"{pretty}: {row.Change}  {dims}";
                ImGui.PushStyleColor(ImGuiCol.Text, color);
                var open = ImGui.TreeNodeEx($"##oduiorigin_{row.Path}", ImGuiTreeNodeFlags.DefaultOpen, label);
                ImGui.PopStyleColor();
                if (ImGui.IsItemHovered())
                {
                    ImGuiHelper.DrawRect(el.Position, el.Size, 255, 255, 0);
                }

                ImGui.SameLine();
                ImGuiHelper.DisplayTextAndCopyOnClick($"(copy)##copyorigin_{row.Path}", pretty);
                ImGui.SameLine();
                ImGuiHelper.IntPtrToImGui($"##oduiaddr_{row.Path}", el.Address);

                if (open)
                {
                    for (var i = 0; i < el.TotalChildrens; i++)
                    {
                        var child = el[i];
                        if (child != null)
                        {
                            RenderLiveUiNode(child, ChildPath(row.Path, i));
                        }
                    }

                    ImGui.TreePop();
                }
            }
        }

        /// <summary>Formats an internal dotted child-index path as "[X][Y][…]" (root = "[root]").</summary>
        private static string FormatPath(string dotted)
        {
            return dotted.Length == 0 ? "[root]" : "[" + string.Join("][", dotted.Split('.')) + "]";
        }

        private static string ChildPath(string parentDotted, int index)
        {
            return parentDotted.Length == 0 ? index.ToString() : parentDotted + "." + index;
        }

        /// <summary>
        ///     Rebuilds a live <see cref="UiElementBase" /> by walking the child-index path from the
        ///     GameUi root, so we can read its real Position/Size and highlight it in-game.
        /// </summary>
        private static UiElementBase? ResolveUiByPath(string path)
        {
            var root = Core.States.InGameStateObject.GameUi.Address;
            if (root == IntPtr.Zero)
            {
                return null;
            }

            var el = new UiElementBase(root, UiParents);
            if (path.Length == 0)
            {
                return el;
            }

            foreach (var seg in path.Split('.'))
            {
                if (!int.TryParse(seg, out var idx) || idx < 0 || idx >= el.TotalChildrens)
                {
                    return null;
                }

                var child = el[idx];
                if (child == null)
                {
                    return null;
                }

                el = child;
            }

            return el;
        }

        /// <summary>
        ///     Recursive collapsible child renderer (GameUiExplorer-style): the node label is the
        ///     element's own "[X][Y][…]" path (green when visible), followed by its dimensions, a
        ///     click-to-copy "(copy)" of that path, and its address. Hovering outlines it in-game.
        /// </summary>
        private static void RenderLiveUiNode(UiElementBase element, string dottedPath)
        {
            var pretty = FormatPath(dottedPath);
            var dims = $"[{element.Position.X:F0},{element.Position.Y:F0} {element.Size.X:F0}x{element.Size.Y:F0}]  ch:{element.TotalChildrens}";
            if (element.IsVisible)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Green);
            }

            var open = ImGui.TreeNode($"##oduilive_{dottedPath}", $"{pretty}   {dims}");
            if (element.IsVisible)
            {
                ImGui.PopStyleColor();
            }

            if (ImGui.IsItemHovered())
            {
                ImGuiHelper.DrawRect(element.Position, element.Size, 255, 255, 0);
            }

            ImGui.SameLine();
            ImGuiHelper.DisplayTextAndCopyOnClick($"(copy)##copylive_{dottedPath}", pretty);

            if (open)
            {
                for (var i = 0; i < element.TotalChildrens; i++)
                {
                    var child = element[i];
                    if (child != null)
                    {
                        RenderLiveUiNode(child, ChildPath(dottedPath, i));
                    }
                }

                ImGui.TreePop();
            }
        }

        private static void DrawEntityInspector()
        {
            var area = Core.States.InGameStateObject.CurrentAreaInstance;
            if (!ImGui.CollapsingHeader($"Entity inspector ({PinnedEntities.Count} pinned)###odinspector"))
            {
                return;
            }

            if (area == null || area.Address == IntPtr.Zero)
            {
                ImGui.TextColored(Grey, "Not in game.");
                return;
            }

            if (ImGui.Button("Track nearby entities"))
            {
                TrackNearbyEntities();
            }

            ImGui.SameLine();
            if (ImGui.Button("Clear tracked"))
            {
                PinnedEntities.Clear();
            }

            ImGui.Checkbox("Live entity boxes (no tracking needed)", ref liveBoxes);

            ImGui.SetNextItemWidth(140);
            ImGui.SliderInt("Max boxes", ref maxBoxes, 1, 500);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(140);
            ImGui.SliderFloat("Box scale", ref boxScale, 0.2f, 3f);
            ImGui.SameLine();
            if (ImGui.Button($"Unhide all ({HiddenBoxEntityIds.Count})"))
            {
                HiddenBoxEntityIds.Clear();
            }

            ImGui.TextColored(Green, "green = all components OK");
            ImGui.SameLine();
            ImGui.TextColored(Yellow, "· yellow = something unverifiable");
            ImGui.SameLine();
            ImGui.TextColored(Red, "· red = broken");
            ImGui.TextDisabled("Hover thickens a box's border · click it for details · [x] hides it · ignored-entity paths are skipped");
            ImGui.Separator();
            ImGui.TextDisabled("Or pin an entity below to open a floating card verifying each of its components live.");

            void PinRow(Entity? e, string tag)
            {
                if (e == null || !e.IsValid || e.Address == IntPtr.Zero)
                {
                    return;
                }

                var pinned = PinnedEntities.Contains(e);
                if (ImGui.SmallButton($"{(pinned ? "Unpin" : "Pin")}##odpin_{tag}_{e.Id}"))
                {
                    if (pinned)
                    {
                        PinnedEntities.Remove(e);
                    }
                    else
                    {
                        PinnedEntities.Add(e);
                    }
                }

                ImGui.SameLine();
                ImGui.Text($"{tag}#{e.Id}  ·  {e.EntityType}  ·  0x{e.Address.ToInt64():X}  ·  {e.Path}");
            }

            PinRow(area.Player, "Player");
            PinRow(Core.States.InGameStateObject.MouseOverEntity, "MouseOver");

            var shown = 0;
            foreach (var kv in area.AwakeEntities)
            {
                if (IsIgnored(kv.Value))
                {
                    continue;
                }

                if (shown++ >= 20)
                {
                    ImGui.TextDisabled("… (showing first 20 awake entities)");
                    break;
                }

                PinRow(kv.Value, kv.Value.EntityType.ToString());
            }
        }

        private static void DrawPinnedCards()
        {
            for (var i = PinnedEntities.Count - 1; i >= 0; i--)
            {
                var e = PinnedEntities[i];
                var open = true;
                ImGui.SetNextWindowSize(new Vector2(560, 520), ImGuiCond.FirstUseEver);
                if (ImGui.Begin($"{e.EntityType}#{e.Id}###odcard_{e.GetHashCode()}", ref open))
                {
                    RecordOcclusion();
                    DrawCardBody(e);
                }

                ImGui.End();
                if (!open)
                {
                    PinnedEntities.RemoveAt(i);
                }
            }
        }

        private static void DrawCardBody(Entity e)
        {
            if (!e.IsValid || e.Address == IntPtr.Zero)
            {
                ImGui.TextColored(Grey, "Entity is no longer valid (left the area / exploded).");
                return;
            }

            var components = e.GetComponentAddressPairs()
                .Where(kv => kv.Value != IntPtr.Zero)
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .ToList();

            var results = new List<(string Name, bool Unmapped, RootResult Root)>(components.Count);
            var degraded = new List<string>();
            var verified = 0;
            foreach (var kv in components)
            {
                var mapped = Eng.ComponentOffsetTypes.TryGetValue(kv.Key, out var structType);
                var root = Eng.VerifyRoot(mapped ? structType! : typeof(GameOffsets.Objects.Components.ComponentHeader),
                    kv.Key, kv.Value, e.Address, "EntityPtr");
                results.Add((kv.Key, !mapped, root));
                if (mapped)
                {
                    if (root.Verdict == Eng.ProbeVerdict.Degraded)
                    {
                        degraded.Add(kv.Key);
                    }
                    else if (root.Verdict == Eng.ProbeVerdict.Intact)
                    {
                        verified++;
                    }
                }
            }

            if (degraded.Count > 0)
            {
                ImGui.TextColored(Red, $"DEGRADED: {string.Join(", ", degraded)}");
            }
            else
            {
                ImGui.TextColored(Green, $"OK · {verified} verified");
            }

            var pos = e.TryGetComponent<Render>(out var render, false)
                ? $"pos {render.GridPosition.X:0},{render.GridPosition.Y:0},{render.TerrainHeight:0}"
                : "pos —";
            ImGui.TextDisabled(pos);
            ImGui.SameLine();
            ImGui.TextDisabled("·");
            ImGui.SameLine();
            ImGuiHelper.IntPtrToImGui("##odcardaddr", e.Address);
            ImGui.Text($"Path: {e.Path}");
            ImGui.Text($"Id: {e.Id}   State: {e.EntityState}");
            ImGui.Separator();
            ImGui.Text($"Components ({results.Count})");

            foreach (var (name, unmapped, root) in results)
            {
                string suffix;
                Vector4 color;
                if (unmapped)
                {
                    // No registered layout, but if the header back-points to the owner the component
                    // pointer itself is verified — report that rather than a bare "Unverifiable".
                    var headerOk = root.ReadOk && root.Fields.Any(f => f.Kind == Eng.FieldKind.OwnerPtr && f.Status == Eng.FieldStatus.Pass);
                    color = headerOk ? Green : Yellow;
                    suffix = headerOk ? "unmapped · header OK (no layout to verify)" : "unmapped · Unverifiable";
                }
                else
                {
                    color = VerdictColor(root.Verdict);
                    var strong = root.Fields.Count(IsStrongPass);
                    var total = root.Fields.Count(f => f.Status != Eng.FieldStatus.Skip);
                    suffix = $"{VerdictWord(root.Verdict)} ({strong}/{total})";
                }

                ImGui.PushStyleColor(ImGuiCol.Text, color);
                var nodeOpen = ImGui.TreeNode($"{name}: {suffix}###odcomp_{e.GetHashCode()}_{name}");
                ImGui.PopStyleColor();
                if (nodeOpen)
                {
                    if (!root.ReadOk)
                    {
                        ImGui.TextColored(Red, $"unreadable: {root.Note}");
                    }
                    else
                    {
                        DrawFieldTable($"card_{e.GetHashCode()}_{name}", root.Fields);
                    }

                    ImGui.TreePop();
                }
            }
        }

        private static bool IsStrongPass(FieldRow f)
        {
            return f.Status == Eng.FieldStatus.Pass && f.Kind is Eng.FieldKind.Vector or Eng.FieldKind.OwnerPtr or Eng.FieldKind.Pointer;
        }

        private static void UpdateInteractLatches()
        {
            var state = Core.States.InGameStateObject;
            var ui = state?.GameUi;
            if (ui == null)
            {
                return;
            }

            void Latch(string name, bool resolved, bool visible)
            {
                InteractSeen.TryGetValue(name, out var prev);
                InteractSeen[name] = (prev.Resolved || resolved, prev.Visible || visible);
            }

            Latch("Minimap", ui.MiniMap.Address != IntPtr.Zero, ui.MiniMap.IsVisible);
            Latch("Large in-area map", ui.LargeMap.Address != IntPtr.Zero, ui.LargeMap.IsVisible);
            Latch("World / checkpoint map", ui.WorldMapPanel.Address != IntPtr.Zero, ui.WorldMapPanel.IsVisible);
            Latch("Atlas map", ui.Atlas.Address != IntPtr.Zero, ui.Atlas.IsVisible);
            Latch("Atlas skills panel", ui.AtlasSkillsPanel.Address != IntPtr.Zero, ui.AtlasSkillsPanel.IsVisible);
            Latch("Left panel (character/skills)", ui.LeftPanel.Address != IntPtr.Zero, ui.LeftPanel.IsVisible);
            Latch("Right panel (inventory/stash)", ui.RightPanel.Address != IntPtr.Zero, ui.RightPanel.IsVisible);
            Latch("Passive skill tree", ui.IsPassiveSkillTreeOpen, ui.IsPassiveSkillTreeOpen);
            Latch("Sekhemas trial map", ui.SekhemasTrialMapPanel.Address != IntPtr.Zero, ui.SekhemasTrialMapPanel.IsVisible);
            Latch("Chat", ui.ChatParent.Address != IntPtr.Zero, ui.ChatParent.IsVisible);
        }

        private static readonly (string Name, string Hint)[] InteractHints =
        {
            ("Minimap", "always on in-world"),
            ("Large in-area map", "hold/press the map key (Tab)"),
            ("World / checkpoint map", "interact with a checkpoint"),
            ("Atlas map", "open the Atlas from a checkpoint"),
            ("Atlas skills panel", "open Atlas passive tree"),
            ("Left panel (character/skills)", "press C or the skills key"),
            ("Right panel (inventory/stash)", "press I / open a stash"),
            ("Passive skill tree", "press P"),
            ("Sekhemas trial map", "during Trial of the Sekhemas"),
            ("Chat", "open the chat box"),
        };

        private static void TrackNearbyEntities()
        {
            var area = Core.States.InGameStateObject.CurrentAreaInstance;
            var player = area?.Player;
            if (area == null || player == null)
            {
                return;
            }

            var nearest = area.AwakeEntities.Values
                .Where(e => e.IsValid && e.Address != IntPtr.Zero && !IsIgnored(e))
                .OrderBy(e => e.DistanceFrom(player))
                .Take(MaxPinnedCards);
            foreach (var e in nearest)
            {
                if (!PinnedEntities.Contains(e) && PinnedEntities.Count < MaxPinnedCards)
                {
                    PinnedEntities.Add(e);
                }
            }
        }

        /// <summary>
        ///     Draws a 3D wireframe box around each nearby entity on the background draw list,
        ///     coloured by the entity's aggregate component verdict (green = all OK, yellow =
        ///     something unverifiable, red = broken). The box under the cursor draws with a bold
        ///     border; clicking it opens a pinned inspector card and its [x] hides that entity.
        /// </summary>
        private static void DrawEntityBoxes()
        {
            var state = Core.States.InGameStateObject;
            var area = state.CurrentAreaInstance;
            var world = state.CurrentWorldInstance;
            var player = area?.Player;
            if (area == null || player == null || area.Address == IntPtr.Zero || world.Address == IntPtr.Zero)
            {
                return;
            }

            boxFrame++;
            if (area.Address != lastBoxArea)
            {
                BoxVerdicts.Clear();
                lastBoxArea = area.Address;
            }

            var candidates = area.AwakeEntities.Values
                .Where(e => e.IsValid && e.Address != IntPtr.Zero && !HiddenBoxEntityIds.Contains(e.Id) && !IsIgnored(e))
                .OrderBy(e => e.DistanceFrom(player))
                .Take(Math.Max(1, maxBoxes))
                .ToList();

            var boxes = new List<(Entity Entity, Vector2[] Pts, Vector2 Min, Vector2 Max, uint Color)>(candidates.Count);
            var recomputes = 0;
            foreach (var e in candidates)
            {
                if (!e.TryGetComponent<Render>(out var render, false) ||
                    !TryComputeBox(world, render, out var pts, out var min, out var max))
                {
                    continue;
                }

                boxes.Add((e, pts, min, max, VerdictBoxColor(e, ref recomputes)));
            }

            var mouse = ImGui.GetMousePos();
            var occluded = MouseOccluded(mouse);
            var draw = ImGui.GetBackgroundDrawList();

            // Nearest-first: the first box whose (label-inclusive) rect holds the cursor is the one
            // we let the user select. -1 when the cursor is over one of our panels or nothing.
            var hovered = -1;
            if (!occluded)
            {
                for (var i = 0; i < boxes.Count; i++)
                {
                    var (_, _, min, max, _) = boxes[i];
                    if (mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y - 18 && mouse.Y <= max.Y)
                    {
                        hovered = i;
                        break;
                    }
                }
            }

            for (var i = 0; i < boxes.Count; i++)
            {
                var (entity, pts, min, _, color) = boxes[i];
                DrawWireBox(draw, pts, color, i == hovered ? 3.5f : 1.5f);

                var label = $"{entity.EntityType}#{entity.Id}";
                var labelPos = new Vector2(min.X, min.Y - 16);
                var textSize = ImGui.CalcTextSize(label);
                draw.AddRectFilled(labelPos, labelPos + textSize + new Vector2(4, 2), BoxLabelBg);
                draw.AddText(labelPos + new Vector2(2, 0), color, label);

                if (i == hovered)
                {
                    // The [x] is drawn only on the hovered box (the only interactive one).
                    var closeMin = new Vector2(labelPos.X + textSize.X + 6, min.Y - 16);
                    draw.AddRect(closeMin, closeMin + new Vector2(14, 14), BoxRed);
                    draw.AddText(closeMin + new Vector2(3, -1), BoxRed, "x");
                }
            }

            if (hovered >= 0)
            {
                HandleBoxInteraction(boxes[hovered]);
            }
        }

        /// <summary>
        ///     Places an invisible ImGui window (with body + [x] buttons) over the hovered box.
        ///     This is what makes clicking work: the transparent overlay is click-through over the
        ///     game, so a raw world click reaches the game, not ImGui. An ImGui window at the box
        ///     makes the overlay opaque there for one region, so the click is captured by us.
        /// </summary>
        private static void HandleBoxInteraction((Entity Entity, Vector2[] Pts, Vector2 Min, Vector2 Max, uint Color) box)
        {
            var (entity, _, min, max, _) = box;
            var winMin = new Vector2(min.X, min.Y - 18);
            var winMax = new Vector2(Math.Max(max.X, min.X + 130), max.Y);
            var size = winMax - winMin;
            if (size.X < 4 || size.Y < 4)
            {
                return;
            }

            ImGui.SetNextWindowPos(winMin);
            ImGui.SetNextWindowSize(size);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove |
                                           ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoNav |
                                           ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBackground |
                                           ImGuiWindowFlags.NoBringToFrontOnFocus;
            if (ImGui.Begin("##odboxhit", flags))
            {
                ImGui.SetCursorScreenPos(min);
                if (ImGui.InvisibleButton("##odboxbody", new Vector2(Math.Max(1f, max.X - min.X), Math.Max(1f, max.Y - min.Y))))
                {
                    if (!PinnedEntities.Contains(entity) && PinnedEntities.Count < MaxPinnedCards)
                    {
                        PinnedEntities.Add(entity);
                    }
                }

                ImGui.SetCursorScreenPos(new Vector2(min.X, min.Y - 16));
                if (ImGui.InvisibleButton("##odboxclose", new Vector2(14, 14)))
                {
                    HiddenBoxEntityIds.Add(entity.Id);
                }
            }

            ImGui.End();
            ImGui.PopStyleVar();
        }

        /// <summary>
        ///     Returns the box colour for an entity from its cached aggregate verdict, recomputing
        ///     (within a per-frame budget) when the cache entry is missing or stale.
        /// </summary>
        private static uint VerdictBoxColor(Entity e, ref int recomputes)
        {
            if (BoxVerdicts.TryGetValue(e.Id, out var cached) && boxFrame - cached.Frame < BoxVerdictTtlFrames)
            {
                return VerdictToBoxColor(cached.Verdict);
            }

            if (recomputes < BoxRecomputeBudgetPerFrame)
            {
                recomputes++;
                var verdict = ComputeEntityVerdict(e);
                BoxVerdicts[e.Id] = (verdict, boxFrame);
                return VerdictToBoxColor(verdict);
            }

            // Budget spent this frame: reuse a stale value if we have one, else grey until next frame.
            return cached.Frame > 0 ? VerdictToBoxColor(cached.Verdict) : BoxGrey;
        }

        private static uint VerdictToBoxColor(Eng.ProbeVerdict v) => v switch
        {
            Eng.ProbeVerdict.Degraded => BoxRed,
            Eng.ProbeVerdict.Unverifiable => BoxYellow,
            Eng.ProbeVerdict.Intact => BoxGreen,
            _ => BoxGrey,
        };

        /// <summary>
        ///     Aggregates an entity's per-component verdicts into one: red if anything is broken,
        ///     yellow if anything is genuinely unverifiable, green otherwise. An unmapped component
        ///     whose header (in-module vtable + owner back-pointer) checks out counts as OK — the
        ///     component pointer itself is verified even though its field layout isn't registered.
        /// </summary>
        private static Eng.ProbeVerdict ComputeEntityVerdict(Entity e)
        {
            var anyDegraded = false;
            var anyUnverifiable = false;
            foreach (var kv in e.GetComponentAddressPairs())
            {
                if (kv.Value == IntPtr.Zero)
                {
                    continue;
                }

                if (Eng.ComponentOffsetTypes.TryGetValue(kv.Key, out var structType))
                {
                    var v = Eng.VerifyRoot(structType, kv.Key, kv.Value, e.Address, "EntityPtr").Verdict;
                    if (v == Eng.ProbeVerdict.Degraded)
                    {
                        anyDegraded = true;
                    }
                    else if (v == Eng.ProbeVerdict.Unverifiable)
                    {
                        anyUnverifiable = true;
                    }
                }
                else if (!HeaderVerified(e, kv.Value))
                {
                    anyUnverifiable = true;
                }
            }

            return anyDegraded ? Eng.ProbeVerdict.Degraded
                : anyUnverifiable ? Eng.ProbeVerdict.Unverifiable
                : Eng.ProbeVerdict.Intact;
        }

        /// <summary>
        ///     True when an unmapped component's <see cref="GameOffsets.Objects.Components.ComponentHeader" />
        ///     back-points to its owning entity (i.e. the component pointer is real and attached),
        ///     even though its field layout isn't registered for full verification.
        /// </summary>
        private static bool HeaderVerified(Entity e, IntPtr componentAddr)
        {
            var r = Eng.VerifyRoot(typeof(GameOffsets.Objects.Components.ComponentHeader), "hdr", componentAddr, e.Address, "EntityPtr");
            return r.ReadOk && r.Fields.Any(f => f.Kind == Eng.FieldKind.OwnerPtr && f.Status == Eng.FieldStatus.Pass);
        }

        private static bool TryComputeBox(WorldData world, Render render, out Vector2[] pts, out Vector2 min, out Vector2 max)
        {
            pts = new Vector2[8];
            min = default;
            max = default;

            var wp = render.WorldPosition;
            var mb = render.ModelBounds;
            var bx = Math.Abs(mb.X) * boxScale;
            var by = Math.Abs(mb.Y) * boxScale;
            var bz = Math.Abs(mb.Z) * boxScale;

            // WorldPosition.Z is the feet/ground plane; subtracting ModelBounds.Z lifts to the top
            // (Z decreases upward toward the healthbar), matching the HealthBars plugin convention.
            var zBottom = wp.Z;
            var zTop = wp.Z - bz;
            float[] xs = { wp.X - bx, wp.X + bx };
            float[] ys = { wp.Y - by, wp.Y + by };

            pts[0] = W2S(world, xs[0], ys[0], zBottom);
            pts[1] = W2S(world, xs[1], ys[0], zBottom);
            pts[2] = W2S(world, xs[1], ys[1], zBottom);
            pts[3] = W2S(world, xs[0], ys[1], zBottom);
            pts[4] = W2S(world, xs[0], ys[0], zTop);
            pts[5] = W2S(world, xs[1], ys[0], zTop);
            pts[6] = W2S(world, xs[1], ys[1], zTop);
            pts[7] = W2S(world, xs[0], ys[1], zTop);

            min = pts[0];
            max = pts[0];
            foreach (var p in pts)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }

            // Cull degenerate / off-screen / behind-camera projections (W2S produces garbage when
            // the point is behind the camera, which lands far outside the window).
            var w = Core.Process.WindowArea;
            var cx = (min.X + max.X) * 0.5f;
            var cy = (min.Y + max.Y) * 0.5f;
            if (cx < -2000 || cy < -2000 || cx > w.Width + 2000 || cy > w.Height + 2000)
            {
                return false;
            }

            return max.X - min.X >= 2 && max.Y - min.Y >= 2;
        }

        private static Vector2 W2S(WorldData world, float x, float y, float z)
        {
            return world.WorldToScreen(new StdTuple3D<float> { X = x, Y = y, Z = z }, z);
        }

        private static void DrawWireBox(ImDrawListPtr draw, Vector2[] p, uint color, float thickness)
        {
            // bottom face
            draw.AddLine(p[0], p[1], color, thickness);
            draw.AddLine(p[1], p[2], color, thickness);
            draw.AddLine(p[2], p[3], color, thickness);
            draw.AddLine(p[3], p[0], color, thickness);

            // top face
            draw.AddLine(p[4], p[5], color, thickness);
            draw.AddLine(p[5], p[6], color, thickness);
            draw.AddLine(p[6], p[7], color, thickness);
            draw.AddLine(p[7], p[4], color, thickness);

            // verticals
            draw.AddLine(p[0], p[4], color, thickness);
            draw.AddLine(p[1], p[5], color, thickness);
            draw.AddLine(p[2], p[6], color, thickness);
            draw.AddLine(p[3], p[7], color, thickness);
        }

        private static void SafeSweep()
        {
            try
            {
                lastSweep = Eng.RunSweep();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OffsetHelper.SafeSweep] {ex}");
            }
        }

        private static void StartSelfTest()
        {
            if (selfTestRunning)
            {
                return;
            }

            selfTestRunning = true;
            selfTestSummary = "re-scanning patterns…";
            selfTestColor = Blue;

            // PatternFinder scans the whole main module — run off the render thread so the overlay
            // doesn't stall for the scan duration.
            _ = Task.Run(() =>
            {
                try
                {
                    var baseAddr = Core.Process.Address;
                    var mainModule = Core.Process.Information?.MainModule;
                    if (baseAddr == IntPtr.Zero || mainModule == null)
                    {
                        selfTestSummary = "game not attached";
                        selfTestColor = Grey;
                        return;
                    }

                    var procSize = mainModule.ModuleMemorySize;
                    var offsets = PatternFinder.Find(Core.Process.Handle, baseAddr, procSize);
                    var per = new Dictionary<string, string>(StringComparer.Ordinal);
                    var unchanged = 0;
                    var relocated = 0;
                    foreach (var kv in offsets)
                    {
                        var displacement = Core.Process.Handle.ReadMemory<int>(baseAddr + kv.Value);
                        var addr = baseAddr + kv.Value + displacement + 0x04;
                        var cached = Core.Process.StaticAddresses.TryGetValue(kv.Key, out var c) ? c : IntPtr.Zero;
                        if (cached == addr)
                        {
                            unchanged++;
                            per[kv.Key] = $"unchanged @0x{addr.ToInt64():X}";
                        }
                        else
                        {
                            relocated++;
                            per[kv.Key] = $"relocated 0x{cached.ToInt64():X} → 0x{addr.ToInt64():X}";
                        }
                    }

                    selfTestPerPattern = per;
                    selfTestSummary = $"rescan PASS — {unchanged} unchanged, {relocated} relocated";
                    selfTestColor = relocated > 0 ? Yellow : Green;
                }
                catch (Exception ex)
                {
                    selfTestPerPattern = new Dictionary<string, string>(StringComparer.Ordinal);
                    selfTestSummary = "rescan FAILED — a signature no longer matches (a pattern broke)";
                    selfTestColor = Red;
                    Console.WriteLine($"[OffsetHelper.SelfTest] {ex}");
                }
                finally
                {
                    selfTestRunning = false;
                }
            });
        }

        private static Vector4 VerdictColor(Eng.ProbeVerdict v) => v switch
        {
            Eng.ProbeVerdict.Intact => Green,
            Eng.ProbeVerdict.Degraded => Red,
            Eng.ProbeVerdict.Unverifiable => Yellow,
            _ => Grey,
        };

        private static string VerdictWord(Eng.ProbeVerdict v) => v switch
        {
            Eng.ProbeVerdict.Intact => "Intact",
            Eng.ProbeVerdict.Degraded => "Degraded",
            Eng.ProbeVerdict.Unverifiable => "Unverifiable",
            _ => "No root",
        };

        private static Vector4 StatusColor(Eng.FieldStatus s) => s switch
        {
            Eng.FieldStatus.Pass => Green,
            Eng.FieldStatus.Fail => Red,
            Eng.FieldStatus.Weak => Blue,
            _ => Grey,
        };

        private static string StatusWord(Eng.FieldStatus s) => s switch
        {
            Eng.FieldStatus.Pass => "PASS",
            Eng.FieldStatus.Fail => "FAIL",
            Eng.FieldStatus.Weak => "weak",
            _ => "—",
        };
    }
}
