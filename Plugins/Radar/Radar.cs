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

        // Radar scale settings were tuned while MapUiElement.Zoom was stale and always
        // read as about 1.35. Keep using that legacy baseline for radar placement/icon
        // size so the newly-correct live DV zoom value does not invalidate all existing
        // calibration. Revisit once Shift/DefaultShift and map pan state are found.
        private const float LegacyRadarZoomBaseline = 1.35f;

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

        private string SettingPathname => Path.Join(this.DllDirectory, "config", "settings.txt");

        private string ImportantTgtPathName => Path.Join(this.DllDirectory, "important_tgt_files.txt");

        private string BossArenaTgtPathName => Path.Join(this.DllDirectory, "boss_arena_tgt_files.txt");

        private string StairsTgtPathName => Path.Join(this.DllDirectory, "stairs_tgt_files.txt");

        /// <inheritdoc/>
        public override void DrawSettings()
        {
            ImGui.TextWrapped("If your mini/large map icon are not working/visible. Open this " +
                "setting window, click anywhere on it and then hide this setting window. It will fix the issue.");
            ImGui.DragFloat("Large Map Fix", ref this.Settings.LargeMapScaleMultiplier, 0.01f, 1f, 30f, "%.4f");
            ImGuiHelper.ToolTip("This slider is for fixing large map (icons) offset. " +
                "You have to use it if you feel that LargeMap Icons " +
                "are moving while your player is moving. You only have " +
                "to find a value that works for you per game window resolution. " +
                "Basically, you don't have to change it unless you change your " +
                "game window resolution. Also, please contribute back, let me know " +
                "what resolution you use and what value works best for you. " +
                "This slider has no impact on mini-map icons. For windowed-full-screen " +
                "default value should be good enough. If you want to add precise value " +
                "(e.g. 13.7345) press CTRL + LMB. NOTE: this value is 100x the internally " +
                "applied scale (e.g. 15.8 here == 0.158 effective).");
            ImGui.DragFloat("Large Map X Offset", ref this.Settings.LargeMapXOffset, 0.1f);
            ImGuiHelper.ToolTip("Adjusts only the large map overlay horizontally. Negative moves it left, positive moves it right.");
            ImGui.DragFloat("Large Map Y Offset", ref this.Settings.LargeMapYOffset, 0.1f);
            ImGuiHelper.ToolTip("Adjusts only the large map overlay vertically. Negative moves it up, positive moves it down.");
            ImGui.DragFloat("Mini Map Icon Scale", ref this.Settings.MiniMapScaleMultiplier, 0.005f, 0.01f, 2f, "%.3f");
            ImGuiHelper.ToolTip("Scales the size of icons drawn on the mini-map only. " +
                "The mini-map draws icons much larger than the large map by default; " +
                "turn this down to shrink them.");
            ImGui.DragFloat("Mini Map Zoom", ref this.Settings.MiniMapZoomMultiplier, 0.005f, 0.01f, 3f, "%.3f");
            ImGuiHelper.ToolTip("Controls how far mini-map icons sit from your character " +
                "(the mini-map's effective zoom). If enemies appear too far out in all " +
                "directions, turn this down; too clustered, turn it up. Does not affect icon size.");
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
                    "Runestone Icons",
                    this.Settings.RunestoneIcons,
                    "Icon for Expedition campaign Runestone terrain (all terrain variants combined).");

                this.Settings.DrawIconsSettingToImGui(
                    "Expedition Marker Icons",
                    this.Settings.ExpeditionMarkerIcons,
                    "Icons for expedition markers, keyed by MinimapIcon name. Set size to 0 to disable.");

                this.Settings.DrawIconsSettingToImGui(
                    "Expedition Remnant Icons",
                    this.Settings.ExpeditionRemnantIcons,
                    "Icons for expedition remnants with specific mods. Set size to 0 to disable.");

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

            if (Core.States.InGameStateObject.GameUi.IsPassiveSkillTreeVisible)
            {
                return;
            }

            if (largeMap.IsVisible)
            {
                var largeMapRealCenter = largeMap.Center + largeMap.Shift + largeMap.DefaultShift;
                largeMapRealCenter.X += this.Settings.LargeMapXOffset;
                largeMapRealCenter.Y += this.Settings.LargeMapYOffset;
                var largeMapModifiedZoom = this.Settings.LargeMapScaleMultiplier / 100f * LegacyRadarZoomBaseline;
                Helper.DiagonalLength = this.largeMapDiagonalLength;
                Helper.Scale = largeMapModifiedZoom;
                ImGui.SetNextWindowPos(this.Settings.CullWindowPos);
                ImGui.SetNextWindowSize(this.Settings.CullWindowSize);
                ImGui.SetNextWindowBgAlpha(0f);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
                ImGui.Begin("Large Map Culling Window", ImGuiHelper.TransparentWindowFlags);
                ImGui.PopStyleVar();
                this.DrawLargeMap(largeMapRealCenter);
                this.DrawTgtFiles(largeMapRealCenter);
                this.DrawTgtIcons(largeMapRealCenter, largeMapModifiedZoom * 5f);
                this.DrawMapIcons(largeMapRealCenter, largeMapModifiedZoom * 5f);
                ImGui.End();
            }

            if (miniMap.IsVisible)
            {
                Helper.DiagonalLength = this.miniMapDiagonalLength;
                Helper.Scale = LegacyRadarZoomBaseline * this.Settings.MiniMapZoomMultiplier;
                var miniMapCenter = miniMap.Position +
                    (miniMap.Size / 2) +
                    miniMap.DefaultShift +
                    miniMap.Shift;
                ImGui.SetNextWindowPos(miniMap.Position);
                ImGui.SetNextWindowSize(miniMap.Size);
                ImGui.SetNextWindowBgAlpha(0f);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
                ImGui.Begin("###minimapRadar", ImGuiHelper.TransparentWindowFlags);
                ImGui.PopStyleVar();
                var miniMapIconScale = LegacyRadarZoomBaseline * this.Settings.MiniMapScaleMultiplier;
                this.DrawTgtIcons(miniMapCenter, miniMapIconScale);
                this.DrawMapIcons(miniMapCenter, miniMapIconScale);
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

        private void DrawLargeMap(Vector2 mapCenter)
        {
            if (!this.Settings.DrawWalkableMap)
            {
                return;
            }

            if (this.walkableMapTexture == IntPtr.Zero)
            {
                return;
            }

            var player = Core.States.InGameStateObject.CurrentAreaInstance.Player;
            if (!player.TryGetComponent<Render>(out var pRender))
            {
                return;
            }

            var rectf = new RectangleF(
                -pRender.GridPosition.X,
                -pRender.GridPosition.Y,
                this.walkableMapDimension.X,
                this.walkableMapDimension.Y);

            var p1 = Helper.DeltaInWorldToMapDelta(
                new Vector2(rectf.Left, rectf.Top), -pRender.TerrainHeight);
            var p2 = Helper.DeltaInWorldToMapDelta(
                new Vector2(rectf.Right, rectf.Top), -pRender.TerrainHeight);
            var p3 = Helper.DeltaInWorldToMapDelta(
                new Vector2(rectf.Right, rectf.Bottom), -pRender.TerrainHeight);
            var p4 = Helper.DeltaInWorldToMapDelta(
                new Vector2(rectf.Left, rectf.Bottom), -pRender.TerrainHeight);
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

        private void DrawTgtFiles(Vector2 mapCenter)
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
            if (!currentAreaInstance.Player.TryGetComponent<Render>(out var playerRender))
            {
                return;
            }

            var pPos = new Vector2(playerRender.GridPosition.X, playerRender.GridPosition.Y);
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
                    location - pPos, -playerRender.TerrainHeight + height);
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

        private void DrawTgtIcons(Vector2 mapCenter, float iconSizeMultiplier)
        {
            var fgDraw = ImGui.GetWindowDrawList();
            var currentAreaInstance = Core.States.InGameStateObject.CurrentAreaInstance;
            if (!currentAreaInstance.Player.TryGetComponent<Render>(out var playerRender))
            {
                return;
            }

            var pPos = new Vector2(playerRender.GridPosition.X, playerRender.GridPosition.Y);

            foreach (var tgtKV in currentAreaInstance.TgtTilesLocations)
            {
                if (tgtKV.Key.StartsWith(TempleTgtPrefix) && tgtKV.Key.EndsWith(":1-y:1"))
                {
                    if (!this.Settings.TempleIcons.TryGetValue("Vaal Ruins", out var templeIcon))
                    {
                        continue;
                    }

                    this.DrawIconAtTgtLocations(fgDraw, mapCenter, pPos, playerRender, tgtKV.Value, templeIcon, iconSizeMultiplier, shiftUp: true);
                }
                else if (tgtKV.Key.StartsWith(RunestoneTgtPrefix) && tgtKV.Key.EndsWith(":1-y:1"))
                {
                    if (!this.Settings.RunestoneIcons.TryGetValue("Runestones", out var runestoneIcon))
                    {
                        continue;
                    }

                    this.DrawIconAtTgtLocations(fgDraw, mapCenter, pPos, playerRender, tgtKV.Value, runestoneIcon, iconSizeMultiplier, shiftUp: true);
                }
                else if (this.Settings.BossArenaTgts.ContainsKey(tgtKV.Key))
                {
                    if (!this.Settings.BossIcons.TryGetValue("Boss Arena", out var bossIcon))
                    {
                        continue;
                    }

                    this.DrawIconAtTgtLocations(fgDraw, mapCenter, pPos, playerRender, tgtKV.Value, bossIcon, iconSizeMultiplier);
                }
                else if (this.Settings.StairsTgts.ContainsKey(tgtKV.Key))
                {
                    if (!this.Settings.BaseIcons.TryGetValue("Stairs", out var stairsIcon))
                    {
                        continue;
                    }

                    this.DrawIconAtTgtLocations(fgDraw, mapCenter, pPos, playerRender, tgtKV.Value, stairsIcon, iconSizeMultiplier);
                }
            }
        }

        private void DrawIconAtTgtLocations(
            ImDrawListPtr fgDraw,
            Vector2 mapCenter,
            Vector2 pPos,
            Render playerRender,
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
                    location - pPos, -playerRender.TerrainHeight + height);
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

        private void DrawMapIcons(Vector2 mapCenter, float iconSizeMultiplier)
        {
            var fgDraw = ImGui.GetWindowDrawList();
            var currentAreaInstance = Core.States.InGameStateObject.CurrentAreaInstance;
            if (!currentAreaInstance.Player.TryGetComponent<Render>(out var playerRender))
            {
                return;
            }

            var clipMin = ImGui.GetWindowPos();
            var clipMax = clipMin + ImGui.GetWindowSize();
            var clipPadding = iconSizeMultiplier * 4f;
            var pPos = new Vector2(playerRender.GridPosition.X, playerRender.GridPosition.Y);

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
                var fpos = Helper.DeltaInWorldToMapDelta(ePos - pPos, entityRender.TerrainHeight - playerRender.TerrainHeight);
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
                        else if (entityValue.EntityCustomGroup == RadarSettings.BossCheckpointGroup)
                        {
                            if (baseIcons.TryGetValue("Boss Checkpoint", out var bossCheckpointIcon) &&
                                bossCheckpointIcon.IconScale > 0)
                            {
                                DrawIcon(bossCheckpointIcon);
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
                            if (this.Settings.RunestoneIcons.TryGetValue("Rune Encounter", out var runeEncounterIcon) &&
                                runeEncounterIcon.IconScale > 0)
                            {
                                DrawIcon(runeEncounterIcon);
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
            var map = Core.States.InGameStateObject.GameUi.MiniMap;
            var widthSq = map.Size.X * map.Size.X;
            var heightSq = map.Size.Y * map.Size.Y;
            this.miniMapDiagonalLength = Math.Sqrt(widthSq + heightSq);
        }

        private void UpdateLargeMapDetails()
        {
            var map = Core.States.InGameStateObject.GameUi.LargeMap;
            var widthSq = map.Size.X * map.Size.X;
            var heightSq = map.Size.Y * map.Size.Y;
            this.largeMapDiagonalLength = Math.Sqrt(widthSq + heightSq);
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
            if (bytesPerRow <= 0)
            {
                return;
            }

            var mapEdgeDetector = new MapEdgeDetector(mapWalkableData, bytesPerRow);
            var configuration = Configuration.Default.Clone();
            configuration.PreferContiguousImageBuffers = true;
            using Image<Rgba32> image = new(configuration, bytesPerRow * 2, mapEdgeDetector.TotalRows);
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
            this.RemoveMapTexture();
            this.currentAreaName = string.Empty;
        }
    }
}
