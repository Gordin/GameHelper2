// <copyright file="Radar.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Radar
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using System.Threading;
    using System.Threading.Tasks;
    using Coroutine;
    using GameHelper;
    using GameHelper.CoroutineEvents;
    using GameHelper.Plugin;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteEnums.Entity;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.Utils;
    using ImGuiNET;
    using Newtonsoft.Json;
    using SixLabors.ImageSharp;
    using SixLabors.ImageSharp.PixelFormats;
    using SixLabors.ImageSharp.Processing.Processors.Transforms;
    using SixLabors.ImageSharp.Processing;

    /// <summary>
    /// <see cref="Radar"/> plugin.
    /// </summary>
    public sealed class Radar : PCore<RadarSettings>
    {
        private const string TempleTgtPrefix = "Metadata/Terrain/Leagues/Incursion/Tiles/Features/Waygates/WaygateDevice";

        // All campaign rune terrain tiles (e.g. GrimTangle_Runestones, and other
        // terrain variants) live under this folder; matching the prefix combines them all.
        private const string RunestoneTgtPrefix = "Metadata/Terrain/Leagues/Expedition/Tiles/CampaignRunes/";

        private readonly string delveChestStarting = "Metadata/Chests/DelveChests/";
        private readonly Dictionary<uint, string> delveChestCache = new();

        /// <summary>
        /// If we don't do this, user will be asked to
        /// setup the culling window everytime they open the game.
        /// </summary>
        private bool skipOneSettingChange = false;
        private bool isAddNewPOIHeaderOpened = false;
        private ActiveCoroutine? onMove;
        private ActiveCoroutine? onForegroundChange;
        private ActiveCoroutine? onGameClose;
        private ActiveCoroutine? onAreaChange;

        private string currentAreaName = string.Empty;
        private string tmpTileName = string.Empty;
        private string tmpDisplayName = string.Empty;
        private int tmpTgtSelectionCounter = 0;
        private string tmpTileFilter = string.Empty;
        private bool addTileForAllAreas = false;

        private double miniMapDiagonalLength = 0x00;

        private double largeMapDiagonalLength = 0x00;

        private IntPtr walkableMapTexture = IntPtr.Zero;
        private Vector2 walkableMapDimension = Vector2.Zero;
        private readonly Dictionary<string, Vector2> textHalfSizeCache = new(StringComparer.Ordinal);
        private readonly Dictionary<int, Vector2> poiIndexHalfSizeCache = new();

        // Pathfinding: cache computed paths and throttle recomputation
        private long nextPoiRecomputeTime = 0;
        private long nextPoiFullRecomputeTime = 0;
        private Dictionary<string, List<Vector2>?> poiPathCache = new();
        private Task? pendingPathTask = null;

        // Entity pathfinding: cache and throttle for entity-icon-based paths
        private long nextEntityRecomputeTime = 0;
        private long nextEntityFullRecomputeTime = 0;
        private Dictionary<uint, List<Vector2>?> entityPathCache = new();
        private Task? pendingEntityPathTask = null;
        private readonly List<(uint entityId, Vector2 gridPos, Vector4 color)> entityPathSnapshot = new();
        private readonly List<(string cacheKey, Vector2 gridPos, Vector4 color)> tileIconPathSnapshot = new();
        private Dictionary<string, List<Vector2>?> tileIconPathCache = new();

        private string SettingPathname => Path.Join(this.DllDirectory, "config", "settings.txt");

        private string ImportantTgtPathName => Path.Join(this.DllDirectory, "important_tgt_files.txt");

        private string BossArenaTgtPathName => Path.Join(this.DllDirectory, "boss_arena_tgt_files.txt");

        private string StairsTgtPathName => Path.Join(this.DllDirectory, "stairs_tgt_files.txt");

        /// <inheritdoc/>
        public override void DrawSettings()
        {
            ImGui.TextWrapped("If your mini/large map icon are not working/visible. Open this " +
                "setting window, click anywhere on it and then hide this setting window. It will fix the issue.");
            ImGui.DragFloat("Large Map Fix", ref this.Settings.LargeMapScaleMultiplier, 0.001f, 0.1f, 2.0f);
            ImGuiHelper.ToolTip("This slider is for fixing large map (icons) offset. " +
                "You have to use it if you feel that LargeMap Icons " +
                "are moving while your player is moving. You only have " +
                "to find a value that works for you per game window resolution. " +
                "Basically, you don't have to change it unless you change your " +
                "game window resolution. Also, please contribute back, let me know " +
                "what resolution you use and what value works best for you. " +
                "This slider has no impact on mini-map icons. For windowed-full-screen " +
                "default value should be good enough. If you want to add precise value " +
                "(e.g. 0.137345) press CTRL + LMB");
            ImGui.DragFloat("Large Map X Offset", ref this.Settings.LargeMapXOffset, 0.1f);
            ImGuiHelper.ToolTip("Adjusts only the large map overlay horizontally. Negative moves it left, positive moves it right.");
            ImGui.DragFloat("Large Map Y Offset", ref this.Settings.LargeMapYOffset, 0.1f);
            ImGuiHelper.ToolTip("Adjusts only the large map overlay vertically. Negative moves it up, positive moves it down.");
            ImGui.DragFloat("Mini Map X Offset", ref this.Settings.MiniMapXOffset, 0.1f);
            ImGuiHelper.ToolTip("Adjusts only the mini-map overlay horizontally. Negative moves it left, positive moves it right.");
            ImGui.DragFloat("Mini Map Zoom", ref this.Settings.MiniMapZoomMultiplier, 0.001f, 0.01f, 3f, "%.3f");
            ImGuiHelper.ToolTip("Controls how far mini-map icons sit from your character (the mini-map's effective zoom).");

            ImGui.Checkbox("Auto-Detect Local Co-op Mode", ref this.Settings.AutoDetectCoopMode);
            ImGuiHelper.ToolTip("Automatically detects when you are playing local co-op in controller mode and adjusts map centering.");
            if (!this.Settings.AutoDetectCoopMode)
            {
                ImGui.Checkbox("Enable Local Co-op Map Hack Centering", ref this.Settings.EnableCoopMode);
                ImGuiHelper.ToolTip("Centers the map/maphack on the midpoint of P1 and P2 when playing co-op.");
            }

            ImGui.Checkbox("Hide Radar when in Hideout/Town", ref this.Settings.DrawWhenNotInHideoutOrTown);
            ImGui.Checkbox("Hide Radar when game is in the background", ref this.Settings.DrawWhenForeground);
            ImGui.Checkbox("Hide Radar when game is paused", ref this.Settings.DrawWhenNotPaused);
            if (ImGui.Checkbox("Modify Large Map Culling Window", ref this.Settings.ModifyCullWindow))
            {
                if (this.Settings.ModifyCullWindow)
                {
                    this.Settings.MakeCullWindowFullScreen = false;
                }
            }

            ImGui.TreePush("radar_culling_window");
            if (ImGui.Checkbox("Make Culling Window Cover Whole Game", ref this.Settings.MakeCullWindowFullScreen))
            {
                this.Settings.ModifyCullWindow = !this.Settings.MakeCullWindowFullScreen;
                this.Settings.CullWindowPos = Vector2.Zero;
                this.Settings.CullWindowSize.X = Core.Process.WindowArea.Width;
                this.Settings.CullWindowSize.Y = Core.Process.WindowArea.Height;
            }

            if (ImGui.TreeNode("Culling window advance options"))
            {
                ImGui.Checkbox("Draw maphack in culling window", ref this.Settings.DrawMapInCull);
                ImGui.Checkbox("Draw POIs in culling window", ref this.Settings.DrawPOIInCull);
                ImGui.TreePop();
            }

            ImGui.TreePop();
            ImGui.Separator();
            ImGui.NewLine();
            if (ImGui.Checkbox("Draw Area/Zone Map (maphack)", ref this.Settings.DrawWalkableMap))
            {
                if (this.Settings.DrawWalkableMap)
                {
                    if (this.walkableMapTexture == IntPtr.Zero)
                    {
                        this.ReloadMapTexture();
                    }
                }
                else
                {
                    this.RemoveMapTexture();
                }
            }

            if (ImGui.ColorEdit4("Drawn Map Color", ref this.Settings.WalkableMapColor))
            {
                if (this.walkableMapTexture != IntPtr.Zero)
                {
                    this.ReloadMapTexture();
                }
            }

            ImGui.Separator();
            ImGui.NewLine();
            ImGui.Checkbox("Show terrain points of interest (A.K.A Terrain POI)", ref this.Settings.ShowImportantPOI);
            ImGui.ColorEdit4("Terrain POI text color", ref this.Settings.POIColor);
            ImGui.Checkbox("Add black background to Terrain POI text", ref this.Settings.EnablePOIBackground);
            ImGui.Checkbox("Show Straight-Line Arrow to POIs", ref this.Settings.ShowStraightLine);
            ImGuiHelper.ToolTip("Draws a straight line+arrow to each POI. Green = clear, Red = blocked.");
            ImGui.Checkbox("Show A* Smooth Path to POIs", ref this.Settings.ShowSmoothPath);
            ImGuiHelper.ToolTip("Computes and draws the actual shortest walkable path (cyan).");
            ImGui.DragFloat("POI Path Thickness", ref this.Settings.DirectionLineThickness, 0.1f, 0.1f, 10.0f, "%.1f");
            ImGui.DragInt("Path Recompute Segments", ref this.Settings.PathRecomputeSegments, 0.1f, 0, 20);
            ImGuiHelper.ToolTip("0 = full recompute every cycle. Set to 3-5 to only recompute the first few segments of a cached path, reusing the tail. Higher values = faster but paths may be slightly stale when the player moves.");
            ImGui.DragInt("Path Recompute Interval (ms)", ref this.Settings.PathRecomputeIntervalMs, 1f, 5, 1000);
            ImGuiHelper.ToolTip("How often paths are recomputed. Lower = more responsive, higher = less CPU usage.");
            ImGui.DragInt("Full Recompute Interval (ms)", ref this.Settings.PathFullRecomputeIntervalMs, 100f, 1000, 10000);
            ImGuiHelper.ToolTip("How often a full path recompute is forced, ignoring the segment-skip optimization. Ensures paths never get stuck stale.");
            this.isAddNewPOIHeaderOpened = ImGui.CollapsingHeader("Add or Modify Terrain POI");
            if (this.isAddNewPOIHeaderOpened)
            {
                this.AddNewPOIWidget();
                this.ShowPOIWidget();
            }

            ImGui.Separator();
            ImGui.NewLine();
            ImGui.Checkbox("Hide Entities outside the network bubble", ref this.Settings.HideOutsideNetworkBubble);
            ImGui.Checkbox("Show Player Names", ref this.Settings.ShowPlayersNames);
            ImGuiHelper.ToolTip("This button will not work while Player is in the Scourge.");
            ImGui.Checkbox("Show Paths to Icons", ref this.Settings.ShowEntityPaths);
            ImGuiHelper.ToolTip("Global on/off for entity-icon pathing. Does not affect individual icon path settings.");
            ImGui.DragFloat("Icon Path Thickness", ref this.Settings.IconPathThickness, 0.1f, 0.1f, 10.0f, "%.1f");
            if (ImGui.CollapsingHeader("Icons Setting"))
            {
                this.Settings.DrawIconsSettingToImGui(
                    "BaseGame Icons",
                    this.Settings.BaseIcons,
                    "Blockages icon can be set from Delve Icons category i.e. 'Blockage OR DelveWall'");

                this.Settings.DrawPOIMonsterSettingToImGui(this.DllDirectory);
                this.Settings.OtherImportantObjectsSettingToImGui(this.DllDirectory);
                this.Settings.DrawIconsSettingToImGui(
                    "Breach Icons",
                    this.Settings.BreachIcons,
                    "Breach bosses are same as BaseGame Icons -> Unique Monsters.");

                this.Settings.DrawIconsSettingToImGui(
                    "Delirium Icons",
                    this.Settings.DeliriumIcons,
                    string.Empty);

                this.Settings.DrawIconsSettingToImGui(
                    "Expedition Icons",
                    this.Settings.ExpeditionIcons,
                    string.Empty);

                this.Settings.DrawIconsSettingToImGui(
                    "Temple Icons",
                    this.Settings.TempleIcons,
                    "Icons for Incursion Waygate devices (Vaal Ruins).");

                this.Settings.DrawIconsSettingToImGui(
                    "Expedition Marker Icons",
                    this.Settings.ExpeditionMarkerIcons,
                    "Icons for expedition markers, keyed by MinimapIcon name. Set size to 0 to disable.");

                this.Settings.DrawIconsSettingToImGui(
                    "Expedition Remnant Icons",
                    this.Settings.ExpeditionRemnantIcons,
                    "Icons for expedition remnants with specific mods. Set size to 0 to disable.");

                this.Settings.DrawIconsSettingToImGui(
                    "Runestone Icons",
                    this.Settings.RunestoneIcons,
                    "Icons for runestone encounters.");

                this.Settings.DrawIconsSettingToImGui(
                    "Boss Icons",
                    this.Settings.BossIcons,
                    "Icons for map boss arenas.");
            }
        }

        /// <inheritdoc/>
        public override void DrawUI()
        {
            var largeMap = Core.States.InGameStateObject.GameUi.LargeMap;
            var miniMap = Core.States.InGameStateObject.GameUi.MiniMap;
            var areaDetails = Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails;
            if (this.Settings.ModifyCullWindow)
            {
                ImGui.SetNextWindowPos(largeMap.Center, ImGuiCond.Appearing);
                ImGui.SetNextWindowSize(new Vector2(400f), ImGuiCond.Appearing);
                ImGui.Begin("Large Map Culling Window");
                ImGui.TextWrapped("This is a culling window for the large map icons. " +
                                  "Any large map icons outside of this window will be hidden automatically. " +
                                  "Feel free to change the position/size of this window. " +
                                  "Once you are happy with the dimensions, double click this window. " +
                                  "You can bring this window back from the settings menu.");
                this.Settings.CullWindowPos = ImGui.GetWindowPos();
                this.Settings.CullWindowSize = ImGui.GetWindowSize();
                if (ImGui.IsWindowHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    this.Settings.ModifyCullWindow = false;
                }

                ImGui.End();
            }
            
            if (this.Settings.DrawWhenNotPaused && Core.States.GameCurrentState != GameStateTypes.InGameState)
            {
                return;
            }

            if (Core.States.GameCurrentState is not (GameStateTypes.InGameState or GameStateTypes.EscapeState))
            {
                return;
            }

            if (this.Settings.DrawWhenForeground && !Core.Process.Foreground)
            {
                return;
            }

            if (this.Settings.DrawWhenNotInHideoutOrTown &&
                (areaDetails.IsHideout || areaDetails.IsTown))
            {
                return;
            }

            if (Core.States.InGameStateObject.GameUi.IsAnyLargePanelOpen)
            {
                return;
            }

            if (this.Settings.MakeCullWindowFullScreen)
            {
                this.Settings.CullWindowPos = Vector2.Zero;
                this.Settings.CullWindowSize.X = Core.Process.WindowArea.Size.Width;
                this.Settings.CullWindowSize.Y = Core.Process.WindowArea.Size.Height;
            }

            var currentAreaInstance = Core.States.InGameStateObject.CurrentAreaInstance;
            if (!currentAreaInstance.Player.TryGetComponent<Render>(out var playerRender))
            {
                return;
            }

            var trackingPos = new Vector2(playerRender.GridPosition.X, playerRender.GridPosition.Y);
            var trackingHeight = playerRender.TerrainHeight;

            var playerOther = currentAreaInstance.AwakeEntities.Values
                .FirstOrDefault(e => e.EntitySubtype == EntitySubtypes.PlayerOther);
            if (this.IsLocalCoopActive(playerRender, playerOther != null))
            {
                if (playerOther != null && playerOther.TryGetComponent<Render>(out var pOtherRender))
                {
                    trackingPos = (trackingPos + new Vector2(pOtherRender.GridPosition.X, pOtherRender.GridPosition.Y)) / 2f;
                    trackingHeight = (trackingHeight + pOtherRender.TerrainHeight) / 2f;
                }
            }

            this.CollectEntityPaths();
            this.RebuildEntityPaths();

            if (largeMap.IsVisible && !Core.States.InGameStateObject.GameUi.WorldMapPanel.IsVisible)
            {
                if (this.largeMapDiagonalLength <= 0)
                {
                    this.UpdateLargeMapDetails();
                }

                var largeMapRealCenter = largeMap.Center + largeMap.Shift + largeMap.DefaultShift;
                // Calibrated X bias baked in so LargeMapXOffset defaults to 0.
                const float LargeMapXBias = -5.7f;
                largeMapRealCenter.X += LargeMapXBias + this.Settings.LargeMapXOffset;
                largeMapRealCenter.Y += this.Settings.LargeMapYOffset;
                // Scale factor calibrated so LargeMapScaleMultiplier = 1.0 produces correct placement.
                const float LargeMapScaleBaseline = 0.188f;
                var largeMapModifiedZoom = this.Settings.LargeMapScaleMultiplier * largeMap.Zoom * LargeMapScaleBaseline;
                Helper.DiagonalLength = this.largeMapDiagonalLength;
                Helper.Scale = largeMapModifiedZoom;
                ImGui.SetNextWindowPos(this.Settings.CullWindowPos);
                ImGui.SetNextWindowSize(this.Settings.CullWindowSize);
                ImGui.SetNextWindowBgAlpha(0f);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
                ImGui.Begin("Large Map Culling Window", ImGuiHelper.TransparentWindowFlags);
                ImGui.PopStyleVar();
                this.DrawLargeMap(largeMapRealCenter, trackingPos, trackingHeight);
                this.DrawTgtFiles(largeMapRealCenter, trackingPos, trackingHeight);
                this.DrawDirectionLines(largeMapRealCenter, trackingPos, trackingHeight);
                this.DrawTgtIcons(largeMapRealCenter, trackingPos, trackingHeight, largeMapModifiedZoom * 5f);
                this.DrawMapIcons(largeMapRealCenter, trackingPos, trackingHeight, largeMapModifiedZoom * 5f);
                this.DrawEntityPaths(largeMapRealCenter, trackingPos, trackingHeight);
                ImGui.End();
            }

            if (miniMap.IsVisible)
            {
                if (this.miniMapDiagonalLength <= 0)
                {
                    this.UpdateMiniMapDetails();
                }

                Helper.DiagonalLength = this.miniMapDiagonalLength;
                // Calibrated baseline baked in so MiniMapZoomMultiplier = 1.0 produces correct placement.
                const float MiniMapZoomBaseline = 0.748f;
                Helper.Scale = miniMap.Zoom * this.Settings.MiniMapZoomMultiplier * MiniMapZoomBaseline;
                var miniMapCenter = miniMap.Position +
                    (miniMap.Size / 2) +
                    miniMap.DefaultShift +
                    miniMap.Shift;
                // Calibrated X bias baked in so MiniMapXOffset defaults to 0.
                const float MiniMapXBias = -5f;
                miniMapCenter.X += MiniMapXBias + this.Settings.MiniMapXOffset;
                ImGui.SetNextWindowPos(miniMap.Position);
                ImGui.SetNextWindowSize(miniMap.Size);
                ImGui.SetNextWindowBgAlpha(0f);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
                ImGui.Begin("###minimapRadar", ImGuiHelper.TransparentWindowFlags);
                ImGui.PopStyleVar();
                this.DrawTgtIcons(miniMapCenter, trackingPos, trackingHeight, miniMap.Zoom);
                this.DrawMapIcons(miniMapCenter, trackingPos, trackingHeight, miniMap.Zoom);
                this.DrawEntityPaths(miniMapCenter, trackingPos, trackingHeight);
                ImGui.End();
            }
        }

        /// <inheritdoc/>
        public override void OnDisable()
        {
            this.onMove?.Cancel();
            this.onForegroundChange?.Cancel();
            this.onGameClose?.Cancel();
            this.onAreaChange?.Cancel();
            this.onMove = null;
            this.onForegroundChange = null;
            this.onGameClose = null;
            this.onAreaChange = null;
            this.CleanUpRadarPluginCaches();
        }

        /// <inheritdoc/>
        public override void OnEnable(bool isGameOpened)
        {
            if (!isGameOpened)
            {
                this.skipOneSettingChange = true;
            }

            if (File.Exists(this.SettingPathname))
            {
                var content = File.ReadAllText(this.SettingPathname);
                this.Settings = JsonConvert.DeserializeObject<RadarSettings>(content) ?? new RadarSettings();
            }

            if (File.Exists(this.ImportantTgtPathName))
            {
                var tgtfiles = File.ReadAllText(this.ImportantTgtPathName);
                this.Settings.ImportantTgts = JsonConvert.DeserializeObject
                    <Dictionary<string, Dictionary<string, string>>>(tgtfiles)
                    ?? new Dictionary<string, Dictionary<string, string>>();
            }

            if (File.Exists(this.BossArenaTgtPathName))
            {
                var bossfiles = File.ReadAllText(this.BossArenaTgtPathName);
                this.Settings.BossArenaTgts = JsonConvert.DeserializeObject
                    <Dictionary<string, string>>(bossfiles) ?? new Dictionary<string, string>();
            }

            if (File.Exists(this.StairsTgtPathName))
            {
                var stairsfiles = File.ReadAllText(this.StairsTgtPathName);
                this.Settings.StairsTgts = JsonConvert.DeserializeObject
                    <Dictionary<string, string>>(stairsfiles) ?? new Dictionary<string, string>();
            }

            this.Settings.AddDefaultIcons(this.DllDirectory);

            this.onMove = CoroutineHandler.Start(this.OnMove());
            this.onForegroundChange = CoroutineHandler.Start(this.OnForegroundChange());
            this.onGameClose = CoroutineHandler.Start(this.OnClose());
            this.onAreaChange = CoroutineHandler.Start(this.ClearCachesAndUpdateAreaInfo());
            this.GenerateMapTexture();
        }

        /// <inheritdoc/>
        public override void SaveSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.SettingPathname) ?? string.Empty);
            var settingsData = JsonConvert.SerializeObject(this.Settings, Formatting.Indented);
            File.WriteAllText(this.SettingPathname, settingsData);

            if (this.Settings.ImportantTgts.Count > 0)
            {
                var tgtfiles = JsonConvert.SerializeObject(
                    this.Settings.ImportantTgts, Formatting.Indented);
                File.WriteAllText(this.ImportantTgtPathName, tgtfiles);
            }

            if (this.Settings.BossArenaTgts.Count > 0)
            {
                var bossfiles = JsonConvert.SerializeObject(
                    this.Settings.BossArenaTgts, Formatting.Indented);
                File.WriteAllText(this.BossArenaTgtPathName, bossfiles);
            }

            if (this.Settings.StairsTgts.Count > 0)
            {
                var stairsfiles = JsonConvert.SerializeObject(
                    this.Settings.StairsTgts, Formatting.Indented);
                File.WriteAllText(this.StairsTgtPathName, stairsfiles);
            }
        }

        private void DrawLargeMap(Vector2 mapCenter, Vector2 trackingPos, float trackingHeight)
        {
            if (!this.Settings.DrawWalkableMap)
            {
                return;
            }

            if (this.walkableMapTexture == IntPtr.Zero)
            {
                return;
            }

            var rectf = new RectangleF(
                -trackingPos.X,
                -trackingPos.Y,
                this.walkableMapDimension.X,
                this.walkableMapDimension.Y);

            var p1 = Helper.DeltaInWorldToMapDelta(
                new Vector2(rectf.Left, rectf.Top), -trackingHeight);
            var p2 = Helper.DeltaInWorldToMapDelta(
                new Vector2(rectf.Right, rectf.Top), -trackingHeight);
            var p3 = Helper.DeltaInWorldToMapDelta(
                new Vector2(rectf.Right, rectf.Bottom), -trackingHeight);
            var p4 = Helper.DeltaInWorldToMapDelta(
                new Vector2(rectf.Left, rectf.Bottom), -trackingHeight);
            p1 += mapCenter;
            p2 += mapCenter;
            p3 += mapCenter;
            p4 += mapCenter;

            if (this.Settings.DrawMapInCull)
            {
                ImGui.GetWindowDrawList().AddImageQuad(this.walkableMapTexture, p1, p2, p3, p4);
            }
            else
            {
                ImGui.GetBackgroundDrawList().AddImageQuad(this.walkableMapTexture, p1, p2, p3, p4);
            }
        }

        private void DrawTgtFiles(Vector2 mapCenter, Vector2 trackingPos, float trackingHeight)
        {
            var col = ImGuiHelper.Color(
                (uint)(this.Settings.POIColor.X * 255),
                (uint)(this.Settings.POIColor.Y * 255),
                (uint)(this.Settings.POIColor.Z * 255),
                (uint)(this.Settings.POIColor.W * 255));

            ImDrawListPtr fgDraw;
            if (this.Settings.DrawPOIInCull)
            {
                fgDraw = ImGui.GetWindowDrawList();
            }
            else
            {
                fgDraw = ImGui.GetBackgroundDrawList();
            }

            var currentAreaInstance = Core.States.InGameStateObject.CurrentAreaInstance;
            var pPos = trackingPos;
            var clipMin = ImGui.GetWindowPos();
            var clipMax = clipMin + ImGui.GetWindowSize();

            void drawString(string text, Vector2 location, Vector2 stringImGuiSize, bool drawBackground)
            {
                float height = 0;
                if (location.X < currentAreaInstance.GridHeightData[0].Length &&
                    location.Y < currentAreaInstance.GridHeightData.Length)
                {
                    height = currentAreaInstance.GridHeightData[(int)location.Y][(int)location.X];
                }

                var fpos = Helper.DeltaInWorldToMapDelta(
                    location - pPos, -trackingHeight + height);
                var textMin = mapCenter + fpos - stringImGuiSize;
                var textMax = mapCenter + fpos + stringImGuiSize;
                if (textMax.X < clipMin.X || textMin.X > clipMax.X || textMax.Y < clipMin.Y || textMin.Y > clipMax.Y)
                {
                    return;
                }

                if (drawBackground)
                {
                    fgDraw.AddRectFilled(
                        textMin,
                        textMax,
                        ImGuiHelper.Color(0, 0, 0, 200));
                }

                fgDraw.AddText(
                    ImGui.GetFont(),
                    ImGui.GetFontSize(),
                    textMin,
                    col,
                    text);
            }

            if (this.isAddNewPOIHeaderOpened)
            {
                var counter = 0;
                foreach (var tgtKV in currentAreaInstance.TgtTilesLocations)
                {
                    if (!(this.Settings.POIFrequencyFilter > 0 &&
                        tgtKV.Value.Count > this.Settings.POIFrequencyFilter))
                    {
                        if (!this.poiIndexHalfSizeCache.TryGetValue(counter, out var tgtKImGuiSize))
                        {
                            tgtKImGuiSize = ImGui.CalcTextSize(counter.ToString()) / 2;
                            this.poiIndexHalfSizeCache[counter] = tgtKImGuiSize;
                        }

                        for (var i = 0; i < tgtKV.Value.Count; i++)
                        {
                            drawString(counter.ToString(), tgtKV.Value[i], tgtKImGuiSize, false);
                        }
                    }

                    counter++;
                }
            }
            else if (this.Settings.ShowImportantPOI)
            {
                if (this.Settings.ImportantTgts.TryGetValue(this.currentAreaName, out var importantTgtsOfCurrentArea))
                {
                    foreach (var tile in importantTgtsOfCurrentArea)
                    {
                        if (currentAreaInstance.TgtTilesLocations.TryGetValue(tile.Key, out var locations))
                        {
                            var strSize = this.GetTextHalfSize(tile.Value);
                            for (var i = 0; i < locations.Count; i++)
                            {
                                drawString(tile.Value, locations[i], strSize, this.Settings.EnablePOIBackground);
                            }
                        }
                    }
                }

                if (this.Settings.ImportantTgts.TryGetValue("common", out var importantTgtsOfAllAreas))
                {
                    foreach (var tile in importantTgtsOfAllAreas)
                    {
                        if (currentAreaInstance.TgtTilesLocations.TryGetValue(tile.Key, out var locations))
                        {
                            var strSize = this.GetTextHalfSize(tile.Value);
                            for (var i = 0; i < locations.Count; i++)
                            {
                                drawString(tile.Value, locations[i], strSize, this.Settings.EnablePOIBackground);
                            }
                        }
                    }
                }
            }
        }

        private void DrawDirectionLines(Vector2 mapCenter, Vector2 trackingPos, float trackingHeight)
        {
            var showStraight = this.Settings.ShowStraightLine;
            var showSmooth = this.Settings.ShowSmoothPath;
            if ((!showStraight && !showSmooth) || !this.Settings.ShowImportantPOI)
            {
                return;
            }

            var currentAreaInstance = Core.States.InGameStateObject.CurrentAreaInstance;
            if (!currentAreaInstance.Player.TryGetComponent<Render>(out var playerRender))
            {
                return;
            }

            var walkableData = currentAreaInstance.GridWalkableData;
            var bytesPerRow = currentAreaInstance.TerrainMetadata.BytesPerRow;
            if (walkableData == null || walkableData.Length == 0 || bytesPerRow <= 0)
            {
                return;
            }

            var actualPlayerPos = new Vector2(playerRender.GridPosition.X, playerRender.GridPosition.Y);
            var pPos = actualPlayerPos;
            var gridHeightData = currentAreaInstance.GridHeightData;

            // Build door-override map: open doors force their cells to walkable
            var doorOverrides = LineWalker.BuildDoorOverrideMap(currentAreaInstance);

            ImDrawListPtr fgDraw;
            if (this.Settings.DrawPOIInCull)
            {
                fgDraw = ImGui.GetWindowDrawList();
            }
            else
            {
                fgDraw = ImGui.GetBackgroundDrawList();
            }

            var clearColor = ImGuiHelper.Color(0, 255, 0, 220);
            var blockedColor = ImGuiHelper.Color(255, 60, 60, 220);
            var thickness = this.Settings.DirectionLineThickness;
            const float ArrowSize = 9f;

            // Predefined palette of distinct colors for POI paths, cycled per POI
            var poiPalette = new[]
            {
                ImGuiHelper.Color(0, 220, 255, 220),   // cyan
                ImGuiHelper.Color(255, 200, 0, 220),    // gold
                ImGuiHelper.Color(255, 105, 180, 220),  // hot pink
                ImGuiHelper.Color(0, 255, 127, 220),    // spring green
                ImGuiHelper.Color(255, 99, 71, 220),    // tomato
                ImGuiHelper.Color(123, 104, 238, 220),  // medium slate blue
                ImGuiHelper.Color(255, 165, 0, 220),    // orange
                ImGuiHelper.Color(50, 205, 50, 220),    // lime green
            };
            var poiColorIndex = 0;

            // --- Collect POI snapshot ---
            var poiSnapshot = new List<(string cacheKey, Vector2 gridPos)>();

            void CollectFrom(Dictionary<string, string> tileDict, string prefix)
            {
                foreach (var tile in tileDict)
                {
                    if (currentAreaInstance.TgtTilesLocations.TryGetValue(tile.Key, out var locations))
                    {
                        for (var i = 0; i < locations.Count; i++)
                        {
                            poiSnapshot.Add(($"{prefix}|{tile.Key}|{i}", locations[i]));
                        }
                    }
                }
            }

            if (this.Settings.ImportantTgts.TryGetValue(this.currentAreaName, out var importantTgtsOfCurrentArea))
            {
                CollectFrom(importantTgtsOfCurrentArea, "area");
            }

            if (this.Settings.ImportantTgts.TryGetValue("common", out var importantTgtsOfAllAreas))
            {
                CollectFrom(importantTgtsOfAllAreas, "common");
            }

            if (poiSnapshot.Count == 0)
            {
                return;
            }

            // --- Throttled background pathfinding ---
            var now = Environment.TickCount64;
            var forceFull = now >= this.nextPoiFullRecomputeTime;
            var shouldRecompute = now >= this.nextPoiRecomputeTime;
            if (forceFull)
            {
                this.nextPoiFullRecomputeTime = now + this.Settings.PathFullRecomputeIntervalMs;
                shouldRecompute = true;
            }
            if (shouldRecompute)
            {
                this.nextPoiRecomputeTime = now + this.Settings.PathRecomputeIntervalMs;

                // Only launch if no previous task is still running
                if (this.pendingPathTask == null || this.pendingPathTask.IsCompleted)
                {
                    var snap = poiSnapshot;
                    var wd = walkableData;
                    var bpr = bytesPerRow;
                    var pp = pPos;
                    var doors = doorOverrides;
                    var segs = forceFull ? 0 : this.Settings.PathRecomputeSegments;
                    var oldCache = this.poiPathCache;
                    this.pendingPathTask = Task.Run(() =>
                    {
                        var newCache = new Dictionary<string, List<Vector2>?>();
                        foreach (var (key, pos) in snap)
                        {
                            oldCache.TryGetValue(key, out var prev);
                            newCache[key] = ComputePath(wd, bpr, pp, pos, doors, prev, segs);
                        }

                        // Atomically swap the cache — render thread sees old or new, never torn
                        Interlocked.Exchange(ref this.poiPathCache, newCache);
                    });
                }
            }

            // --- Draw each POI from cache ---
            foreach (var (cacheKey, gridPos) in poiSnapshot)
            {
                var paletteColor = poiPalette[poiColorIndex % poiPalette.Length];
                poiColorIndex++;
                // Get terrain height at the POI location
                float poiHeight = 0;
                if (gridPos.X < gridHeightData[0].Length &&
                    gridPos.Y < gridHeightData.Length)
                {
                    poiHeight = gridHeightData[(int)gridPos.Y][(int)gridPos.X];
                }

                var poiFpos = Helper.DeltaInWorldToMapDelta(
                    gridPos - trackingPos, -trackingHeight + poiHeight);
                var poiScreen = mapCenter + poiFpos;
                var playerScreen = mapCenter + Helper.DeltaInWorldToMapDelta(
                    actualPlayerPos - trackingPos, -trackingHeight + playerRender.TerrainHeight);

                // --- Straight-line arrow ---
                if (showStraight)
                {
                    var lineResult = LineWalker.CheckLine(walkableData, bytesPerRow, pPos, gridPos, doorOverrides);
                    var color = lineResult.IsClear ? clearColor : blockedColor;

                    fgDraw.AddLine(playerScreen, poiScreen, color, thickness);

                    var dir = poiScreen - playerScreen;
                    var len = dir.Length();
                    if (len > 0.5f)
                    {
                        dir /= len;
                        var perp = new Vector2(-dir.Y, dir.X);
                        var tip = poiScreen;
                        var baseCenter = tip - (dir * ArrowSize);
                        var leftWing = baseCenter + (perp * (ArrowSize * 0.45f));
                        var rightWing = baseCenter - (perp * (ArrowSize * 0.45f));
                        fgDraw.AddTriangleFilled(leftWing, rightWing, tip, color);
                    }
                }

                // --- A* smooth path from cache ---
                if (showSmooth &&
                    this.poiPathCache.TryGetValue(cacheKey, out var cachedPath) &&
                    cachedPath != null && cachedPath.Count > 1)
                {
                    var prevScreen = playerScreen;
                    for (var pi = 1; pi < cachedPath.Count; pi++)
                    {
                        var pt = cachedPath[pi];
                        float ptHeight = 0;
                        var ix = (int)pt.X;
                        var iy = (int)pt.Y;
                        if (ix >= 0 && ix < gridHeightData[0].Length &&
                            iy >= 0 && iy < gridHeightData.Length)
                        {
                            ptHeight = gridHeightData[iy][ix];
                        }

                        var ptFpos = Helper.DeltaInWorldToMapDelta(pt - trackingPos, -trackingHeight + ptHeight);
                        var ptScreen = mapCenter + ptFpos;
                        fgDraw.AddLine(prevScreen, ptScreen, paletteColor, thickness + 1f);
                        prevScreen = ptScreen;
                    }
                }
            }
        }

        private void DrawTgtIcons(Vector2 mapCenter, Vector2 trackingPos, float trackingHeight, float iconSizeMultiplier)
        {
            var fgDraw = ImGui.GetWindowDrawList();
            var currentAreaInstance = Core.States.InGameStateObject.CurrentAreaInstance;

            foreach (var tgtKV in currentAreaInstance.TgtTilesLocations)
            {
                if (tgtKV.Key.StartsWith(TempleTgtPrefix) && tgtKV.Key.EndsWith(":1-y:1"))
                {
                    if (!this.Settings.TempleIcons.TryGetValue("Vaal Ruins", out var templeIcon))
                    {
                        continue;
                    }

                    this.DrawIconAtTgtLocations(fgDraw, mapCenter, trackingPos, trackingHeight, tgtKV.Value, templeIcon, iconSizeMultiplier, shiftUp: true);
                }
                else if (tgtKV.Key.StartsWith(RunestoneTgtPrefix) && tgtKV.Key.EndsWith(":1-y:1"))
                {
                    if (!this.Settings.RunestoneIcons.TryGetValue("Runestones", out var runestoneIcon))
                    {
                        continue;
                    }

                    this.DrawIconAtTgtLocations(fgDraw, mapCenter, trackingPos, trackingHeight, tgtKV.Value, runestoneIcon, iconSizeMultiplier, shiftUp: true);
                }
                else if (this.Settings.BossArenaTgts.ContainsKey(tgtKV.Key))
                {
                    if (!this.Settings.BossIcons.TryGetValue("Boss Arena", out var bossIcon))
                    {
                        continue;
                    }

                    this.DrawIconAtTgtLocations(fgDraw, mapCenter, trackingPos, trackingHeight, tgtKV.Value, bossIcon, iconSizeMultiplier);
                }
                else if (this.Settings.StairsTgts.ContainsKey(tgtKV.Key))
                {
                    if (!this.Settings.BaseIcons.TryGetValue("Stairs", out var stairsIcon))
                    {
                        continue;
                    }

                    this.DrawIconAtTgtLocations(fgDraw, mapCenter, trackingPos, trackingHeight, tgtKV.Value, stairsIcon, iconSizeMultiplier);
                }
            }
        }

        private void DrawIconAtTgtLocations(
            ImDrawListPtr fgDraw,
            Vector2 mapCenter,
            Vector2 trackingPos,
            float trackingHeight,
            List<Vector2> locations,
            IconPicker icon,
            float iconSizeMultiplier,
            bool shiftUp = false)
        {
            var currentAreaInstance = Core.States.InGameStateObject.CurrentAreaInstance;
            for (var i = 0; i < locations.Count; i++)
            {
                var location = locations[i];
                float height = 0;
                if (location.X < currentAreaInstance.GridHeightData[0].Length &&
                    location.Y < currentAreaInstance.GridHeightData.Length)
                {
                    height = currentAreaInstance.GridHeightData[(int)location.Y][(int)location.X];
                }

                var fpos = Helper.DeltaInWorldToMapDelta(
                    location - trackingPos, -trackingHeight + height);
                var iconSizeMultiplierVector = new Vector2(iconSizeMultiplier);
                iconSizeMultiplierVector *= icon.IconScale;
                var offset = shiftUp ? new Vector2(0, iconSizeMultiplierVector.Y) : Vector2.Zero;
                fgDraw.AddImage(
                    icon.TexturePtr,
                    mapCenter + fpos - iconSizeMultiplierVector - offset,
                    mapCenter + fpos + iconSizeMultiplierVector - offset,
                    icon.UV0,
                    icon.UV1);
            }
        }

        private void DrawMapIcons(Vector2 mapCenter, Vector2 trackingPos, float trackingHeight, float iconSizeMultiplier)
        {
            var fgDraw = ImGui.GetWindowDrawList();
            var currentAreaInstance = Core.States.InGameStateObject.CurrentAreaInstance;

            var clipMin = ImGui.GetWindowPos();
            var clipMax = clipMin + ImGui.GetWindowSize();
            var clipPadding = iconSizeMultiplier * 4f;
            var pPos = trackingPos;

            var baseIcons = this.Settings.BaseIcons;
            var expeditionIcons = this.Settings.ExpeditionIcons;
            var breachIcons = this.Settings.BreachIcons;
            var deliriumIcons = this.Settings.DeliriumIcons;
            var poiMonsterIcons = this.Settings.POIMonsters;
            var otherImportantObjects = this.Settings.OtherImportantObjects;

            var npcIcon = baseIcons["NPC"];
            var specialNpcIcon = baseIcons["Special NPC"];
            var leaderIcon = baseIcons["Leader"];
            var playerIcon = baseIcons["Player"];
            var selfIcon = baseIcons["Self"];
            var allOtherChestIcon = baseIcons["All Other Chest"];
            var rareChestIcon = baseIcons["Rare Chests"];
            var magicChestIcon = baseIcons["Magic Chests"];
            var expeditionChestIcon = expeditionIcons["Generic Expedition Chests"];
            var breachChestIcon = breachIcons["Breach Chest"];
            var strongboxIcon = baseIcons["Strongbox"];
            var shrineIcon = baseIcons["Shrine"];
            var pinnacleBossHiddenIcon = baseIcons["Pinnacle Boss Not Attackable"];
            var friendlyIcon = baseIcons["Friendly"];
            var deliriumBombIcon = deliriumIcons["Delirium Bomb"];
            var deliriumSpawnerIcon = deliriumIcons["Delirium Spawner"];
            var normalMonsterIcon = baseIcons["Normal Monster"];
            var magicMonsterIcon = baseIcons["Magic Monster"];
            var rareMonsterIcon = baseIcons["Rare Monster"];
            var uniqueMonsterIcon = baseIcons["Unique Monster"];

            foreach (var entity in currentAreaInstance.AwakeEntities)
            {
                var entityValue = entity.Value;
                if (this.Settings.HideOutsideNetworkBubble && !entityValue.IsValid)
                {
                    continue;
                }

                if (entityValue.EntityState == EntityStates.Useless)
                {
                    continue;
                }

                if (!entityValue.TryGetComponent<Render>(out var entityRender))
                {
                    continue;
                }

                var ePos = new Vector2(entityRender.GridPosition.X, entityRender.GridPosition.Y);
                var fpos = Helper.DeltaInWorldToMapDelta(ePos - pPos, entityRender.TerrainHeight - trackingHeight);
                var screenPos = mapCenter + fpos;
                if (screenPos.X < clipMin.X - clipPadding || screenPos.X > clipMax.X + clipPadding ||
                    screenPos.Y < clipMin.Y - clipPadding || screenPos.Y > clipMax.Y + clipPadding)
                {
                    continue;
                }

                var iconSizeMultiplierVector = Vector2.One * iconSizeMultiplier;

                void DrawIcon(IconPicker icon)
                {
                    var scaled = iconSizeMultiplierVector * icon.IconScale;
                    fgDraw.AddImage(
                        icon.TexturePtr,
                        screenPos - scaled,
                        screenPos + scaled,
                        icon.UV0,
                        icon.UV1);
                }

                switch (entityValue.EntityType)
                {
                    case EntityTypes.NPC:
                        DrawIcon(entityValue.EntitySubtype == EntitySubtypes.SpecialNPC ? specialNpcIcon : npcIcon);
                        break;
                    case EntityTypes.Player:
                        if (entityValue.EntitySubtype == EntitySubtypes.PlayerOther)
                        {
                            if (this.Settings.ShowPlayersNames && entityValue.TryGetComponent<Player>(out var playerComp))
                            {
                                var pNameSizeH = this.GetTextHalfSize(playerComp.Name);
                                fgDraw.AddRectFilled(screenPos - pNameSizeH, screenPos + pNameSizeH,
                                    ImGuiHelper.Color(0, 0, 0, 200));
                                fgDraw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), screenPos - pNameSizeH,
                                    ImGuiHelper.Color(255, 128, 128, 255), playerComp.Name);
                            }
                            else
                            {
                                DrawIcon(entityValue.EntityState == EntityStates.PlayerLeader ? leaderIcon : playerIcon);
                            }
                        }
                        else
                        {
                            DrawIcon(selfIcon);
                        }

                        break;
                    case EntityTypes.Chest:
                        switch (entityValue.EntitySubtype)
                        {
                            case EntitySubtypes.None:
                                DrawIcon(allOtherChestIcon);
                                break;
                            case EntitySubtypes.ChestWithRareRarity:
                                DrawIcon(rareChestIcon);
                                break;
                            case EntitySubtypes.ChestWithMagicRarity:
                                DrawIcon(magicChestIcon);
                                break;
                            case EntitySubtypes.ExpeditionChest:
                                if (entityValue.Path.Contains("LeagueFaction") &&
                                    this.Settings.ExpeditionMarkerIcons.TryGetValue("Logbook", out var logbookIcon) &&
                                    logbookIcon.IconScale > 0)
                                {
                                    DrawIcon(logbookIcon);
                                }
                                else
                                {
                                    DrawIcon(expeditionChestIcon);
                                }

                                break;
                            case EntitySubtypes.BreachChest:
                                DrawIcon(breachChestIcon);
                                break;
                            case EntitySubtypes.Strongbox:
                                DrawIcon(strongboxIcon);
                                break;
                        }

                        break;
                    case EntityTypes.Shrine:
                        if ((entityValue.TryGetComponent<Shrine>(out var shrineComp) && shrineComp.IsUsed) ||
                            (entityValue.TryGetComponent<Targetable>(out var targ) && !targ.IsTargetable))
                        {
                            break;
                        }

                        DrawIcon(shrineIcon);
                        break;
                    case EntityTypes.Monster:
                        switch (entityValue.EntityState)
                        {
                            case EntityStates.None:
                                if (entityValue.EntitySubtype == EntitySubtypes.POIMonster)
                                {
                                    if (!poiMonsterIcons.TryGetValue(entityValue.EntityCustomGroup, out var poiIcon))
                                    {
                                        poiIcon = poiMonsterIcons[-1];
                                    }

                                    DrawIcon(poiIcon);
                                }
                                else if (entityValue.TryGetComponent<ObjectMagicProperties>(out var omp))
                                {
                                    DrawIcon(this.RarityToIconMapping(omp.Rarity, normalMonsterIcon, magicMonsterIcon, rareMonsterIcon, uniqueMonsterIcon));
                                }

                                break;
                            case EntityStates.PinnacleBossHidden:
                                DrawIcon(pinnacleBossHiddenIcon);
                                break;
                            case EntityStates.MonsterFriendly:
                                DrawIcon(friendlyIcon);
                                break;
                            default:
                                break;
                        }

                        break;
                    case EntityTypes.DeliriumBomb:
                        DrawIcon(deliriumBombIcon);
                        break;
                    case EntityTypes.DeliriumSpawner:
                        DrawIcon(deliriumSpawnerIcon);
                        break;
                    case EntityTypes.OtherImportantObjects:
                        if (entityValue.EntityCustomGroup == RadarSettings.ExpeditionMarkerGroup)
                        {
                            if (entityValue.TryGetComponent<MinimapIcon>(out var minimapIcon) &&
                                !string.IsNullOrEmpty(minimapIcon.IconName) &&
                                RadarSettings.ExpeditionMarkerIconNameMap.TryGetValue(minimapIcon.IconName, out var displayName) &&
                                this.Settings.ExpeditionMarkerIcons.TryGetValue(displayName, out var expMarkerIcon) &&
                                expMarkerIcon.IconScale > 0)
                            {
                                DrawIcon(expMarkerIcon);
                            }
                        }
                        else if (entityValue.EntityCustomGroup == RadarSettings.ExpeditionRemnantGroup)
                        {
                            if (entityValue.TryGetComponent<ObjectMagicProperties>(out var remnantOmp))
                            {
                                foreach (var modName in remnantOmp.ModNames)
                                {
                                    foreach (var (modSubstring, remnantDisplayName) in RadarSettings.ExpeditionRemnantModMap)
                                    {
                                        if (modName.Contains(modSubstring) &&
                                            this.Settings.ExpeditionRemnantIcons.TryGetValue(remnantDisplayName, out var remnantIcon) &&
                                            remnantIcon.IconScale > 0)
                                        {
                                            DrawIcon(remnantIcon);
                                            goto doneRemnant;
                                        }
                                    }
                                }
                                doneRemnant:;
                            }
                        }
                        else if (entityValue.EntityCustomGroup == RadarSettings.RuneEncounterGroup)
                        {
                            var hasMinimapIcon = entityValue.TryGetComponent<MinimapIcon>(out var runeMmIcon);

                            if (hasMinimapIcon &&
                                !string.IsNullOrEmpty(runeMmIcon!.IconName) &&
                                RadarSettings.RunestoneIconNameMap.TryGetValue(runeMmIcon.IconName, out var runeDisplayName) &&
                                this.Settings.RunestoneIcons.TryGetValue(runeDisplayName, out var runeIcon) &&
                                runeIcon.IconScale > 0)
                            {
                                DrawIcon(runeIcon);
                            }
                            else if (this.Settings.RunestoneIcons.TryGetValue("Runestone Encounter", out runeIcon) &&
                                     runeIcon.IconScale > 0)
                            {
                                DrawIcon(runeIcon);
                            }
                        }
                        else
                        {
                            if (!otherImportantObjects.TryGetValue(entityValue.EntityCustomGroup, out var mopoiIcon))
                            {
                                mopoiIcon = otherImportantObjects[-1];
                            }

                            DrawIcon(mopoiIcon);
                        }

                        break;
                    case EntityTypes.Renderable:
                        fgDraw.AddCircleFilled(screenPos, 3f, 0xFFFFFFFF);
                        break;
                }
            }
        }

        /// <summary>
        /// Collects entities whose icon has ShowPath enabled.
        /// Mirrors the icon-selection logic from DrawMapIcons but only gathers
        /// entities that need paths, without drawing anything.
        /// </summary>
        private void CollectEntityPaths()
        {
            this.entityPathSnapshot.Clear();
            this.tileIconPathSnapshot.Clear();

            if (!this.Settings.ShowEntityPaths)
            {
                return;
            }

            var currentAreaInstance = Core.States.InGameStateObject.CurrentAreaInstance;
            if (!currentAreaInstance.Player.TryGetComponent<Render>(out var playerRender))
            {
                return;
            }

            var pPos = new Vector2(playerRender.GridPosition.X, playerRender.GridPosition.Y);
            var baseIcons = this.Settings.BaseIcons;

            // Helper: if icon exists and has ShowPath, add to snapshot
            void TryAdd(uint entityId, Vector2 ePos, IconPicker icon)
            {
                if (icon != null && icon.ShowPath && icon.IconScale > 0)
                {
                    this.entityPathSnapshot.Add((entityId, ePos, icon.PathColor));
                }
            }

            foreach (var entity in currentAreaInstance.AwakeEntities)
            {
                var ev = entity.Value;
                if (this.Settings.HideOutsideNetworkBubble && !ev.IsValid)
                {
                    continue;
                }

                if (ev.EntityState == EntityStates.Useless)
                {
                    continue;
                }

                if (!ev.TryGetComponent<Render>(out var er))
                {
                    continue;
                }

                var ePos = new Vector2(er.GridPosition.X, er.GridPosition.Y);
                var eId = entity.Key.id;

                switch (ev.EntityType)
                {
                    case EntityTypes.NPC:
                        TryAdd(eId, ePos,
                            ev.EntitySubtype == EntitySubtypes.SpecialNPC
                                ? baseIcons["Special NPC"]
                                : baseIcons["NPC"]);
                        break;

                    case EntityTypes.Player:
                        if (ev.EntitySubtype == EntitySubtypes.PlayerOther)
                        {
                            TryAdd(eId, ePos,
                                ev.EntityState == EntityStates.PlayerLeader
                                    ? baseIcons["Leader"]
                                    : baseIcons["Player"]);
                        }

                        break;

                    case EntityTypes.Chest:
                        IconPicker? chestIcon = ev.EntitySubtype switch
                        {
                            EntitySubtypes.ChestWithRareRarity => baseIcons["Rare Chests"],
                            EntitySubtypes.ChestWithMagicRarity => baseIcons["Magic Chests"],
                            EntitySubtypes.BreachChest => this.Settings.BreachIcons["Breach Chest"],
                            EntitySubtypes.Strongbox => baseIcons["Strongbox"],
                            EntitySubtypes.ExpeditionChest => ev.Path.Contains("LeagueFaction") &&
                                this.Settings.ExpeditionMarkerIcons.TryGetValue("Logbook", out var lb) && lb.IconScale > 0
                                    ? lb
                                    : this.Settings.ExpeditionIcons["Generic Expedition Chests"],
                            _ => baseIcons["All Other Chest"],
                        };
                        TryAdd(eId, ePos, chestIcon);
                        break;

                    case EntityTypes.Shrine:
                        if ((ev.TryGetComponent<Shrine>(out var sc) && sc.IsUsed) ||
                            (ev.TryGetComponent<Targetable>(out var t) && !t.IsTargetable))
                        {
                            break;
                        }

                        TryAdd(eId, ePos, baseIcons["Shrine"]);
                        break;

                    case EntityTypes.Monster:
                        switch (ev.EntityState)
                        {
                            case EntityStates.None:
                                if (ev.EntitySubtype == EntitySubtypes.POIMonster)
                                {
                                    if (!this.Settings.POIMonsters.TryGetValue(ev.EntityCustomGroup, out var poiIcon))
                                    {
                                        poiIcon = this.Settings.POIMonsters[-1];
                                    }

                                    TryAdd(eId, ePos, poiIcon);
                                }
                                else if (ev.TryGetComponent<ObjectMagicProperties>(out var omp))
                                {
                                    TryAdd(eId, ePos, this.RarityToIconMapping(
                                        omp.Rarity,
                                        baseIcons["Normal Monster"],
                                        baseIcons["Magic Monster"],
                                        baseIcons["Rare Monster"],
                                        baseIcons["Unique Monster"]));
                                }

                                break;

                            case EntityStates.PinnacleBossHidden:
                                TryAdd(eId, ePos, baseIcons["Pinnacle Boss Not Attackable"]);
                                break;

                            case EntityStates.MonsterFriendly:
                                TryAdd(eId, ePos, baseIcons["Friendly"]);
                                break;
                        }

                        break;

                    case EntityTypes.DeliriumBomb:
                        TryAdd(eId, ePos, this.Settings.DeliriumIcons["Delirium Bomb"]);
                        break;

                    case EntityTypes.DeliriumSpawner:
                        TryAdd(eId, ePos, this.Settings.DeliriumIcons["Delirium Spawner"]);
                        break;

                    case EntityTypes.OtherImportantObjects:
                        if (ev.EntityCustomGroup == RadarSettings.ExpeditionMarkerGroup)
                        {
                            if (ev.TryGetComponent<MinimapIcon>(out var mmIcon) &&
                                !string.IsNullOrEmpty(mmIcon.IconName) &&
                                RadarSettings.ExpeditionMarkerIconNameMap.TryGetValue(
                                    mmIcon.IconName, out var displayName) &&
                                this.Settings.ExpeditionMarkerIcons.TryGetValue(
                                    displayName, out var expIcon) &&
                                expIcon.IconScale > 0)
                            {
                                TryAdd(eId, ePos, expIcon);
                            }
                        }
                        else if (ev.EntityCustomGroup == RadarSettings.ExpeditionRemnantGroup)
                        {
                            if (ev.TryGetComponent<ObjectMagicProperties>(out var remnantOmp))
                            {
                                foreach (var modName in remnantOmp.ModNames)
                                {
                                    foreach (var (modSubstring, remnantDisplayName) in
                                        RadarSettings.ExpeditionRemnantModMap)
                                    {
                                        if (modName.Contains(modSubstring) &&
                                            this.Settings.ExpeditionRemnantIcons.TryGetValue(
                                                remnantDisplayName, out var remnantIcon) &&
                                            remnantIcon.IconScale > 0)
                                        {
                                            TryAdd(eId, ePos, remnantIcon);
                                            goto doneRemnantCollect;
                                        }
                                    }
                                }

                                doneRemnantCollect:;
                            }
                        }
                        else if (ev.EntityCustomGroup == RadarSettings.RuneEncounterGroup)
                        {
                            if (ev.TryGetComponent<MinimapIcon>(out var runeMmIcon) &&
                                !string.IsNullOrEmpty(runeMmIcon.IconName) &&
                                RadarSettings.RunestoneIconNameMap.TryGetValue(
                                    runeMmIcon.IconName, out var runeDisplayName) &&
                                this.Settings.RunestoneIcons.TryGetValue(
                                    runeDisplayName, out var runeIcon) &&
                                runeIcon.IconScale > 0)
                            {
                                TryAdd(eId, ePos, runeIcon);
                            }
                            else if (this.Settings.RunestoneIcons.TryGetValue(
                                         "Runestone Encounter", out runeIcon) &&
                                     runeIcon.IconScale > 0)
                            {
                                TryAdd(eId, ePos, runeIcon);
                            }
                        }
                        else
                        {
                            if (!this.Settings.OtherImportantObjects.TryGetValue(
                                    ev.EntityCustomGroup, out var mopoiIcon))
                            {
                                mopoiIcon = this.Settings.OtherImportantObjects[-1];
                            }

                            TryAdd(eId, ePos, mopoiIcon);
                        }

                        break;

                    // Renderable entities have no icon — skip
                }
            }

            // --- Terrain-tile icon paths (Temple, Runestone, Boss Arena, Stairs) ---
            var tgtLocations = currentAreaInstance.TgtTilesLocations;
            foreach (var tgtKV in tgtLocations)
            {
                IconPicker? tileIcon = null;

                if (tgtKV.Key.StartsWith(TempleTgtPrefix) && tgtKV.Key.EndsWith(":1-y:1"))
                {
                    this.Settings.TempleIcons.TryGetValue("Vaal Ruins", out tileIcon);
                }
                else if (tgtKV.Key.StartsWith(RunestoneTgtPrefix) && tgtKV.Key.EndsWith(":1-y:1"))
                {
                    this.Settings.RunestoneIcons.TryGetValue("Runestones", out tileIcon);
                }
                else if (this.Settings.BossArenaTgts.ContainsKey(tgtKV.Key))
                {
                    this.Settings.BossIcons.TryGetValue("Boss Arena", out tileIcon);
                }
                else if (this.Settings.StairsTgts.ContainsKey(tgtKV.Key))
                {
                    this.Settings.BaseIcons.TryGetValue("Stairs", out tileIcon);
                }

                if (tileIcon != null && tileIcon.ShowPath && tileIcon.IconScale > 0)
                {
                    for (var i = 0; i < tgtKV.Value.Count; i++)
                    {
                        this.tileIconPathSnapshot.Add((
                            $"tile|{tgtKV.Key}|{i}",
                            tgtKV.Value[i],
                            tileIcon.PathColor));
                    }
                }
            }
        }

        /// <summary>
        /// Rebuilds entity paths on a background thread (throttled).
        /// </summary>
        private void RebuildEntityPaths()
        {
            if (!this.Settings.ShowEntityPaths)
            {
                return;
            }

            var now = Environment.TickCount64;
            var forceFull = now >= this.nextEntityFullRecomputeTime;
            if (now < this.nextEntityRecomputeTime && !forceFull)
            {
                return;
            }

            if (forceFull)
            {
                this.nextEntityFullRecomputeTime = now + this.Settings.PathFullRecomputeIntervalMs;
            }

            this.nextEntityRecomputeTime = now + this.Settings.PathRecomputeIntervalMs;

            if (this.entityPathSnapshot.Count == 0)
            {
                return;
            }

            if (this.pendingEntityPathTask != null && !this.pendingEntityPathTask.IsCompleted)
            {
                return;
            }

            var currentAreaInstance = Core.States.InGameStateObject.CurrentAreaInstance;
            if (!currentAreaInstance.Player.TryGetComponent<Render>(out var playerRender))
            {
                return;
            }

            var walkableData = currentAreaInstance.GridWalkableData;
            var bytesPerRow = currentAreaInstance.TerrainMetadata.BytesPerRow;
            if (walkableData == null || walkableData.Length == 0 || bytesPerRow <= 0)
            {
                return;
            }

            var pPos = new Vector2(playerRender.GridPosition.X, playerRender.GridPosition.Y);
            var doorOverrides = LineWalker.BuildDoorOverrideMap(currentAreaInstance);

            var snap = this.entityPathSnapshot.ToArray();
            var tileSnap = this.tileIconPathSnapshot.ToArray();
            var segs = forceFull ? 0 : this.Settings.PathRecomputeSegments;
            var oldEntityCache = this.entityPathCache;
            var oldTileCache = this.tileIconPathCache;
            var wd = walkableData;
            var bpr = bytesPerRow;
            var pp = pPos;
            var doors = doorOverrides;

            this.pendingEntityPathTask = Task.Run(() =>
            {
                var newCache = new Dictionary<uint, List<Vector2>?>();
                foreach (var (id, pos, _) in snap)
                {
                    oldEntityCache.TryGetValue(id, out var prev);
                    newCache[id] = ComputePath(wd, bpr, pp, pos, doors, prev, segs);
                }

                var newTileCache = new Dictionary<string, List<Vector2>?>();
                foreach (var (key, pos, _) in tileSnap)
                {
                    oldTileCache.TryGetValue(key, out var prev);
                    newTileCache[key] = ComputePath(wd, bpr, pp, pos, doors, prev, segs);
                }

                Interlocked.Exchange(ref this.entityPathCache, newCache);
                Interlocked.Exchange(ref this.tileIconPathCache, newTileCache);
            });
        }

        /// <summary>
        /// Draws cached entity paths. Must be called after CollectEntityPaths.
        /// </summary>
        private void DrawEntityPaths(Vector2 mapCenter, Vector2 trackingPos, float trackingHeight)
        {
            if (!this.Settings.ShowEntityPaths ||
                (this.entityPathSnapshot.Count == 0 && this.tileIconPathSnapshot.Count == 0))
            {
                return;
            }

            var currentAreaInstance = Core.States.InGameStateObject.CurrentAreaInstance;
            var gridHeightData = currentAreaInstance.GridHeightData;

            ImDrawListPtr fgDraw;
            if (this.Settings.DrawPOIInCull)
            {
                fgDraw = ImGui.GetWindowDrawList();
            }
            else
            {
                fgDraw = ImGui.GetBackgroundDrawList();
            }

            var thickness = this.Settings.IconPathThickness;

            foreach (var (entityId, _, color) in this.entityPathSnapshot)
            {
                if (!this.entityPathCache.TryGetValue(entityId, out var cachedPath) ||
                    cachedPath == null || cachedPath.Count <= 1)
                {
                    continue;
                }

                var pathColor = ImGuiHelper.Color(
                    (uint)(color.X * 255),
                    (uint)(color.Y * 255),
                    (uint)(color.Z * 255),
                    (uint)(color.W * 255));

                var startPt = cachedPath[0];
                float startPtHeight = 0;
                var sx = (int)startPt.X;
                var sy = (int)startPt.Y;
                if (sx >= 0 && sx < gridHeightData[0].Length &&
                    sy >= 0 && sy < gridHeightData.Length)
                {
                    startPtHeight = gridHeightData[sy][sx];
                }
                var prevScreen = mapCenter + Helper.DeltaInWorldToMapDelta(startPt - trackingPos, -trackingHeight + startPtHeight);

                for (var pi = 1; pi < cachedPath.Count; pi++)
                {
                    var pt = cachedPath[pi];
                    float ptHeight = 0;
                    var ix = (int)pt.X;
                    var iy = (int)pt.Y;
                    if (ix >= 0 && ix < gridHeightData[0].Length &&
                        iy >= 0 && iy < gridHeightData.Length)
                    {
                        ptHeight = gridHeightData[iy][ix];
                    }

                    var ptFpos = Helper.DeltaInWorldToMapDelta(pt - trackingPos, -trackingHeight + ptHeight);
                    var ptScreen = mapCenter + ptFpos;
                    fgDraw.AddLine(prevScreen, ptScreen, pathColor, thickness + 1f);
                    prevScreen = ptScreen;
                }
            }

            // --- Terrain-tile icon paths ---
            foreach (var (cacheKey, _, color) in this.tileIconPathSnapshot)
            {
                if (!this.tileIconPathCache.TryGetValue(cacheKey, out var cachedPath) ||
                    cachedPath == null || cachedPath.Count <= 1)
                {
                    continue;
                }

                var pathColor = ImGuiHelper.Color(
                    (uint)(color.X * 255),
                    (uint)(color.Y * 255),
                    (uint)(color.Z * 255),
                    (uint)(color.W * 255));

                var startPt = cachedPath[0];
                float startPtHeight = 0;
                var sx = (int)startPt.X;
                var sy = (int)startPt.Y;
                if (sx >= 0 && sx < gridHeightData[0].Length &&
                    sy >= 0 && sy < gridHeightData.Length)
                {
                    startPtHeight = gridHeightData[sy][sx];
                }
                var prevScreen = mapCenter + Helper.DeltaInWorldToMapDelta(startPt - trackingPos, -trackingHeight + startPtHeight);

                for (var pi = 1; pi < cachedPath.Count; pi++)
                {
                    var pt = cachedPath[pi];
                    float ptHeight = 0;
                    var ix = (int)pt.X;
                    var iy = (int)pt.Y;
                    if (ix >= 0 && ix < gridHeightData[0].Length &&
                        iy >= 0 && iy < gridHeightData.Length)
                    {
                        ptHeight = gridHeightData[iy][ix];
                    }

                    var ptFpos = Helper.DeltaInWorldToMapDelta(pt - trackingPos, -trackingHeight + ptHeight);
                    var ptScreen = mapCenter + ptFpos;
                    fgDraw.AddLine(prevScreen, ptScreen, pathColor, thickness + 1f);
                    prevScreen = ptScreen;
                }
            }
        }

        /// <summary>
        /// Smart path computation with optional partial recompute.
        /// If segments > 0 and a previous path exists, only recomputes the first
        /// N segments and splices the tail of the old path.
        /// </summary>
        private static List<Vector2>? ComputePath(
            byte[] walkableData,
            int bytesPerRow,
            Vector2 playerPos,
            Vector2 targetPos,
            HashSet<(int, int)>? doorOverrides,
            List<Vector2>? previousPath,
            int segments)
        {
            // Full recompute if segments is 0, no previous path, or path is too short
            if (segments <= 0 || previousPath == null || previousPath.Count <= segments + 1)
            {
                var lineResult = LineWalker.CheckLine(walkableData, bytesPerRow, playerPos, targetPos, doorOverrides);
                if (lineResult.IsClear)
                {
                    return new List<Vector2> { playerPos, targetPos };
                }

                return Pathfinder.FindPath(walkableData, bytesPerRow, playerPos, targetPos, doorOverrides);
            }

            // Partial recompute: path from player to old path point N, then splice
            var splicePoint = previousPath[segments];
            var partialPath = Pathfinder.FindPath(walkableData, bytesPerRow, playerPos, splicePoint, doorOverrides);

            if (partialPath == null || partialPath.Count == 0)
            {
                // Partial path failed — fall back to full recompute
                var lineResult = LineWalker.CheckLine(walkableData, bytesPerRow, playerPos, targetPos, doorOverrides);
                if (lineResult.IsClear)
                {
                    return new List<Vector2> { playerPos, targetPos };
                }

                return Pathfinder.FindPath(walkableData, bytesPerRow, playerPos, targetPos, doorOverrides);
            }

            // Splice: partial path + tail of old path (skip the splice point to avoid duplicate)
            var result = new List<Vector2>(partialPath.Count + previousPath.Count - segments);
            result.AddRange(partialPath);
            for (var i = segments + 1; i < previousPath.Count; i++)
            {
                result.Add(previousPath[i]);
            }

            return result;
        }

        private IEnumerator<Wait> ClearCachesAndUpdateAreaInfo()
        {
            while (true)
            {
                yield return new Wait(RemoteEvents.AreaChanged);
                this.CleanUpRadarPluginCaches();
                this.currentAreaName = Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails.Id;
                this.GenerateMapTexture();
                this.LogBossArenaTgtMatches();
            }
        }

        private void LogBossArenaTgtMatches()
        {
            var currentAreaInstance = Core.States.InGameStateObject.CurrentAreaInstance;
            Console.WriteLine($"BossArena: area={this.currentAreaName}, TgtTilesLocations count={currentAreaInstance.TgtTilesLocations.Count}, BossArenaTgts count={this.Settings.BossArenaTgts.Count}");
            foreach (var bossTgt in this.Settings.BossArenaTgts)
            {
                if (currentAreaInstance.TgtTilesLocations.ContainsKey(bossTgt.Key))
                {
                    Console.WriteLine($"  BossArena MATCH: \"{bossTgt.Key}\"");
                }
            }
        }

        private IEnumerator<Wait> OnMove()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnMoved);
                this.UpdateMiniMapDetails();
                this.UpdateLargeMapDetails();
                if (this.Settings.MakeCullWindowFullScreen)
                {
                    this.Settings.CullWindowPos = Vector2.Zero;

                    this.Settings.CullWindowSize.X = Core.Process.WindowArea.Size.Width;
                    this.Settings.CullWindowSize.Y = Core.Process.WindowArea.Size.Height;
                    this.skipOneSettingChange = false;
                }
                else if (this.skipOneSettingChange)
                {
                    this.skipOneSettingChange = false;
                }
                else
                {
                    this.Settings.ModifyCullWindow = true;
                }
            }
        }

        private IEnumerator<Wait> OnClose()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnClose);
                this.skipOneSettingChange = true;
                this.CleanUpRadarPluginCaches();
            }
        }

        private IEnumerator<Wait> OnForegroundChange()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnForegroundChanged);
                this.UpdateMiniMapDetails();
                this.UpdateLargeMapDetails();
            }
        }

        private void UpdateMiniMapDetails()
        {
            // Scale the base-resolution diagonal with the window height so that
            // the world-to-pixel ratio tracks the game's own vertical scaling.
            // Changing only the window width does not affect the map scale.
            var map = Core.States.InGameStateObject.GameUi.MiniMap;
            var baseRes = GameOffsets.Objects.UiElement.UiElementBaseFuncs.BaseResolution;
            var baseDiag = Math.Sqrt((baseRes.X * baseRes.X) + (baseRes.Y * baseRes.Y));
            this.miniMapDiagonalLength = baseDiag * map.Size.Y / baseRes.Y;
        }

        private void UpdateLargeMapDetails()
        {
            var map = Core.States.InGameStateObject.GameUi.LargeMap;
            var baseRes = GameOffsets.Objects.UiElement.UiElementBaseFuncs.BaseResolution;
            var baseDiag = Math.Sqrt((baseRes.X * baseRes.X) + (baseRes.Y * baseRes.Y));
            this.largeMapDiagonalLength = baseDiag * map.Size.Y / baseRes.Y;
        }

        private void ReloadMapTexture()
        {
            this.RemoveMapTexture();
            this.GenerateMapTexture();
        }

        private void RemoveMapTexture()
        {
            this.walkableMapTexture = IntPtr.Zero;
            this.walkableMapDimension = Vector2.Zero;
            Core.Overlay.RemoveImage("walkable_map");
        }

        private void GenerateMapTexture()
        {
            if (Core.States.GameCurrentState is not (GameStateTypes.InGameState or GameStateTypes.EscapeState))
            {
                return;
            }

            var instance = Core.States.InGameStateObject.CurrentAreaInstance;
            var gridHeightData = instance.GridHeightData;
            var mapWalkableData = instance.GridWalkableData;
            var bytesPerRow = instance.TerrainMetadata.BytesPerRow;
            var worldToGridHeightMultiplier = instance.WorldToGridConvertor * 2f;
            if (bytesPerRow <= 0 || mapWalkableData == null || mapWalkableData.Length == 0 ||
                gridHeightData == null || gridHeightData.Length == 0)
            {
                // Diagnostic: log terrain metadata for areas with missing data (e.g. Sekhemas)
                var meta = instance.TerrainMetadata;
                Console.WriteLine(
                    $"[Radar] No terrain data for area {instance.AreaHash}. " +
                    $"TotalTiles=({meta.TotalTiles.X},{meta.TotalTiles.Y}) " +
                    $"BytesPerRow={meta.BytesPerRow} " +
                    $"WalkableLen={mapWalkableData?.Length ?? -1} " +
                    $"HeightLen={gridHeightData?.Length ?? -1} " +
                    $"TileHeightMul={meta.TileHeightMultiplier}");
                return;
            }

            var mapEdgeDetector = new MapEdgeDetector(mapWalkableData, bytesPerRow);
            var imageWidth = bytesPerRow * 2;
            var imageHeight = mapEdgeDetector.TotalRows;

            // Guard against enormous images (e.g. procedurally generated areas)
            if (imageWidth <= 0 || imageHeight <= 0 ||
                (long)imageWidth * imageHeight > 200_000_000)
            {
                return;
            }

            var configuration = Configuration.Default.Clone();
            configuration.PreferContiguousImageBuffers = true;
            using Image<Rgba32> image = new(configuration, imageWidth, imageHeight);
            Parallel.For(0, gridHeightData.Length, y =>
            {
                for (var x = 1; x < gridHeightData[y].Length - 1; x++)
                {
                    if (!mapEdgeDetector.IsBorder(x, y))
                    {
                        continue;
                    }

                    var height = (int)(gridHeightData[y][x] / worldToGridHeightMultiplier);
                    var imageX = x - height;
                    var imageY = y - height;

                    if (mapEdgeDetector.IsInsideMapBoundary(imageX, imageY))
                    {
                        image[imageX, imageY] = new Rgba32(this.Settings.WalkableMapColor);
                    }
                }
            });
#if DEBUG
            image.Save(this.DllDirectory +
                       @$"/current_map_{Core.States.InGameStateObject.CurrentAreaInstance.AreaHash}.jpeg");
#endif
            this.walkableMapDimension = new Vector2(image.Width, image.Height);
            if (Math.Max(image.Width, image.Height) > 8192)
            {
                var (newWidth, newHeight) = (image.Width, image.Height);
                if (image.Height > image.Width)
                {
                    newWidth = newWidth * 8192 / newHeight;
                    newHeight = 8192;
                }
                else
                {
                    newHeight = newHeight * 8192 / newWidth;
                    newWidth = 8192;
                }

                var targetSize = new Size(newWidth, newHeight);
                var resizer = new ResizeProcessor(new ResizeOptions { Size = targetSize }, image.Size)
                    .CreatePixelSpecificCloningProcessor(configuration, image, image.Bounds);
                resizer.Execute();
            }

            Core.Overlay.AddOrGetImagePointer("walkable_map", image, false, out var t);
            this.walkableMapTexture = t;
        }

        private IconPicker RarityToIconMapping(
            Rarity rarity,
            IconPicker normalMonsterIcon,
            IconPicker magicMonsterIcon,
            IconPicker rareMonsterIcon,
            IconPicker uniqueMonsterIcon)
        {
            return rarity switch
            {
                Rarity.Magic => magicMonsterIcon,
                Rarity.Rare => rareMonsterIcon,
                Rarity.Unique => uniqueMonsterIcon,
                _ => normalMonsterIcon,
            };
        }

        private Vector2 GetTextHalfSize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return Vector2.Zero;
            }

            if (!this.textHalfSizeCache.TryGetValue(text, out var size))
            {
                size = ImGui.CalcTextSize(text) / 2;
                this.textHalfSizeCache[text] = size;
            }

            return size;
        }

        private string DelveChestPathToIcon(string path)
        {
            return path.Replace(this.delveChestStarting, null, StringComparison.Ordinal);
        }

        private void DrawEntityPathEnding(string path, ImDrawListPtr fgDraw, Vector2 pos)
        {
            var lastIndex = path.LastIndexOf('/') + 1;
            if (lastIndex < 0 || lastIndex >= path.Length)
            {
                lastIndex = 0;
            }

            var displayName = path.AsSpan(lastIndex, path.Length - lastIndex);
            var pNameSizeH = ImGui.CalcTextSize(displayName) / 2;
            fgDraw.AddRectFilled(pos - pNameSizeH, pos + pNameSizeH,
                ImGuiHelper.Color(0, 0, 0, 200));
            fgDraw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), pos - pNameSizeH,
                ImGuiHelper.Color(255, 128, 128, 255), displayName);

        }

        private void AddNewPOIWidget()
        {
            var tgttilesInArea = Core.States.InGameStateObject.CurrentAreaInstance.TgtTilesLocations;
            ImGui.InputText("Area Name", ref this.currentAreaName, 200, ImGuiInputTextFlags.ReadOnly);
            ImGui.NewLine();
            ImGui.InputInt("Filter on Max POI frenquency", ref this.Settings.POIFrequencyFilter);
            ImGui.InputText("Filter by text", ref this.tmpTileFilter, 200);
            if (ImGui.InputInt("Select POI via Index###tgtSelectorCounter", ref this.tmpTgtSelectionCounter) &&
                this.tmpTgtSelectionCounter < tgttilesInArea.Keys.Count)
            {
                this.tmpTileName = tgttilesInArea.Keys.ElementAt(this.tmpTgtSelectionCounter);
            }

            ImGui.NewLine();
            if (ImGuiHelper.IEnumerableComboBox<string>("POI Path",
                tgttilesInArea.Keys.Where(k => string.IsNullOrEmpty(this.tmpTileFilter) ||
                k.Contains(this.tmpTileFilter, StringComparison.OrdinalIgnoreCase)),
                ref this.tmpTileName))
            {
                Console.WriteLine($"POI Path selected: {this.tmpTileName}");
            }
            ImGui.InputText("POI Display Name", ref this.tmpDisplayName, 200);
            ImGui.Checkbox("Add for all Areas", ref this.addTileForAllAreas);
            ImGui.SameLine();
            if (ImGui.Button("Add POI"))
            {
                var key = this.addTileForAllAreas ? "common" : this.currentAreaName;
                if (!string.IsNullOrEmpty(key) &&
                    !string.IsNullOrEmpty(this.tmpTileName) &&
                    !string.IsNullOrEmpty(this.tmpDisplayName))
                {
                    if (!this.Settings.ImportantTgts.ContainsKey(key))
                    {
                        this.Settings.ImportantTgts[key] = new();
                    }

                    this.Settings.ImportantTgts[key]
                        [this.tmpTileName] = this.tmpDisplayName;

                    this.tmpTileName = string.Empty;
                    this.tmpDisplayName = string.Empty;
                }
            }
        }

        private void ShowPOIWidget()
        {
            if (ImGui.TreeNode($"Important Terrain POIs common for all Areas"))
            {
                if (this.Settings.ImportantTgts.ContainsKey("common"))
                {
                    foreach (var tgt in this.Settings.ImportantTgts["common"])
                    {
                        if (ImGui.SmallButton($"Delete##{tgt.Key}"))
                        {
                            this.Settings.ImportantTgts["common"].Remove(tgt.Key);
                        }

                        ImGui.SameLine();
                        ImGui.Text($"POI Path: {tgt.Key}, Display: {tgt.Value}");
                        ImGuiHelper.ToolTip("Click me to Modify.");
                        if (ImGui.IsItemClicked())
                        {
                            this.tmpTileName = tgt.Key;
                            this.tmpDisplayName = tgt.Value;
                        }
                    }
                }

                ImGui.TreePop();
            }

            if (ImGui.TreeNode($"Important Terrain POIs in Area: {this.currentAreaName}##import_time_in_area"))
            {
                if (this.Settings.ImportantTgts.ContainsKey(this.currentAreaName))
                {
                    foreach (var tgt in this.Settings.ImportantTgts[this.currentAreaName])
                    {
                        if (ImGui.SmallButton($"Delete##{tgt.Key}"))
                        {
                            this.Settings.ImportantTgts[this.currentAreaName].Remove(tgt.Key);
                        }

                        ImGui.SameLine();
                        ImGui.Text($"POI Path: {tgt.Key}, Display: {tgt.Value}");
                        ImGuiHelper.ToolTip("Click me to Modify.");
                        if (ImGui.IsItemClicked())
                        {
                            this.tmpTileName = tgt.Key;
                            this.tmpDisplayName = tgt.Value;
                        }
                    }
                }

                ImGui.TreePop();
            }
        }

        private void CleanUpRadarPluginCaches()
        {
            this.delveChestCache.Clear();
            this.textHalfSizeCache.Clear();
            this.poiIndexHalfSizeCache.Clear();
            this.poiPathCache.Clear();
            this.nextPoiRecomputeTime = 0;
            this.nextPoiFullRecomputeTime = 0;
            this.pendingPathTask = null;
            this.entityPathCache.Clear();
            this.nextEntityRecomputeTime = 0;
            this.nextEntityFullRecomputeTime = 0;
            this.pendingEntityPathTask = null;
            this.entityPathSnapshot.Clear();
            this.tileIconPathCache.Clear();
            this.tileIconPathSnapshot.Clear();
            this.RemoveMapTexture();
            this.currentAreaName = string.Empty;
        }

        private bool IsLocalCoopActive(Render playerRender, bool hasOtherPlayer)
        {
            if (!this.Settings.AutoDetectCoopMode)
            {
                return this.Settings.EnableCoopMode;
            }

            if (!Core.GHSettings.EnableControllerMode || !hasOtherPlayer)
            {
                return false;
            }

            var worldData = Core.States.InGameStateObject.CurrentWorldInstance;
            if (worldData == null || worldData.Address == IntPtr.Zero)
            {
                return false;
            }

            var screenPos = worldData.WorldToScreen(playerRender.WorldPosition, playerRender.TerrainHeight);
            if (screenPos == Vector2.Zero)
            {
                return false;
            }

            var screenCenter = new Vector2(
                Core.Process.WindowArea.Width / 2f,
                Core.Process.WindowArea.Height / 2f);
            return Vector2.Distance(screenPos, screenCenter) > 35f;
        }
    }
}
