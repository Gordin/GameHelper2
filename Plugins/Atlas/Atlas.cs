namespace Atlas
{
    using GameHelper;
    using GameHelper.Plugin;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using GameHelper.RemoteObjects.UiElement;
    using GameHelper.Utils;
    using GameOffsets.Natives;
    using ImGuiNET;
    using Newtonsoft.Json;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Drawing;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using System.Runtime.InteropServices;
    using System.Text;

    public sealed class Atlas : PCore<AtlasSettings>
    {
        private const uint CompletedNodeDotColor = 0xFF00FF00;
        private const uint DotOutlineColor = 0xFF000000;

        private const int ChannelGrid = 0;
        private const int ChannelLines = 1;
        private const int ChannelDots = 2;
        private const int ChannelLabels = 3;

        private string SettingPathname => Path.Join(DllDirectory, "config", "settings.txt");
        private string NewGroupName = string.Empty;

        private static readonly Dictionary<string, ContentInfo> MapTags = [];
        private static readonly Dictionary<string, ContentInfo> MapPlain = [];
        private static readonly Dictionary<byte, BiomeInfo> Biomes = [];

        // ── Per-node static-data cache ──────────────────────────────────────
        // Reading + chasing pointers for all ~1700 atlas nodes every frame was the FPS killer
        // (tens of thousands of cross-process reads per frame). The slow-changing per-node data
        // (map id, biome, completed/accessible state, content badges) is cached and refreshed on
        // an interval instead; each frame we only read the node's UiElementBase for a live screen
        // position (so panning/zoom stay exact) and draw the nodes that are actually on-screen.
        private struct NodeData
        {
            public int Index;
            public IntPtr Address;
            public StdTuple2D<int> GridPosition;
            public List<StdTuple2D<int>> ConnectedGridPositions;
            public string MapName;          // normalized
            public byte BiomeId;
            public AtlasNodeState State;
            public int BadgeCount;
            public List<string> RawContents;
            public string Type;             // "normal" or "unique"
            public List<string> Tags;       // e.g. "lineage", "arbiter"
        }
        private readonly List<NodeData> nodeCache = new();
        private int cacheFrameCounter = int.MaxValue;   // force refresh on first frame
        private int cachedAtlasCount = -1;
        private const int CacheRefreshFrames = 20;       // rebuild static data ~3×/sec at 60fps

        // Cached routing graph — the node graph doesn't change while the
        // atlas is open, so rebuild only with the node cache.
        private Dictionary<StdTuple2D<int>, List<StdTuple2D<int>>> cachedRouteGraph;
        private HashSet<StdTuple2D<int>> cachedAccessible;
        private Dictionary<StdTuple2D<int>, StdTuple2D<int>> cachedBfsTree;

        public override void OnDisable()
        {
        }

        public override void OnEnable(bool isGameOpened)
        {
            if (File.Exists(SettingPathname))
            {
                var content = File.ReadAllText(SettingPathname);
                var serializerSettings = new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace };
                Settings = JsonConvert.DeserializeObject<AtlasSettings>(content, serializerSettings);
            }

            LoadBiomeMap();
            LoadContentMap();
        }

        public override void SaveSettings()
        {
            var dir = Path.GetDirectoryName(SettingPathname);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var settingsData = JsonConvert.SerializeObject(Settings, Formatting.Indented);
            File.WriteAllText(SettingPathname, settingsData);
        }

        public override void DrawSettings()
        {
            #region SettingsUI
            ImGui.SeparatorText("Search Maps");
            ImGui.InputTextWithHint("Search Map", "You can search multiple maps at once using a comma separator ','", ref Settings.SearchQuery, 256);
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear"))
                Settings.SearchQuery = string.Empty;
            ImGui.SeparatorText("Show shortest path to");

            PathRow("Citadels", ref Settings.DrawLinesToCitadel, ref Settings.CitadelPathColor, ref Settings.CitadelMaxHops);
            PathRow("Towers", ref Settings.DrawLinesToTowers, ref Settings.TowerPathColor, ref Settings.TowerMaxHops);
            PathRow("Search", ref Settings.DrawLinesToSearch, ref Settings.SearchPathColor, ref Settings.SearchMaxHops);
            PathRow("Unique Maps", ref Settings.DrawLinesToUniqueMaps, ref Settings.UniquePathColor, ref Settings.UniqueMaxHops);
            PathRow("Lineage Maps", ref Settings.DrawLinesToLineageMaps, ref Settings.LineagePathColor, ref Settings.LineageMaxHops);
            PathRow("Arbiter Maps", ref Settings.DrawLinesToArbiterMaps, ref Settings.ArbiterPathColor, ref Settings.ArbiterMaxHops);

            ImGui.SliderFloat("Path Thickness", ref Settings.PathLineThickness, 1.0f, 8.0f);

            ImGui.SeparatorText("Atlas Settings");
            ImGui.Checkbox("Hide Completed Maps", ref Settings.HideCompletedMaps);
            ImGui.Checkbox("Hide Not Accessible Maps", ref Settings.HideNotAccessibleMaps);
            ImGui.Checkbox("Show Map Counts", ref Settings.ShowMapCounts);
            ImGuiHelper.ToolTip("Draw connected-node and badge counts under each map label on the Atlas.");
            ImGui.Checkbox("Show Biome Border", ref Settings.ShowBiomeBorder);
            if (Settings.ShowBiomeBorder)
                if (ImGui.TreeNode("Biome Settings"))
                {
                    ImGui.SetNextItemWidth(180);
                    ImGui.SliderFloat("Biome Border Thickness", ref Settings.BiomeBorderThickness, 1.0f, 6.0f);

                    if (ImGui.BeginTable("split", 3))
                    {
                        foreach (var biome in Biomes)
                        {
                            ImGui.TableNextColumn();
                            var id = biome.Key;
                            var info = biome.Value;

                            if (!Settings.BiomeOverrides.TryGetValue(id, out var ov))
                            {
                                ov = new ContentOverride();
                                Settings.BiomeOverrides[id] = ov;
                            }

                            bool show = ov.Show ?? info.Show;
                            if (ImGui.Checkbox($"##Show##{id}", ref show))
                            {
                                ov.Show = show;
                                ApplyBiomeOverrides();
                            }

                            var border = ov.BorderColor ?? info.BdColor;
                            ImGui.SameLine();
                            ColorSwatch($"Border Color##Biome{id}", ref border);
                            if (!ColorsEqual(border, ov.BorderColor ?? info.BdColor))
                            {
                                ov.BorderColor = border;
                                ApplyBiomeOverrides();
                            }

                            var label = string.IsNullOrWhiteSpace(info.Label) ? $"Biome {id}" : info.Label;
                            ImGui.SameLine();
                            ImGui.Text(label);
                        }
                        ImGui.EndTable();
                    }

                    ImGui.TreePop();
                }

            ImGui.Checkbox("Show Atlas Graph", ref Settings.ShowAtlasGraph);
            if (Settings.ShowAtlasGraph)
            {
                ImGui.SameLine();
                ColorSwatch("##AtlasGraphLineColor", ref Settings.AtlasGraphLineColor);
                ImGui.SliderFloat("Graph X-Offset", ref Settings.AtlasGraphOffsetX, -200f, 200f);
                ImGui.SliderFloat("Graph Y-Offset", ref Settings.AtlasGraphOffsetY, -200f, 200f);
            }

            ImGui.SeparatorText("Layout Settings");
            var nudge = Settings.AnchorNudge;
            if (ImGui.SliderFloat2("Layout Nudge (px)", ref nudge, -60f, 60f))
                Settings.AnchorNudge = nudge;
            ImGui.SliderFloat("Scale Multiplier", ref Settings.ScaleMultiplier, 0.5f, 3.0f);

            ImGui.SeparatorText("Map Groups");

            if (ImGui.TreeNode("Settings"))
            {
                ImGui.InputTextWithHint("##MapGroupName", "group name", ref Settings.GroupNameInput, 256);
                ImGui.SameLine();
                if (ImGui.Button("Add new map group"))
                {
                    Settings.MapGroups.Add(new MapGroupSettings(Settings.GroupNameInput, Settings.DefaultBackgroundColor, Settings.DefaultFontColor));
                    Settings.GroupNameInput = string.Empty;
                }

                for (int i = 0; i < Settings.MapGroups.Count; i++)
                {
                    var mapGroup = Settings.MapGroups[i];
                    if (ImGui.TreeNode($"{mapGroup.Name}##MapGroup{i}"))
                    {
                        float buttonSize = ImGui.GetFrameHeight();
                        if (TriangleButton($"##Up{i}", buttonSize, new Vector4(1, 1, 1, 1), true))
                        {
                            MoveMapGroup(i, -1);
                        }
                        ImGui.SameLine();
                        if (TriangleButton($"##Down{i}", buttonSize, new Vector4(1, 1, 1, 1), false))
                        {
                            MoveMapGroup(i, 1);
                        }
                        ImGui.SameLine();
                        if (ImGui.Button($"Rename Group##{i}"))
                        {
                            NewGroupName = mapGroup.Name;
                            ImGui.OpenPopup($"RenamePopup##{i}");
                        }
                        ImGui.SameLine();
                        if (ImGui.Button($"Delete Group##{i}"))
                        {
                            DeleteMapGroup(i);
                        }
                        ImGui.SameLine();
                        ColorSwatch($"##MapGroupBackgroundColor{i}", ref mapGroup.BackgroundColor);
                        ImGui.SameLine();
                        ImGui.Text("Background Color");
                        ImGui.SameLine();
                        ColorSwatch($"##MapGroupFontColor{i}", ref mapGroup.FontColor);
                        ImGui.SameLine(); ImGui.Text("Font Color");

                        for (int j = 0; j < mapGroup.Maps.Count; j++)
                        {
                            var mapName = mapGroup.Maps[j];
                            if (ImGui.InputTextWithHint($"##MapName{i}-{j}", "map name", ref mapName, 256))
                                mapGroup.Maps[j] = mapName;

                            ImGui.SameLine();
                            if (ImGui.Button($"Delete##MapNameDelete{i}-{j}"))
                            {
                                mapGroup.Maps.RemoveAt(j);
                                break;
                            }
                        }

                        if (ImGui.Button($"Add new map##AddNewMap{i}"))
                            mapGroup.Maps.Add(string.Empty);

                        if (ImGui.BeginPopupModal($"RenamePopup##{i}", ImGuiWindowFlags.AlwaysAutoResize))
                        {
                            ImGui.InputText("New Name", ref NewGroupName, 256);
                            if (ImGui.Button("OK"))
                            {
                                mapGroup.Name = NewGroupName;
                                ImGui.CloseCurrentPopup();
                            }
                            ImGui.SameLine();
                            if (ImGui.Button("Cancel"))
                            {
                                ImGui.CloseCurrentPopup();
                            }
                            ImGui.EndPopup();
                        }
                        ImGui.TreePop();
                    }
                }
                ImGui.TreePop();
            }
            #endregion
        }

        public override void DrawUI()
        {
            var inventoryPanel = InventoryPanel();

            var isGameHelperForeground = Process.GetCurrentProcess().MainWindowHandle == GetForegroundWindow();
            if (!Core.Process.Foreground && !isGameHelperForeground)
                return;

            var player = Core.States.InGameStateObject.CurrentAreaInstance.Player;
            if (!player.TryGetComponent<Render>(out _))
                return;

            var drawList = ImGui.GetBackgroundDrawList();

            drawList.ChannelsSplit(4);

            var atlasUi = Core.States.InGameStateObject.GameUi.Atlas;
            if (atlasUi.Address == IntPtr.Zero || !atlasUi.IsVisible)
                return;

            // The GameHelper Data Visualization entry GameUi.Atlas already resolves the atlas
            // node-list panel and materializes its children as UiElementBase instances. Use that
            // instead of opening a separate process handle just to walk UiElement ChildrensPtr.
            var atlasCount = atlasUi.TotalChildrens;

            if (atlasCount <= 0 || atlasCount > 10000)
                return;

            if (++cacheFrameCounter >= CacheRefreshFrames || cachedAtlasCount != atlasCount || nodeCache.Count == 0)
            {
                this.RefreshNodeCache(atlasUi, atlasCount);
                cacheFrameCounter = 0;
            }

            var panelTopLeft = atlasUi.Position;
            var panelSize = atlasUi.Size;
            var panelRect = new RectangleF(panelTopLeft.X, panelTopLeft.Y, panelSize.X, panelSize.Y);

            // Screen positions change per frame (panning), but the graph
            // topology is cached with the node cache (~3×/sec).
            var allCenters = new Dictionary<StdTuple2D<int>, Vector2>(nodeCache.Count);
            foreach (var nd in nodeCache)
            {
                var nu = atlasUi[nd.Index];
                if (nu == null) continue;
                allCenters[nd.GridPosition] = nu.Position + nu.Size * 0.5f;
            }

            var towers = new HashSet<string>(
                Settings.MapGroups
                    .Where(tower => string.Equals(tower.Name, "Towers", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(tower => tower.Maps)
                    .Select(NormalizeName),
                StringComparer.OrdinalIgnoreCase);
            var searchQuery = NormalizeName(Settings.SearchQuery);
            bool doSearch = !string.IsNullOrWhiteSpace(searchQuery);
            List<string> searchList = [];
            if (doSearch)
            {
                searchList = searchQuery
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(NormalizeName)
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
            }
            float resScale = ComputeDisplayScale(Settings.BaseWidth, Settings.BaseHeight);
            float uiScale = Math.Clamp(Settings.ScaleMultiplier * resScale, 0.5f, 4.0f);
            using (new FontScaleScope(uiScale))
            {
                if (!Settings.ControllerMode)
                    if (inventoryPanel)
                        return;

                // Off-screen labels/badges are culled (nothing to draw); a margin keeps
                // partially-visible labels alive. Lines below are drawn before this cull so
                // off-screen citadel/tower/search targets still get their line.
                var screenBounds = new RectangleF(0, 0, ImGui.GetIO().DisplaySize.X, ImGui.GetIO().DisplaySize.Y);
                screenBounds.Inflate(64f, 64f);
                var graphOffset = new Vector2(Settings.AtlasGraphOffsetX, Settings.AtlasGraphOffsetY) * uiScale;

                // Apply graph offset to routing centers so all lines share
                // the same coordinate space.
                var shiftedCenters = allCenters;
                if (graphOffset != Vector2.Zero)
                {
                    shiftedCenters = new Dictionary<StdTuple2D<int>, Vector2>(allCenters.Count);
                    foreach (var kv in allCenters)
                        shiftedCenters[kv.Key] = kv.Value + graphOffset;
                }

                if (Settings.ShowAtlasGraph)
                {
                    drawList.ChannelsSetCurrent(ChannelGrid);
                    float lineTh = MathF.Max(1f, uiScale * 2.5f);

                    static bool IsCanonical(StdTuple2D<int> a, StdTuple2D<int> b)
                    {
                        return (a.X < b.X) || (a.X == b.X && a.Y <= b.Y);
                    }

                    foreach (var nd in nodeCache)
                    {
                        if (!shiftedCenters.TryGetValue(nd.GridPosition, out var sa))
                            continue;

                        bool srcOnScreen = screenBounds.Contains(sa.X, sa.Y);

                        foreach (var dst in nd.ConnectedGridPositions)
                        {
                            if (!IsCanonical(nd.GridPosition, dst))
                                continue;

                            if (!shiftedCenters.TryGetValue(dst, out var da))
                                continue;

                            if (!srcOnScreen && !screenBounds.Contains(da.X, da.Y))
                                continue;

                            drawList.AddLine(sa, da, ImGuiHelper.Color(Settings.AtlasGraphLineColor), lineTh);
                        }
                    }
                }

                foreach (var nd in nodeCache)
                {
                    var mapName = nd.MapName;

                    if (string.IsNullOrWhiteSpace(mapName))
                        continue;
                    if (!IsPrintableUnicode(mapName))
                        continue;
                    if (doSearch && !searchList.Any(searchTerm => mapName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    bool completed = nd.State == AtlasNodeState.CompletedBase;
                    bool notAccessible = nd.State != AtlasNodeState.AccessibleNow && nd.State != AtlasNodeState.CompletedBase;

                    if (Settings.HideCompletedMaps && completed)
                        continue;
                    if (Settings.HideNotAccessibleMaps && notAccessible)
                        continue;

                    var nodeUi = atlasUi[nd.Index];
                    if (nodeUi == null)
                        continue;

                    var textSize = ImGui.CalcTextSize(mapName);
                    var nodeCenter = nodeUi.Position + nodeUi.Size * 0.5f;
                    Vector2 drawPosition = nodeCenter - textSize * 0.5f + Settings.AnchorNudge;

                    var padding = new Vector2(5, 2) * uiScale;
                    var bgPos = drawPosition - padding;
                    var bgSize = textSize + padding * 2;

                    // ── Routing ──────────────────────────────────────────────
                    // Determine if this node is a routing target.
                    bool routeTarget = false;
                    uint routeColor = 0;
                    int maxHops = 0;
                    if (Settings.DrawLinesToCitadel && mapName.EndsWith("Citadel", StringComparison.OrdinalIgnoreCase))
                        { routeTarget = true; routeColor = ImGuiHelper.Color(Settings.CitadelPathColor); maxHops = Settings.CitadelMaxHops; }
                    else if (Settings.DrawLinesToTowers && towers.Contains(mapName) && !completed)
                        { routeTarget = true; routeColor = ImGuiHelper.Color(Settings.TowerPathColor); maxHops = Settings.TowerMaxHops; }
                    else if (Settings.DrawLinesToSearch && doSearch
                        && searchList.Any(s => mapName.Contains(s, StringComparison.OrdinalIgnoreCase)))
                        { routeTarget = true; routeColor = ImGuiHelper.Color(Settings.SearchPathColor); maxHops = Settings.SearchMaxHops; }
                    else if (Settings.DrawLinesToUniqueMaps && !completed
                        && string.Equals(nd.Type, "unique", StringComparison.OrdinalIgnoreCase))
                        { routeTarget = true; routeColor = ImGuiHelper.Color(Settings.UniquePathColor); maxHops = Settings.UniqueMaxHops; }
                    else if (Settings.DrawLinesToLineageMaps && !completed
                        && nd.Tags.Exists(t => string.Equals(t, "lineage", StringComparison.OrdinalIgnoreCase)))
                        { routeTarget = true; routeColor = ImGuiHelper.Color(Settings.LineagePathColor); maxHops = Settings.LineageMaxHops; }
                    else if (Settings.DrawLinesToArbiterMaps && !completed
                        && nd.Tags.Exists(t => string.Equals(t, "arbiter", StringComparison.OrdinalIgnoreCase)))
                        { routeTarget = true; routeColor = ImGuiHelper.Color(Settings.ArbiterPathColor); maxHops = Settings.ArbiterMaxHops; }

                    if (routeTarget)
                    {
                        float thickness = MathF.Max(1f, uiScale * Settings.PathLineThickness);
                        var path = PathFromAccessible(nd.GridPosition, cachedBfsTree, cachedAccessible);
                        int hops = path?.Count > 0 ? path.Count - 1 : int.MaxValue;

                        if (path != null && path.Count > 0 && hops <= maxHops)
                        {
                            // Full node-path from accessible frontier to target.
                            DrawNodePath(drawList, path, shiftedCenters, routeColor, thickness, screenBounds);

                            // Green dot on the accessible entry.
                            if (shiftedCenters.TryGetValue(path[0], out var entryC))
                            {
                                drawList.ChannelsSetCurrent(ChannelDots);
                                float sr = MathF.Max(3f, thickness * 1.3f);
                                drawList.AddCircleFilled(entryC, sr, ImGuiHelper.Color(new Vector4(0.2f, 1f, 0.2f, 1f)));
                                drawList.AddCircle(entryC, sr, DotOutlineColor, 0, MathF.Max(1f, sr * 0.35f));
                            }

                            // Hop count above the target.
                            drawList.ChannelsSetCurrent(ChannelLabels);
                            string ht = hops.ToString();
                            var hts = ImGui.CalcTextSize(ht);
                            var hp = new Vector2(nodeCenter.X - hts.X * 0.5f, nodeCenter.Y - (nodeUi.Size.Y * 0.5f) - hts.Y - 2f * uiScale);
                            var hpad = new Vector2(4, 1) * uiScale;
                            drawList.AddRectFilled(hp - hpad, hp + hts + hpad, ImGuiHelper.Color(new Vector4(0, 0, 0, 0.75f)), 3f * uiScale);
                            drawList.AddText(hp, ImGuiHelper.Color(new Vector4(1f, 0.9f, 0.2f, 1f)), ht);
                        }
                        }

                    if (!screenBounds.IntersectsWith(new RectangleF(bgPos.X, bgPos.Y, bgSize.X, bgSize.Y)))
                        continue;

                    var group = Settings.MapGroups.Find(g => g.Maps.Exists(
                        m => NormalizeName(m).Equals(mapName, StringComparison.OrdinalIgnoreCase)));

                    var backgroundColor = group?.BackgroundColor ?? Settings.DefaultBackgroundColor;
                    var fontColor = group?.FontColor ?? Settings.DefaultFontColor;
                    if (completed)
                        backgroundColor.W *= 0.4f;

                    drawList.ChannelsSetCurrent(ChannelLabels);
                    float rounding = 3f * uiScale;

                    if (Settings.ShowBiomeBorder && Biomes.TryGetValue(nd.BiomeId, out var biome) && biome.Show)
                    {
                        var biomeColor = biome.BdColor;
                        if (completed)
                            biomeColor.W *= 0.4f;

                        float bBorderTh = MathF.Max(1f, uiScale * Settings.BiomeBorderThickness);
                        var half = bBorderTh * 0.5f;
                        var outMin = bgPos - new Vector2(half, half);
                        var outMax = (bgPos + bgSize) + new Vector2(half, half);
                        var outRounding = MathF.Max(0f, rounding + half);

                        drawList.AddRect(outMin, outMax, ImGuiHelper.Color(biomeColor),
                            outRounding, ImDrawFlags.RoundCornersAll, bBorderTh);
                    }

                    drawList.AddRectFilled(bgPos, bgPos + bgSize, ImGuiHelper.Color(backgroundColor), rounding);
                    drawList.AddText(drawPosition, ImGuiHelper.Color(fontColor), mapName);

                    float labelCenterX = drawPosition.X + textSize.X * 0.5f;
                    float nextRowTopY = drawPosition.Y + textSize.Y + (4f * uiScale);
                    float rowGap = 4f * uiScale;

                    CategorizeContents(nd.RawContents, MapTags, MapPlain, out var flags, out var contents);

                    if (Settings.ShowMapBadges)
                        DrawSquares(drawList, flags, labelCenterX, ref nextRowTopY, rowGap, uiScale);

                    DrawSquares(drawList, contents, labelCenterX, ref nextRowTopY, rowGap, uiScale);

                    if (Settings.ShowMapCounts)
                    {
                        var countText = $"Links: {nd.ConnectedGridPositions.Count}  Badges: {nd.BadgeCount}";
                        var countTextSize = ImGui.CalcTextSize(countText);
                        var countPos = new Vector2(labelCenterX - countTextSize.X * 0.5f, nextRowTopY);
                        drawList.AddText(countPos, ImGuiHelper.Color(fontColor), countText);
                    }
                }

                drawList.ChannelsMerge();
            }
        }

        // Rebuild the per-node static-data cache (map id / biome / state / content names). This is
        // the expensive pass (pointer chains + wide-string reads per node), so it runs only on an
        // interval — not every frame. Positions are NOT cached here; they're read live each frame.
        private void RefreshNodeCache(UiElementBase atlasUi, int atlasCount)
        {
            nodeCache.Clear();
            foreach (var map in Core.States.InGameStateObject.GameUi.AtlasMaps)
            {
                if (map.Index < 0 || map.Index >= atlasCount)
                    continue;

                nodeCache.Add(new NodeData
                {
                    Index = map.Index,
                    Address = map.Address,
                    GridPosition = map.GridPosition,
                    ConnectedGridPositions = map.ConnectedGridPositions.ToList(),
                    MapName = NormalizeName(map.DisplayName),
                    BiomeId = map.BiomeId,
                    State = ToAtlasNodeState(map.State),
                    BadgeCount = map.BadgeCount,
                    RawContents = map.ContentNames.ToList(),
                    Type = map.Type ?? "normal",
                    Tags = map.Tags.ToList(),
                });
            }
            cachedAtlasCount = atlasCount;

            // Rebuild the routing graph + BFS tree. The node topology
            // doesn't change while the atlas is open, so this runs at
            // the same cadence as the node cache (~3×/sec at 60 fps).
            cachedRouteGraph = new Dictionary<StdTuple2D<int>, List<StdTuple2D<int>>>();
            cachedAccessible = new HashSet<StdTuple2D<int>>();
            foreach (var nd in nodeCache)
            {
                cachedRouteGraph[nd.GridPosition] = nd.ConnectedGridPositions;
                if (nd.State == AtlasNodeState.AccessibleNow)
                    cachedAccessible.Add(nd.GridPosition);
            }
            cachedBfsTree = MultiSourceBfs(cachedRouteGraph, cachedAccessible, new HashSet<StdTuple2D<int>>());
        }

        private static AtlasNodeState ToAtlasNodeState(AtlasMapNodeState state)
        {
            return state switch
            {
                AtlasMapNodeState.CompletedBase => AtlasNodeState.CompletedBase,
                AtlasMapNodeState.AccessibleNow => AtlasNodeState.AccessibleNow,
                _ => AtlasNodeState.None,
            };
        }

        #region Routing helpers

        // Multi-source BFS from all accessible nodes over the undirected
        // graph, skipping blocked (failed) nodes. Returns a cameFrom tree
        // pointing toward the nearest source — reconstruct paths with
        // PathFromAccessible.
        private static Dictionary<StdTuple2D<int>, StdTuple2D<int>> MultiSourceBfs(
            Dictionary<StdTuple2D<int>, List<StdTuple2D<int>>> graph,
            HashSet<StdTuple2D<int>> sources,
            HashSet<StdTuple2D<int>> blocked)
        {
            var cameFrom = new Dictionary<StdTuple2D<int>, StdTuple2D<int>>();
            var visited = new HashSet<StdTuple2D<int>>();
            var queue = new Queue<StdTuple2D<int>>();

            foreach (var s in sources)
                if (graph.ContainsKey(s) && !blocked.Contains(s) && visited.Add(s))
                    queue.Enqueue(s);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (!graph.TryGetValue(cur, out var neighbors))
                    continue;
                foreach (var nb in neighbors)
                {
                    if (blocked.Contains(nb) || !visited.Add(nb))
                        continue;
                    cameFrom[nb] = cur;
                    queue.Enqueue(nb);
                }
            }

            return cameFrom;
        }

        // Reconstruct the shortest-hop path from any accessible source to
        // target, or null if unreachable.
        private static List<StdTuple2D<int>> PathFromAccessible(
            StdTuple2D<int> target,
            Dictionary<StdTuple2D<int>, StdTuple2D<int>> cameFrom,
            HashSet<StdTuple2D<int>> sources)
        {
            if (sources.Contains(target))
                return new List<StdTuple2D<int>> { target };
            if (!cameFrom.ContainsKey(target))
                return null;

            var path = new List<StdTuple2D<int>> { target };
            var cur = target;
            while (cameFrom.TryGetValue(cur, out var prev))
            {
                cur = prev;
                path.Add(cur);
            }
            path.Reverse();
            return path;
        }

        // Draw consecutive node centers with dots at each hop.
        private static void DrawNodePath(
            ImDrawListPtr drawList,
            List<StdTuple2D<int>> path,
            Dictionary<StdTuple2D<int>, Vector2> centers,
            uint color,
            float thickness,
            RectangleF screenBounds)
        {
            drawList.ChannelsSetCurrent(ChannelLines);
            Vector2? prev = null;
            foreach (var g in path)
            {
                if (!centers.TryGetValue(g, out var c))
                    { prev = null; continue; }
                if (prev.HasValue)
                {
                    // Draw segment only when at least one endpoint is on screen.
                    if (screenBounds.Contains(prev.Value.X, prev.Value.Y)
                        || screenBounds.Contains(c.X, c.Y))
                    {
                        drawList.AddLine(prev.Value, c, color, thickness);
                    }
                }
                prev = c;
            }

            // Dots only for on-screen nodes.
            drawList.ChannelsSetCurrent(ChannelDots);
            foreach (var g in path)
            {
                if (centers.TryGetValue(g, out var c) && screenBounds.Contains(c.X, c.Y))
                    drawList.AddCircleFilled(c, thickness * 0.9f, color);
            }
        }

#endregion

        private void LoadBiomeMap()
        {
            var path = Path.Join(DllDirectory, "json", "biome.json");
            if (!File.Exists(path))
                return;

            var json = File.ReadAllText(path);
            var contents = JsonConvert.DeserializeObject<Dictionary<string, BiomeInfo>>(json);

            Biomes.Clear();

            if (contents is null)
                return;

            foreach (var content in contents)
            {
                if (byte.TryParse(content.Key, out var id))
                    Biomes[id] = content.Value;
            }

            ApplyBiomeOverrides();
        }

        private void LoadContentMap()
        {
            var path = Path.Join(DllDirectory, "json", "content.json");
            if (!File.Exists(path))
                return;

            var json = File.ReadAllText(path);
            var contents = JsonConvert.DeserializeObject<Dictionary<string, ContentInfo>>(json);

            MapTags.Clear();
            MapPlain.Clear();

            if (contents is null)
                return;

            foreach (var content in contents)
            {
                if (content.Key.All(char.IsLetter))
                    MapTags[content.Key] = content.Value;
                else
                    MapPlain[content.Key] = content.Value;
            }

            ApplyContentOverrides();
        }

        private static float ComputeDisplayScale(float refW, float refH)
        {
            var io = ImGui.GetIO();
            var sx = io.DisplaySize.X / MathF.Max(1f, refW);
            var sy = io.DisplaySize.Y / MathF.Max(1f, refH);
            return MathF.Min(sx, sy);
        }

        private static void DrawSquares(ImDrawListPtr drawList, List<ContentInfo> infos, float centerX,
            ref float nextRowTopY, float rowGap, float uiScale)
        {
            if (infos.Count == 0)
                return;

            const float fixedHeightBase = 18f;
            const float paddingBase = 6f;
            float fixedHeight = fixedHeightBase * uiScale;
            float padding = paddingBase * uiScale;

            var widths = new List<float>(infos.Count);
            float totalW = 0f;

            foreach (var info in infos)
            {
                var abbrev = string.IsNullOrWhiteSpace(info.Abbrev) ? info.Label[..1] : info.Abbrev;
                var textSize = ImGui.CalcTextSize(abbrev);
                float w = MathF.Max(fixedHeight, textSize.X + padding);
                widths.Add(w);
                totalW += w;
            }

            var basePos = new Vector2(centerX - totalW * 0.5f, nextRowTopY);

            for (int i = 0; i < infos.Count; i++)
            {
                var info = infos[i];
                string abbrev;
                if (string.IsNullOrWhiteSpace(info.Abbrev))
                    abbrev = !string.IsNullOrEmpty(info.Label) ? info.Label.Substring(0, 1) : "?";
                else
                    abbrev = info.Abbrev;
                var boxSize = new Vector2(widths[i], fixedHeight);
                var squareMin = basePos;
                var squareMax = squareMin + boxSize;

                drawList.AddRectFilled(squareMin, squareMax, ImGuiHelper.Color(info.BgColor));

                var textSize = ImGui.CalcTextSize(abbrev);
                var textPos = squareMin + (boxSize - textSize) * 0.5f;
                drawList.AddText(textPos, ImGuiHelper.Color(info.FtColor), abbrev);

                basePos.X += boxSize.X;
            }

            nextRowTopY += fixedHeight + rowGap;
        }

        private readonly struct FontScaleScope : IDisposable
        {
            private readonly ImFontPtr _font;
            private readonly float _prevScale;
            public FontScaleScope(float scale)
            {
                _font = ImGui.GetFont();
                _prevScale = _font.Scale;
                _font.Scale = _prevScale * scale;
                ImGui.PushFont(_font);
            }
            public void Dispose()
            {
                ImGui.PopFont();
                _font.Scale = _prevScale;
            }
        }

        private void MoveMapGroup(int index, int direction)
        {
            if (index < 0 || index >= Settings.MapGroups.Count)
                return;

            int to = index + direction;
            if (to < 0 || to >= Settings.MapGroups.Count)
                return;

            var item = Settings.MapGroups[index];
            Settings.MapGroups.RemoveAt(index);
            Settings.MapGroups.Insert(to, item);
        }

        private void DeleteMapGroup(int index)
        {
            if (index < 0 || index >= Settings.MapGroups.Count)
                return;

            Settings.MapGroups.RemoveAt(index);
        }

        private static void ColorSwatch(string label, ref Vector4 color)
        {
            if (ImGui.ColorButton(label, color))
                ImGui.OpenPopup(label);

            if (ImGui.BeginPopup(label))
            {
                ImGui.ColorPicker4(label, ref color);
                ImGui.EndPopup();
            }
        }

        private static bool TriangleButton(string id, float buttonSize, Vector4 color, bool isUp)
        {
            var pressed = ImGui.Button(id, new Vector2(buttonSize, buttonSize));
            var drawList = ImGui.GetWindowDrawList();
            var pos = ImGui.GetItemRectMin();
            var triSize = buttonSize * 0.5f;
            var center = new Vector2(pos.X + buttonSize * 0.5f, pos.Y + buttonSize * 0.5f);

            Vector2 p1, p2, p3;
            if (isUp)
            {
                p1 = new Vector2(center.X, center.Y - triSize * 0.5f);
                p2 = new Vector2(center.X - triSize * 0.5f, center.Y + triSize * 0.5f);
                p3 = new Vector2(center.X + triSize * 0.5f, center.Y + triSize * 0.5f);
            }
            else
            {
                p1 = new Vector2(center.X - triSize * 0.5f, center.Y - triSize * 0.5f);
                p2 = new Vector2(center.X + triSize * 0.5f, center.Y - triSize * 0.5f);
                p3 = new Vector2(center.X, center.Y + triSize * 0.5f);
            }

            drawList.AddTriangleFilled(p1, p2, p3, ImGuiHelper.Color(color));

            return pressed;
        }

        static bool IsPrintableUnicode(string str)
        {
            if (string.IsNullOrEmpty(str))
                return false;

            if (str.All(ch => ch == '?' || char.IsWhiteSpace(ch)))
                return false;

            foreach (var rune in str.EnumerateRunes())
            {
                if (rune.Value == 0xFFFD)
                    return false;

                var cat = Rune.GetUnicodeCategory(rune);
                switch (cat)
                {
                    case UnicodeCategory.Control:
                    case UnicodeCategory.Format:
                    case UnicodeCategory.Surrogate:
                    case UnicodeCategory.PrivateUse:
                    case UnicodeCategory.OtherNotAssigned:
                        return false;
                }
            }

            return true;
        }

        private static string NormalizeName(string s) =>
            string.IsNullOrWhiteSpace(s)
                ? s
                : CollapseWhitespace(s.Replace('\u00A0', ' ').Trim());

        private static string CollapseWhitespace(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;

            var sb = new StringBuilder(s.Length);
            bool prevSpace = false;
            foreach (var ch in s)
            {
                bool isSpace = char.IsWhiteSpace(ch);
                if (isSpace)
                {
                    if (!prevSpace) sb.Append(' ');
                }
                else
                {
                    sb.Append(ch);
                }
                prevSpace = isSpace;
            }

            return sb.ToString();
        }

        private static bool InventoryPanel()
        {
            return Core.States.InGameStateObject.GameUi.RightPanel.IsVisible;
        }

        private static void CategorizeContents(IEnumerable<string> raws,
            Dictionary<string, ContentInfo> tagMap,
            Dictionary<string, ContentInfo> plainMap,
            out List<ContentInfo> flags,
            out List<ContentInfo> contents)
        {
            flags = [];
            contents = [];
            foreach (var raw in raws)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var info = MatchContent(NormalizeName(raw), tagMap, plainMap);
                if (info is null || !info.Show)
                    continue;

                if (info.IsFlag) flags.Add(info);
                else contents.Add(info);
            }
        }

        private static ContentInfo MatchContent(string contentName,
            Dictionary<string, ContentInfo> tagMap,
            Dictionary<string, ContentInfo> plainMap)
        {
            if (string.IsNullOrWhiteSpace(contentName))
                return null;

            var normalized = contentName.Replace("\u00A0", " ").Trim();

            int lb = normalized.IndexOf('[');
            int rb = lb >= 0 ? normalized.IndexOf(']', lb + 1) : -1;
            if (lb >= 0 && rb > lb + 1)
            {
                var inside = normalized.Substring(lb + 1, rb - lb - 1);
                var pipe = inside.IndexOf('|');
                var tag = (pipe >= 0 ? inside[..pipe] : inside).Trim();

                if (tagMap.TryGetValue(tag, out var tagInfo))
                    return tagInfo;

                if (plainMap.TryGetValue(tag, out var tagAsPlain))
                    return tagAsPlain;
            }

            foreach (var map in plainMap)
            {
                if (normalized.Contains(map.Key, StringComparison.OrdinalIgnoreCase))
                    return map.Value;
            }

            foreach (var tag in tagMap)
            {
                if (normalized.Contains(tag.Key, StringComparison.OrdinalIgnoreCase))
                    return tag.Value;
            }

            return null;
        }

        private void ApplyBiomeOverrides()
        {
            foreach (var entry in Settings.BiomeOverrides)
            {
                if (Biomes.TryGetValue(entry.Key, out var info))
                {
                    var ov = entry.Value;
                    if (ov.BorderColor.HasValue)
                        info.BorderColor = [ov.BorderColor.Value.X, ov.BorderColor.Value.Y, ov.BorderColor.Value.Z, ov.BorderColor.Value.W];

                    if (ov.Show.HasValue)
                        info.Show = ov.Show.Value;
                }
            }
        }

        private void ApplyContentOverrides()
        {
            foreach (var entry in Settings.ContentOverrides)
            {
                if (MapTags.TryGetValue(entry.Key, out var info) ||
                    MapPlain.TryGetValue(entry.Key, out info))
                {
                    var ov = entry.Value;
                    if (ov.BackgroundColor.HasValue)
                        info.BackgroundColor = [ov.BackgroundColor.Value.X, ov.BackgroundColor.Value.Y, ov.BackgroundColor.Value.Z, ov.BackgroundColor.Value.W];

                    if (ov.FontColor.HasValue)
                        info.FontColor = [ov.FontColor.Value.X, ov.FontColor.Value.Y, ov.FontColor.Value.Z, ov.FontColor.Value.W];

                    if (ov.Show.HasValue)
                        info.Show = ov.Show.Value;

                    if (!string.IsNullOrEmpty(ov.Abbrev))
                        info.Abbrev = ov.Abbrev;
                }
            }
        }

        private static bool ColorsEqual(Vector4 a, Vector4 b, float eps = 0.001f)
        {
            return Math.Abs(a.X - b.X) < eps &&
                   Math.Abs(a.Y - b.Y) < eps &&
                   Math.Abs(a.Z - b.Z) < eps &&
                   Math.Abs(a.W - b.W) < eps;
        }

        private static void PathRow(string label, ref bool enabled, ref Vector4 color, ref int maxHops)
        {
            ImGui.Checkbox($"##{label}Enabled", ref enabled);
            ImGui.SameLine();
            ColorSwatch($"##{label}Color", ref color);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80);
            ImGui.SliderInt($"##{label}Hops", ref maxHops, 1, 200);
            ImGuiHelper.ToolTip("Maximum path length in maps to clear.");
            ImGui.SameLine();
            ImGui.Text(label);
        }

        [DllImport("user32.dll")]
        private static extern nint GetForegroundWindow();

    }
}