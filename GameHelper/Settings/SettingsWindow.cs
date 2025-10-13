// <copyright file="SettingsWindow.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Settings
{
    using ClickableTransparentOverlay;
    using ClickableTransparentOverlay.Win32;
    using Coroutine;
    using CoroutineEvents;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteEnums.Entity;
    using GameHelper.RemoteObjects.Components;
    using GameOffsets.Objects.States.InGameState;
    using ImGuiNET;
    using Plugin;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using Utils;
    using Vortice.Direct3D11;
    using static System.Net.WebRequestMethods;

    /// <summary>
    ///     Creates the MainMenu on the UI.
    /// </summary>
    internal static class SettingsWindow
    {
        private static bool _themeInitialized = false;

        private static Vector4 scammedColor = new(1f, 1f, 0f, 1f);
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

        private static bool _fontSelectorOpen;
        private static string _fontBrowserDir = string.Empty;
        private static string _fontSearch = string.Empty;
        private static string _fontSelectedPath = string.Empty;

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

        private static void DrawMenuBar()
        {
            if (ImGui.BeginMenuBar())
            {
                if (Core.GHSettings.ShowTogglePlugins && ImGui.BeginMenu("Toggle Plugins"))
                {
                    foreach (var container in PManager.Plugins)
                    {
                        var isEnabled = container.Metadata.Enable;
                        if (ImGui.Checkbox($"{container.Name}", ref isEnabled))
                        {
                            container.Metadata.Enable = !container.Metadata.Enable;
                            if (container.Metadata.Enable)
                            {
                                container.Plugin.OnEnable(Core.Process.Address != IntPtr.Zero);
                            }
                            else
                            {
                                container.Plugin.SaveSettings();
                                container.Plugin.OnDisable();
                            }
                        }
                    }

                    ImGui.EndMenu();
                }

#if DEBUG
                ImGui.Checkbox("ImGui Demo Window", ref showImGuiDemo);
                if (showImGuiDemo)
                {
                    ImGui.ShowDemoWindow(ref showImGuiDemo);
                }
#endif

                ImGui.EndMenuBar();
            }
        }

        private static void DrawTabs()
        {
            if (ImGui.BeginTabBar("pluginsTabBar", ImGuiTabBarFlags.AutoSelectNewTabs | ImGuiTabBarFlags.Reorderable))
            {
                if (ImGui.BeginTabItem("Core"))
                {
                    if (ImGui.BeginChild("CoreChildSetting"))
                    {
                        DrawCoreSettings();
                    }

                    ImGui.EndChild();
                    ImGui.EndTabItem();
                }

                foreach (var container in PManager.Plugins)
                {
                    if (container.Metadata.Enable && ImGui.BeginTabItem(container.Name))
                    {
                        if (ImGui.BeginChild("PluginChildSetting"))
                        {
                            container.Plugin.DrawSettings();
                        }

                        ImGui.EndChild();
                        ImGui.EndTabItem();
                    }
                }

                ImGui.EndTabBar();
            }
        }

        /// <summary>
        ///     Draws the currently selected settings on ImGui.
        /// </summary>
        private static void DrawCoreSettings()
        {
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
            ImGui.TextColored(scammedColor, "This is free software, if you purchased a copy you have been scammed.");
            ImGui.TextColored(scammedColor, "Download from https://gitlab.com/arsenic2k/gamehelper2");
            ImGui.NewLine();

            if (Core.Process?.TargetProcessUserHasReadAccess == true)
            {
                ImGui.TextColored(new Vector4(1f, 0f, 0f, 1f),
                    "Warning: the target process has read access to the GameHelper2 folder.");
                ImGui.SameLine();
                ImGui.TextLinkOpenURL("(Steps to Fix)",
                    @"https://copilot.microsoft.com/?q=How can I run path of exile 2 in limited user mode? 
                                                                After creating a user, make sure to tell me how to 
                                                                deny all permissions to the Gamehelper folder to 
                                                                this new user. Make sure I deny the permissions 
                                                                before you tell me to run Path of Exile 2. The game 
                                                                is running on steam. Refer me how to run it by using 
                                                                steam in limited user (open steam with shift+right click, 
                                                                different user), and also by shortcut.");
            }

            ImGui.TextColored(Vector4.One, "Developer of this software is not responsible for " +
                              "any loss that may happen due to the usage of this software. Use this " +
                              "software at your own risk.");
            ImGui.NewLine();
            ImGui.TextColored(Vector4.One, "All Settings (including plugins) are saved automatically " +
                  $"when you close the overlay or hide it via {Core.GHSettings.MainMenuHotKey} button.");

            ImGui.PopTextWrapPos();
            DrawInputConfigWidget();
            DrawGeneralConfig();
            DrawToolsConfig();
            DrawChangeFontWidget();
#if DEBUG
            DrawReloadPluginWidget();
#endif
            if (ImGui.CollapsingHeader("Legacy Settings"))
            {
                ImGui.Spacing();
                ImGui.SameLine();
                ImGui.InputText("Party Leader Name", ref Core.GHSettings.LeaderName, 200);
                ImGui.Spacing();
                ImGui.SameLine();
                DrawPoiWidget();
                ImGui.Spacing();
                ImGui.SameLine();
                DrawMonstersToIgnore();
                ImGui.Spacing();
                ImGui.SameLine();
                DrawNPCWidget();
                ImGui.Spacing();
                ImGui.SameLine();
                DrawMiscObjWidget();
                ImGui.Spacing();
                ImGui.SameLine();
                DrawNearbyWidget();
            }
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
        private static void DrawChangeFontWidget()
        {
            if (ImGui.CollapsingHeader("Font and Language"))
            {
                ImGui.InputText("Font", ref Core.GHSettings.FontPathName, 300, ImGuiInputTextFlags.ReadOnly);
                ImGui.SameLine();
                if (ImGui.Button("Browse..."))
                {
                    _fontSelectorOpen = true;
                    _fontSelectedPath = string.Empty;
                    _fontSearch = string.Empty;

                    try
                    {
                        var start = Core.GHSettings.FontPathName;
                        var dir = (!string.IsNullOrEmpty(start) && Directory.Exists(Path.GetDirectoryName(start)))
                                  ? Path.GetDirectoryName(start)
                                  : Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
                        _fontBrowserDir = string.IsNullOrEmpty(dir) ? Directory.GetCurrentDirectory() : dir;
                    }
                    catch
                    {
                        _fontBrowserDir = Directory.GetCurrentDirectory();
                    }

                    ImGui.OpenPopup("Font Selector");
                }
                ImGui.SameLine();
                if (ImGui.Button("Reset"))
                {
                    Core.GHSettings.FontPathName = Core.GHSettings.DefaultFontPathName;
                    ApplyFont();
                }

                DrawFontSelectorPopup();

                ImGui.DragInt("Font Size", ref Core.GHSettings.FontSize, 0.1f, 13, 40);

                var languageChanged = ImGuiHelper.EnumComboBox("Glyph Language", ref Core.GHSettings.FontLanguage);
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

                ImGui.Checkbox("Is Taiwan client", ref Core.GHSettings.IsTaiwanClient);

                if (ImGui.Button("Apply Changes"))
                {
                    ApplyFont();
                }
            }
        }

        private static void ApplyFont()
        {
            if (MiscHelper.TryConvertStringToImGuiGlyphRanges(Core.GHSettings.FontCustomGlyphRange, out var glyphranges))
            {
                Core.Overlay.ReplaceFont(
                    Core.GHSettings.FontPathName,
                    Core.GHSettings.FontSize,
                    glyphranges);
            }
            else
            {
                Core.Overlay.ReplaceFont(
                    Core.GHSettings.FontPathName,
                    Core.GHSettings.FontSize,
                    Core.GHSettings.FontLanguage);
            }
        }

        private static void DrawFontSelectorPopup()
        {
            if (!_fontSelectorOpen) return;

            var open = true;
            if (ImGui.BeginPopupModal("Font Selector", ref open, ImGuiWindowFlags.AlwaysAutoResize))
            {
                if (!Directory.Exists(_fontBrowserDir))
                {
                    try { _fontBrowserDir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts); }
                    catch { _fontBrowserDir = Directory.GetCurrentDirectory(); }
                }

                ImGui.InputText("Folder", ref _fontBrowserDir, 512);
                ImGui.SameLine();
                if (ImGui.Button("Up"))
                {
                    try
                    {
                        var parent = Directory.GetParent(_fontBrowserDir)?.FullName;
                        if (!string.IsNullOrEmpty(parent)) _fontBrowserDir = parent;
                    }
                    catch { }
                }

                ImGui.InputText("Search", ref _fontSearch, 128);
                ImGui.Separator();

                var avail = ImGui.GetContentRegionAvail();
                var leftWidth = 10f;
                var rightWidth = Math.Max(240f, avail.X - leftWidth - ImGui.GetStyle().ItemSpacing.X);

                ImGui.BeginChild("dirs", new Vector2(leftWidth, 240f));
                IEnumerable<string> dirs = Enumerable.Empty<string>();
                try { dirs = Directory.EnumerateDirectories(_fontBrowserDir).OrderBy(Path.GetFileName); } catch { }
                foreach (var d in dirs)
                {
                    var name = Path.GetFileName(d);
                    if (ImGui.Selectable("[DIR] " + name))
                    {
                        _fontBrowserDir = d;
                    }
                }
                ImGui.EndChild();

                ImGui.SameLine();

                ImGui.BeginChild("files", new Vector2(rightWidth, 240));
                IEnumerable<string> files = Enumerable.Empty<string>();
                try
                {
                    files = Directory.EnumerateFiles(_fontBrowserDir, "*.*")
                        .Where(f =>
                        {
                            var ext = Path.GetExtension(f).ToLowerInvariant();
                            return ext == ".ttf" || ext == ".otf" || ext == ".ttc";
                        })
                        .Where(f => string.IsNullOrWhiteSpace(_fontSearch) ||
                                    Path.GetFileName(f).Contains(_fontSearch, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(Path.GetFileName);
                }
                catch { }

                foreach (var f in files)
                {
                    var fn = Path.GetFileName(f);
                    var isSelected = string.Equals(_fontSelectedPath, f, StringComparison.OrdinalIgnoreCase);
                    if (ImGui.Selectable(fn, isSelected))
                    {
                        _fontSelectedPath = f;
                    }

                    if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    {
                        _fontSelectedPath = f;
                        Core.GHSettings.FontPathName = _fontSelectedPath;
                        ImGui.CloseCurrentPopup();
                        _fontSelectorOpen = false;
                    }
                }
                ImGui.EndChild();

                ImGui.Separator();
                ImGui.BeginDisabled(string.IsNullOrEmpty(_fontSelectedPath));
                if (ImGui.Button("Select"))
                {
                    Core.GHSettings.FontPathName = _fontSelectedPath;
                    ImGui.CloseCurrentPopup();
                    _fontSelectorOpen = false;
                    ApplyFont();
                }
                ImGui.EndDisabled();
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    ImGui.CloseCurrentPopup();
                    _fontSelectorOpen = false;
                }

                ImGui.EndPopup();
            }
            else if (!open)
            {
                _fontSelectorOpen = false;
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
                for (var i = 0; i < Core.GHSettings.PoiMonstersCategories2.Count; i++)
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
                if (ImGui.Button("add##MonsterPoiWidget"))
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

                for (var i = 0; i < Core.GHSettings.MonstersPathsToIgnore.Count; i++)
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

                for (var i = 0; i < Core.GHSettings.SpecialNPCPaths.Count; i++)
                {
                    ImGui.Text($"Path: {Core.GHSettings.SpecialNPCPaths[i]}");
                    ImGui.SameLine();
                    if (ImGui.Button($"Delete##{i}specialNPCPath"))
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

                for (var i = 0; i < Core.GHSettings.SpecialMiscObjPaths.Count; i++)
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
                ImGui.DragInt("Key Timeout (ms)", ref Core.GHSettings.KeyPressTimeout, 0.2f, 60, 300);
                ImGuiHelper.ToolTip("When GameOverlay press a key in the game, the key " +
                    "has to go to the GGG server for it to work. This process takes " +
                    "time equal to your latency x 3. During this time GameOverlay might " +
                    "press that key again. Set the key timeout value to latency x 3 so " +
                    "this doesn't happen. e.g. for 30ms latency, set it to 90ms. Also, " +
                    "do not go below 60 (due to server ticks), no matter how good your latency is.");
                ImGuiHelper.NonContinuousEnumComboBox("Settings Window Key", ref Core.GHSettings.MainMenuHotKey);
                ImGuiHelper.NonContinuousEnumComboBox("Disable Rendering Key", ref Core.GHSettings.DisableAllRenderingKey);
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
                    ImGui.SameLine();
                    ImGui.Checkbox("Show minimum stats", ref Core.GHSettings.MinimumPerfStats);
                }

                ImGui.Checkbox("Performance Profiler", ref Core.GHSettings.ShowPerfProfiler);
                ImGui.Checkbox("Data Visualization (DV)", ref Core.GHSettings.ShowDataVisualization);
                ImGui.SameLine();
                ImGui.Checkbox("Game UiExplorer (GE)", ref Core.GHSettings.ShowGameUiExplorer);
#if DEBUG
                ImGui.Checkbox("Krangled Passive Detector", ref Core.GHSettings.ShowKrangledPassiveDetector);
#endif
            }
        }

        /// <summary>
        ///     Draws the imgui widget for showing general config
        /// </summary>
        private static void DrawGeneralConfig()
        {
            if (ImGui.CollapsingHeader("General Config"))
            {
                var theme = Core.GHSettings.ThemeColor;
                if (ImGui.ColorEdit4("Theme Color", ref theme, ImGuiColorEditFlags.DisplayRGB | ImGuiColorEditFlags.AlphaBar))
                {
                    Core.GHSettings.ThemeColor = theme;
                    ApplyTheme(theme);
                }
                ImGui.SameLine();
                if (ImGui.Button("Reset"))
                {
                    Core.GHSettings.ThemeColor = State.DefaultThemeColor;
                    ApplyTheme(Core.GHSettings.ThemeColor);
                }
                ImGui.Separator();
#if DEBUG
                if (ImGui.Checkbox("Fix Taskbar not showing", ref Core.GHSettings.FixTaskbarNotShowing))
                {
                    if (Core.States.GameCurrentState != GameStateTypes.GameNotLoaded)
                    {
                        CoroutineHandler.RaiseEvent(GameHelperEvents.OnMoved);
                    }
                }

                ImGui.Checkbox("Disable entity processing when in town or hideout",
                    ref Core.GHSettings.DisableEntityProcessingInTownOrHideout);
#endif
                ImGui.Checkbox("Hide this window on start", ref Core.GHSettings.HideSettingWindowOnStart);
                ImGui.Checkbox("Close GameHelper when Game Exit", ref Core.GHSettings.CloseWhenGameExit);
                if (ImGui.Checkbox("V-Sync", ref Core.Overlay.VSync))
                {
                    Core.GHSettings.Vsync = Core.Overlay.VSync;
                }
                ImGui.SameLine();
                ImGui.BeginDisabled(Core.Overlay.VSync);
                if (ImGui.InputInt("FPS Limiter (0 to disable)", ref Core.GHSettings.FPSLimit))
                {
                    Core.Overlay.FPSLimit = Core.GHSettings.FPSLimit;
                }

                ImGui.EndDisabled();

                ImGuiHelper.ToolTip("WARNING: There is no rate limiter in GameHelper, once V-Sync is off,\n" +
                    "it's your responsibility to use external rate limiter e.g. NVIDIA Control Panel\n" +
                    "-> Manage 3D Settings -> Set Max Framerate to what your monitor support.");
#if DEBUG
                ImGui.Checkbox("Process all renderable entities", ref Core.GHSettings.ProcessAllRenderableEntities);
                ImGuiHelper.ToolTip("WARNING: This is a debug only feature, it should not be used when actually playing the game." +
                    "It will greatly reduce the GH speed as well as increase crashes/gliches. Always keep it unchecked.");
                ImGui.Checkbox("Disable debug counters (do it on 6 man party + juiced maps only)", ref Core.GHSettings.DisableAllCounters);
#endif
                ImGui.Text("Multi-threading");
                ImGuiHelper.ToolTip("This limits the entity reading algorithm to a set number of CPUs." +
                    " Select -1 to disable this limit. Use Task Manager CPU usage stat + Misc Tools -> performance stats" +
                    " to figure out best FPS to CPU usage ratio.");
                ImGui.SameLine();
                if (ImGui.RadioButton("-1", Core.GHSettings.EntityReaderMaxDegreeOfParallelism == -1))
                {
                    Core.GHSettings.EntityReaderMaxDegreeOfParallelism = -1;
                }
                ImGui.SameLine();

                // detect logical processors (threads) and find the largest power of two <= that count
                var logicalThreads = Math.Max(1, Environment.ProcessorCount);
                int maxPow2 = 1;
                while ((maxPow2 << 1) <= logicalThreads)
                {
                    maxPow2 <<= 1;
                }

                // if the saved value exceeds the cap (and isn't -1), clamp it to the cap
                if (Core.GHSettings.EntityReaderMaxDegreeOfParallelism > maxPow2 &&
                    Core.GHSettings.EntityReaderMaxDegreeOfParallelism != -1)
                {
                    Core.GHSettings.EntityReaderMaxDegreeOfParallelism = maxPow2;
                }

                // render options: 2, 4, 8, ... up to maxPow2 (only if >= 2)
                for (int i = 2; i <= maxPow2; i <<= 1)
                {
                    if (ImGui.RadioButton(i.ToString(), Core.GHSettings.EntityReaderMaxDegreeOfParallelism == i))
                    {
                        Core.GHSettings.EntityReaderMaxDegreeOfParallelism = i;
                    }

                    if ((i << 1) <= maxPow2)
                    {
                        ImGui.SameLine();
                    }
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

        private static void ApplyTheme(Vector4 baseColor)
        {
            var style = ImGui.GetStyle();
            var colors = style.Colors;

            Vector4 Shade(float f) => new(
                Math.Clamp(baseColor.X * f, 0f, 1f),
                Math.Clamp(baseColor.Y * f, 0f, 1f),
                Math.Clamp(baseColor.Z * f, 0f, 1f),
                baseColor.W);

            colors[(int)ImGuiCol.CheckMark] = Shade(1.00f);
            colors[(int)ImGuiCol.SliderGrab] = Shade(0.90f);
            colors[(int)ImGuiCol.SliderGrabActive] = Shade(1.10f);

            colors[(int)ImGuiCol.Button] = Shade(0.80f);
            colors[(int)ImGuiCol.ButtonHovered] = Shade(0.95f);
            colors[(int)ImGuiCol.ButtonActive] = Shade(1.10f);

            colors[(int)ImGuiCol.Header] = Shade(0.65f);
            colors[(int)ImGuiCol.HeaderHovered] = Shade(0.85f);
            colors[(int)ImGuiCol.HeaderActive] = Shade(0.80f);

            colors[(int)ImGuiCol.MenuBarBg] = Shade(0.55f);
            colors[(int)ImGuiCol.PopupBg] = Shade(0.40f) with { W = 0.98f };

            colors[(int)ImGuiCol.TitleBg] = Shade(0.45f);
            colors[(int)ImGuiCol.TitleBgActive] = Shade(0.70f);
            colors[(int)ImGuiCol.TitleBgCollapsed] = Shade(0.35f);

            colors[(int)ImGuiCol.FrameBg] = Shade(0.35f) with { W = 1.0f };
            colors[(int)ImGuiCol.FrameBgHovered] = Shade(0.45f) with { W = 1.0f };
            colors[(int)ImGuiCol.FrameBgActive] = Shade(0.55f) with { W = 1.0f };

            colors[(int)ImGuiCol.Tab] = Shade(0.70f);
            colors[(int)ImGuiCol.TabHovered] = Shade(0.95f);
            colors[(int)ImGuiCol.TabSelected] = Shade(1.05f);
            //colors[(int)ImGuiCol.TabUnfocused] = Shade(0.60f);
            //colors[(int)ImGuiCol.TabUnfocusedActive] = Shade(0.80f);

            colors[(int)ImGuiCol.SeparatorHovered] = Shade(0.90f);
            colors[(int)ImGuiCol.SeparatorActive] = Shade(1.10f);

            colors[(int)ImGuiCol.ResizeGrip] = Shade(0.85f);
            colors[(int)ImGuiCol.ResizeGripHovered] = Shade(1.00f);
            colors[(int)ImGuiCol.ResizeGripActive] = Shade(1.15f);
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

                if (!_themeInitialized)
                {
                    ApplyTheme(Core.GHSettings.ThemeColor);
                    _themeInitialized = true;
                }

                if (Utils.IsKeyPressedAndNotTimeout(Core.GHSettings.MainMenuHotKey))
                {
                    isSettingsWindowVisible = !isSettingsWindowVisible;
                    ImGui.GetIO().WantCaptureMouse = true;
                    if (!isSettingsWindowVisible)
                    {
                        CoroutineHandler.RaiseEvent(GameHelperEvents.TimeToSaveAllSettings);
                    }
                }

                if (!isSettingsWindowVisible)
                {
                    continue;
                }

                ImGui.SetNextWindowSizeConstraints(new Vector2(800, 600), Vector2.One * float.MaxValue);
                var isMainMenuExpanded = ImGui.Begin(
                    $"Game Overlay Settings [ {Core.GetVersion()} ] [ {Core.States.GameCurrentState} ]",
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

                DrawMenuBar();
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
