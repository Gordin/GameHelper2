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
    using L = GameHelper.Localization.OverlayLocalization;

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
            ImGui.TextDisabled(L.F("settings.menu.hide_show", "Hide/show menu: {0}", Core.GHSettings.MainMenuHotKey));

#if DEBUG
            ImGui.SameLine();
            ImGui.Checkbox(L.Label("settings.debug.imgui_demo", "ImGui Demo", "ImGuiDemo"), ref showImGuiDemo);
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
                if (ImGui.BeginTabItem(L.Title("settings.tabs.general", "General", "GeneralTab")))
                {
                    if (ImGui.BeginChild("GeneralChildSetting"))
                    {
                        DrawCoreSettings();
                    }

                    ImGui.EndChild();
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem(L.Title("settings.tabs.plugins", "Plugins", "PluginsTab")))
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
                L.T("settings.plugin.title", "Plugin Management"),
                L.T(
                    "settings.plugin.subtitle",
                    "Enable or disable plugins. Enabled plugins get their own settings tab. Changes are saved automatically."));

            var enabledCount = PManager.Plugins.Count(p => p.Metadata.Enable);
            ImGui.TextDisabled(L.F("settings.plugin.active_count", "Active: {0} / {1}", enabledCount, PManager.Plugins.Count));
            ImGui.SameLine();
            if (ImGui.SmallButton(L.Label("settings.plugin.enable_all", "Enable all", "EnableAllPlugins")))
            {
                SetAllPlugins(true);
            }

            ImGui.SameLine();
            if (ImGui.SmallButton(L.Label("settings.plugin.disable_all", "Disable all", "DisableAllPlugins")))
            {
                SetAllPlugins(false);
            }

            ImGui.Spacing();

            if (!ImGui.BeginTable(
                "pluginTable",
                4,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersOuter | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.ScrollY,
                new Vector2(0, 0)))
            {
                return;
            }

            ImGui.TableSetupColumn(L.T("settings.plugin.column.plugin", "Plugin"), ImGuiTableColumnFlags.WidthStretch, 0.45f);
            ImGui.TableSetupColumn(L.T("settings.plugin.column.description", "Description"), ImGuiTableColumnFlags.WidthStretch, 1.0f);
            ImGui.TableSetupColumn(L.T("settings.plugin.column.status", "Status"), ImGuiTableColumnFlags.WidthFixed, 70f);
            ImGui.TableSetupColumn(L.T("settings.plugin.column.enable", "Enable"), ImGuiTableColumnFlags.WidthFixed, 60f);
            ImGui.TableHeadersRow();

            foreach (var container in PManager.Plugins)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.Text(container.Name);

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                var description = container.Plugin.GetDescription();
                if (string.IsNullOrWhiteSpace(description))
                {
                    ImGui.TextDisabled("-");
                }
                else
                {
                    ImGui.TextUnformatted(description);
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(description);
                    }
                }

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                if (container.Metadata.Enable)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.Success);
                    ImGui.Text(L.T("settings.plugin.status.active", "Active"));
                    ImGui.PopStyleColor();
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.TextMuted);
                    ImGui.Text(L.T("settings.plugin.status.off", "Off"));
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
            PManager.SetPluginEnabled(container, enabled);
            CoroutineHandler.RaiseEvent(GameHelperEvents.TimeToSaveAllSettings);
        }

        /// <summary>
        ///     Draws the currently selected settings on ImGui.
        /// </summary>
        private static void DrawCoreSettings()
        {
            ImGuiTheme.SectionHeader(
                L.T("settings.status.title", "Status"),
                L.F(
                    "settings.status.subtitle",
                    "All settings (including plugins) are saved automatically when you close the overlay or hide it via {0}.",
                    Core.GHSettings.MainMenuHotKey));
            ImGui.Text(L.T("settings.status.current_game_state", "Current Game State:"));
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.Accent);
            ImGui.Text($"{Core.States.GameCurrentState}");
            ImGui.PopStyleColor();
            ImGui.InputText(L.Label("settings.status.party_leader_name", "Party Leader Name", "PartyLeaderName"), ref Core.GHSettings.LeaderName, 200);

            ImGuiTheme.SectionHeader(L.T("settings.language.title", "Language"));
            DrawUiLanguageWidget();

            ImGuiTheme.SectionHeader(L.T("settings.controls.title", "Controls & Display"));
            DrawInputConfigWidget();
            DrawNearbyWidget();
            DrawToolsConfig();

            ImGuiTheme.SectionHeader(
                L.T("settings.filters.title", "Filters & Tracking"),
                L.T("settings.filters.subtitle", "Advanced entity filters. Change zone or restart after edits."));
            DrawPoiWidget();
            DrawMonstersToIgnore();
            DrawNPCWidget();
            DrawMiscObjWidget();

            ImGuiTheme.SectionHeader(L.T("settings.advanced.title", "Advanced"));
            DrawMiscConfig();
            ChangeFontWidget();
            DrawReloadPluginWidget();

            ImGuiTheme.SectionHeader(L.T("settings.about.title", "About"));
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
            ImGui.TextColored(color, L.T("settings.about.scam", "This is free software, if you purchased a copy you have been scammed"));
            ImGui.TextColored(color, L.T("settings.about.version", "For PoE2 0.5.4c"));
            ImGui.TextColored(color, L.T("settings.about.zero_day", "Zero Day developer is Kronos"));
            ImGui.TextColored(color, L.T("settings.about.offset", "Offset updater is Arsenic, Nabeora, Lafko"));
            ImGui.TextColored(color, L.T("settings.about.discord", "Official GameHelper2 Discord is https://discord.gg/864GyuM5S"));
            ImGui.NewLine();
            ImGui.TextColored(
                Vector4.One,
                L.T(
                    "settings.about.disclaimer",
                    "Developer of this software is not responsible for any loss that may happen due to the usage of this software. Use this software at your own risk."));
            ImGui.PopTextWrapPos();
        }

        private static void DrawUiLanguageWidget()
        {
            var selectedLanguage = Core.GHSettings.UiLanguage;
            if (ImGui.BeginCombo(
                    L.Label("settings.language.label", "UI Language", "UiLanguage"),
                    L.DisplayName(selectedLanguage)))
            {
                foreach (var language in L.SupportedLanguages)
                {
                    var selected = language == selectedLanguage;
                    if (ImGui.Selectable(L.DisplayName(language), selected))
                    {
                        Core.GHSettings.UiLanguage = language;
                        CoroutineHandler.RaiseEvent(GameHelperEvents.TimeToSaveAllSettings);
                    }

                    if (selected)
                    {
                        ImGui.SetItemDefaultFocus();
                    }
                }

                ImGui.EndCombo();
            }

            ImGuiHelper.ToolTip(
                L.T(
                    "settings.language.tooltip",
                    "Controls GameHelper's main overlay text. Font glyph range is configured separately below."));
        }

        private static void DrawNearbyWidget()
        {
            if (ImGui.CollapsingHeader(L.Title("settings.nearby.title", "Nearby Monster Config", "NearbyMonsterConfig")))
            {
                ImGui.DragInt(L.Label("settings.nearby.small_range", "Small Range", "SmallRange"), ref Core.GHSettings.InnerCircle.Meaning,
                    1f, 0, Core.GHSettings.OuterCircle.Meaning);
                ImGui.SameLine();
                ImGui.Checkbox($"{L.T("settings.nearby.visible", "Visible")}##small", ref Core.GHSettings.InnerCircle.IsVisible);

                ImGui.DragInt(L.Label("settings.nearby.large_range", "Large Range", "LargeRange"), ref Core.GHSettings.OuterCircle.Meaning,
                    1f, Core.GHSettings.InnerCircle.Meaning, AreaInstanceConstants.NETWORK_BUBBLE_RADIUS);
                ImGui.SameLine();
                ImGui.Checkbox($"{L.T("settings.nearby.visible", "Visible")}##large", ref Core.GHSettings.OuterCircle.IsVisible);

                // ImGui.SameLine(0f, 30f);
                // ImGui.Checkbox($"Follow Mouse##{name}", ref value.FollowMouse);
            }
        }

        /// <summary>
        ///     Draws the ImGui widget for changing fonts.
        /// </summary>
        private static void ChangeFontWidget()
        {
            if (ImGui.CollapsingHeader(L.Title("settings.font.title", "Change Fonts", "ChangeFonts")))
            {
                ImGui.Checkbox(
                    L.Label(
                        "settings.font.universal",
                        "Universal Font (render any language across the whole overlay)",
                        "UniversalFont"),
                    ref Core.GHSettings.UniversalFont);
                ImGuiHelper.ToolTip(
                    L.T(
                        "settings.font.universal.tooltip",
                        "Loads a bundled merged font (DejaVuSans + the font below + GNU Unifont over the whole Unicode BMP) so text in any language renders everywhere. The font below is still merged in as the priority for its language. Building the full atlas is heavier, so this is off by default."));

                ImGui.InputText(L.Label("settings.font.pathname", "Pathname", "FontPathname"), ref Core.GHSettings.FontPathName, 300);
                ImGui.DragInt(L.Label("settings.font.size", "Size", "FontSize"), ref Core.GHSettings.FontSize, 0.1f, 13, 40);
                var languageChanged = ImGuiHelper.EnumComboBox(
                    L.Label("settings.font.glyph_range", "Font Glyph Range", "FontGlyphRange"),
                    ref Core.GHSettings.FontLanguage);
                var customLanguage = ImGui.InputText(
                    L.Label("settings.font.custom_glyph_ranges", "Custom Glyph Ranges", "FontCustomGlyphRanges"),
                    ref Core.GHSettings.FontCustomGlyphRange,
                    100);
                ImGuiHelper.ToolTip(
                    L.T(
                        "settings.font.custom_glyph_ranges.tooltip",
                        "This is advance level feature. Do not modify this if you don't know what you are doing. Example usage:- If you have downloaded and pointed to the ArialUnicodeMS.ttf font, you can use 0x0020, 0xFFFF, 0x00 text in this field to load all of the font texture in ImGui. Note the 0x00 as the last item in the range."));
                if (languageChanged)
                {
                    Core.GHSettings.FontCustomGlyphRange = string.Empty;
                }

                if (customLanguage)
                {
                    Core.GHSettings.FontLanguage = FontGlyphRangeType.English;
                }

                if (ImGui.Button(L.Label("settings.font.apply_changes", "Apply Changes", "ApplyFontChanges")))
                {
                    UniversalFont.ApplyFromSettings();
                }
            }
        }

        private static string FilterTooltip(EntityFilterType filterType) =>
            filterType == EntityFilterType.PATH ||
            filterType == EntityFilterType.PATHANDRARITY ||
            filterType == EntityFilterType.PATHANDSTAT
                ? L.T(
                    "settings.common.path_match.tooltip",
                    "Path is going to be checked from left to right (i.e. String.StartsWith), up till the filter length.")
                : L.T("settings.common.mod_match.tooltip", "Mod name is fully checked, it need to be 100% match.");

        /// <summary>
        ///     Draws the ImGui widget for changing POI monsters.
        /// </summary>
        private static void DrawPoiWidget()
        {
            var isOpened = ImGui.CollapsingHeader(L.Title("settings.poi.title", "Special Monster Tracker (A.K.A Monster POI)", "MonsterPoi"));
            ImGuiHelper.ToolTip(
                L.T(
                    "settings.poi.tooltip",
                    "In order to figure out the path/mod to add please open DV -> States -> InGameState -> CurrentAreaInstance -> Awake Entities -> click dump button against the entity you want to add. This will create a new file in entity_dumps folder with all mod names and path of that entity."));
            if (isOpened)
            {
                ImGui.TextWrapped(L.T("settings.common.restart_or_zone", "Please restart gamehelper or change area/zone if you make any changes over here."));
                for (var i = Core.GHSettings.PoiMonstersCategories2.Count - 1; i >= 0; i--)
                {
                    var (filtertype, filter, rarity, stat, group) = Core.GHSettings.PoiMonstersCategories2[i];
                    var isChanged = false;
                    ImGui.SetNextItemWidth(ImGui.GetFontSize() * 10);
                    if (ImGuiHelper.EnumComboBox($"{L.T("settings.common.filter_type", "Filter type")}##{i}MonsterPoiWidgetFilterType", ref filtertype))
                    {
                        isChanged = true;
                    }

                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(ImGui.GetFontSize() * 27);
                    if (ImGui.InputText($"{L.T("settings.common.filter", "Filter")}##{i}MonsterPoiWidgetFilter", ref filter, 200))
                    {
                        isChanged = true;
                    }

                    ImGuiHelper.ToolTip(FilterTooltip(filtertype));
                    ImGui.SameLine();
                    if (filtertype == EntityFilterType.PATHANDRARITY || filtertype == EntityFilterType.MODANDRARITY)
                    {
                        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                        if (ImGuiHelper.EnumComboBox($"{L.T("settings.common.rarity", "Rarity")}##{i}MonsterPoiWidgetRarity", ref rarity))
                        {
                            isChanged = true;
                        }

                        ImGui.SameLine();
                    }

                    if (filtertype == EntityFilterType.PATHANDSTAT)
                    {
                        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                        if (ImGuiHelper.NonContinuousEnumComboBox($"{L.T("settings.common.stat", "Stat")}##{i}MonsterPoiWidgetStat", ref stat))
                        {
                            isChanged = true;
                        }

                        ImGui.SameLine();
                    }

                    ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                    if (ImGui.InputInt($"{L.T("settings.common.group_number", "Group Number")}##{i}MonsterPoiWidgetGroup", ref group))
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
                    if (ImGui.Button($"{L.T("settings.common.delete_lower", "delete")}##{i}MonsterPoiWidget"))
                    {
                        Core.GHSettings.PoiMonstersCategories2.RemoveAt(i);
                    }
                }

                ImGui.Separator();
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 10);
                ImGuiHelper.EnumComboBox($"{L.T("settings.common.filter_type", "Filter type")}##addMonsterPoiWidgetFilterType", ref efilterType);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 17);
                ImGui.InputText($"{L.T("settings.common.filter", "Filter")}##addMonsterPoiWidgetFilter", ref filterText, 200);
                ImGuiHelper.ToolTip(FilterTooltip(efilterType));
                ImGui.SameLine();
                if (efilterType == EntityFilterType.PATHANDRARITY || efilterType == EntityFilterType.MODANDRARITY)
                {
                    ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                    ImGuiHelper.EnumComboBox($"{L.T("settings.common.rarity", "Rarity")}##addMonsterPoiWidgetRarity", ref erarity);
                    ImGui.SameLine();
                }

                if (efilterType == EntityFilterType.PATHANDSTAT)
                {
                    ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                    ImGuiHelper.NonContinuousEnumComboBox($"{L.T("settings.common.stat", "Stat")}##addMonsterPoiWidgetStat", ref eStats);
                    ImGui.SameLine();
                }

                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                if (ImGui.InputInt($"{L.T("settings.common.group_number", "Group Number")}##addMonsterPoiWidgetGroup", ref filterGroup) && filterGroup < 0)
                {
                    filterGroup = 0;
                }

                ImGui.SameLine();
                if(ImGui.Button($"{L.T("settings.common.add_lower", "add")}##MonsterPoiWidget"))
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
            var isOpened = ImGui.CollapsingHeader(L.Title("settings.ignore.title", "Ignore Monsters", "IgnoreMonsters"));
            ImGuiHelper.ToolTip(
                L.T(
                    "settings.ignore.tooltip",
                    "In order to figure out the path, please open DV -> States -> InGameState -> CurrentAreaInstance -> Awake Entities -> Click Path -> see NPC path in the game world"));
            if (isOpened)
            {
                ImGui.TextWrapped(L.T("settings.common.restart_or_zone", "Please restart gamehelper or change area/zone if you make any changes over here."));
                ImGui.InputText(L.Label("settings.ignore.monster_metadata_path", "Monster metadata path", "ToRemove"), ref monterPathToIgnore, 200);
                ImGuiHelper.ToolTip(
                    L.T(
                        "settings.common.path_match.tooltip",
                        "Path is going to be checked from left to right (i.e. String.StartsWith), up till the filter length."));
                ImGui.SameLine();
                if (ImGui.Button($"{L.T("settings.common.add", "Add")}##monsterPathToRemove") && !string.IsNullOrEmpty(monterPathToIgnore))
                {
                    Core.GHSettings.MonstersPathsToIgnore.Add(monterPathToIgnore);
                    monterPathToIgnore = string.Empty;
                }

                for (var i = Core.GHSettings.MonstersPathsToIgnore.Count - 1; i >= 0; i--)
                {
                    ImGui.Text(L.F("settings.common.path", "Path: {0}", Core.GHSettings.MonstersPathsToIgnore[i]));
                    ImGui.SameLine();
                    if (ImGui.Button($"{L.T("settings.common.delete", "Delete")}##{i}monsterPathToRemove"))
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
            var isOpened = ImGui.CollapsingHeader(L.Title("settings.npc.title", "Special NPC Metadata Paths", "SpecialNpcMetadataPaths"));
            ImGuiHelper.ToolTip(
                L.T(
                    "settings.npc.tooltip",
                    "In order to figure out the path, please open DV -> States -> InGameState -> CurrentAreaInstance -> Awake Entities -> Click Path -> see NPC path in the game world"));
            if (isOpened)
            {
                ImGui.TextWrapped(L.T("settings.common.restart_or_zone", "Please restart gamehelper or change area/zone if you make any changes over here."));
                ImGui.InputText(L.Label("settings.npc.path", "NPC Path", "specialNPCPath"), ref specialNpcPath, 200);
                ImGuiHelper.ToolTip(
                    L.T(
                        "settings.common.path_match.tooltip",
                        "Path is going to be checked from left to right (i.e. String.StartsWith), up till the filter length."));
                ImGui.SameLine();
                if (ImGui.Button($"{L.T("settings.common.add", "Add")}##specialNPCPath") && !string.IsNullOrEmpty(specialNpcPath))
                {
                    Core.GHSettings.SpecialNPCPaths.Add(specialNpcPath);
                    specialNpcPath = string.Empty;
                }

                for (var i = Core.GHSettings.SpecialNPCPaths.Count - 1; i >= 0; i--)
                {
                    ImGui.Text(L.F("settings.common.path", "Path: {0}", Core.GHSettings.SpecialNPCPaths[i]));
                    ImGui.SameLine();
                    if(ImGui.Button($"{L.T("settings.common.delete", "Delete")}##{i}specialNPCPath"))
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
            var isOpened = ImGui.CollapsingHeader(L.Title("settings.object.title", "Special Objects Metadata Paths", "SpecialObjectsMetadataPaths"));
            ImGuiHelper.ToolTip(
                L.T(
                    "settings.object.tooltip",
                    "In order to figure out the path, please open DV -> States -> InGameState -> CurrentAreaInstance -> Awake Entities -> Click Path -> see objects path in the game world"));
            if (isOpened)
            {
                ImGui.TextWrapped(L.T("settings.common.restart_or_zone", "Please restart gamehelper or change area/zone if you make any changes over here."));
                ImGui.InputText(L.Label("settings.object.path", "Object Path", "MiscObjWidgetPath"), ref specialMiscObjPath, 200);
                ImGuiHelper.ToolTip(
                    L.T(
                        "settings.common.path_match.tooltip",
                        "Path is going to be checked from left to right (i.e. String.StartsWith), up till the filter length."));
                ImGui.SameLine();
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
                if (ImGui.InputInt($"{L.T("settings.common.group_number", "Group Number")}##MiscObjgroup", ref filterGroup) && filterGroup < 0)
                {
                    filterGroup = 0;
                }

                ImGui.SameLine();
                if (ImGui.Button($"{L.T("settings.common.add_lower", "add")}##MiscObjadd"))
                {
                    Core.GHSettings.SpecialMiscObjPaths.Add(new(specialMiscObjPath, filterGroup));
                    specialMiscObjPath = string.Empty;
                    filterGroup = 0;
                }

                for (var i = Core.GHSettings.SpecialMiscObjPaths.Count - 1; i >= 0; i--)
                {
                    ImGui.Text(L.F(
                        "settings.common.path_group",
                        "Path: {0}, GroupId: {1}",
                        Core.GHSettings.SpecialMiscObjPaths[i].path,
                        Core.GHSettings.SpecialMiscObjPaths[i].group));
                    ImGui.SameLine();
                    if (ImGui.Button($"{L.T("settings.common.delete", "Delete")}##MiscObjDel{i}"))
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
            if (ImGui.CollapsingHeader(L.Title("settings.input.title", "Input Config", "InputConfig")))
            {
                ImGui.DragInt(L.Label("settings.input.key_timeout", "Key Timeout", "KeyTimeout"), ref Core.GHSettings.KeyPressTimeout, 0.2f, 60, 300);
                ImGuiHelper.ToolTip(
                    L.T(
                        "settings.input.key_timeout.tooltip",
                        "When GameOverlay press a key in the game, the key has to go to the GGG server for it to work. This process takes time equal to your latency x 3. During this time GameOverlay might press that key again. Set the key timeout value to latency x 3 so this doesn't happen. e.g. for 30ms latency, set it to 90ms. Also, do not go below 60 (due to server ticks), no matter how good your latency is."));
                ImGuiHelper.NonContinuousEnumComboBox(L.Label("settings.input.settings_window_key", "Settings Window Key", "SettingsWindowKey"), ref Core.GHSettings.MainMenuHotKey);
                ImGuiHelper.NonContinuousEnumComboBox(L.Label("settings.input.disable_rendering_key", "Disable Rendering Key", "DisableRenderingKey"), ref Core.GHSettings.DisableAllRenderingKey);
                ImGuiHelper.NonContinuousEnumComboBox(L.Label("settings.input.element_finder_key", "Element Finder Key", "ElementFinderKey"), ref Core.GHSettings.ElementFinderHotKey);
            }
        }

        /// <summary>
        ///     Draws the imgui widget for enabling/disabling tools.
        /// </summary>
        private static void DrawToolsConfig()
        {
            if (ImGui.CollapsingHeader(L.Title("settings.tools.title", "Misc Tools", "MiscTools")))
            {
                ImGui.Checkbox(L.Label("settings.tools.performance_stats", "Performance Stats", "PerformanceStats"), ref Core.GHSettings.ShowPerfStats);
                if (Core.GHSettings.ShowPerfStats)
                {
                    ImGui.Spacing();
                    ImGui.SameLine();
                    ImGui.Spacing();
                    ImGui.SameLine();
                    ImGui.Checkbox(L.Label("settings.tools.hide_when_background", "Hide when game is in background", "HidePerfStatsWhenBg"), ref Core.GHSettings.HidePerfStatsWhenBg);
                    ImGui.Spacing();
                    ImGui.SameLine();
                    ImGui.Spacing();
                    ImGui.SameLine();
                    ImGui.Checkbox(L.Label("settings.tools.show_minimum_stats", "Show minimum stats", "MinimumPerfStats"), ref Core.GHSettings.MinimumPerfStats);
                }

                ImGui.Checkbox(L.Label("settings.tools.game_ui_explorer", "Game UiExplorer (GE)", "GameUiExplorer"), ref Core.GHSettings.ShowGameUiExplorer);
                ImGui.Checkbox(L.Label("settings.tools.element_finder", "Element Finder", "ElementFinder"), ref Core.GHSettings.ShowElementFinder);
                ImGui.Checkbox(L.Label("settings.tools.data_visualization", "Data Visualization (DV)", "DataVisualization"), ref Core.GHSettings.ShowDataVisualization);
                ImGui.Checkbox(L.Label("settings.tools.performance_profiler", "Performance Profiler", "PerformanceProfiler"), ref Core.GHSettings.ShowPerfProfiler);
                ImGui.Checkbox(L.Label("settings.tools.memory_read_diagnostics", "Memory Read Diagnostics", "MemoryReadDiagnostics"), ref Core.GHSettings.ShowMemoryDiagnostics);
                ImGui.Checkbox(L.Label("settings.tools.offset_helper", "OffsetHelper (OH)", "OffsetHelper"), ref Core.GHSettings.ShowOffsetHelper);
#if DEBUG
                ImGui.Checkbox(L.Label("settings.tools.krangled_passive_detector", "Krangled Passive Detector", "KrangledPassiveDetector"), ref Core.GHSettings.ShowKrangledPassiveDetector);
#endif
            }
        }

        /// <summary>
        ///     Draws the imgui widget for showing misc config
        /// </summary>
        private static void DrawMiscConfig()
        {
            if (ImGui.CollapsingHeader(L.Title("settings.misc.title", "Miscellaneous Config", "MiscellaneousConfig")))
            {
                if (ImGui.Checkbox(L.Label("settings.misc.fix_taskbar", "Fix Taskbar not showing", "FixTaskbarNotShowing"), ref Core.GHSettings.FixTaskbarNotShowing))
                {
                    if (Core.States.GameCurrentState != GameStateTypes.GameNotLoaded)
                    {
                        CoroutineHandler.RaiseEvent(GameHelperEvents.OnMoved);
                    }
                }

                ImGui.Checkbox(L.Label("settings.misc.disable_entity_processing", "Disable entity processing when in town or hideout", "DisableEntityProcessingInTownOrHideout"),
                    ref Core.GHSettings.DisableEntityProcessingInTownOrHideout);
                ImGui.Checkbox(L.Label("settings.misc.hide_overlay_on_start", "Hide overlay settings upon start", "HideSettingWindowOnStart"), ref Core.GHSettings.HideSettingWindowOnStart);
                ImGui.Checkbox(L.Label("settings.misc.close_when_game_exit", "Close GameHelper when Game Exit", "CloseWhenGameExit"), ref Core.GHSettings.CloseWhenGameExit);
                if (ImGui.Checkbox(L.Label("settings.misc.vsync", "V-Sync", "VSync"), ref Core.Overlay.VSync))
                {
                    Core.GHSettings.Vsync = Core.Overlay.VSync;
                }

                ImGui.BeginDisabled(Core.Overlay.VSync);
                if (ImGui.InputInt(L.Label("settings.misc.fps_limiter", "FPS Limiter (0 to disable)", "FPSLimiter"), ref Core.GHSettings.FPSLimit))
                {
                    Core.Overlay.FPSLimit = Core.GHSettings.FPSLimit;
                }

                ImGui.EndDisabled();

                ImGuiHelper.ToolTip(
                    L.T(
                        "settings.misc.fps_limiter.tooltip",
                        "WARNING: There is no rate limiter in GameHelper, once V-Sync is off,\nit's your responsibility to use external rate limiter e.g. NVIDIA Control Panel\n-> Manage 3D Settings -> Set Max Framerate to what your monitor support."));
                ImGui.Checkbox(L.Label("settings.misc.process_all_renderable", "Process all renderable entities", "ProcessAllRenderableEntities"), ref Core.GHSettings.ProcessAllRenderableEntities);
                ImGuiHelper.ToolTip(
                    L.T(
                        "settings.misc.process_all_renderable.tooltip",
                        "WARNING: This will greatly reduce GH speed as well as increase crashes/glitches. Always keep it unchecked."));
                ImGui.Checkbox(L.Label("settings.misc.disable_debug_counters", "Disable debug counters (do it on 6 man party + juiced maps only)", "DisableAllCounters"), ref Core.GHSettings.DisableAllCounters);
                ImGui.Text(L.T("settings.misc.entity_max_degree", "Entity MaxDegreeOfParallelism"));
                ImGuiHelper.ToolTip(
                    L.T(
                        "settings.misc.entity_max_degree.tooltip",
                        "This limits the entity reading algorithm to a set number of CPUs. Select -1 to disable this limit. Use Task Manager CPU usage stat + Misc Tools -> performance stats to figure out best FPS to CPU usage ratio."));
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

                ImGui.Checkbox(L.Label("settings.misc.is_taiwan_client", "Is Taiwan client", "IsTaiwanClient"), ref Core.GHSettings.IsTaiwanClient);

                ImGui.Separator();
                ImGui.Text(L.T("settings.misc.entity_staleness", "Entity Staleness Fixes"));
                ImGuiHelper.ToolTip(
                    L.T(
                        "settings.misc.entity_staleness.tooltip",
                        "These options help detect and fix stale entity data (e.g. NPCs that teleport but keep old position in memory)."));

                ImGui.Checkbox(L.Label("settings.misc.enable_npc_cleanup", "Enable NPC entity cleanup", "EnableNpcEntityCleanup"), ref Core.GHSettings.EnableNpcEntityCleanup);
                ImGuiHelper.ToolTip(
                    L.T(
                        "settings.misc.enable_npc_cleanup.tooltip",
                        "Include NPC entities in the removal logic when they go invalid.\nPrevents stale NPC entities from lingering in the entity dictionary."));

                ImGui.Checkbox(L.Label("settings.misc.enable_stale_cleanup", "Enable stale entity cleanup", "EnableStaleEntityCleanup"), ref Core.GHSettings.EnableStaleEntityCleanup);
                ImGuiHelper.ToolTip(
                    L.T(
                        "settings.misc.enable_stale_cleanup.tooltip",
                        "Remove any entity that stays invalid for many consecutive frames,\nregardless of entity type. Catches NPCs and other entities that\nthe default cleanup misses."));

                if (Core.GHSettings.EnableStaleEntityCleanup)
                {
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(80);
                    ImGui.InputInt(L.Label("settings.misc.threshold_frames", "threshold (frames)", "StaleEntityFrameThreshold"), ref Core.GHSettings.StaleEntityFrameThreshold);
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
            if (ImGui.CollapsingHeader(L.Title("settings.reload.title", "Reload Plugin", "ReloadPlugin")))
            {
                ImGuiHelper.IEnumerableComboBox<string>(L.Label("settings.reload.plugins", "Plugins", "ReloadPlugins"), PManager.PluginNames, ref pluginForHotReload);
                ImGui.BeginDisabled(!pluginLoaded || string.IsNullOrEmpty(pluginForHotReload));
                if (ImGui.Button(L.Label("settings.reload.unload", "Unload Plugin", "UnloadPlugin")))
                {
                    if (PManager.UnloadPlugin(pluginForHotReload))
                    {
                        pluginLoaded = false;
                    }
                }

                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.BeginDisabled(pluginLoaded || string.IsNullOrEmpty(pluginForHotReload));
                if (ImGui.Button(L.Label("settings.reload.load", "Load Plugin", "LoadPlugin")))
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
                ImGui.Text(L.T("settings.confirm.quit", "Do you want to quit the GameHelper overlay?"));
                ImGui.Separator();
                if (ImGui.Button(L.Label("settings.confirm.yes", "Yes", "ConfirmQuitYes"), new Vector2(ImGui.GetContentRegionAvail().X / 2f, ImGui.GetTextLineHeight() * 2)))
                {
                    Core.GHSettings.IsOverlayRunning = false;
                    ImGui.CloseCurrentPopup();
                    isOverlayRunningLocal = true;
                }

                ImGui.SameLine();
                if (ImGui.Button(L.Label("settings.confirm.no", "No", "ConfirmQuitNo"), new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeight() * 2)))
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
                    $"{L.F("settings.window.title", "Game Overlay Settings [ {0} ]", Core.GetVersion())}###GameOverlaySettings",
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
