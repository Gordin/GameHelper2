// <copyright file="PreloadAlert.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace PreloadAlert
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using Coroutine;
    using GameHelper;
    using GameHelper.CoroutineEvents;
    using GameHelper.Plugin;
    using GameHelper.RemoteEnums;
    using GameHelper.Utils;
    using ImGuiNET;
    using Newtonsoft.Json;

    /// <summary>
    ///     Displays important preload on the screen.
    /// </summary>
    public sealed class PreloadAlert : PCore<PreloadSettings>
    {
        private const ImGuiColorEditFlags ColorEditFlags = ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel;

        private List<PreloadInfo> preloadFoundList = new();
        private readonly PreloadsContainer preloads = new();

        private PreloadInfo tmpAddPreloadValue = new();
        private string tmpAddPreloadKey = string.Empty;
        private string tmpModifyPreloadKey = string.Empty;

        private bool isPreloadAlertHovered;
        private ActiveCoroutine? onPreloadUpdated;

        private string preloadListFilter = string.Empty;

        private Stopwatch lastMapSpawnTimer = Stopwatch.StartNew();

        private string PreloadFileName => Path.Join(this.DllDirectory, "preloads.txt");
        private string SettingPathname => Path.Join(this.DllDirectory, "config", "settings.txt");

        /// <summary>
        ///     Clear all the important and found preloads and stops the co-routines.
        /// </summary>
        public override void OnDisable()
        {
            this.preloads.Clear();
            this.preloadFoundList = new();
            this.onPreloadUpdated?.Cancel();
            this.onPreloadUpdated = null;
        }

        /// <summary>
        ///     Reads the settings and preloads from the disk and
        ///     starts this plugin co-routine.
        /// </summary>
        /// <param name="isGameOpened">value indicating whether game is opened or not.</param>
        public override void OnEnable(bool isGameOpened)
        {
            if (File.Exists(this.SettingPathname))
            {
                var content = File.ReadAllText(this.SettingPathname);
                this.Settings = JsonConvert.DeserializeObject<PreloadSettings>(content) ?? new PreloadSettings();
            }

            this.preloads.Load(this.PreloadFileName);
            this.onPreloadUpdated = CoroutineHandler.Start(this.OnPreloadsUpdated());
        }

        /// <summary>
        ///     Save this plugin settings to disk.
        ///     NOTE: it will always lock the preload window before storing.
        /// </summary>
        public override void SaveSettings()
        {
            var lockStatus = this.Settings.Locked;
            this.Settings.Locked = true;
            Directory.CreateDirectory(Path.GetDirectoryName(this.SettingPathname) ?? string.Empty);
            var settingsData = JsonConvert.SerializeObject(this.Settings, Formatting.Indented);
            File.WriteAllText(this.SettingPathname, settingsData);
            this.Settings.Locked = lockStatus;
            this.preloads.Save(this.PreloadFileName);
        }

        /// <summary>
        ///     Draws the settings for this plugin on settings window.
        /// </summary>
        public override void DrawSettings()
        {
            ImGui.TextWrapped(this.PluginText.T("settings.description", "If you find something new and want to add it to the preload " +
                              "you can use Core -> Data Visualization -> CurrentAreaLoadedFiles feature."));
            ImGui.Separator();
            this.DisplaySettings();
            this.DisplayAllImportantPreloads();
        }

        /// <summary>
        ///     Draws the Ui for this plugin.
        /// </summary>
        public override void DrawUI()
        {
            if (this.Settings.EnableHideUi && this.Settings.Locked &&
                (!Core.Process.Foreground || Core.States.GameCurrentState != GameStateTypes.InGameState))
            {
                return;
            }

            if (this.Settings.HideWindowWhenEmpty && this.preloadFoundList.Count == 0)
            {
                return;
            }

            var areaDetails = Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails;
            if (this.Settings.HideWhenInTownOrHideout && (areaDetails.IsHideout || areaDetails.IsTown))
            {
                return;
            }
            if (Core.States.InGameStateObject.GameUi.SkillTreeNodesUiElements.Count > 0)
            {
                return;
            }

            var windowName = this.PluginText.Title("window.preload", "Preload Window", "PreloadAlertWindow");
            ImGui.PushStyleColor(ImGuiCol.WindowBg, this.isPreloadAlertHovered ? Vector4.Zero : this.Settings.BackgroundColor);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, this.isPreloadAlertHovered ? 0.0f : 0.5f);

            if (this.Settings.Locked)
            {
                ImGui.SetNextWindowPos(this.Settings.Pos);
                ImGui.SetNextWindowSize(this.Settings.Size);
                ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
                ImGui.Begin(windowName, ImGuiHelper.TransparentWindowFlags);
                ImGui.PopStyleVar();
                this.isPreloadAlertHovered = ImGui.IsMouseHoveringRect(this.Settings.Pos, this.Settings.Pos + this.Settings.Size);
            }
            else
            {
                ImGui.Begin(windowName, ImGuiWindowFlags.NoSavedSettings);
                ImGui.TextColored(new Vector4(.86f, .71f, .36f, 1), this.PluginText.T("window.edit_background_color", "Edit Background Color: "));
                ImGui.SameLine();
                ImGui.ColorEdit4(
                    this.PluginText.Label("window.background_color", "Background Color", "PreloadAlertBackground"),
                    ref this.Settings.BackgroundColor,
                    ColorEditFlags);
                ImGui.TextColored(new Vector4(1, 1, 1, 1), this.PluginText.F("window.dummy_preload", "Dummy Preload {0}", 1));
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0, 1, 1, 1), this.PluginText.F("window.dummy_preload", "Dummy Preload {0}", 2));
                ImGui.TextColored(new Vector4(1, 0, 1, 1), this.PluginText.F("window.dummy_preload", "Dummy Preload {0}", 3));
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(1, 1, 0, 1), this.PluginText.F("window.dummy_preload", "Dummy Preload {0}", 4));
                ImGui.TextColored(new Vector4(1, 0, 0, 1), this.PluginText.F("window.dummy_preload", "Dummy Preload {0}", 5));
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0, 1, 0, 1), this.PluginText.F("window.dummy_preload", "Dummy Preload {0}", 6));
                ImGui.TextColored(new Vector4(0, 0, 1, 1), this.PluginText.F("window.dummy_preload", "Dummy Preload {0}", 7));
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0, 0, 0, 1), this.PluginText.F("window.dummy_preload", "Dummy Preload {0}", 8));
                this.Settings.Pos = ImGui.GetWindowPos();
                this.Settings.Size = ImGui.GetWindowSize();
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    this.Settings.Locked = true;
                }
            }

            if (!this.isPreloadAlertHovered)
            {
                if (this.Settings.TimeSinceLastMapSpawn)
                {
                    ImGui.Text(this.PluginText.F("window.timer", "Timer: {0:00s}", this.lastMapSpawnTimer.Elapsed.TotalSeconds));
                    ImGui.Separator();
                }
                if (areaDetails.IsHideout)
                {
                    ImGui.Text(this.PluginText.T("window.not_updated_hideout", "Preloads are not updated in hideout."));
                }
                else if (areaDetails.IsTown)
                {
                    ImGui.Text(this.PluginText.T("window.not_updated_town", "Preloads are not updated in town."));
                }
                else if (this.Settings.Locked)
                {
                    for (var i = 0; i < this.preloadFoundList.Count; i++)
                    {
                        ImGui.TextColored(this.preloadFoundList[i].Color, this.preloadFoundList[i].DisplayName);
                    }
                }
            }

            ImGui.End();
            ImGui.PopStyleVar();
            ImGui.PopStyleColor();
        }

        private void DisplaySettings()
        {
            if (ImGui.CollapsingHeader(this.PluginText.Title("section.settings", "Settings", "PreloadAlertSettings")))
            {
                ImGui.Checkbox(this.PluginText.Label("settings.lock_window", "Lock/Unlock preload window", "PreloadAlertLockWindow"), ref this.Settings.Locked);
                ImGuiHelper.ToolTip(this.PluginText.T("settings.lock_window.tooltip", "You can also lock it by double clicking the preload window. " +
                                  "However, you can only unlock it from here."));
                ImGui.Checkbox(this.PluginText.Label("settings.hide_locked_not_ingame", "Hide when locked & not ingame", "PreloadAlertHideLockedNotIngame"), ref this.Settings.EnableHideUi);
                ImGui.Checkbox(this.PluginText.Label("settings.hide_when_empty", "Hide when no preload found", "PreloadAlertHideWhenEmpty"), ref this.Settings.HideWindowWhenEmpty);
                ImGui.Checkbox(this.PluginText.Label("settings.hide_in_town_hideout", "Hide when in town or hideout", "PreloadAlertHideInTownHideout"), ref this.Settings.HideWhenInTownOrHideout);
                ImGui.Checkbox(this.PluginText.Label("settings.show_time_since_last_map", "Show time since last map opened", "PreloadAlertShowTimeSinceLastMap"), ref this.Settings.TimeSinceLastMapSpawn);
            }
        }

        private void DisplayAllImportantPreloads()
        {
            if (ImGui.CollapsingHeader(this.PluginText.Title("section.all_important_preloads", "All Important Preloads", "PreloadAlertAllImportantPreloads")))
            {
                ImGui.InputText(this.PluginText.Label("preloads.filter", "Preload Alert List Filter", "PreloadAlertListFilter"), ref this.preloadListFilter, 200);
                if (this.preloads.Count() == 0)
                {
                    ImGui.Text(this.PluginText.T("preloads.none_found", "No important preload found. Did you forget to copy preload.txt " +
                        "file in the preload alert plugin folder?"));
                }
                else
                {
                    ImGui.BeginTable("AllImportantPreloadsTable", 7, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY);
                    ImGui.TableSetupColumn(this.PluginText.T("preloads.column.enable", "Enable"), ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHide);
                    ImGui.TableSetupColumn(this.PluginText.T("preloads.column.priority", "Priority"), ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHide);
                    ImGui.TableSetupColumn(this.PluginText.T("preloads.column.log", "Log"), ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHide);
                    ImGui.TableSetupColumn(this.PluginText.T("preloads.column.color", "Color"), ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHide);
                    ImGui.TableSetupColumn(this.PluginText.T("preloads.column.display_name", "Display Name"), ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoHide);
                    ImGui.TableSetupColumn(this.PluginText.T("preloads.column.path", "Path"), ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoHide);
                    ImGui.TableSetupColumn(this.PluginText.T("preloads.column.delete", "Delete"), ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHide | ImGuiTableColumnFlags.NoHeaderLabel);
                    ImGui.TableSetupScrollFreeze(7, 4);

                    ImGui.TableNextColumn();
                    ImGui.TableHeader(this.PluginText.T("preloads.header.enabled", "Enabled"));
                    ImGuiHelper.ToolTip(this.PluginText.T("preloads.header.enabled.tooltip", "Enables the preload."));

                    ImGui.TableNextColumn();
                    ImGui.TableHeader(this.PluginText.T("preloads.header.priority", "Priority"));
                    ImGuiHelper.ToolTip(this.PluginText.T("preloads.header.priority.tooltip", "Sets the priority of the preload. Lower numbers show up at the top of the preload window."), 20f);

                    ImGui.TableNextColumn();
                    ImGui.TableHeader(this.PluginText.T("preloads.header.log", "Log"));
                    ImGuiHelper.ToolTip(this.PluginText.T("preloads.header.log.tooltip", "Log to file when preload found in area/zone."));

                    ImGui.TableNextColumn();
                    ImGui.TableHeader(this.PluginText.T("preloads.header.color", "Color"));
                    ImGuiHelper.ToolTip(this.PluginText.T("preloads.header.color.tooltip", "Sets the color of the preload"));

                    ImGui.TableNextColumn();
                    ImGui.TableHeader(this.PluginText.T("preloads.header.display_name", "Display Name"));
                    ImGuiHelper.ToolTip(this.PluginText.T("preloads.header.display_name.tooltip", "Sets the name that appears in the preload window. Press Enter to save changes."), 20f);

                    ImGui.TableNextColumn();
                    ImGui.TableHeader(this.PluginText.T("preloads.header.path", "Path"));
                    ImGuiHelper.ToolTip(this.PluginText.T("preloads.header.path.tooltip", "Path of the preload you want to show in the preload window. Press Enter to save changes."), 20f);


                    ImGui.TableNextColumn();
                    ImGui.TableHeader("");

                    ImGui.TableNextRow();
                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();

                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5.2f);
                    ImGui.InputInt($"##Priority", ref this.tmpAddPreloadValue.Priority);
                    this.tmpAddPreloadValue.Priority = Math.Max(0, this.tmpAddPreloadValue.Priority);

                    ImGui.TableNextColumn();
                    ImGui.Checkbox($"##Log", ref this.tmpAddPreloadValue.LogToDisk);

                    ImGui.TableNextColumn();
                    ImGuiHelper.CenterElementInColumn(ImGui.GetFrameHeight());
                    ImGui.ColorEdit4($"##Color", ref this.tmpAddPreloadValue.Color, ImGuiColorEditFlags.NoInputs);

                    ImGui.TableNextColumn();
                    ImGui.PushStyleColor(ImGuiCol.Text, this.tmpAddPreloadValue.Color);
                    ImGui.SetNextItemWidth(-1);
                    ImGui.InputText($"##DisplayName", ref this.tmpAddPreloadValue.DisplayName, 80);
                    ImGui.PopStyleColor();

                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(-1);
                    ImGui.InputText($"##Path", ref this.tmpAddPreloadKey, 250);

                    ImGui.TableNextColumn();
                    if (ImGui.Button(this.PluginText.Label("button.add", "Add", "PreloadAlertAddPreload")))
                    {
                        if (!string.IsNullOrEmpty(this.tmpAddPreloadKey) || !string.IsNullOrEmpty(this.tmpAddPreloadValue.DisplayName))
                        {
                            this.preloads.AddOrUpdate(this.tmpAddPreloadKey, this.tmpAddPreloadValue, this.tmpAddPreloadValue.Priority);
                            this.tmpAddPreloadKey = string.Empty;
                            this.tmpAddPreloadValue = new();
                        }
                    }

                    ImGui.TableNextRow();
                    ImGui.TableNextRow();

                    var preloadCache = this.preloads.GetUpToDateCache();
                    for (var i = 0; i < preloadCache.Count; i++)
                    {
                        var key = preloadCache[i];
                        var preloadInfo = this.preloads.Get(key);

                        if (!string.IsNullOrEmpty(this.preloadListFilter) &&
                            !(key.Contains(this.preloadListFilter, StringComparison.OrdinalIgnoreCase) ||
                            preloadInfo.DisplayName.Contains(this.preloadListFilter, StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        ImGui.TableNextColumn();
                        ImGuiHelper.CenterElementInColumn(ImGui.GetFrameHeight());
                        if (ImGui.Checkbox($"##Enabled{key}", ref preloadInfo.Enabled))
                        {
                            this.preloads.AddOrUpdate(key, preloadInfo, i);
                        }

                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5.2f);
                        preloadInfo.Priority = i;
                        ImGui.InputInt($"##Priority{key}", ref preloadInfo.Priority);
                        if (ImGui.IsItemDeactivatedAfterEdit())
                        {
                            preloadInfo.Priority = Math.Max(0, preloadInfo.Priority);
                            this.preloads.AddOrUpdate(key, preloadInfo, preloadInfo.Priority);
                        }

                        ImGui.TableNextColumn();
                        ImGuiHelper.CenterElementInColumn(ImGui.GetFrameHeight());
                        if (ImGui.Checkbox($"##logToDisk{key}", ref preloadInfo.LogToDisk))
                        {
                            this.preloads.AddOrUpdate(key, preloadInfo, i);
                        }

                        ImGui.TableNextColumn();
                        ImGuiHelper.CenterElementInColumn(ImGui.GetFrameHeight());
                        if (ImGui.ColorEdit4($"##Color{key}", ref preloadInfo.Color, ImGuiColorEditFlags.NoInputs))
                        {
                            this.preloads.AddOrUpdate(key, preloadInfo, i);
                        }

                        ImGui.TableNextColumn();
                        ImGui.PushStyleColor(ImGuiCol.Text, preloadInfo.Color);
                        ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
                        ImGui.SetNextItemWidth(-1);
                        if (ImGui.InputText($"##DisplayName{key}", ref preloadInfo.DisplayName, 100, ImGuiInputTextFlags.EnterReturnsTrue))
                        {
                            if (!string.IsNullOrEmpty(preloadInfo.DisplayName))
                            {
                                this.preloads.AddOrUpdate(key, preloadInfo, i);
                            }
                        }
                        ImGui.PopStyleColor();
                        ImGui.PopStyleColor();

                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(-1);
                        ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
                        this.tmpModifyPreloadKey = key;
                        if (ImGui.InputText($"##Key{key}", ref this.tmpModifyPreloadKey, 100, ImGuiInputTextFlags.EnterReturnsTrue))
                        {
                            if (!string.IsNullOrEmpty(this.tmpModifyPreloadKey))
                            {
                                this.preloads.Remove(key);
                                this.preloads.Add(this.tmpModifyPreloadKey, preloadInfo, i);
                            }
                        }

                        ImGui.PopStyleColor();

                        ImGui.TableNextColumn();
                        if (ImGui.SmallButton($"{this.PluginText.T("button.delete", "Delete")}##{key}"))
                        {
                            this.preloads.Remove(key);
                        }
                    }

                    ImGui.EndTable();
                }
            }
        }

        private IEnumerator<Wait> OnPreloadsUpdated()
        {
            Dictionary<PreloadInfo, byte> preloadFound = new();
            var logFilePathname = Path.Join(this.DllDirectory, "preloads_found.log");
            List<string> writeToFile = new();
            while (true)
            {
                yield return new Wait(HybridEvents.PreloadsUpdated);
                this.lastMapSpawnTimer.Restart();
                preloadFound.Clear();
                var areaInfo = $"{Core.States.AreaLoading.CurrentAreaName}, {Core.States.InGameStateObject.CurrentAreaInstance.AreaHash}";
                var cache = this.preloads.GetUpToDateCache();
                for (var i = 0; i < cache.Count; i++)
                {
                    var key = cache[i];
                    if (Core.CurrentAreaLoadedFiles.PathNames.TryGetValue(key, out _))
                    {
                        var preloadInfo = this.preloads.Get(key);
                        preloadInfo.Priority = i;
                        preloadFound[preloadInfo] = 1;
                        if (preloadInfo.LogToDisk)
                        {
                            writeToFile.Add($"{DateTime.Now}, {areaInfo}, {preloadInfo.DisplayName}");
                        }
                    }
                }

                this.preloadFoundList = preloadFound.Keys.Where(key => key.Enabled).OrderBy(data => data.Priority).ToList();
                if (writeToFile.Count > 0)
                {
                    File.AppendAllLines(logFilePathname, writeToFile);
                    writeToFile.Clear();
                }
            }
        }
    }
}
