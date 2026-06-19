// <copyright file="SettingsWindow.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Settings
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using ClickableTransparentOverlay;
    using ClickableTransparentOverlay.Win32;
    using Coroutine;
    using CoroutineEvents;
    using ImGuiNET;
    using Plugin;
    using Utils;
    using GameOffsets.Objects.States.InGameState;
    using GameHelper.RemoteEnums.Entity;
    using GameHelper.RemoteEnums;
    using GameHelper.Ui;

    /// <summary>
    ///     Creates the MainMenu on the UI.
    /// </summary>
    internal static class SettingsWindow
    {
        private static Vector4 color = new(1f, 1f, 0f, 1f);
        private static bool isOverlayRunningLocal = true;
        private static bool isSettingsWindowVisible = true;

        private static EntityFilterType efilterType = EntityFilterType.PATH;
        private static string filterText = string.Empty;
        private static Rarity erarity = Rarity.Normal;
        private static GameStats eStats = 0;
        private static int filterGroup = 0;

        private static string specialNpcPath = string.Empty;

        private static string specialMiscObjPath = string.Empty;

        private static string monterPathToIgnore = string.Empty;

#if DEBUG
        private static string pluginForHotReload = string.Empty;
        private static bool pluginLoaded = true;
        private static bool showImGuiDemo = false;
#endif

        /// <summary>
        ///     Initializes the Main Menu.
        /// </summary>
        internal static void InitializeCoroutines()
        {
            HideOnStartCheck();
            CoroutineHandler.Start(SaveCoroutine());
            Core.CoroutinesRegistrar.Add(CoroutineHandler.Start(
                RenderCoroutine(),
                "[Settings] Draw Core/Plugin settings",
                int.MaxValue));
        }

        private static void DrawManuBar()
        {
            if (!ImGui.BeginMenuBar())
            {
                return;
            }

            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.TextMuted);
            ImGui.Text($"GameHelper {Core.GetVersion()}");
            ImGui.PopStyleColor();
            ImGui.SameLine();
            ImGui.TextDisabled("|");
            ImGui.SameLine();
            ImGui.TextDisabled($"Hide/show menu: {Core.GHSettings.MainMenuHotKey}");

#if DEBUG
            ImGui.SameLine();
            ImGui.Checkbox("ImGui Demo", ref showImGuiDemo);
            if (showImGuiDemo)
            {
                ImGui.ShowDemoWindow(ref showImGuiDemo);
            }
#endif

            ImGui.EndMenuBar();
        }

        private static void DrawTabs()
        {
            if (ImGui.BeginTabBar("settingsTabBar", ImGuiTabBarFlags.AutoSelectNewTabs | ImGuiTabBarFlags.Reorderable))
            {
                if (ImGui.BeginTabItem("General"))
                {
                    if (ImGui.BeginChild("GeneralChildSetting"))
                    {
                        DrawCoreSettings();
                    }

                    ImGui.EndChild();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Plugins"))
                {
                    if (ImGui.BeginChild("PluginsChildSetting"))
                    {
                        DrawPluginManager();
                    }

                    ImGui.EndChild();
                    ImGui.EndTabItem();
                }

                DrawPluginSettingsTabs();

                ImGui.EndTabBar();
            }
        }

        /// <summary>
        ///     Draws the per-plugin settings tabs for every enabled plugin.
        /// </summary>
        private static void DrawPluginSettingsTabs()
        {
            foreach (var container in PManager.Plugins)
            {
                if (!container.Metadata.Enable)
                {
                    continue;
                }

                ImGui.PushStyleColor(ImGuiCol.Tab, new Vector4(0.16f, 0.20f, 0.30f, 1f));
                ImGui.PushStyleColor(ImGuiCol.TabHovered, new Vector4(0.28f, 0.38f, 0.55f, 1f));
                ImGui.PushStyleColor(ImGuiCol.TabSelected, ImGuiTheme.Accent);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.96f, 1f, 1f));

                if (ImGui.BeginTabItem($"{container.Name}##pluginCfg"))
                {
                    ImGuiTheme.BeginPanel($"PluginPanel_{container.Name}");
                    container.Plugin.DrawSettings();
                    ImGuiTheme.EndPanel();
                    ImGui.EndTabItem();
                }

                ImGui.PopStyleColor(4);
            }
        }

        /// <summary>
        ///     Draws the plugin manager table (enable/disable plugins).
        /// </summary>
        private static void DrawPluginManager()
        {
            ImGuiTheme.SectionHeader(
                "Plugin Management",
                "Enable or disable plugins. Enabled plugins get their own settings tab. Changes are saved automatically.");

            var enabledCount = PManager.Plugins.Count(p => p.Metadata.Enable);
            ImGui.TextDisabled($"Active: {enabledCount} / {PManager.Plugins.Count}");
            ImGui.SameLine();
            if (ImGui.SmallButton("Enable all"))
            {
                SetAllPlugins(true);
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Disable all"))
            {
                SetAllPlugins(false);
            }

            ImGui.Spacing();

            if (!ImGui.BeginTable(
                "pluginTable",
                3,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY,
                new Vector2(0, 0)))
            {
                return;
            }

            ImGui.TableSetupColumn("Plugin", ImGuiTableColumnFlags.WidthStretch, 0.7f);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 70f);
            ImGui.TableSetupColumn("Enable", ImGuiTableColumnFlags.WidthFixed, 60f);
            ImGui.TableHeadersRow();

            foreach (var container in PManager.Plugins)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text(container.Name);

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                if (container.Metadata.Enable)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.Success);
                    ImGui.Text("Active");
                    ImGui.PopStyleColor();
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.TextMuted);
                    ImGui.Text("Off");
                    ImGui.PopStyleColor();
                }

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                var enabled = container.Metadata.Enable;
                if (ImGui.Checkbox($"##enable_{container.Name}", ref enabled))
                {
                    SetPluginEnabled(container, enabled);
                }
            }

            ImGui.EndTable();
        }

        private static void SetAllPlugins(bool enabled)
        {
            foreach (var container in PManager.Plugins)
            {
                SetPluginEnabled(container, enabled);
            }
        }

        private static void SetPluginEnabled(PluginContainer container, bool enabled)
        {
            if (container.Metadata.Enable == enabled)
            {
                return;
            }

            container.Metadata.Enable = enabled;
            if (enabled)
            {
                container.Plugin.OnEnable(Core.Process.Address != IntPtr.Zero);
            }
            else
            {
                container.Plugin.SaveSettings();
                container.Plugin.OnDisable();
            }

            CoroutineHandler.RaiseEvent(GameHelperEvents.TimeToSaveAllSettings);
        }

        /// <summary>
        ///     Draws the currently selected settings on ImGui.
        /// </summary>
        private static void DrawCoreSettings()
        {
            ImGuiTheme.SectionHeader(
                "Status",
                $"All settings (including plugins) are saved automatically when you close the overlay or hide it via {Core.GHSettings.MainMenuHotKey}.");
            ImGui.Text("Current Game State:");
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.Accent);
            ImGui.Text($"{Core.States.GameCurrentState}");
            ImGui.PopStyleColor();
            ImGui.InputText("Party Leader Name", ref Core.GHSettings.LeaderName, 200);

            ImGuiTheme.SectionHeader("Controls & Display");
            DrawInputConfigWidget();
            DrawNearbyWidget();
            DrawToolsConfig();

            ImGuiTheme.SectionHeader(
                "Filters & Tracking",
                "Advanced entity filters. Change zone or restart after edits.");
            DrawPoiWidget();
            DrawMonstersToIgnore();
            DrawNPCWidget();
            DrawMiscObjWidget();

            ImGuiTheme.SectionHeader("Advanced");
            DrawMiscConfig();
            ChangeFontWidget();
            DrawReloadPluginWidget();

            ImGuiTheme.SectionHeader("About");
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
            ImGui.TextColored(color, "This is free software, if you purchased a copy you have been scammed");
            ImGui.TextColored(color, "For PoE2 0.5.2");
            ImGui.TextColored(color, "Zero Day developer is Kronos");
            ImGui.TextColored(color, "Offset updater is Arsenic, Nabeora, Lafko");
            ImGui.TextColored(color, "Official GameHelper2 Discord is https://discord.gg/864GyuM5S");
            ImGui.NewLine();
            ImGui.TextColored(Vector4.One, "Developer of this software is not responsible for " +
                              "any loss that may happen due to the usage of this software. Use this " +
                              "software at your own risk.");
            ImGui.PopTextWrapPos();
        }

        private static void DrawNearbyWidget()
        {
            if (ImGui.CollapsingHeader("Nearby Monster Config"))
            {
                ImGui.DragInt($"Small Range", ref Core.GHSettings.InnerCircle.Meaning,
                    1f, 0, Core.GHSettings.OuterCircle.Meaning);
                ImGui.SameLine();
                ImGui.Checkbox($"Visible##small", ref Core.GHSettings.InnerCircle.IsVisible);

                ImGui.DragInt($"Large Range", ref Core.GHSettings.OuterCircle.Meaning,
                    1f, Core.GHSettings.InnerCircle.Meaning, AreaInstanceConstants.NETWORK_BUBBLE_RADIUS);
                ImGui.SameLine();
                ImGui.Checkbox($"Visible##large", ref Core.GHSettings.OuterCircle.IsVisible);

                // ImGui.SameLine(0f, 30f);
                // ImGui.Checkbox($"Follow Mouse##{name}", ref value.FollowMouse);
            }
        }

        /// <summary>
        ///     Draws the ImGui widget for changing fonts.
        /// </summary>
        private static void ChangeFontWidget()
        {
            if (ImGui.CollapsingHeader("Change Fonts"))
            {
                ImGui.Checkbox("Universal Font (render any language across the whole overlay)", ref Core.GHSettings.UniversalFont);
                ImGuiHelper.ToolTip("Loads a bundled merged font (DejaVuSans + the font below + GNU Unifont over the whole " +
                    "Unicode BMP) so text in any language renders everywhere. The font below is still merged in as the " +
                    "priority for its language. Building the full atlas is heavier, so this is off by default.");

                ImGui.InputText("Pathname", ref Core.GHSettings.FontPathName, 300);
                ImGui.DragInt("Size", ref Core.GHSettings.FontSize, 0.1f, 13, 40);
                var languageChanged = ImGuiHelper.EnumComboBox("Language", ref Core.GHSettings.FontLanguage);
                var customLanguage = ImGui.InputText("Custom Glyph Ranges", ref Core.GHSettings.FontCustomGlyphRange, 100);
                ImGuiHelper.ToolTip("This is advance level feature. Do not modify this if you don't know what you are doing. " +
                    "Example usage:- If you have downloaded and pointed to the ArialUnicodeMS.ttf font, you can use " +
                    "0x0020, 0xFFFF, 0x00 text in this field to load all of the font texture in ImGui. Note the 0x00" +
                    " as the last item in the range.");
                if (languageChanged)
                {
                    Core.GHSettings.FontCustomGlyphRange = string.Empty;
                }

                if (customLanguage)
                {
                    Core.GHSettings.FontLanguage = FontGlyphRangeType.English;
                }

                if (ImGui.Button("Apply Changes"))
                {
                    UniversalFont.ApplyFromSettings();
                }
            }
        }

        /// <summary>
        ///     Draws the ImGui widget for changing POI monsters.
        /// </summary>
        private static void DrawPoiWidget()
        {
            var isOpened = ImGui.CollapsingHeader("Special Monster Tracker (A.K.A Monster POI)");
            ImGuiHelper.ToolTip("In order to figure out the path/mod to add " +
                "please open DV -> States -> InGameState -> CurrentAreaInstance -> " +
                "Awake Entities -> click dump button against the entity you want to add. " +
                "This will create a new file in entity_dumps folder with all mod names and " +
                "path of that entity.");
            if (isOpened)
            {
                ImGui.TextWrapped("Please restart gamehelper or change area/zone if you make any changes over here.");
                for (var i = Core.GHSettings.PoiMonstersCategories2.Count - 1; i >= 0; i--)
                {
                    var (filtertype, filter, rarity, stat, group) = Core.GHSettings.PoiMonstersCategories2[i];
                    var isChanged = false;
                    ImGui.SetNextItemWidth(ImGui.GetFontSize() * 10);
                    if (ImGuiHelper.EnumComboBox($"Filter type     ##{i}MonsterPoiWidget", ref filtertype))
                    {
                        isChanged = true;
                    }

                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(ImGui.GetFontSize() * 27);
                    if (ImGui.InputText($"Filter     ##{i}MonsterPoiWidget", ref filter, 200))
                    {
                        isChanged = true;
                    }

                    ImGuiHelper.ToolTip(filtertype == EntityFilterType.PATH ||
                        filtertype == EntityFilterType.PATHANDRARITY ||
                        filtertype == EntityFilterType.PATHANDSTAT ?
                        "Path is going to be checked from left to right (i.e. String.StartsWith), up till the filter length." :
                        "Mod name is fully checked, it need to be 100% match.");
                    ImGui.SameLine();
                    if (filtertype == EntityFilterType.PATHANDRARITY || filtertype == EntityFilterType.MODANDRARITY)
                    {
                        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                        if (ImGuiHelper.EnumComboBox($"Rarity     ##{i}MonsterPoiWidget", ref rarity))
                        {
                            isChanged = true;
                        }

                        ImGui.SameLine();
                    }

                    if (filtertype == EntityFilterType.PATHANDSTAT)
                    {
                        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                        if (ImGuiHelper.NonContinuousEnumComboBox($"Stat        ##{i}MonsterPoiWidget", ref stat))
                        {
                            isChanged = true;
                        }

                        ImGui.SameLine();
                    }

                    ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                    if (ImGui.InputInt($"Group Number##{i}MonsterPoiWidget", ref group))
                    {
                        if (group < 0)
                        {
                            group = 0;
                        }

                        isChanged = true;
                    }

                    if (isChanged)
                    {
                        Core.GHSettings.PoiMonstersCategories2[i] = new(filtertype, filter, rarity, stat, group);
                    }

                    ImGui.SameLine();
                    if (ImGui.Button($"delete##{i}MonsterPoiWidget"))
                    {
                        Core.GHSettings.PoiMonstersCategories2.RemoveAt(i);
                    }
                }

                ImGui.Separator();
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 10);
                ImGuiHelper.EnumComboBox($"Filter type     ##addMonsterPoiWidget", ref efilterType);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 17);
                ImGui.InputText($"Filter     ##addMonsterPoiWidget", ref filterText, 200);
                ImGuiHelper.ToolTip(efilterType == EntityFilterType.PATH ||
                    efilterType == EntityFilterType.PATHANDRARITY ||
                    efilterType == EntityFilterType.PATHANDSTAT ?
                    "Path is going to be checked from left to right (i.e. String.StartsWith), up till the filter length." :
                    "Mod name is fully checked, it need to be 100% match.");
                ImGui.SameLine();
                if (efilterType == EntityFilterType.PATHANDRARITY || efilterType == EntityFilterType.MODANDRARITY)
                {
                    ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                    ImGuiHelper.EnumComboBox($"Rarity     ##addMonsterPoiWidget", ref erarity);
                    ImGui.SameLine();
                }

                if (efilterType == EntityFilterType.PATHANDSTAT)
                {
                    ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                    ImGuiHelper.NonContinuousEnumComboBox($"Stat        ##addMonsterPoiWidget", ref eStats);
                    ImGui.SameLine();
                }

                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                if (ImGui.InputInt($"Group Number##addMonsterPoiWidget", ref filterGroup) && filterGroup < 0)
                {
                    filterGroup = 0;
                }

                ImGui.SameLine();
                if(ImGui.Button("add##MonsterPoiWidget"))
                {
                    Core.GHSettings.PoiMonstersCategories2.Add(new(efilterType, filterText, erarity, eStats, filterGroup));
                    efilterType = EntityFilterType.PATH;
                    eStats = GameStats.is_capturable_monster;
                    filterText = string.Empty;
                    filterGroup = 0;
                }
            }
        }

        /// <summary>
        ///     Draws the ImGui widget for ignoring monsters.
        /// </summary>
        private static void DrawMonstersToIgnore()
        {
            var isOpened = ImGui.CollapsingHeader("Ignore Monsters");
            ImGuiHelper.ToolTip("In order to figure out the path, please open " +
                "DV -> States -> InGameState -> CurrentAreaInstance -> Awake Entities -> " +
                "Click Path -> see NPC path in the game world");
            if (isOpened)
            {
                ImGui.TextWrapped("Please restart gamehelper or change area/zone if you make any changes over here.");
                ImGui.InputText("Monster metadata path##ToRemove", ref monterPathToIgnore, 200);
                ImGuiHelper.ToolTip("Path is going to be checked from left to right (i.e. String.StartsWith), up till the filter length.");
                ImGui.SameLine();
                if (ImGui.Button("Add##monsterPathToRemove") && !string.IsNullOrEmpty(monterPathToIgnore))
                {
                    Core.GHSettings.MonstersPathsToIgnore.Add(monterPathToIgnore);
                    monterPathToIgnore = string.Empty;
                }

                for (var i = Core.GHSettings.MonstersPathsToIgnore.Count - 1; i >= 0; i--)
                {
                    ImGui.Text($"Path: {Core.GHSettings.MonstersPathsToIgnore[i]}");
                    ImGui.SameLine();
                    if (ImGui.Button($"Delete##{i}monsterPathToRemove"))
                    {
                        Core.GHSettings.MonstersPathsToIgnore.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>
        ///     Draws the ImGui widget for defining important NPCs.
        /// </summary>
        private static void DrawNPCWidget()
        {
            var isOpened = ImGui.CollapsingHeader("Special NPC Metadata Paths");
            ImGuiHelper.ToolTip("In order to figure out the path, please open " +
                "DV -> States -> InGameState -> CurrentAreaInstance -> Awake Entities -> " +
                "Click Path -> see NPC path in the game world");
            if (isOpened)
            {
                ImGui.TextWrapped("Please restart gamehelper or change area/zone if you make any changes over here.");
                ImGui.InputText("NPC Path##specialNPCPath", ref specialNpcPath, 200);
                ImGuiHelper.ToolTip("Path is going to be checked from left to right (i.e. String.StartsWith), up till the filter length.");
                ImGui.SameLine();
                if (ImGui.Button("Add##specialNPCPath") && !string.IsNullOrEmpty(specialNpcPath))
                {
                    Core.GHSettings.SpecialNPCPaths.Add(specialNpcPath);
                    specialNpcPath = string.Empty;
                }

                for (var i = Core.GHSettings.SpecialNPCPaths.Count - 1; i >= 0; i--)
                {
                    ImGui.Text($"Path: {Core.GHSettings.SpecialNPCPaths[i]}");
                    ImGui.SameLine();
                    if(ImGui.Button($"Delete##{i}specialNPCPath"))
                    {
                        Core.GHSettings.SpecialNPCPaths.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>
        ///     Draws the ImGui widget for defining important MiscellaneousObjects.
        /// </summary>
        private static void DrawMiscObjWidget()
        {
            var isOpened = ImGui.CollapsingHeader("Special Objects Metadata Paths");
            ImGuiHelper.ToolTip("In order to figure out the path, please open " +
                "DV -> States -> InGameState -> CurrentAreaInstance -> Awake Entities -> " +
                "Click Path -> see objects path in the game world");
            if (isOpened)
            {
                ImGui.TextWrapped("Please restart gamehelper or change area/zone if you make any changes over here.");
                ImGui.InputText("Object Path##MiscObjWidget", ref specialMiscObjPath, 200);
                ImGuiHelper.ToolTip("Path is going to be checked from left to right (i.e. String.StartsWith), up till the filter length.");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                if (ImGui.InputInt($"Group Number##MiscObjgroup", ref filterGroup) && filterGroup < 0)
                {
                    filterGroup = 0;
                }

                ImGui.SameLine();
                if (ImGui.Button("add##MiscObjadd"))
                {
                    Core.GHSettings.SpecialMiscObjPaths.Add(new(specialMiscObjPath, filterGroup));
                    specialMiscObjPath = string.Empty;
                    filterGroup = 0;
                }

                for (var i = Core.GHSettings.SpecialMiscObjPaths.Count - 1; i >= 0; i--)
                {
                    ImGui.Text($"Path: {Core.GHSettings.SpecialMiscObjPaths[i].path}, GroupId: {Core.GHSettings.SpecialMiscObjPaths[i].group}");
                    ImGui.SameLine();
                    if (ImGui.Button($"Delete##MiscObjDel{i}"))
                    {
                        Core.GHSettings.SpecialMiscObjPaths.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>
        ///     Draws the ImGui widget for changing keyboard related settings
        /// </summary>
        private static void DrawInputConfigWidget()
        {
            if (ImGui.CollapsingHeader("Input Config"))
            {
                ImGui.DragInt("Key Timeout", ref Core.GHSettings.KeyPressTimeout, 0.2f, 60, 300);
                ImGuiHelper.ToolTip("When GameOverlay press a key in the game, the key " +
                    "has to go to the GGG server for it to work. This process takes " +
                    "time equal to your latency x 3. During this time GameOverlay might " +
                    "press that key again. Set the key timeout value to latency x 3 so " +
                    "this doesn't happen. e.g. for 30ms latency, set it to 90ms. Also, " +
                    "do not go below 60 (due to server ticks), no matter how good your latency is.");
                ImGuiHelper.NonContinuousEnumComboBox("Settings Window Key", ref Core.GHSettings.MainMenuHotKey);
                ImGuiHelper.NonContinuousEnumComboBox("Disable Rendering Key", ref Core.GHSettings.DisableAllRenderingKey);
                ImGuiHelper.NonContinuousEnumComboBox("Element Finder Key", ref Core.GHSettings.ElementFinderHotKey);
            }
        }

        /// <summary>
        ///     Draws the imgui widget for enabling/disabling tools.
        /// </summary>
        private static void DrawToolsConfig()
        {
            if (ImGui.CollapsingHeader("Misc Tools"))
            {
                ImGui.Checkbox("Performance Stats", ref Core.GHSettings.ShowPerfStats);
                if (Core.GHSettings.ShowPerfStats)
                {
                    ImGui.Spacing();
                    ImGui.SameLine();
                    ImGui.Spacing();
                    ImGui.SameLine();
                    ImGui.Checkbox("Hide when game is in background", ref Core.GHSettings.HidePerfStatsWhenBg);
                    ImGui.Spacing();
                    ImGui.SameLine();
                    ImGui.Spacing();
                    ImGui.SameLine();
                    ImGui.Checkbox("Show minimum stats", ref Core.GHSettings.MinimumPerfStats);
                }

                ImGui.Checkbox("Game UiExplorer (GE)", ref Core.GHSettings.ShowGameUiExplorer);
                ImGui.Checkbox("Element Finder", ref Core.GHSettings.ShowElementFinder);
                ImGui.Checkbox("Data Visualization (DV)", ref Core.GHSettings.ShowDataVisualization);
                ImGui.Checkbox("Performance Profiler", ref Core.GHSettings.ShowPerfProfiler);
                ImGui.Checkbox("Memory Read Diagnostics", ref Core.GHSettings.ShowMemoryDiagnostics);
#if DEBUG
                ImGui.Checkbox("Krangled Passive Detector", ref Core.GHSettings.ShowKrangledPassiveDetector);
#endif
            }
        }

        /// <summary>
        ///     Draws the imgui widget for showing misc config
        /// </summary>
        private static void DrawMiscConfig()
        {
            if (ImGui.CollapsingHeader("Miscellaneous Config"))
            {
                if (ImGui.Checkbox("Fix Taskbar not showing", ref Core.GHSettings.FixTaskbarNotShowing))
                {
                    if (Core.States.GameCurrentState != GameStateTypes.GameNotLoaded)
                    {
                        CoroutineHandler.RaiseEvent(GameHelperEvents.OnMoved);
                    }
                }

                ImGui.Checkbox("Disable entity processing when in town or hideout",
                    ref Core.GHSettings.DisableEntityProcessingInTownOrHideout);
                ImGui.Checkbox("Hide overlay settings upon start", ref Core.GHSettings.HideSettingWindowOnStart);
                ImGui.Checkbox("Close GameHelper when Game Exit", ref Core.GHSettings.CloseWhenGameExit);
                if (ImGui.Checkbox("V-Sync", ref Core.Overlay.VSync))
                {
                    Core.GHSettings.Vsync = Core.Overlay.VSync;
                }

                ImGui.BeginDisabled(Core.Overlay.VSync);
                if (ImGui.InputInt("FPS Limiter (0 to disable)", ref Core.GHSettings.FPSLimit))
                {
                    Core.Overlay.FPSLimit = Core.GHSettings.FPSLimit;
                }

                ImGui.EndDisabled();

                ImGuiHelper.ToolTip("WARNING: There is no rate limiter in GameHelper, once V-Sync is off,\n" +
                    "it's your responsibility to use external rate limiter e.g. NVIDIA Control Panel\n" +
                    "-> Manage 3D Settings -> Set Max Framerate to what your monitor support.");
                ImGui.Checkbox("Process all renderable entities", ref Core.GHSettings.ProcessAllRenderableEntities);
                ImGuiHelper.ToolTip("WARNING: This will greatly reduce GH speed as well as increase crashes/glitches. Always keep it unchecked.");
                ImGui.Checkbox("Disable debug counters (do it on 6 man party + juiced maps only)", ref Core.GHSettings.DisableAllCounters);
                ImGui.Text("Entity MaxDegreeOfParallelism");
                ImGuiHelper.ToolTip("This limits the entity reading algorithm to a set number of CPUs." +
                    " Select -1 to disable this limit. Use Task Manager CPU usage stat + Misc Tools -> performance stats" +
                    " to figure out best FPS to CPU usage ratio.");
                ImGui.SameLine();
                if (ImGui.RadioButton("-1", Core.GHSettings.EntityReaderMaxDegreeOfParallelism == -1))
                {
                    Core.GHSettings.EntityReaderMaxDegreeOfParallelism = -1;
                }
                ImGui.SameLine();

                for (var i = 2; i < 128; i*=2)
                {
                    if (ImGui.RadioButton(i.ToString(), Core.GHSettings.EntityReaderMaxDegreeOfParallelism == i))
                    {
                        Core.GHSettings.EntityReaderMaxDegreeOfParallelism = i;
                    }

                    if (i*2 < 128)
                    {
                        ImGui.SameLine();
                    }
                }

                ImGui.Checkbox("Is Taiwan client", ref Core.GHSettings.IsTaiwanClient);

                ImGui.Separator();
                ImGui.Text("Entity Staleness Fixes");
                ImGuiHelper.ToolTip("These options help detect and fix stale entity data " +
                    "(e.g. NPCs that teleport but keep old position in memory).");

                ImGui.Checkbox("Enable NPC entity cleanup", ref Core.GHSettings.EnableNpcEntityCleanup);
                ImGuiHelper.ToolTip("Include NPC entities in the removal logic when they go invalid.\n" +
                    "Prevents stale NPC entities from lingering in the entity dictionary.");

                ImGui.Checkbox("Enable stale entity cleanup", ref Core.GHSettings.EnableStaleEntityCleanup);
                ImGuiHelper.ToolTip("Remove any entity that stays invalid for many consecutive frames,\n" +
                    "regardless of entity type. Catches NPCs and other entities that\n" +
                    "the default cleanup misses.");

                if (Core.GHSettings.EnableStaleEntityCleanup)
                {
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(80);
                    ImGui.InputInt("threshold (frames)", ref Core.GHSettings.StaleEntityFrameThreshold);
                    if (Core.GHSettings.StaleEntityFrameThreshold < 10)
                        Core.GHSettings.StaleEntityFrameThreshold = 10;
                }
            }
        }

        /// <summary>
        ///     Draws the imgui widget for reloading plugins
        /// </summary>
        private static void DrawReloadPluginWidget()
        {
#if DEBUG
            if (ImGui.CollapsingHeader("Reload Plugin"))
            {
                ImGuiHelper.IEnumerableComboBox<string>("Plugins", PManager.PluginNames, ref pluginForHotReload);
                ImGui.BeginDisabled(!pluginLoaded || string.IsNullOrEmpty(pluginForHotReload));
                if (ImGui.Button("Unload Plugin"))
                {
                    if (PManager.UnloadPlugin(pluginForHotReload))
                    {
                        pluginLoaded = false;
                    }
                }

                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.BeginDisabled(pluginLoaded || string.IsNullOrEmpty(pluginForHotReload));
                if (ImGui.Button("Load Plugin"))
                {
                    if (PManager.LoadPlugin(pluginForHotReload))
                    {
                        pluginLoaded = true;
                    }
                }

                ImGui.EndDisabled();
            }
#endif
        }

        /// <summary>
        ///     Draws the closing confirmation popup on ImGui.
        /// </summary>
        private static void DrawConfirmationPopup()
        {
            ImGui.SetNextWindowPos(new Vector2(Core.Overlay.Size.Width / 3f, Core.Overlay.Size.Height / 3f));
            if (ImGui.BeginPopup("GameHelperCloseConfirmation"))
            {
                ImGui.Text("Do you want to quit the GameHelper overlay?");
                ImGui.Separator();
                if (ImGui.Button("Yes", new Vector2(ImGui.GetContentRegionAvail().X / 2f, ImGui.GetTextLineHeight() * 2)))
                {
                    Core.GHSettings.IsOverlayRunning = false;
                    ImGui.CloseCurrentPopup();
                    isOverlayRunningLocal = true;
                }

                ImGui.SameLine();
                if (ImGui.Button("No", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeight() * 2)))
                {
                    ImGui.CloseCurrentPopup();
                    isOverlayRunningLocal = true;
                }

                ImGui.EndPopup();
            }
        }

        /// <summary>
        ///     Hides the overlay on startup.
        /// </summary>
        private static void HideOnStartCheck()
        {
            if (Core.GHSettings.HideSettingWindowOnStart)
            {
                isSettingsWindowVisible = false;
            }
        }

        /// <summary>
        ///     Draws the Settings Window.
        /// </summary>
        /// <returns>co-routine IWait.</returns>
        private static IEnumerator<Wait> RenderCoroutine()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnRender);
                if (Utils.IsKeyPressedAndNotTimeout(Core.GHSettings.MainMenuHotKey))
                {
                    isSettingsWindowVisible = !isSettingsWindowVisible;
                    ImGui.GetIO().WantCaptureMouse = true;
                    if (!isSettingsWindowVisible)
                    {
                        CoroutineHandler.RaiseEvent(GameHelperEvents.TimeToSaveAllSettings);
                    }
                }

                Core.IsSettingsMenuOpen = isSettingsWindowVisible;
                if (!isSettingsWindowVisible)
                {
                    continue;
                }

                ImGui.SetNextWindowSizeConstraints(new Vector2(800, 600), Vector2.One * float.MaxValue);
                var isMainMenuExpanded = ImGui.Begin(
                    $"Game Overlay Settings [ {Core.GetVersion()} ]",
                    ref isOverlayRunningLocal,
                    ImGuiWindowFlags.MenuBar);

                if (!isOverlayRunningLocal)
                {
                    ImGui.OpenPopup("GameHelperCloseConfirmation");
                }

                DrawConfirmationPopup();
                if (!Core.GHSettings.IsOverlayRunning)
                {
                    CoroutineHandler.RaiseEvent(GameHelperEvents.TimeToSaveAllSettings);
                }

                if (!isMainMenuExpanded)
                {
                    ImGui.End();
                    continue;
                }

                DrawManuBar();
                DrawTabs();
                ImGui.End();
            }
        }

        /// <summary>
        ///     Saves the GameHelper settings to disk.
        /// </summary>
        /// <returns>co-routine IWait.</returns>
        private static IEnumerator<Wait> SaveCoroutine()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.TimeToSaveAllSettings);
                JsonHelper.SafeToFile(Core.GHSettings, State.CoreSettingFile);
            }
        }
    }
}
