// <copyright file="RadarSettings.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Radar
{
    using System.Collections.Generic;
    using System.IO;
    using System.Numerics;
    using GameHelper.Plugin;
    using ImGuiNET;
    using Newtonsoft.Json;
    using GameHelper.Utils;

    /// <summary>
    /// <see cref="Radar"/> plugin settings class.
    /// </summary>
    public sealed class RadarSettings : IPSettings
    {
        private static readonly Vector2 IconSize = new(64, 64);
        private static int poiMonsterGroupNumber = 0;

        /// <summary>
        /// Multipler to apply to the Large Map icons
        /// so they display correctly on the screen.
        /// </summary>
        public float LargeMapScaleMultiplier = 0.1738f;

        /// <summary>
        /// Do not draw the Radar plugin stuff when game is in the background.
        /// </summary>
        public bool DrawWhenForeground = true;

        /// <summary>
        /// Do not draw the Radar plugin stuff when user is in hideout/town.
        /// </summary>
        public bool DrawWhenNotInHideoutOrTown = true;
        
        /// <summary>
        /// Do not draw the Radar plugin stuff when user is in pause menu.
        /// </summary>
        public bool DrawWhenNotPaused = true;

        /// <summary>
        /// Hides all the entities that are outside the network bubble.
        /// </summary>
        public bool HideOutsideNetworkBubble = false;

        /// <summary>
        /// Gets a value indicating whether user wants to modify large map culling window or not.
        /// </summary>
        public bool ModifyCullWindow = false;

        /// <summary>
        /// Gets a value indicating whether user wants culling window
        /// to cover the full game or not.
        /// </summary>
        public bool MakeCullWindowFullScreen = true;

        /// <summary>
        /// Gets a value indicating whether to draw the map in culling window or not.
        /// </summary>
        public bool DrawMapInCull = true;

        /// <summary>
        /// Gets a value indicating whether to draw the POI in culling window or not.
        /// </summary>
        public bool DrawPOIInCull = true;

        /// <summary>
        /// Gets a value indicating whether user wants to draw walkable map or not.
        /// </summary>
        public bool DrawWalkableMap = true;

        /// <summary>
        /// Gets a value indicating what color to use for drawing walkable map.
        /// </summary>
        public Vector4 WalkableMapColor = new Vector4(150f) / 255f;

        /// <summary>
        /// Gets the position of the cull window that the user wants.
        /// </summary>
        public Vector2 CullWindowPos = Vector2.Zero;

        /// <summary>
        /// Get the size of the cull window that the user wants.
        /// </summary>
        public Vector2 CullWindowSize = Vector2.Zero;

        /// <summary>
        /// Gets a value indicating wether user wants to show Player icon or names.
        /// </summary>
        public bool ShowPlayersNames = false;

        /// <summary>
        /// Gets a value indicating what is the maximum frequency a POI should have
        /// </summary>
        public int POIFrequencyFilter = 0;

        /// <summary>
        /// Gets a value indicating wether user want to show important tgt names or not.
        /// </summary>
        public bool ShowImportantPOI = true;

        /// <summary>
        /// Gets a value indicating what color to use for drawing the POI.
        /// </summary>
        public Vector4 POIColor = new(1f, 0.5f, 0.5f, 1f);

        /// <summary>
        /// Gets a value indicating wether user want to draw a background when drawing the POI.
        /// </summary>
        public bool EnablePOIBackground = true;

        /// <summary>
        /// Gets the Tgts and their expected clusters per area/zone/map.
        /// </summary>
        [JsonIgnore]
        public Dictionary<string, Dictionary<string, string>> ImportantTgts = new();

        /// <summary>
        /// Icons to display on the map. Base game includes normal chests, strongboxes, monsters etc.
        /// </summary>
        public Dictionary<string, IconPicker> BaseIcons = new();

        /// <summary>
        /// Icons to display on the map. POIMonsters includes icons for monsters that are in custom category created by user
        /// </summary>
        public Dictionary<int, IconPicker> POIMonsters = new();

        /// <summary>
        /// Icons to display on the map. Breach includes breach chests.
        /// </summary>
        public Dictionary<string, IconPicker> BreachIcons = new();

        /// <summary>
        /// Icons to display on the map. Delirium includes the special spawners and bombs that
        /// delirium brings and they can't be convered by base icons.
        /// </summary>
        public Dictionary<string, IconPicker> DeliriumIcons = new();

        /// <summary>
        /// Icons to display on the map. Delirium includes the special spawners and bombs that
        /// delirium brings and they can't be convered by base icons.
        /// </summary>
        public Dictionary<string, IconPicker> ExpeditionIcons = new();

        /// <summary>
        /// Icons to display on the map. Temple includes the Incursion waygate devices.
        /// </summary>
        public Dictionary<string, IconPicker> TempleIcons = new();

        /// <summary>
        /// Icons to display on the map. Boss arena icons for endgame maps.
        /// </summary>
        public Dictionary<string, IconPicker> BossIcons = new();

        /// <summary>
        /// Gets the boss arena TGT paths and their display names.
        /// </summary>
        [JsonIgnore]
        public Dictionary<string, string> BossArenaTgts = new();

        /// <summary>
        /// Icons to display on the map. This list includes icons for
        /// OtherImportantObjects that are in custom category created by user
        /// </summary>
        public Dictionary<int, IconPicker> OtherImportantObjects = new();

        /// <summary>
        /// Draws the icons setting via the ImGui widgets.
        /// </summary>
        /// <param name="headingText">Text to display as heading.</param>
        /// <param name="icons">Icons settings to draw.</param>
        /// <param name="helpingText">helping text to display at the top.</param>
        public void DrawIconsSettingToImGui(
            string headingText,
            Dictionary<string, IconPicker> icons,
            string helpingText)
        {
            var isOpened = ImGui.TreeNode($"{headingText}##treeNode");
            if (!string.IsNullOrEmpty(helpingText))
            {
                ImGuiHelper.ToolTip(helpingText);
            }

            if (isOpened)
            {
                ImGui.Columns(2, $"icons columns##{headingText}", false);
                foreach (var icon in icons)
                {
                    ImGui.Text(icon.Key);
                    ImGui.NextColumn();
                    icon.Value.ShowSettingWidget();
                    ImGui.NextColumn();
                }

                ImGui.Columns(1);
                ImGui.TreePop();
            }
        }

        /// <summary>
        ///     draws the POIMonster setting widget.
        /// </summary>
        /// <param name="dllDirectory">directory where the plugin dll is located.</param>
        public void DrawPOIMonsterSettingToImGui(string dllDirectory)
        {
            if (ImGui.TreeNode($"Monster POI Icons"))
            {
                ImGui.Columns(2, $"icons columns##POIMonsterCol", false);
                foreach (var poimonster in this.POIMonsters)
                {
                    ImGui.Text(poimonster.Key  == -1 ? "Default Group" : $"Group {poimonster.Key}");
                    ImGui.NextColumn();
                    poimonster.Value.ShowSettingWidget();
                    ImGui.SameLine();
                    if (poimonster.Key != -1 && ImGui.Button($"Delete##{poimonster.Key}"))
                    {
                        _ = this.POIMonsters.Remove(poimonster.Key);
                    }

                    ImGui.NextColumn();
                }

                ImGui.Columns(1);
                ImGui.Separator();
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                if (ImGui.InputInt("Group Number##poimonster", ref poiMonsterGroupNumber) && poiMonsterGroupNumber < 0)
                {
                    poiMonsterGroupNumber = 0;
                }

                ImGui.SameLine();
                if (ImGui.Button("Add##POIMonsterGroupAdd"))
                {
                    this.POIMonsters.TryAdd(poiMonsterGroupNumber, new(Path.Join(dllDirectory, "icons.png"), 12, 44, 30, IconSize));
                }

                ImGui.TreePop();
            }
        }

        /// <summary>
        ///     draws the OtherImportantObjects setting widget.
        /// </summary>
        /// <param name="dllDirectory">directory where the plugin dll is located.</param>
        public void OtherImportantObjectsSettingToImGui(string dllDirectory)
        {
            if (ImGui.TreeNode($"Special Objects Icons"))
            {
                ImGui.Columns(2, $"icons columns##SpecialObjects", false);
                foreach (var obj in this.OtherImportantObjects)
                {
                    ImGui.Text(obj.Key == -1 ? "Default Group" : $"Group {obj.Key}");
                    ImGui.NextColumn();
                    obj.Value.ShowSettingWidget();
                    ImGui.SameLine();
                    if (obj.Key != -1 && ImGui.Button($"Delete##{obj.Key}"))
                    {
                        _ = this.OtherImportantObjects.Remove(obj.Key);
                    }

                    ImGui.NextColumn();
                }

                ImGui.Columns(1);
                ImGui.Separator();
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                if (ImGui.InputInt("Group Number##SpecialObjects", ref poiMonsterGroupNumber) && poiMonsterGroupNumber < 0)
                {
                    poiMonsterGroupNumber = 0;
                }

                ImGui.SameLine();
                if (ImGui.Button("Add##SpecialObjects"))
                {
                    this.OtherImportantObjects.TryAdd(poiMonsterGroupNumber, new(Path.Join(dllDirectory, "icons.png"), 1, 37, 30, IconSize));
                }

                ImGui.TreePop();
            }
        }

        /// <summary>
        /// Adds the default icons if the setting file isn't available.
        /// </summary>
        /// <param name="dllDirectory">directory where the plugin dll is located.</param>
        public void AddDefaultIcons(string dllDirectory)
        {
            var basicIconPathName = Path.Join(dllDirectory, "icons.png");
            this.AddDefaultBaseGameIcons(basicIconPathName);
            this.AddDefaultPOIMonsterIcons(basicIconPathName);
            this.AddDefaultOtherImportantObjectsIcons(basicIconPathName);
            this.AddDefaultBreachIcons(basicIconPathName);
            this.AddDefaultDeliriumIcons(basicIconPathName);
            this.AddDefaultExpeditionIcons(basicIconPathName);
            this.AddDefaultTempleIcons(basicIconPathName);
            this.AddDefaultBossIcons(basicIconPathName);
        }

        private void AddDefaultBaseGameIcons(string iconPathName)
        {
            this.BaseIcons.TryAdd("Self", new IconPicker(iconPathName, 0, 0, 20, IconSize));
            this.BaseIcons.TryAdd("Player", new IconPicker(iconPathName, 2, 0, 20, IconSize));
            this.BaseIcons.TryAdd("Leader", new IconPicker(iconPathName, 3, 1, 20, IconSize));
            this.BaseIcons.TryAdd("NPC", new IconPicker(iconPathName, 3, 0, 30, IconSize));
            this.BaseIcons.TryAdd("Special NPC", new IconPicker(iconPathName, 13, 42, 100, IconSize));
            this.BaseIcons.TryAdd("Strongbox", new IconPicker(iconPathName, 8, 38, 30, IconSize));
            this.BaseIcons.TryAdd("Magic Chests", new IconPicker(iconPathName, 1, 13, 20, IconSize));
            this.BaseIcons.TryAdd("Rare Chests", new IconPicker(iconPathName, 4, 48, 20, IconSize));
            this.BaseIcons.TryAdd("All Other Chest", new IconPicker(iconPathName, 6, 9, 20, IconSize));

            this.BaseIcons.TryAdd("Shrine", new IconPicker(iconPathName, 7, 0, 30, IconSize));

            this.BaseIcons.TryAdd("Friendly", new IconPicker(iconPathName, 1, 0, 10, IconSize));
            this.BaseIcons.TryAdd("Normal Monster", new IconPicker(iconPathName, 0, 14, 20, IconSize));
            this.BaseIcons.TryAdd("Magic Monster", new IconPicker(iconPathName, 6, 3, 20, IconSize));
            this.BaseIcons.TryAdd("Rare Monster", new IconPicker(iconPathName, 4, 57, 30, IconSize));
            this.BaseIcons.TryAdd("Unique Monster", new IconPicker(iconPathName, 6, 57, 30, IconSize));
            this.BaseIcons.TryAdd("Pinnacle Boss Not Attackable", new IconPicker(iconPathName, 5, 15, 30, IconSize));

            this.BaseIcons.TryAdd("Yellow Bestiary Monster", new IconPicker(iconPathName, 6, 2, 35, IconSize));
            this.BaseIcons.TryAdd("Red Bestiary Monster", new IconPicker(iconPathName, 7, 2, 35, IconSize));
        }

        private void AddDefaultPOIMonsterIcons(string iconPathName)
        {
            this.POIMonsters.TryAdd(-1, new IconPicker(iconPathName, 12, 44, 30, IconSize));
        }

        private void AddDefaultOtherImportantObjectsIcons(string iconPathName)
        {
            this.OtherImportantObjects.TryAdd(-1, new IconPicker(iconPathName, 1, 37, 30, IconSize));
        }

        private void AddDefaultBreachIcons(string iconPathName)
        {
            this.BreachIcons.TryAdd("Breach Chest", new IconPicker(iconPathName, 6, 41, 30, IconSize));
        }

        private void AddDefaultDeliriumIcons(string iconPathName)
        {
            this.DeliriumIcons.TryAdd("Delirium Bomb", new IconPicker(iconPathName, 5, 0, 30, IconSize));
            this.DeliriumIcons.TryAdd("Delirium Spawner", new IconPicker(iconPathName, 6, 0, 30, IconSize));
        }

        private void AddDefaultExpeditionIcons(string iconPathName)
        {
            this.ExpeditionIcons.TryAdd("Generic Expedition Chests", new IconPicker(iconPathName, 5, 41, 30, IconSize));
        }

        private void AddDefaultTempleIcons(string iconPathName)
        {
            this.TempleIcons.TryAdd("Vaal Ruins", new IconPicker(iconPathName, 9, 2, 75, IconSize));
        }

        private void AddDefaultBossIcons(string iconPathName)
        {
            this.BossIcons.TryAdd("Boss Arena", new IconPicker(iconPathName, 9, 2, 75, IconSize));
        }
    }
}
