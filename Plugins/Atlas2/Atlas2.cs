namespace Atlas2
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

    public sealed partial class Atlas2 : PCore<Atlas2Settings>
    {
        /// <inheritdoc />
        public override IReadOnlyCollection<string> ConflictsWith => new[] { "Atlas" };

        private const uint CompletedNodeDotColor = 0xFF00FF00;
        private const uint DotOutlineColor = 0xFF000000;
        private static readonly Vector4 VaalBeaconBorderColor = new(1f, 0.84f, 0f, 1f);

        private const int ChannelGrid = 0;
        private const int ChannelLines = 1;
        private const int ChannelDots = 2;
        private const int ChannelLabels = 3;

        private string SettingPathname => Path.Join(DllDirectory, "config", "settings.txt");
        private string NewGroupName = string.Empty;

        private static readonly Dictionary<string, ContentInfo> MapTags = [];
        private static readonly Dictionary<string, ContentInfo> MapPlain = [];
        private static readonly Dictionary<byte, BiomeInfo> Biomes = [];
        private static readonly Dictionary<string, (IntPtr Ptr, int W, int H)> IconCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<((int X, int Y) Chunk, Vector2 Center, float Half)> fogShipIcons = [];

        // Named-map pathfinding categories — matched by exact (normalized, case-insensitive) display
        // name against nd.MapName. Each pairs with a DrawLinesTo*/*PathColor/*MaxHops setting.
        private static readonly HashSet<string> AtlasProgressionMaps = new(StringComparer.OrdinalIgnoreCase)
        {
            "Precursor Tower", "Ancient Gateway", "The Burning Monolith", "Western Gateway",
            "Eastern Gateway", "Western Enigma Chamber", "Eastern Enigma Chamber", "The Origin Tower",
        };
        private static readonly HashSet<string> QuestsMaps = new(StringComparer.OrdinalIgnoreCase) { "The Withered Willow" };
        private static readonly HashSet<string> RitualMaps = new(StringComparer.OrdinalIgnoreCase) { "Caer Tarth", "Crux of Nothingness" };
        private static readonly HashSet<string> BreachMaps = new(StringComparer.OrdinalIgnoreCase) { "Hive Colony" };
        private static readonly HashSet<string> ExpeditionMaps = new(StringComparer.OrdinalIgnoreCase)
        {
            "Ruins of Kingsmarch", "Moor of Fallen Skies", "Fallen Star", "Obscure Island",
            "Mournful Cliffside", "Secluded Temple",
        };
        private static readonly HashSet<string> AbyssMaps = new(StringComparer.OrdinalIgnoreCase) { "The Well of Souls" };
        private static readonly HashSet<string> TempleMaps = new(StringComparer.OrdinalIgnoreCase) { "Vaal Ruins" };

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
            public string InternalId;       // internal map id (e.g. "MapUniqueReactor_04"), locale-independent
            public string MapName;          // normalized display name
            public byte BiomeId;
            public AtlasNodeState State;
            public int BadgeCount;
            public List<string> RawContents;
            public List<string> ContentDisplay;    // merged, de-duped MAPPED content names (tokens + badges)
            public List<string> ContentDisplayAll; // same, but also includes raw hex for unmapped values (debug)
            public List<string> ContentIcons;
            public string Type;             // "normal" or "unique"
            public List<string> Tags;       // e.g. "lineage", "arbiter"
            public bool Drawable;
            public bool RitualSpecial;
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

        // Maps excluded from routing as a start/pass-through node (they may still be a route TARGET).
        // Matched by internal map id (locale-independent, unlike the display name); e.g.
        // "MapUniqueReactor_04" ("Site of the Chosen") is accessible but must not be used to reach
        // anything else.
        private static readonly HashSet<string> RoutingExcludedMaps = new(StringComparer.OrdinalIgnoreCase)
        {
            "MapUniqueReactor_04",
        };

        public override void OnDisable()
        {
        }

        public override void OnEnable(bool isGameOpened)
        {
            if (File.Exists(SettingPathname))
            {
                var content = File.ReadAllText(SettingPathname);
                var serializerSettings = new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace };
                Settings = JsonConvert.DeserializeObject<Atlas2Settings>(content, serializerSettings);
            }

            if (Settings.CategorySettingsVersion != 10 || Settings.MapGroups == null
                || !Settings.MapGroups.Any(group => !string.IsNullOrEmpty(group.BuiltInKey))
                || Settings.MapGroups.Any(group => group.BuiltInTargets.ContainsKey("Lineage-tagged maps")
                    || group.BuiltInTargets.ContainsKey("Arbiter-tagged maps")
                    || group.BuiltInTargets.ContainsKey("Unique map type")))
            {
                var defaults = new Atlas2Settings();
                Settings.MapGroups = defaults.MapGroups;
                Settings.CategorySettingsVersion = defaults.CategorySettingsVersion;
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
            DrawUnifiedCategories();

            ImGui.SliderFloat("Path Thickness", ref Settings.PathLineThickness, 1.0f, 8.0f);

            ImGui.SeparatorText("Atlas Settings");
            ImGui.Checkbox("Hide Completed Maps", ref Settings.HideCompletedMaps);
            ImGui.Checkbox("Hide Not Accessible Maps", ref Settings.HideNotAccessibleMaps);
            ImGui.Checkbox("Show Map Counts", ref Settings.ShowMapCounts);
            ImGuiHelper.ToolTip("Draw connected-node and badge counts under each map label on the Atlas.");
            ImGui.Checkbox("Show Content", ref Settings.ShowContent);
            ImGuiHelper.ToolTip("Draw the node's content under each map label, using the known names.");
            ImGui.SameLine();
            ImGui.Checkbox("Show Node Index (debug/RE)", ref Settings.ShowNodeIndex);
            if (Settings.ShowContent)
            {
                ImGui.Indent();
                ImGui.Checkbox("Show Content Icons", ref Settings.ShowContentIcons);
                if (Settings.ShowContentIcons)
                    ImGui.SliderFloat("Content Icon Size", ref Settings.ContentIconSize, 16f, 64f);
                ImGui.Checkbox("Debug Content", ref Settings.ShowContentDebug);
                ImGuiHelper.ToolTip("Also show unmapped content as its raw 0x value (for identifying new content).");
                ImGui.Unindent();
            }
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

            if (ImGui.TreeNode("Uncharted Waters"))
            {
                ImGui.Checkbox("Highlight hovered ship leylines", ref Settings.ShowUnchartedLeylines);
                ImGuiHelper.ToolTip("Highlights the atlas nodes and connections revealed by the hovered Uncharted Waters ship.");
                if (Settings.ShowUnchartedLeylines)
                {
                    ImGui.ColorEdit4("Leyline Color", ref Settings.UnchartedLeylineColor);
                    ImGui.SliderFloat("Leyline Thickness", ref Settings.UnchartedLeylineThickness, 1f, 12f);
                }

                ImGui.Checkbox("Show ships in fog", ref Settings.ShowShipsInFog);
                ImGuiHelper.ToolTip("Marks Uncharted Waters ships that the game is not currently rendering.");
                if (Settings.ShowShipsInFog)
                    ImGui.SliderFloat("Ship Icon Size", ref Settings.ShipIconSize, 16f, 96f);
                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Ritual Atlas Line"))
            {
                ImGui.Checkbox("Predict Ritual mods", ref Settings.ShowRitualPrediction);
                ImGuiHelper.ToolTip("Predicts the deterministic Rite modifiers for eligible Ritual-line routes.");
                ImGui.Checkbox("Head of the King planner", ref Settings.ShowRitualPlanner);
                ImGuiHelper.ToolTip("Lists and highlights Ritual routes and their predicted rewards while line mode is active.");
                if (Settings.ShowRitualPlanner)
                    DrawRewardWeightsTable();
                ImGui.TreePop();
            }

            ImGui.SeparatorText("Layout Settings");
            var nudge = Settings.AnchorNudge;
            if (ImGui.SliderFloat2("Layout Nudge (px)", ref nudge, -60f, 60f))
                Settings.AnchorNudge = nudge;
            ImGui.SliderFloat("Scale Multiplier", ref Settings.ScaleMultiplier, 0.5f, 3.0f);

            if (false && ImGui.TreeNode("Legacy Map Groups"))
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

            bool ritualLineMode = Read<byte>(atlasUi.Address + 0x637) != 0;
            ritualHoverGrid = nodeCache.Where(node => node.State == AtlasNodeState.AccessibleNow)
                .Select(node => (Node: node, Ui: atlasUi[node.Index]))
                .Where(entry => entry.Ui != null && ImGui.GetMousePos().X >= entry.Ui.Position.X &&
                    ImGui.GetMousePos().X <= entry.Ui.Position.X + entry.Ui.Size.X &&
                    ImGui.GetMousePos().Y >= entry.Ui.Position.Y &&
                    ImGui.GetMousePos().Y <= entry.Ui.Position.Y + entry.Ui.Size.Y)
                .Select(entry => (StdTuple2D<int>?)entry.Node.GridPosition).FirstOrDefault();
            ritualPredictions = ritualLineMode && Settings.ShowRitualPrediction
                ? BuildRitualPredictions(atlasUi.Address)
                : EmptyRitualPredictions;
            if (ritualLineMode && Settings.ShowRitualPlanner)
                BuildPlannerChains(atlasUi.Address);

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

                // Split into draw channels only after every early-return guard above has passed, so
                // the shared background draw list's splitter is always merged before we return (an
                // unmerged split makes the next plugin that splits the same list hit ImGui's
                // "nested channel splitting" assertion).
                drawList.ChannelsSplit(4);

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

                // Destination labels grouped by their actual first edge. Drawing is deferred until
                // every route is known so each stack can be centered around that edge's midpoint.
                var routeLabels = new Dictionary<(StdTuple2D<int> Start, StdTuple2D<int> FirstHop),
                    List<(string Text, uint Color)>>();

                foreach (var nd in nodeCache)
                {
                    var mapName = nd.MapName;

                    if (string.IsNullOrWhiteSpace(mapName))
                        continue;
                    if (!IsPrintableUnicode(mapName))
                        continue;
                    var matchesSearch = !doSearch || searchList.Any(searchTerm => MatchesSearch(nd, mapName, searchTerm));
                    if (!matchesSearch)
                        continue;

                    bool completed = nd.State == AtlasNodeState.CompletedBase;
                    bool notAccessible = nd.State != AtlasNodeState.AccessibleNow && nd.State != AtlasNodeState.CompletedBase;

                    // ── Routing ──────────────────────────────────────────────
                    // Determine if this node is a routing target. This MUST happen before the
                    // "hide not accessible" cull below: route targets are maps you haven't reached
                    // yet (so they read as not-accessible), and culling them first would mean a path
                    // is never drawn when "Hide Not Accessible Maps" is on.
                    bool routeTarget = false;
                    uint routeColor = 0;
                    int maxHops = 0;
                    var routeCategory = Settings.MapGroups.FirstOrDefault(category => category.DrawPath && !completed
                        && MatchesCategory(category, nd, mapName, doSearch, matchesSearch));
                    if (routeCategory != null)
                    {
                        routeTarget = true;
                        routeColor = ImGuiHelper.Color(MostColorfulColor(routeCategory.FontColor, routeCategory.BackgroundColor));
                        maxHops = routeCategory.MaxHops;
                    }

                    if (Settings.HideCompletedMaps && completed)
                        continue;
                    // Route targets stay visible even when "Hide Not Accessible Maps" is on, so the
                    // map you're routing to (and its path) isn't hidden along with the rest.
                    if (Settings.HideNotAccessibleMaps && notAccessible && !routeTarget)
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

                            // Destination and path length at the midpoint of the first edge. Routes
                            // sharing an entry stack on consecutive rows instead of drawing on top
                            // of one another.
                            if (path.Count >= 2)
                            {
                                var edge = (path[0], path[1]);
                                if (!routeLabels.TryGetValue(edge, out var labels))
                                {
                                    labels = new List<(string Text, uint Color)>();
                                    routeLabels[edge] = labels;
                                }
                                labels.Add(($"{mapName} ({hops})", routeColor));
                            }
                        }
                        }

                    if (!screenBounds.IntersectsWith(new RectangleF(bgPos.X, bgPos.Y, bgSize.X, bgSize.Y)))
                        continue;

                    var group = Settings.MapGroups.FirstOrDefault(g => MatchesCategory(g, nd, mapName, doSearch, matchesSearch));

                    var backgroundColor = group?.BackgroundColor ?? Settings.DefaultBackgroundColor;
                    var fontColor = group?.FontColor ?? Settings.DefaultFontColor;
                    if (completed)
                        backgroundColor.W *= 0.4f;

                    drawList.ChannelsSetCurrent(ChannelLabels);
                    float rounding = 3f * uiScale;

                    Vector4? borderColor = null;
                    if (HasAtlasContent(nd, "Vaal Beacon"))
                    {
                        borderColor = VaalBeaconBorderColor;
                    }
                    else if (HasAtlasContent(nd, "Corruption"))
                    {
                        borderColor = CategoryPathColor("corrupted_nexus", Settings.CorruptedNexusPathColor);
                    }
                    else if (HasAtlasContent(nd, "Ritual"))
                    {
                        borderColor = CategoryPathColor("ritual", Settings.RitualPathColor);
                    }
                    else if (Biomes.TryGetValue(nd.BiomeId, out var biome) && biome.Show)
                    {
                        borderColor = biome.BdColor;
                    }

                    if (Settings.ShowBiomeBorder && borderColor.HasValue)
                    {
                        var biomeColor = borderColor.Value;
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

                    if (Settings.ShowNodeIndex)
                    {
                        var indexText = nd.Index.ToString(CultureInfo.InvariantCulture);
                        var indexSize = ImGui.CalcTextSize(indexText);
                        var indexPos = new Vector2(bgPos.X - indexSize.X - (7f * uiScale), drawPosition.Y);
                        var indexPad = new Vector2(3f, 1f) * uiScale;
                        drawList.AddRectFilled(indexPos - indexPad, indexPos + indexSize + indexPad,
                            ImGuiHelper.Color(new Vector4(0f, 0f, 0f, 0.8f)), rounding);
                        drawList.AddText(indexPos, ImGuiHelper.Color(fontColor), indexText);
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
                        nextRowTopY += countTextSize.Y + rowGap;
                    }

                    if (Settings.ShowContent)
                    {
                        // Merged, de-duped content (tokens + badges) from core: one line each. Normally
                        // only mapped names; with Debug Content on, also show unmapped values as raw hex.
                        var contentList = Settings.ShowContentDebug ? nd.ContentDisplayAll : nd.ContentDisplay;
                        if (contentList is { Count: > 0 })
                        {
                            if (Settings.ShowContentIcons)
                                DrawContentIcons(drawList, nd.ContentIcons, labelCenterX, drawPosition.Y, uiScale);
                            foreach (var content in contentList)
                            {
                                DrawContentLine(drawList, content, labelCenterX, ref nextRowTopY, rowGap, fontColor);
                            }
                        }
                    }
                }

                drawList.ChannelsSetCurrent(ChannelLabels);
                foreach (var group in routeLabels)
                {
                    if (!shiftedCenters.TryGetValue(group.Key.Start, out var startCenter)
                        || !shiftedCenters.TryGetValue(group.Key.FirstHop, out var firstHopCenter))
                        continue;

                    var midpoint = (startCenter + firstHopCenter) * 0.5f;
                    float lineHeight = ImGui.GetTextLineHeight() + (4f * uiScale);
                    float firstRowY = midpoint.Y - ((group.Value.Count - 1) * lineHeight * 0.5f);
                    for (int row = 0; row < group.Value.Count; row++)
                    {
                        var label = group.Value[row];
                        var labelSize = ImGui.CalcTextSize(label.Text);
                        var labelPos = new Vector2(midpoint.X - (labelSize.X * 0.5f),
                            firstRowY + (row * lineHeight) - (labelSize.Y * 0.5f));
                        var labelPad = new Vector2(4f, 1f) * uiScale;
                        drawList.AddRectFilled(labelPos - labelPad, labelPos + labelSize + labelPad,
                            ImGuiHelper.Color(new Vector4(0f, 0f, 0f, 1f)), 3f * uiScale);
                        drawList.AddText(labelPos, label.Color, label.Text);
                    }
                }

                if (Settings.ShowShipsInFog)
                    DrawFogShips(drawList, panelRect, uiScale, allCenters);
                else
                    fogShipIcons.Clear();

                if (Settings.ShowUnchartedLeylines)
                    DrawUnchartedLeylines(drawList, atlasUi, panelRect, uiScale, ImGui.GetMousePos(), shiftedCenters);

                if (ritualPredictions.Count > 0)
                {
                    drawList.ChannelsSetCurrent(ChannelLabels);
                    foreach (var prediction in ritualPredictions)
                    {
                        if (!allCenters.TryGetValue(prediction.Key, out var center))
                            continue;
                        var size = ImGui.CalcTextSize(prediction.Value);
                        drawList.AddText(center - new Vector2(size.X * 0.5f, size.Y + 18f * uiScale),
                            ImGuiHelper.Color(new Vector4(0.25f, 1f, 0.35f, 1f)), prediction.Value);
                    }
                }

                if (ritualLineMode && Settings.ShowRitualPlanner)
                    DrawPlannerOverlay(drawList, ImGui.GetIO().DisplaySize * 0.5f, uiScale);

                drawList.ChannelsMerge();
                if (ritualLineMode && Settings.ShowRitualPlanner)
                    DrawPlannerWindow();
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
                    InternalId = map.MapId,
                    MapName = NormalizeName(map.DisplayName),
                    BiomeId = map.BiomeId,
                    State = ToAtlasNodeState(map.State),
                    BadgeCount = map.BadgeCount,
                    RawContents = map.ContentNames.ToList(),
                    ContentDisplay = map.GetContentDisplayNames(includeUnmapped: false).ToList(),
                    ContentDisplayAll = map.GetContentDisplayNames(includeUnmapped: true).ToList(),
                    ContentIcons = map.Badges.Concat<object>(map.Effects)
                        .Select(content => content switch
                        {
                            AtlasMapNodeBadge badge => badge.Icon,
                            AtlasMapNodeEffect effect => effect.Icon,
                            _ => null,
                        })
                        .Where(icon => !string.IsNullOrWhiteSpace(icon)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    Type = map.Type ?? "normal",
                    Tags = map.Tags.ToList(),
                    Drawable = !string.IsNullOrWhiteSpace(map.DisplayName),
                    RitualSpecial = IsRitualSpecialNode(map.Address),
                });
            }
            cachedAtlasCount = atlasCount;

            // Rebuild the routing graph + BFS tree. The node topology
            // doesn't change while the atlas is open, so this runs at
            // the same cadence as the node cache (~3×/sec at 60 fps).
            cachedRouteGraph = new Dictionary<StdTuple2D<int>, List<StdTuple2D<int>>>();
            cachedAccessible = new HashSet<StdTuple2D<int>>();
            var routingExcluded = new HashSet<StdTuple2D<int>>();
            foreach (var nd in nodeCache)
            {
                cachedRouteGraph[nd.GridPosition] = nd.ConnectedGridPositions;
                if (RoutingExcludedMaps.Contains(nd.InternalId))
                    routingExcluded.Add(nd.GridPosition); // never a start/pass-through (still a valid target)
                else if (nd.State == AtlasNodeState.AccessibleNow)
                    cachedAccessible.Add(nd.GridPosition);
            }
            cachedBfsTree = MultiSourceBfs(cachedRouteGraph, cachedAccessible, new HashSet<StdTuple2D<int>>(), routingExcluded);
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

        // Multi-source BFS from all accessible nodes over the undirected graph, skipping blocked
        // (failed) nodes. Nodes in `noPass` are never seeded as a source and never expanded, so they
        // can't be a start or a pass-through — but they can still be *reached* (a path may end at one),
        // so routing TO such a node still works. Returns a cameFrom tree pointing toward the nearest
        // source — reconstruct paths with PathFromAccessible.
        private static Dictionary<StdTuple2D<int>, StdTuple2D<int>> MultiSourceBfs(
            Dictionary<StdTuple2D<int>, List<StdTuple2D<int>>> graph,
            HashSet<StdTuple2D<int>> sources,
            HashSet<StdTuple2D<int>> blocked,
            HashSet<StdTuple2D<int>> noPass)
        {
            var cameFrom = new Dictionary<StdTuple2D<int>, StdTuple2D<int>>();
            var visited = new HashSet<StdTuple2D<int>>();
            var queue = new Queue<StdTuple2D<int>>();

            foreach (var s in sources)
                if (graph.ContainsKey(s) && !blocked.Contains(s) && !noPass.Contains(s) && visited.Add(s))
                    queue.Enqueue(s);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                // A no-pass node can be reached (it's a valid target) but is never expanded, so no
                // route is ever drawn THROUGH it.
                if (noPass.Contains(cur))
                    continue;
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

        private void DrawFogShips(ImDrawListPtr drawList, RectangleF panelRect, float uiScale,
            IReadOnlyDictionary<StdTuple2D<int>, Vector2> centers)
        {
            fogShipIcons.Clear();
            var buttons = Core.States.InGameStateObject.GameUi.AtlasOceanButtons;
            var visibleChunks = buttons.Where(button => button.IsVisible)
                .Select(button => (button.GridPosition.X >> 4, button.GridPosition.Y >> 4)).ToHashSet();
            var hidden = buttons.Where(button => !button.IsVisible)
                .GroupBy(button => (button.GridPosition.X >> 4, button.GridPosition.Y >> 4))
                .Where(group => !visibleChunks.Contains(group.Key));

            drawList.ChannelsSetCurrent(ChannelLabels);
            float height = MathF.Max(8f, Settings.ShipIconSize * uiScale);
            bool haveIcon = TryGetIcon("UnchartedShip", out var ptr, out var iw, out var ih);
            foreach (var group in hidden)
            {
                var anchor = group.First().GridPosition;
                if (!centers.TryGetValue(anchor, out var center))
                {
                    var nearest = centers.Where(entry =>
                            (entry.Key.X >> 4, entry.Key.Y >> 4) == group.Key)
                        .OrderBy(entry => Math.Abs(entry.Key.X - anchor.X) + Math.Abs(entry.Key.Y - anchor.Y))
                        .FirstOrDefault();
                    center = nearest.Value;
                }

                if (center == Vector2.Zero || !panelRect.Contains(center.X, center.Y))
                    continue;

                if (haveIcon)
                {
                    float width = height * iw / Math.Max(1, ih);
                    drawList.AddImage(ptr, center - new Vector2(width, height) * 0.5f,
                        center + new Vector2(width, height) * 0.5f);
                }
                else
                {
                    float radius = height * 0.35f;
                    drawList.AddCircleFilled(center, radius, ImGuiHelper.Color(new Vector4(0.04f, 0.08f, 0.12f, 0.9f)));
                    drawList.AddCircle(center, radius, ImGuiHelper.Color(Settings.UnchartedLeylineColor), 0,
                        MathF.Max(1.5f, radius * 0.25f));
                }

                fogShipIcons.Add((group.Key, center, height * 0.5f));
            }
        }

        private static bool IsRitualSpecialNode(IntPtr address)
        {
            if (address == IntPtr.Zero)
                return true;
            var row = Read<IntPtr>(address + 0x300);
            return row == IntPtr.Zero || Read<int>(row + 0x7C) != 0;
        }

        private void DrawUnchartedLeylines(ImDrawListPtr drawList, UiElementBase atlasUi, RectangleF panelRect,
            float uiScale, Vector2 mouse, IReadOnlyDictionary<StdTuple2D<int>, Vector2> centers)
        {
            (int X, int Y)? hoveredChunk = null;
            foreach (var button in Core.States.InGameStateObject.GameUi.AtlasOceanButtons.Where(button => button.IsVisible))
            {
                var ui = atlasUi[button.Index];
                if (ui == null)
                    continue;
                var min = ui.Position;
                var max = min + ui.Size;
                if (mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y)
                {
                    hoveredChunk = (button.GridPosition.X >> 4, button.GridPosition.Y >> 4);
                    break;
                }
            }

            if (hoveredChunk == null)
            {
                foreach (var icon in fogShipIcons)
                {
                    if (mouse.X >= icon.Center.X - icon.Half && mouse.X <= icon.Center.X + icon.Half &&
                        mouse.Y >= icon.Center.Y - icon.Half && mouse.Y <= icon.Center.Y + icon.Half)
                    {
                        hoveredChunk = icon.Chunk;
                        break;
                    }
                }
            }

            if (hoveredChunk == null)
                return;

            var chunkCenters = centers.Where(entry =>
                    (entry.Key.X >> 4, entry.Key.Y >> 4) == hoveredChunk.Value)
                .ToDictionary(entry => entry.Key, entry => entry.Value);
            var displaySize = ImGui.GetIO().DisplaySize;
            var screenBounds = new RectangleF(0f, 0f, displaySize.X, displaySize.Y);
            drawList.ChannelsSetCurrent(ChannelGrid);
            uint color = ImGuiHelper.Color(Settings.UnchartedLeylineColor);
            float thickness = MathF.Max(1f, Settings.UnchartedLeylineThickness * uiScale);
            foreach (var entry in chunkCenters)
            {
                bool sourceOnScreen = screenBounds.Contains(entry.Value.X, entry.Value.Y);
                if (sourceOnScreen)
                    drawList.AddCircleFilled(entry.Value, MathF.Max(2f, thickness * 0.9f), color);
                if (!cachedRouteGraph.TryGetValue(entry.Key, out var connected))
                    continue;
                foreach (var target in connected)
                {
                    bool canonical = entry.Key.X < target.X || (entry.Key.X == target.X && entry.Key.Y <= target.Y);
                    if (!canonical || !chunkCenters.TryGetValue(target, out var targetCenter))
                        continue;

                    bool targetOnScreen = screenBounds.Contains(targetCenter.X, targetCenter.Y);
                    if (sourceOnScreen || targetOnScreen)
                        drawList.AddLine(entry.Value, targetCenter, color, thickness);
                }
            }
        }

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

        // Draw one centered content line under the map label, advancing the layout cursor.
        private static void DrawContentLine(ImDrawListPtr drawList, string text, float centerX,
            ref float nextRowTopY, float rowGap, Vector4 fontColor)
        {
            var size = ImGui.CalcTextSize(text);
            var pos = new Vector2(centerX - size.X * 0.5f, nextRowTopY);
            drawList.AddText(pos, ImGuiHelper.Color(fontColor), text);
            nextRowTopY += size.Y + rowGap;
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

        private void DrawContentIcons(ImDrawListPtr drawList, IReadOnlyList<string> iconsToDraw, float centerX,
            float labelTopY, float uiScale)
        {
            var icons = new List<(IntPtr Ptr, float W)>();
            float height = MathF.Max(8f, Settings.ContentIconSize * uiScale);
            foreach (var basename in iconsToDraw)
            {
                if (!TryGetIcon(basename, out var ptr, out var w, out var h)) continue;
                icons.Add((ptr, height * w / Math.Max(1, h)));
            }
            if (icons.Count == 0) return;
            float gap = 4f * uiScale;
            float width = icons.Sum(icon => icon.W) + gap * (icons.Count - 1);
            float x = centerX - width * 0.5f;
            float y = labelTopY - height - (4f * uiScale);
            foreach (var icon in icons)
            {
                drawList.AddImage(icon.Ptr, new Vector2(x, y), new Vector2(x + icon.W, y + height));
                x += icon.W + gap;
            }
        }

        private bool TryGetIcon(string basename, out IntPtr ptr, out int w, out int h)
        {
            if (IconCache.TryGetValue(basename, out var cached))
            { ptr = cached.Ptr; w = cached.W; h = cached.H; return ptr != IntPtr.Zero; }
            ptr = IntPtr.Zero; w = h = 0;
            var file = Path.Join(DllDirectory, "icons", basename + ".png");
            if (!File.Exists(file)) return false;
            Core.Overlay.AddOrGetImagePointer(file, false, out ptr, out var iw, out var ih);
            w = (int)iw; h = (int)ih;
            IconCache[basename] = (ptr, w, h);
            return ptr != IntPtr.Zero;
        }

        private static bool HasAtlasContent(NodeData node, string text)
        {
            return node.ContentDisplay.Any(content => content.Contains(text, StringComparison.OrdinalIgnoreCase)) ||
                   node.RawContents.Any(content => content.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        private static bool MatchesCategory(MapGroupSettings category, NodeData node, string mapName,
            bool searchActive, bool matchesSearch)
        {
            if (category.Maps.Any(map => NormalizeName(map).Equals(mapName, StringComparison.OrdinalIgnoreCase)))
                return true;

            bool Enabled(string label) => category.BuiltInTargets.TryGetValue(label, out var enabled) && enabled;
            bool Named() => category.BuiltInTargets.Any(target => target.Value
                && NormalizeName(target.Key).Equals(mapName, StringComparison.OrdinalIgnoreCase));

            return category.BuiltInKey switch
            {
                "search" => Enabled("Current search query") && searchActive && matchesSearch,
                "corrupted_nexus" => Enabled("Corrupted Nexus content") && IsCorruptedNexus(node),
                "grand_mirror" => Enabled("Grand Mirror content") && HasAtlasContent(node, "Grand Mirror"),
                "" => false,
                _ => Named(),
            };
        }

        private Vector4 CategoryPathColor(string builtInKey, Vector4 fallback)
        {
            var category = Settings.MapGroups.FirstOrDefault(group => group.BuiltInKey == builtInKey);
            return category == null ? fallback : MostColorfulColor(category.FontColor, category.BackgroundColor);
        }

        private static Vector4 MostColorfulColor(Vector4 foreground, Vector4 background)
        {
            static float Chroma(Vector4 color) =>
                MathF.Max(color.X, MathF.Max(color.Y, color.Z)) - MathF.Min(color.X, MathF.Min(color.Y, color.Z));
            static float Luminance(Vector4 color) =>
                (0.2126f * color.X) + (0.7152f * color.Y) + (0.0722f * color.Z);

            var foregroundChroma = Chroma(foreground);
            var backgroundChroma = Chroma(background);
            if (MathF.Abs(foregroundChroma - backgroundChroma) > 0.001f)
                return backgroundChroma > foregroundChroma ? background : foreground;

            return Luminance(background) > Luminance(foreground) ? background : foreground;
        }

        private static bool MatchesSearch(NodeData node, string mapName, string searchTerm)
        {
            return mapName.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                   HasAtlasContent(node, searchTerm);
        }

        private static bool IsCorruptedNexus(NodeData node)
        {
            return !node.Tags.Exists(tag => string.Equals(tag, "arbiter", StringComparison.OrdinalIgnoreCase)) &&
                   HasAtlasContent(node, "Corruption") &&
                   HasAtlasContent(node, "Powerful Map Boss");
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

        private void DrawUnifiedCategories()
        {
            for (int i = 0; i < Settings.MapGroups.Count; i++)
            {
                var category = Settings.MapGroups[i];
                ImGui.PushID(i);
                ImGui.Checkbox("##route", ref category.DrawPath);
                ImGui.SameLine();
                ColorSwatch("##pathText", ref category.FontColor);
                ImGuiHelper.ToolTip("Node-text color. The path automatically uses the more colorful of the text and background colors.");
                ImGui.SameLine();
                ColorSwatch("##background", ref category.BackgroundColor);
                ImGuiHelper.ToolTip("Node background color. The path automatically uses the more colorful of the text and background colors.");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(75);
                ImGui.SliderInt("##hops", ref category.MaxHops, 1, 200);
                ImGui.SameLine();
                bool open = ImGui.TreeNode($"{category.Name}##category");
                if (open)
                {
                    ImGui.Indent(16f);
                    if (ImGui.SmallButton("Up") && i > 0) MoveMapGroup(i, -1);
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Down") && i + 1 < Settings.MapGroups.Count) MoveMapGroup(i, 1);
                    if (string.IsNullOrEmpty(category.BuiltInKey))
                    {
                        ImGui.SetNextItemWidth(260);
                        ImGui.InputText("Category name", ref category.Name, 256);
                    }

                    var targetNames = category.BuiltInTargets.Keys.ToList();
                    foreach (var target in targetNames)
                    {
                        bool enabled = category.BuiltInTargets[target];
                        if (ImGui.Checkbox($"{target}##fixed", ref enabled)) category.BuiltInTargets[target] = enabled;
                    }

                    for (int j = 0; j < category.Maps.Count; j++)
                    {
                        var map = category.Maps[j];
                        ImGui.SetNextItemWidth(260);
                        if (ImGui.InputTextWithHint($"##map{j}", "map name", ref map, 256)) category.Maps[j] = map;
                        ImGui.SameLine();
                        if (ImGui.SmallButton($"Remove##map{j}")) { category.Maps.RemoveAt(j); break; }
                    }
                    if (ImGui.SmallButton("Add map")) category.Maps.Add(string.Empty);

                    if (string.IsNullOrEmpty(category.BuiltInKey))
                    {
                        ImGui.SameLine();
                        if (ImGui.SmallButton("Delete category"))
                        {
                            Settings.MapGroups.RemoveAt(i);
                            ImGui.Unindent(16f);
                            ImGui.TreePop();
                            ImGui.PopID();
                            break;
                        }
                    }
                    ImGui.Unindent(16f);
                    ImGui.TreePop();
                }
                ImGui.PopID();
            }

            ImGui.InputTextWithHint("##newCategory", "new category name", ref Settings.GroupNameInput, 256);
            ImGui.SameLine();
            if (ImGui.Button("Add category") && !string.IsNullOrWhiteSpace(Settings.GroupNameInput))
            {
                Settings.MapGroups.Add(new MapGroupSettings(Settings.GroupNameInput.Trim(), Settings.DefaultBackgroundColor, Settings.DefaultFontColor));
                Settings.GroupNameInput = string.Empty;
            }
        }

        [DllImport("user32.dll")]
        private static extern nint GetForegroundWindow();

    }
}
