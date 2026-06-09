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
        private const uint CitadelLineColor = 0xFF0000FF;
        private const uint TowerLineColor = 0xFFC6C10D;
        private const uint SearchLineColor = 0xFFFFFFFF;
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
        private static readonly Dictionary<string, int> MapScores = new(StringComparer.OrdinalIgnoreCase);

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
        }
        private readonly List<NodeData> nodeCache = new();
        private int cacheFrameCounter = int.MaxValue;   // force refresh on first frame
        private int cachedAtlasCount = -1;
        private const int CacheRefreshFrames = 20;       // rebuild static data ~3×/sec at 60fps

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
            LoadMapScores();
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
            if (ImGui.TreeNode("Draw Lines Settings"))
            {
                ImGui.Checkbox("Route Lines Through Nodes (Shortest Path)", ref Settings.RouteLinesThroughNodes);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(150);
                ImGui.SliderFloat("Path Thickness", ref Settings.PathLineThickness, 1.0f, 8.0f);
                ImGui.Checkbox("Draw Lines to Search in range", ref Settings.DrawLinesSearchQuery);
                ImGui.SameLine();
                ImGui.SliderFloat("##DrawSearchInRange", ref Settings.DrawSearchInRange, 1.0f, 10.0f);
                ImGui.Checkbox("Draw Lines to Citadels", ref Settings.DrawLinesToCitadel);
                ImGui.Checkbox("Draw Lines to Towers in range", ref Settings.DrawLinesToTowers);
                ImGui.SameLine();
                ImGui.SliderFloat("##DrawTowersInRange", ref Settings.DrawTowersInRange, 1.0f, 10.0f);
                ImGui.TreePop();
            }

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
            if (!player.TryGetComponent<Render>(out var playerRender))
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

            Dictionary<StdTuple2D<int>, Vector2> nodeCenters = null;
            Dictionary<StdTuple2D<int>, List<StdTuple2D<int>>> graph = null;
            Dictionary<StdTuple2D<int>, bool> nodeCompleted = null;
            Dictionary<StdTuple2D<int>, bool> nodeAccessible = null;

            if (Settings.RouteLinesThroughNodes || Settings.DrawGrid)
            {
                BuildAtlasGraph(atlasUi, nodeCache, panelRect,
                    out nodeCenters, out graph, out nodeCompleted, out nodeAccessible);
            }

            var towers = new HashSet<string>(
                Settings.MapGroups
                    .Where(tower => string.Equals(tower.Name, "Towers", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(tower => tower.Maps)
                    .Select(NormalizeName),
                StringComparer.OrdinalIgnoreCase);
            var boundsTowers = CalculateBounds(Settings.DrawTowersInRange);

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
            var boundsSearch = CalculateBounds(Settings.DrawSearchInRange);

            var playerLocation = Core.States.InGameStateObject.CurrentWorldInstance.WorldToScreen(playerRender.WorldPosition);

            float resScale = ComputeDisplayScale(Settings.BaseWidth, Settings.BaseHeight);
            float uiScale = Math.Clamp(Settings.ScaleMultiplier * resScale, 0.5f, 4.0f);
            using (new FontScaleScope(uiScale))
            {
                if (!Settings.ControllerMode)
                    if (inventoryPanel)
                        return;

                if (Settings.DrawGrid && graph != null && nodeCenters != null)
                {
                    drawList.ChannelsSetCurrent(ChannelGrid);
                    float lineTh = MathF.Max(1f, uiScale * 2.5f);

                    static (int x, int y) XY(StdTuple2D<int> t) => (t.X, t.Y);
                    static bool IsCanonical(StdTuple2D<int> a, StdTuple2D<int> b)
                    {
                        var (ax, ay) = XY(a);
                        var (bx, by) = XY(b);
                        return (ax < bx) || (ax == bx && ay <= by);
                    }

                    foreach (var (src, targets) in graph)
                    {
                        if (!nodeCenters.TryGetValue(src, out var a))
                            continue;

                        foreach (var dst in targets)
                        {
                            if (!IsCanonical(src, dst))
                                continue;

                            if (!nodeCenters.TryGetValue(dst, out var b))
                                continue;

                            if (Settings.GridSkipCompleted &&
                                ((nodeCompleted?.TryGetValue(src, out var srcCompleted) == true && srcCompleted) ||
                                 (nodeCompleted?.TryGetValue(dst, out var dstCompleted) == true && dstCompleted)))
                            {
                                continue;
                            }

                            drawList.AddLine(a, b, ImGuiHelper.Color(Settings.GridLineColor), lineTh);
                        }
                    }
                }

                // Off-screen labels/badges are culled (nothing to draw); a margin keeps
                // partially-visible labels alive. Lines below are drawn before this cull so
                // off-screen citadel/tower/search targets still get their line.
                var screenBounds = new RectangleF(0, 0, ImGui.GetIO().DisplaySize.X, ImGui.GetIO().DisplaySize.Y);
                screenBounds.Inflate(64f, 64f);

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
                    var rectCenter = (bgPos + (bgPos + bgSize)) * 0.5f;

                    // Lines to citadels / towers / search hits — drawn even when the target is
                    // off-screen, so this happens before the visibility cull.
                    bool shouldDrawCitadel = Settings.DrawLinesToCitadel && mapName.EndsWith("Citadel", StringComparison.OrdinalIgnoreCase);
                    bool shouldDrawTower = Settings.DrawLinesToTowers && towers.Contains(mapName) && !completed && boundsTowers.Contains(new PointF(drawPosition.X, drawPosition.Y));
                    bool shouldDrawSearch = Settings.DrawLinesSearchQuery && doSearch
                        && searchList.Any(searchTerm => mapName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                        && boundsSearch.Contains(new PointF(drawPosition.X, drawPosition.Y));
                    if (shouldDrawCitadel || shouldDrawTower || shouldDrawSearch)
                    {
                        uint lineColor = shouldDrawCitadel ? CitadelLineColor : shouldDrawTower ? TowerLineColor : SearchLineColor;
                        float thickness = MathF.Max(1f, uiScale * Settings.PathLineThickness);
                        var drewRoute = false;

                        if (Settings.RouteLinesThroughNodes &&
                            graph != null &&
                            nodeCenters != null &&
                            nodeCompleted != null &&
                            nodeAccessible != null &&
                            nodeCenters.ContainsKey(nd.GridPosition) &&
                            TryGetNearestNode(playerLocation, nodeCenters, out var startNode))
                        {
                            var path = FindShortestPathAStar(startNode, nd.GridPosition, graph, nodeCenters);
                            if (path != null && path.Count > 0)
                            {
                                DrawPath(
                                    drawList,
                                    playerLocation,
                                    bgPos,
                                    bgSize,
                                    path,
                                    nodeCenters,
                                    nodeCompleted,
                                    nodeAccessible,
                                    lineColor,
                                    thickness,
                                    ChannelLines,
                                    ChannelDots);
                                drewRoute = true;
                            }
                        }

                        if (!drewRoute)
                        {
                            var intersectionPoint = GetLineRectangleIntersection(playerLocation, rectCenter, bgPos, bgPos + bgSize);

                            drawList.ChannelsSetCurrent(ChannelLines);
                            drawList.AddLine(playerLocation, intersectionPoint, lineColor, thickness);
                            var endDot = OffsetPointOutsideRect(intersectionPoint, rectCenter, thickness * 0.6f);
                            drawList.ChannelsSetCurrent(ChannelDots);
                            drawList.AddCircleFilled(endDot, thickness, lineColor);
                            drawList.AddCircle(endDot, thickness, DotOutlineColor, 0, MathF.Max(1f, thickness * 0.35f));
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

                    if (MapScores.TryGetValue(mapName, out var score))
                    {
                        var scoreStr = score.ToString();
                        var scoreTextSize = ImGui.CalcTextSize(scoreStr);
                        var scoreBgSize = scoreTextSize + padding * 2;
                        
                        var scoreBgPos = new Vector2(bgPos.X + bgSize.X + (2f * uiScale), bgPos.Y);
                        
                        float t = Math.Clamp(score / 6.0f, 0f, 1f);
                        float r = t < 0.5f ? 2f * t : 1f;
                        float g = t < 0.5f ? 1f : 1f - 2f * (t - 0.5f);
                        Vector4 scoreBgColor = new Vector4(r, g, 0f, 0.9f);
                        
                        if (completed)
                            scoreBgColor.W *= 0.4f;

                        drawList.AddRectFilled(scoreBgPos, scoreBgPos + scoreBgSize, ImGuiHelper.Color(scoreBgColor), rounding);
                        var scoreDrawPos = scoreBgPos + padding;
                        drawList.AddText(scoreDrawPos, ImGuiHelper.Color(new Vector4(0, 0, 0, 1)), scoreStr);
                    }

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
                });
            }
            cachedAtlasCount = atlasCount;
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

        private static void BuildAtlasGraph(
            UiElementBase atlasUi,
            IReadOnlyList<NodeData> nodes,
            RectangleF panelRect,
            out Dictionary<StdTuple2D<int>, Vector2> centers,
            out Dictionary<StdTuple2D<int>, List<StdTuple2D<int>>> graph,
            out Dictionary<StdTuple2D<int>, bool> completed,
            out Dictionary<StdTuple2D<int>, bool> accessible)
        {
            centers = new Dictionary<StdTuple2D<int>, Vector2>(nodes.Count);
            completed = new Dictionary<StdTuple2D<int>, bool>(nodes.Count);
            graph = new Dictionary<StdTuple2D<int>, List<StdTuple2D<int>>>(nodes.Count);
            accessible = new Dictionary<StdTuple2D<int>, bool>(nodes.Count);

            foreach (var node in nodes)
            {
                var nodeUi = atlasUi[node.Index];
                if (nodeUi == null)
                    continue;

                var nodeCenter = nodeUi.Position + nodeUi.Size * 0.5f;
                if (!panelRect.Contains(nodeCenter.X, nodeCenter.Y))
                    continue;

                centers[node.GridPosition] = nodeCenter;
                completed[node.GridPosition] = node.State == AtlasNodeState.CompletedBase;
                accessible[node.GridPosition] = node.State == AtlasNodeState.AccessibleNow ||
                                                node.State == AtlasNodeState.CompletedBase;

                if (!graph.ContainsKey(node.GridPosition))
                {
                    graph[node.GridPosition] = new List<StdTuple2D<int>>(node.ConnectedGridPositions.Count);
                }
            }

            foreach (var node in nodes)
            {
                if (!centers.ContainsKey(node.GridPosition))
                    continue;

                foreach (var connected in node.ConnectedGridPositions)
                {
                    if (!centers.ContainsKey(connected) || connected.Equals(node.GridPosition))
                        continue;

                    AddGraphConnection(graph, node.GridPosition, connected);
                }
            }
        }

        private static void AddGraphConnection(
            Dictionary<StdTuple2D<int>, List<StdTuple2D<int>>> graph,
            StdTuple2D<int> source,
            StdTuple2D<int> target)
        {
            if (!graph.TryGetValue(source, out var sourceConnections))
            {
                sourceConnections = new List<StdTuple2D<int>>(4);
                graph[source] = sourceConnections;
            }

            if (!sourceConnections.Contains(target))
            {
                sourceConnections.Add(target);
            }

            if (!graph.TryGetValue(target, out var targetConnections))
            {
                targetConnections = new List<StdTuple2D<int>>(4);
                graph[target] = targetConnections;
            }

            if (!targetConnections.Contains(source))
            {
                targetConnections.Add(source);
            }
        }

        private static bool TryGetNearestNode(Vector2 point, Dictionary<StdTuple2D<int>, Vector2> centers, out StdTuple2D<int> nearest)
        {
            nearest = default;
            float best = float.MaxValue;
            bool found = false;
            foreach (var kv in centers)
            {
                float d = Vector2.DistanceSquared(point, kv.Value);
                if (d < best)
                {
                    best = d;
                    nearest = kv.Key;
                    found = true;
                }
            }
            return found;
        }

        private static float Heuristic(StdTuple2D<int> a, StdTuple2D<int> b, Dictionary<StdTuple2D<int>, Vector2> centers)
        {
            var pa = centers[a];
            var pb = centers[b];
            return Vector2.Distance(pa, pb);
        }

        private static List<StdTuple2D<int>> FindShortestPathAStar(
            StdTuple2D<int> start,
            StdTuple2D<int> goal,
            Dictionary<StdTuple2D<int>, List<StdTuple2D<int>>> graph,
            Dictionary<StdTuple2D<int>, Vector2> centers)
        {
            if (!graph.ContainsKey(start) || !graph.ContainsKey(goal))
                return null;

            var cameFrom = new Dictionary<StdTuple2D<int>, StdTuple2D<int>>();
            var gScore = new Dictionary<StdTuple2D<int>, float> { [start] = 0f };

            var open = new PriorityQueue<StdTuple2D<int>, float>();
            var f0 = Heuristic(start, goal, centers);
            open.Enqueue(start, f0);

            var inOpen = new HashSet<StdTuple2D<int>> { start };

            while (open.Count > 0)
            {
                var current = open.Dequeue();
                inOpen.Remove(current);

                if (current.Equals(goal))
                    return ReconstructPath(cameFrom, current);

                if (!graph.TryGetValue(current, out var neighbors))
                    continue;

                foreach (var nb in neighbors)
                {
                    float tentative = gScore[current] + Vector2.Distance(centers[current], centers[nb]);

                    if (!gScore.TryGetValue(nb, out var old) || tentative < old)
                    {
                        cameFrom[nb] = current;
                        gScore[nb] = tentative;
                        float f = tentative + Heuristic(nb, goal, centers);
                        if (!inOpen.Contains(nb))
                        {
                            open.Enqueue(nb, f);
                            inOpen.Add(nb);
                        }
                    }
                }
            }

            return null;
        }

        private static List<StdTuple2D<int>> ReconstructPath(Dictionary<StdTuple2D<int>, StdTuple2D<int>> cameFrom, StdTuple2D<int> current)
        {
            var path = new List<StdTuple2D<int>> { current };
            while (cameFrom.TryGetValue(current, out var prev))
            {
                current = prev;
                path.Add(current);
            }
            path.Reverse();
            return path;
        }

        private static void DrawPath(
            ImDrawListPtr drawList,
            Vector2 playerLocation,
            Vector2 labelBgPos,
            Vector2 labelBgSize,
            List<StdTuple2D<int>> path,
            Dictionary<StdTuple2D<int>, Vector2> centers,
            Dictionary<StdTuple2D<int>, bool> completedMap,
            Dictionary<StdTuple2D<int>, bool> accessibleMap,
            uint color,
            float thickness,
            int lineChannel,
            int dotChannel)
        {
            if (path == null || path.Count == 0)
                return;

            var segments = new List<(Vector2 A, Vector2 B, uint Col)>(path.Count + 1);
            var dots = new List<(Vector2 P, uint Col, float R)>(path.Count + 2);

            var first = centers[path[0]];
            segments.Add((playerLocation, first, color));

            if (completedMap != null && completedMap.TryGetValue(path[0], out var firstCompleted) && firstCompleted)
                dots.Add((first, CompletedNodeDotColor, thickness));

            for (int i = 1; i<path.Count; i++)
            {
                var a = centers[path[i - 1]];
                var b = centers[path[i]];
                bool aC = completedMap != null && completedMap.TryGetValue(path[i - 1], out var ac) && ac;
                bool bC = completedMap != null && completedMap.TryGetValue(path[i], out var bc) && bc;
                bool aA = accessibleMap != null && accessibleMap.TryGetValue(path[i - 1], out var aa) && aa;
                bool bA = accessibleMap != null && accessibleMap.TryGetValue(path[i], out var ba) && ba;
                
                uint segColor = ((aC && bC) || (aA && bA)) ? CompletedNodeDotColor : color;
                segments.Add((a, b, segColor));

                var atNode = path[i];
                if (completedMap != null && completedMap.TryGetValue(atNode, out var isCompleted) && isCompleted)
                    dots.Add((b, CompletedNodeDotColor, thickness));
            }

            var last = centers[path[^1]];
            var rectCenter = (labelBgPos + (labelBgPos + labelBgSize)) * 0.5f;
            var tip = GetLineRectangleIntersection(last, rectCenter, labelBgPos, labelBgPos + labelBgSize);
            segments.Add((last, tip, color));
            var endDot = OffsetPointOutsideRect(tip, rectCenter, thickness * 0.6f);
            dots.Add((endDot, color, thickness));

            drawList.ChannelsSetCurrent(lineChannel);
            for (int i = 0; i < segments.Count; i++)
                drawList.AddLine(segments[i].A, segments[i].B, segments[i].Col, thickness);

            drawList.ChannelsSetCurrent(dotChannel);
            float outlineTh = MathF.Max(1f, thickness * 0.35f);
            for (int i = 0; i < dots.Count; i++)
            {
                drawList.AddCircleFilled(dots[i].P, dots[i].R, dots[i].Col);
                drawList.AddCircle(dots[i].P, dots[i].R, DotOutlineColor, 0, outlineTh);
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

        private void LoadMapScores()
        {
            var path = Path.Join(DllDirectory, "json", "map_scores.json");
            if (!File.Exists(path))
                return;

            var json = File.ReadAllText(path);
            var contents = JsonConvert.DeserializeObject<Dictionary<string, int>>(json);

            MapScores.Clear();

            if (contents is null)
                return;

            foreach (var content in contents)
            {
                MapScores[content.Key] = content.Value;
            }
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

        private static Vector2 GetLineRectangleIntersection(Vector2 lineStart, Vector2 rectCenter, Vector2 rectMin, Vector2 rectMax)
        {
            if (lineStart.X >= rectMin.X && lineStart.X <= rectMax.X &&
                lineStart.Y >= rectMin.Y && lineStart.Y <= rectMax.Y)
                return lineStart;

            Vector2 direction = rectCenter - lineStart;

            float dirX = direction.X == 0 ? 1e-6f : direction.X;
            float dirY = direction.Y == 0 ? 1e-6f : direction.Y;

            float tMinX = (rectMin.X - lineStart.X) / dirX;
            float tMaxX = (rectMax.X - lineStart.X) / dirX;
            float tMinY = (rectMin.Y - lineStart.Y) / dirY;
            float tMaxY = (rectMax.Y - lineStart.Y) / dirY;

            if (tMinX > tMaxX)
                (tMaxX, tMinX) = (tMinX, tMaxX);

            if (tMinY > tMaxY)
                (tMaxY, tMinY) = (tMinY, tMaxY);

            float tEnter = Math.Max(tMinX, tMinY);
            float tExit = Math.Min(tMaxX, tMaxY);

            if (tEnter > tExit || tEnter < 0)
                return rectCenter;

            float t = Math.Min(tEnter, 1.0f);

            return lineStart + direction * t;
        }

        private static Vector2 OffsetPointOutsideRect(Vector2 borderPoint, Vector2 rectCenter, float distance)
        {
            var dir = borderPoint - rectCenter;
            float lenSq = dir.X * dir.X + dir.Y * dir.Y;
            if (lenSq< 1e-6f)
                return borderPoint;
            dir /= MathF.Sqrt(lenSq);

            return borderPoint + dir* distance;
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

        private static RectangleF CalculateBounds(float range)
        {
            var baseBoundsTowers = new RectangleF(0, 0, ImGui.GetIO().DisplaySize.X, ImGui.GetIO().DisplaySize.Y);

            return RectangleF.Inflate(baseBoundsTowers, baseBoundsTowers.Width * (range - 1.0f), baseBoundsTowers.Height * (range - 1.0f));
        }

        [DllImport("user32.dll")]
        private static extern nint GetForegroundWindow();

    }
}