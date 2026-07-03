// <copyright file="RadarSettings.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Radar
{
    using System.Collections.Generic;
    using System.IO;
    using System.Numerics;
    using GameHelper.Localization;
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
        public float LargeMapScaleMultiplier = 1f;

        /// <summary>
        /// Horizontal screen-space offset applied to the large map overlay.
        /// Negative values move the overlay left.
        /// </summary>
        public float LargeMapXOffset = 0f;

        /// <summary>
        /// Vertical screen-space offset applied to the large map overlay.
        /// Negative values move the overlay up.
        /// </summary>
        public float LargeMapYOffset = 0f;

        /// <summary>
        /// Multiplier applied to the mini-map position zoom (Helper.Scale), controlling how
        /// far icons are placed from the player.
        /// </summary>
        public float MiniMapZoomMultiplier = 1f;

        /// <summary>
        /// Horizontal screen-space offset applied to the mini-map overlay.
        /// Negative values move the overlay left.
        /// </summary>
        public float MiniMapXOffset = 0f;

        /// <summary>
        /// Automatically detect local co-op mode in controller mode.
        /// </summary>
        public bool AutoDetectCoopMode = true;

        /// <summary>
        /// Enable co-op mode maphack centering on the midpoint between P1 and P2.
        /// </summary>
        public bool EnableCoopMode = false;

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
        /// Gets a value indicating whether user wants to show Player icon or names.
        /// </summary>
        public bool ShowPlayersNames = false;

        /// <summary>
        /// Global toggle for drawing paths to entities with icon pathing enabled.
        /// Does not change individual icon ShowPath settings.
        /// </summary>
        public bool ShowEntityPaths = true;

        /// <summary>
        /// When enabled, once the player gets close to a target that has a path,
        /// that target is remembered for the current map and its path is no longer drawn.
        /// Reset on area change or via the "Reset Reached Paths" button.
        /// </summary>
        public bool HideReachedPaths = true;

        /// <summary>
        /// Grid-distance threshold below which a path target counts as "reached"
        /// and is hidden for the remainder of the current map. Used by
        /// <see cref="HideReachedPaths"/>.
        /// </summary>
        public float ReachedPathDistance = 50f;

        /// <summary>
        /// When enabled, the socket-count label on a Runestone Encounter disappears once the
        /// player gets within <see cref="ReachedPathDistance"/> of it (remembered per map).
        /// Independent of <see cref="HideReachedPaths"/>.
        /// </summary>
        public bool HideRunestoneSocketsWhenNear = true;

        /// <summary>
        /// Screen-pixel X/Y offset applied to the Runestone socket-count label position.
        /// </summary>
        public float RunestoneSocketOffsetX = 0f;

        /// <summary>
        /// Screen-pixel Y offset applied to the Runestone socket-count label position.
        /// </summary>
        public float RunestoneSocketOffsetY = 0f;

        /// <summary>
        /// Gets a value indicating what is the maximum frequency a POI should have
        /// </summary>
        public int POIFrequencyFilter = 0;

        /// <summary>
        /// Gets a value indicating whether to draw the straight-line arrow to POIs.
        /// Green if unobstructed, red if blocked.
        /// </summary>
        public bool ShowStraightLine = false;

        /// <summary>
        /// Gets a value indicating whether to draw the A*-computed smooth path to POIs.
        /// </summary>
        public bool ShowSmoothPath = true;

        /// <summary>
        /// Thickness of the POI direction lines in screen pixels.
        /// </summary>
        public float DirectionLineThickness = 0.1f;

        /// <summary>
        /// Thickness of entity/terrain-tile icon paths in screen pixels.
        /// </summary>
        public float IconPathThickness = 1.5f;

        /// <summary>
        /// Number of segments from the start of a cached path to recompute.
        /// 0 = full recompute. Higher values keep more of the old path,
        /// trading accuracy for performance.
        /// </summary>
        public int PathRecomputeSegments = 3;

        /// <summary>
        /// Minimum interval in milliseconds between path recomputations.
        /// Lower values give more responsive paths at higher CPU cost.
        /// </summary>
        public int PathRecomputeIntervalMs = 250;

        /// <summary>
        /// Interval in milliseconds between forced full-path recomputations.
        /// Ensures paths can never drift stale from partial recomputes.
        /// </summary>
        public int PathFullRecomputeIntervalMs = 3000;

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
        /// Gets the stairs TGT paths and their display names.
        /// </summary>
        [JsonIgnore]
        public Dictionary<string, string> StairsTgts = new();

        /// <summary>
        /// Icons for expedition markers, keyed by display name.
        /// </summary>
        public Dictionary<string, IconPicker> ExpeditionMarkerIcons = new();

        /// <summary>
        /// Icons for expedition markers detected by their .ao model file (works pre-detonation,
        /// unlike the MinimapIcon-based <see cref="ExpeditionMarkerIcons"/>), keyed by display name.
        /// </summary>
        public Dictionary<string, IconPicker> ExpeditionModelIcons = new();

        /// <summary>
        /// Icons for expedition remnants with specific mods.
        /// </summary>
        public Dictionary<string, IconPicker> ExpeditionRemnantIcons = new();

        /// <summary>
        /// Icons to display on the map. Runestone includes the Campaign Runestone
        /// terrain tiles (all terrain variants combined under a single "Runestones" entry).
        /// </summary>
        public Dictionary<string, IconPicker> RunestoneIcons = new();

        /// <summary>
        /// Icon to display on the map for Ritual rune objects.
        /// </summary>
        public Dictionary<string, IconPicker> RitualIcons = new();

        /// <summary>
        /// Icons to display on the map for Abyss nodes. "Abyss Crack" and "Abyss Pit"
        /// match specific paths; "Other" matches any other entity with "Abyss" in its path.
        /// </summary>
        public Dictionary<string, IconPicker> AbyssIcons = new();

        /// <summary>
        /// Icons to display on the map for Sanctum (Trial of the Sekhemas) objects.
        /// </summary>
        public Dictionary<string, IconPicker> SekhemasIcons = new();

        /// <summary>
        /// Icons to display on the map for specific Strongbox chests, matched by entity path.
        /// </summary>
        public Dictionary<string, IconPicker> StrongboxIcons = new();

        /// <summary>
        /// Maps a Strongbox entity-path substring to its display-name key in <see cref="StrongboxIcons"/>.
        /// Add a new strongbox type here plus a default in AddDefaultStrongboxIcons.
        /// </summary>
        [JsonIgnore]
        public static readonly Dictionary<string, string> StrongboxPathMap = new()
        {
            { "ResearchStrongbox", "Research Strongbox" },
            { "ArmourerStrongbox", "Armourer Strongbox" },
        };

        /// <summary>
        /// Returns the <see cref="StrongboxIcons"/> key matching the given entity path, or null if none.
        /// </summary>
        public static string? StrongboxKeyForPath(string path)
        {
            foreach (var kv in StrongboxPathMap)
            {
                if (path.Contains(kv.Key))
                {
                    return kv.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// Normalizes an Expedition marker .ao model path to a base key that ignores the numeric
        /// version suffix. e.g. "Metadata/.../chestmarker_02.ao" -> "chestmarker",
        /// "chestmarker2_03.ao" -> "chestmarker2", "chestmarker_signpost_01.ao" -> "chestmarker_signpost".
        /// Returns empty string when the path is empty.
        /// </summary>
        public static string NormalizeExpeditionModelName(string modelPath)
        {
            if (string.IsNullOrEmpty(modelPath))
            {
                return string.Empty;
            }

            var slash = modelPath.LastIndexOf('/');
            var name = slash >= 0 ? modelPath[(slash + 1)..] : modelPath;
            var dot = name.IndexOf('.');
            if (dot >= 0)
            {
                name = name[..dot];
            }

            // Strip a single trailing "_<digits>" version suffix (e.g. "_03"), but keep non-numeric
            // suffixes like "_signpost" so distinct variants don't collapse together.
            var underscore = name.LastIndexOf('_');
            if (underscore > 0 && underscore < name.Length - 1)
            {
                var allDigits = true;
                for (var i = underscore + 1; i < name.Length; i++)
                {
                    if (!char.IsDigit(name[i]))
                    {
                        allDigits = false;
                        break;
                    }
                }

                if (allDigits)
                {
                    name = name[..underscore];
                }
            }

            return name;
        }

        /// <summary>
        /// Returns the <see cref="ExpeditionModelIcons"/> display-name key for a marker .ao model
        /// path, or null if the model isn't mapped.
        /// </summary>
        public static string? ExpeditionModelKeyForPath(string modelPath)
        {
            var normalized = NormalizeExpeditionModelName(modelPath);
            return normalized.Length > 0 && ExpeditionMarkerModelMap.TryGetValue(normalized, out var name)
                ? name
                : null;
        }

        /// <summary>
        /// The group number used for expedition markers in SpecialMiscObjPaths.
        /// </summary>
        [JsonIgnore]
        public const int ExpeditionMarkerGroup = 100;

        /// <summary>
        /// The group number used for expedition remnants in SpecialMiscObjPaths.
        /// </summary>
        [JsonIgnore]
        public const int ExpeditionRemnantGroup = 101;

        /// <summary>
        /// The group number used for boss checkpoints in SpecialMiscObjPaths.
        /// </summary>
        [JsonIgnore]
        public const int BossCheckpointGroup = 102;

        /// <summary>
        /// The group number used for Expedition 2 encounter controllers in SpecialMiscObjPaths.
        /// </summary>
        [JsonIgnore]
        public const int RuneEncounterGroup = 103;

        /// <summary>
        /// The group number used for Ritual rune objects in SpecialMiscObjPaths.
        /// </summary>
        [JsonIgnore]
        public const int RitualGroup = 104;

        /// <summary>
        /// The group number used for Abyss nodes in SpecialMiscObjPaths.
        /// </summary>
        [JsonIgnore]
        public const int AbyssGroup = 105;

        /// <summary>
        /// The group number used for Breach initiators in SpecialMiscObjPaths.
        /// </summary>
        [JsonIgnore]
        public const int BreachInitiatorGroup = 106;

        /// <summary>
        /// The group number used for Sanctum (Sekhemas) objects in SpecialMiscObjPaths.
        /// </summary>
        [JsonIgnore]
        public const int SekhemasGroup = 107;

        /// <summary>
        /// Maps mod name substrings to display names used as keys in ExpeditionRemnantIcons.
        /// </summary>
        [JsonIgnore]
        public static readonly Dictionary<string, string> ExpeditionRemnantModMap = new()
        {
            { "ItemQuantityChest", "Chest Item Quantity Remnant" },
        };

        /// <summary>
        /// Maps MinimapIcon.IconName to display name used as key in ExpeditionMarkerIcons.
        /// </summary>
        [JsonIgnore]
        public static readonly Dictionary<string, string> ExpeditionMarkerIconNameMap = new()
        {
            { "RewardChestExpedition", "Splinter Chest" },
            { "RewardChestArmour", "Armour Chest" },
            { "RewardChestWeapon", "Weapon Chest" },
            { "RewardChestTrinkets", "Trinkets Chest" },
            { "RewardChestCurrency", "Currency Chest" },
            { "RewardChestMaps", "Maps Chest" },
            { "ExpeditionCavernEntrance", "Cavern Entrance" },
        };

        /// <summary>
        /// Maps a normalized Expedition marker .ao model name (see <see cref="NormalizeExpeditionModelName"/>)
        /// to its display-name key in <see cref="ExpeditionModelIcons"/>. The numeric version suffix is
        /// ignored, so "chestmarker.ao"/"chestmarker_02.ao" share one entry while "chestmarker2"/
        /// "chestmarker3" stay distinct. Add new variants here plus a default in AddDefaultExpeditionModelIcons.
        /// </summary>
        [JsonIgnore]
        public static readonly Dictionary<string, string> ExpeditionMarkerModelMap = new()
        {
            { "chestmarker", "Chest Marker" },
            { "chestmarker2", "Chest Marker 2" },
            { "chestmarker3", "Chest Marker 3" },
            { "chestmarker_signpost", "Chest Signpost Marker" },
            { "elitemarker", "Elite Marker" },
            { "monstermarker", "Monster Marker" },
        };

        /// <summary>
        /// Display-name key in <see cref="ExpeditionModelIcons"/> for the catch-all icon drawn on
        /// ExpeditionMarker entities that match neither a known reward MinimapIcon nor a mapped model.
        /// </summary>
        [JsonIgnore]
        public const string UnknownExpeditionMarkerKey = "Unknown Marker";

        /// <summary>
        /// Maps MinimapIcon.IconName to display name used as key in RunestoneIcons.
        /// </summary>
        [JsonIgnore]
        public static readonly Dictionary<string, string> RunestoneIconNameMap = new()
        {
            { "Expedition2RemnantActive", "Runestone Encounter" },
        };

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
        /// <param name="text">Plugin localization resources.</param>
        public void DrawIconsSettingToImGui(
            string headingText,
            Dictionary<string, IconPicker> icons,
            string helpingText,
            PluginLocalization text)
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
                    ImGui.Checkbox($"##show{headingText}{icon.Key}", ref icon.Value.Show);
                    ImGui.SameLine();
                    ImGui.Text(icon.Key);
                    ImGui.NextColumn();
                    icon.Value.ShowSettingWidget(text);
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
        /// <param name="text">Plugin localization resources.</param>
        public void DrawPOIMonsterSettingToImGui(string dllDirectory, PluginLocalization text)
        {
            if (ImGui.TreeNode(text.Title("icons.monster_poi", "Monster POI Icons", "RadarMonsterPoiIcons")))
            {
                ImGui.Columns(2, $"icons columns##POIMonsterCol", false);
                foreach (var poimonster in this.POIMonsters)
                {
                    ImGui.Checkbox($"##showpoimonster{poimonster.Key}", ref poimonster.Value.Show);
                    ImGui.SameLine();
                    ImGui.Text(poimonster.Key  == -1 ? text.T("icons.default_group", "Default Group") : text.F("icons.group", "Group {0}", poimonster.Key));
                    ImGui.NextColumn();
                    poimonster.Value.ShowSettingWidget(text);
                    ImGui.SameLine();
                    if (poimonster.Key != -1 && ImGui.Button(text.Label("button.delete", "Delete", poimonster.Key.ToString())))
                    {
                        _ = this.POIMonsters.Remove(poimonster.Key);
                    }

                    ImGui.NextColumn();
                }

                ImGui.Columns(1);
                ImGui.Separator();
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                if (ImGui.InputInt(text.Label("icons.group_number", "Group Number", "poimonster"), ref poiMonsterGroupNumber) && poiMonsterGroupNumber < 0)
                {
                    poiMonsterGroupNumber = 0;
                }

                ImGui.SameLine();
                if (ImGui.Button(text.Label("button.add", "Add", "POIMonsterGroupAdd")))
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
        /// <param name="text">Plugin localization resources.</param>
        public void OtherImportantObjectsSettingToImGui(string dllDirectory, PluginLocalization text)
        {
            if (ImGui.TreeNode(text.Title("icons.special_objects", "Special Objects Icons", "RadarSpecialObjectsIcons")))
            {
                ImGui.Columns(2, $"icons columns##SpecialObjects", false);
                foreach (var obj in this.OtherImportantObjects)
                {
                    ImGui.Checkbox($"##showspecialobj{obj.Key}", ref obj.Value.Show);
                    ImGui.SameLine();
                    ImGui.Text(obj.Key == -1 ? text.T("icons.default_group", "Default Group") : text.F("icons.group", "Group {0}", obj.Key));
                    ImGui.NextColumn();
                    obj.Value.ShowSettingWidget(text);
                    ImGui.SameLine();
                    if (obj.Key != -1 && ImGui.Button(text.Label("button.delete", "Delete", obj.Key.ToString())))
                    {
                        _ = this.OtherImportantObjects.Remove(obj.Key);
                    }

                    ImGui.NextColumn();
                }

                ImGui.Columns(1);
                ImGui.Separator();
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                if (ImGui.InputInt(text.Label("icons.group_number", "Group Number", "SpecialObjects"), ref poiMonsterGroupNumber) && poiMonsterGroupNumber < 0)
                {
                    poiMonsterGroupNumber = 0;
                }

                ImGui.SameLine();
                if (ImGui.Button(text.Label("button.add", "Add", "SpecialObjects")))
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
            this.AddDefaultExpeditionMarkerIcons(basicIconPathName);
            this.AddDefaultExpeditionModelIcons(basicIconPathName);
            this.AddDefaultExpeditionRemnantIcons(basicIconPathName);
            this.AddDefaultRunestoneIcons(basicIconPathName);
            this.AddDefaultRitualIcons(basicIconPathName);
            this.AddDefaultAbyssIcons(basicIconPathName);
            this.AddDefaultStrongboxIcons(basicIconPathName);
            this.AddDefaultSekhemasIcons(basicIconPathName);
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
            var strongboxBase = new IconPicker(iconPathName, 8, 38, 30, IconSize);
            strongboxBase.Show = false; // hidden by default (specific strongboxes use the Strongboxes category)
            this.BaseIcons.TryAdd("Strongbox", strongboxBase);
            this.BaseIcons.TryAdd("Magic Chests", new IconPicker(iconPathName, 1, 13, 20, IconSize));
            this.BaseIcons.TryAdd("Rare Chests", new IconPicker(iconPathName, 4, 48, 20, IconSize));
            this.BaseIcons.TryAdd("All Other Chest", new IconPicker(iconPathName, 6, 9, 20, IconSize));
            this.BaseIcons.TryAdd("Rune", new IconPicker(iconPathName, 5, 13, 50, IconSize));
            this.BaseIcons.TryAdd("Shrine", new IconPicker(iconPathName, 7, 0, 30, IconSize));
            this.BaseIcons.TryAdd("Friendly", new IconPicker(iconPathName, 1, 0, 10, IconSize));
            this.BaseIcons.TryAdd("Normal Monster", new IconPicker(iconPathName, 0, 14, 20, IconSize));
            this.BaseIcons.TryAdd("Magic Monster", new IconPicker(iconPathName, 6, 3, 20, IconSize));
            this.BaseIcons.TryAdd("Rare Monster", new IconPicker(iconPathName, 4, 57, 30, IconSize));
            this.BaseIcons.TryAdd("Unique Monster", new IconPicker(iconPathName, 6, 57, 30, IconSize));
            this.BaseIcons.TryAdd("Pinnacle Boss Not Attackable", new IconPicker(iconPathName, 5, 15, 30, IconSize));

            this.BaseIcons.TryAdd("Yellow Bestiary Monster", new IconPicker(iconPathName, 6, 2, 35, IconSize));
            this.BaseIcons.TryAdd("Red Bestiary Monster", new IconPicker(iconPathName, 7, 2, 35, IconSize));

            this.BaseIcons.TryAdd("Stairs", new IconPicker(iconPathName, 4, 1, 40, IconSize));
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
            this.BreachIcons.TryAdd("Breach", new IconPicker(iconPathName, 8, 12, 50, IconSize,
                showPath: true,
                pathColor: new System.Numerics.Vector4(0.8f, 0.1f, 0.5f, 1f)));
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
            this.TempleIcons.TryAdd("Vaal Ruins",
                new IconPicker(iconPathName, 9, 2, 75, IconSize,
                    showPath: true,
                    pathColor: new System.Numerics.Vector4(1f, 0.6f, 0f, 1f)));
        }

        private void AddDefaultExpeditionMarkerIcons(string iconPathName)
        {
            this.ExpeditionMarkerIcons.TryAdd("Splinter Chest", new IconPicker(iconPathName, 4, 40, 90, IconSize));
            this.ExpeditionMarkerIcons.TryAdd("Armour Chest", new IconPicker(iconPathName, 1, 39, 0, IconSize));
            this.ExpeditionMarkerIcons.TryAdd("Weapon Chest", new IconPicker(iconPathName, 2, 39, 0, IconSize));
            this.ExpeditionMarkerIcons.TryAdd("Trinkets Chest", new IconPicker(iconPathName, 0, 39, 0, IconSize));
            this.ExpeditionMarkerIcons.TryAdd("Currency Chest", new IconPicker(iconPathName, 10, 38, 0, IconSize));
            this.ExpeditionMarkerIcons.TryAdd("Maps Chest", new IconPicker(iconPathName, 13, 38, 0, IconSize));
            this.ExpeditionMarkerIcons.TryAdd("Cavern Entrance", new IconPicker(iconPathName, 0, 2, 90, IconSize));
            this.ExpeditionMarkerIcons.TryAdd("Logbook", new IconPicker(iconPathName, 4, 40, 90, IconSize));
        }

        private void AddDefaultRunestoneIcons(string iconPathName)
        {
            this.RunestoneIcons.TryAdd("Runestone Encounter",
                new IconPicker(iconPathName, 4, 71, 50, IconSize,
                    showPath: true,
                    pathColor: new System.Numerics.Vector4(0f, 145f / 255f, 209f / 255f, 1f)));
            this.RunestoneIcons.TryAdd("Runestones", new IconPicker(iconPathName, 13, 1, 70, IconSize));
        }

        private void AddDefaultRitualIcons(string iconPathName)
        {
            this.RitualIcons.TryAdd("Ritual",
                new IconPicker(iconPathName, 8, 40, 50, IconSize,
                    showPath: true,
                    pathColor: new System.Numerics.Vector4(113f / 255f, 0f, 1f, 1f)));
        }

        private void AddDefaultSekhemasIcons(string iconPathName)
        {
            this.SekhemasIcons.TryAdd("Sacred Water", new IconPicker(iconPathName, 4, 15, 30, IconSize));
        }

        private void AddDefaultStrongboxIcons(string iconPathName)
        {
            this.StrongboxIcons.TryAdd("Research Strongbox", new IconPicker(iconPathName, 10, 38, 50, IconSize));
            this.StrongboxIcons.TryAdd("Armourer Strongbox", new IconPicker(iconPathName, 1, 39, 50, IconSize));
        }

        private void AddDefaultAbyssIcons(string iconPathName)
        {
            var abyssCrack = new IconPicker(iconPathName, 5, 63, 40, IconSize,
                showPath: false,
                pathColor: new System.Numerics.Vector4(140f / 255f, 1f, 0f, 1f));
            abyssCrack.Show = false; // hidden by default (icon + path off)
            this.AbyssIcons.TryAdd("Abyss Crack", abyssCrack);
            this.AbyssIcons.TryAdd("Abyss Pit", new IconPicker(iconPathName, 7, 63, 50, IconSize,
                showPath: true,
                pathColor: new System.Numerics.Vector4(140f / 255f, 1f, 0f, 1f)));
        }

        private void AddDefaultExpeditionModelIcons(string iconPathName)
        {
            this.ExpeditionModelIcons.TryAdd("Chest Marker", new IconPicker(iconPathName, 1, 70, 30, IconSize));
            this.ExpeditionModelIcons.TryAdd("Chest Marker 2", new IconPicker(iconPathName, 1, 70, 30, IconSize));
            this.ExpeditionModelIcons.TryAdd("Chest Marker 3", new IconPicker(iconPathName, 1, 70, 30, IconSize));
            this.ExpeditionModelIcons.TryAdd("Chest Signpost Marker", new IconPicker(iconPathName, 8, 38, 15, IconSize));
            this.ExpeditionModelIcons.TryAdd("Elite Marker", new IconPicker(iconPathName, 0, 34, 30, IconSize));
            var monsterMarker = new IconPicker(iconPathName, 2, 34, 15, IconSize);
            monsterMarker.Show = false; // disabled by default
            this.ExpeditionModelIcons.TryAdd("Monster Marker", monsterMarker);

            // Catch-all for ExpeditionMarker entities whose .ao model isn't in ExpeditionMarkerModelMap
            // (and which have no known reward MinimapIcon) - surfaces variants we haven't mapped yet.
            this.ExpeditionModelIcons.TryAdd(UnknownExpeditionMarkerKey, new IconPicker(iconPathName, 13, 42, 40, IconSize));
        }

        private void AddDefaultExpeditionRemnantIcons(string iconPathName)
        {
            this.ExpeditionRemnantIcons.TryAdd("Chest Item Quantity Remnant", new IconPicker(iconPathName, 11, 40, 100, IconSize));
        }

        private void AddDefaultBossIcons(string iconPathName)
        {
            this.BossIcons.TryAdd("Boss Arena", new IconPicker(iconPathName, 6, 57, 50, IconSize));
        }
    }
}
