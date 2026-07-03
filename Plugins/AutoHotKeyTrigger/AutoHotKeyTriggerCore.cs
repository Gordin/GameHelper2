// <copyright file="AutoHotKeyTriggerCore.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using ClickableTransparentOverlay.Win32;
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
    using AutoHotKeyTrigger.ProfileManager;
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions;

    /// <summary>
    ///     <see cref="AutoHotKeyTrigger" /> plugin.
    /// </summary>
    public sealed class AutoHotKeyTriggerCore : PCore<AutoHotKeyTriggerSettings>
    {
        private readonly List<(string name, Profile value)> clonesToAdd = new();
        private readonly Vector4 impTextColor = new(255, 255, 0, 255);
        private readonly Vector2 size = new(624, 380);
        private readonly List<string> keyPressInfo = new();
        private bool keyPressInfoAdded = false;
        private bool isDebugWindowHovered = false;
        private DateTime lastInvulnScan = DateTime.MinValue;
        private readonly Dictionary<uint, string> lastInvulnReport = new();
        private ActiveCoroutine? onAreaChange;
        private string debugMessage = string.Empty;
        private string newProfileName = string.Empty;
        private bool stopShowingAutoQuitWarning = false;

        private string SettingPathname => Path.Join(this.DllDirectory, "config", "settings.txt");
        private bool ShouldExecuteAutoQuit =>
            this.Settings.EnableAutoQuit &&
            this.Settings.AutoQuitCondition.Evaluate();

        /// <inheritdoc />
        public override void DrawSettings()
        {
            AhkText.Current = this.PluginText;
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
            ImGui.TextColored(this.impTextColor, this.PluginText.T("warning.settings_file", "Do not trust Settings.txt files for Auto Hokey Trigger from sources you have not personally verified. " +
                              "They may contain malicious content that can compromise your computer. " +
                              "Using profiles with incorrectly configured rules may also lead to you being kicked from the server, " +
                              "or your account being banned as a result of preforming to many actions repeatedly.")) ;
            ImGui.NewLine();
            ImGui.TextColored(this.impTextColor, this.PluginText.T("warning.flask_rules", "Again, all profiles/rules created to use a specified flask(s) should have at a minimum " +
                              "the FLASK_EFFECT and an appropriate number of FLASK_CHARGES defined as part of the use condition of a given profile rule. " +
                              "Failing to to include these two conditions as part of a rule will likely result in Auto Hotkey Trigger spamming the flask(s), " + 
                              "resulting in a possible kick or ban from the game servers because of sending to many actions to the server. " +
                              "You have been warrned, use common sense when creating profiles/rulse with this tool."));
            ImGui.PopTextWrapPos();
            if (ImGui.CollapsingHeader(this.PluginText.Title("section.common_config", "Common Config", "AhkCommonConfig")))
            {
                ImGui.Checkbox(this.PluginText.Label("settings.debug_mode", "Debug Mode", "AhkDebugMode"), ref this.Settings.DebugMode);
                ImGui.SameLine();
                ImGui.Checkbox(this.PluginText.Label("settings.run_in_hideout", "Trigger rules or execute Autoquit in Hideout", "AhkRunInHideout"), ref this.Settings.ShouldRunInHideout);
                ImGuiHelper.ToolTip(this.PluginText.T("settings.debug_mode.tooltip", "The debug mode may prove to be a helpful tool in troubleshooting Auto HotKey Trigger profile rules that are not preforming as expected. " +
                                    "It can also be used to verify if AutoHotKeyTrigger is spamming the profile rule action or not based on the included conditions of a given profile rule. " +
                                    "It is highly suggested to create and test all new profiles/rules with the debug mode turned on to insure that all rules are preforming as expected."));
                ImGuiHelper.NonContinuousEnumComboBox(this.PluginText.Label("settings.dump_status_effects", "Dump Player Status Effects", "AhkDumpStatusEffects"),
                    ref this.Settings.DumpStatusEffectOnMe);
                ImGuiHelper.ToolTip(this.PluginText.T("settings.dump_status_effects.tooltip", "This hotkey will dump the current active player's buff(s), debuff(s) into a text file in the GameHelper -> Plugins -> " +
                                    $"AutoHotKeyTrigger folder. Use this hotkey if the AutoHotKeyTrigger plugin fails to detect for example: " +
                                    $"bleeds, corrupting blood, poison, freeze, ignites or other de(buffs) currently active on the character."));
                ImGui.Checkbox(this.PluginText.Label("settings.scan_invuln_markers", "Scan nearby Unique monsters for invuln markers (1/sec)", "AhkScanInvulnMarkers"),
                    ref this.Settings.ScanUniqueInvulnMarkers);
                ImGuiHelper.ToolTip(this.PluginText.T("settings.scan_invuln_markers.tooltip", "Discovery tool. Once per second, logs any nearby Unique/boss monster whose Stats or Buffs " +
                                    "contain an entry that could mean it's currently invulnerable (names containing cannot_be_damaged, " +
                                    "invulnerable, cannot_die, immune, untargetable, etc.). It only logs when a monster's marker set CHANGES, " +
                                    "so the damageable<->invulnerable transition is easy to spot. Output goes to the AHK Debug Window " +
                                    "(enable Debug Mode to see it live) and is appended to unique_invuln_markers.txt in the plugin folder."));
                ImGuiHelper.IEnumerableComboBox(this.PluginText.Label("settings.profile", "Profile", "AhkProfile"), this.Settings.Profiles.Keys, ref this.Settings.CurrentProfile);
                if (ImGui.Button(this.PluginText.Label("button.default_profile", "Add/Reset and Activate League Start Default Profile", "AhkDefaultProfile")))
                {
                    this.CreateDefaultProfile();
                }
            }

            if (ImGui.CollapsingHeader(this.PluginText.Title("section.add_profile", "Add New Profile", "AhkAddProfile")))
            {
                ImGui.InputText(this.PluginText.Label("settings.name", "Name", "AhkNewProfileName"), ref this.newProfileName, 100);
                ImGui.SameLine();
                if (ImGui.Button(this.PluginText.Label("button.add", "Add", "AhkAddProfileButton")))
                {
                    if (!string.IsNullOrEmpty(this.newProfileName))
                    {
                        this.Settings.Profiles.Add(this.newProfileName, new Profile());
                        this.newProfileName = string.Empty;
                    }
                }
            }

            // separate update to allow settings to draw correctly,
            // does not really hurt performance and only called
            // when the settings window is open
            DynamicCondition.UpdateState();
            if (ImGui.CollapsingHeader(this.PluginText.Title("section.existing_profiles", "Existing Profiles", "AhkExistingProfiles")))
            {
                foreach (var (key, profile) in this.Settings.Profiles)
                {
                    var isOpened = ImGui.TreeNode($"{key} (?)");
                    ImGuiHelper.ToolTip(this.PluginText.T("profiles.tooltip", "Rules (tabs) can be moved via drag and drop. They can be cloned by right click."));
                    if (isOpened)
                    {
                        ImGui.SameLine();
                        if (ImGui.SmallButton(this.PluginText.Label("button.delete_profile", "Delete Profile", $"AhkDeleteProfile_{key}")))
                        {
                            this.Settings.Profiles.Remove(key);
                            if (this.Settings.CurrentProfile == key)
                            {
                                this.Settings.CurrentProfile = string.Empty;
                            }
                        }

                        ImGui.SameLine();
                        if (ImGui.SmallButton(this.PluginText.Label("button.clone_profile", "Clone Profile", $"AhkCloneProfile_{key}")))
                        {
                            this.clonesToAdd.Add(($"{key}1", new(profile)));

                        }

                        profile.DrawSettings(key, this.Settings.Profiles);
                        ImGui.TreePop();
                    }
                }

                this.clonesToAdd.RemoveAll(k => this.Settings.Profiles.TryAdd(k.name, k.value) || true); // remove even if add fails.
            }

            if (ImGui.CollapsingHeader(this.PluginText.Title("section.auto_quit", "Auto Quit", "AhkAutoQuit")))
            {
                ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X / 6);
                ImGui.Checkbox(this.PluginText.Label("settings.enable_autoquit", "Enable AutoQuit", "AhkEnableAutoQuit"), ref this.Settings.EnableAutoQuit);
                this.Settings.AutoQuitCondition.Display(true);
                ImGui.Separator();
                ImGui.Checkbox(this.PluginText.Label("settings.enable_autoquit_hotkey", "Enable AutoQuit Manual Hotkey", "AhkEnableAutoQuitHotkey"), ref this.Settings.EnableAutoQuitKey);
                ImGui.Text(this.PluginText.T("settings.autoquit_hotkey", "Hotkey to manually quit game connection: "));
                ImGui.SameLine();
                ImGuiHelper.NonContinuousEnumComboBox("##Manual Quit HotKey", ref this.Settings.AutoQuitKey);
                ImGui.PopItemWidth();
            }
        }

        /// <inheritdoc />
        public override void DrawUI()
        {
            AhkText.Current = this.PluginText;
            if (this.Settings.ScanUniqueInvulnMarkers)
            {
                this.ScanUniqueInvulnMarkers();
            }

            if (this.Settings.DebugMode)
            {
                ImGui.SetNextWindowSize(size, ImGuiCond.FirstUseEver);
                if (ImGui.Begin(this.PluginText.Title("window.debug", "AHK Debug Window", "AhkDebugWindow"), ref this.Settings.DebugMode,
                    this.isDebugWindowHovered ? ImGuiWindowFlags.MenuBar : ImGuiWindowFlags.None))
                {
                    this.isDebugWindowHovered =  ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
                    if (ImGui.BeginMenuBar())
                    {
                        if (ImGui.Button(this.PluginText.Label("button.clear_history", "Clear History", "AhkClearHistory")))
                        {
                            this.keyPressInfo.Clear();
                        }

                        ImGui.EndMenuBar();
                    }

                    for (var i = 0; i < this.keyPressInfo.Count; i++)
                    {
                        ImGui.Text($"{i}-{this.keyPressInfo[i]}");
                    }

                    if (this.keyPressInfoAdded)
                    {
                        ImGui.SetScrollHereY();
                        this.keyPressInfoAdded = false;
                    }

                    if (!string.IsNullOrEmpty(this.debugMessage))
                    {
                        ImGui.Separator();
                        ImGui.TextWrapped(this.PluginText.F("debug.issues", "Issues: {0}", this.debugMessage));
                    }
                }

                ImGui.End();
            }

            this.AutoQuitWarningUi();
            if (!this.ShouldExecutePlugin())
            {
                return;
            }

            DynamicCondition.UpdateState();
            if (this.ShouldExecuteAutoQuit ||
                (this.Settings.EnableAutoQuitKey &&
                Utils.IsKeyPressedAndNotTimeout(this.Settings.AutoQuitKey, 200)))
            {
                MiscHelper.KillTCPConnectionForProcess(Core.Process.Pid);
            }

            if (Utils.IsKeyPressedAndNotTimeout(this.Settings.DumpStatusEffectOnMe, 200))
            {
                if (Core.States.InGameStateObject.CurrentAreaInstance.Player.TryGetComponent<Buffs>(out var buff))
                {
                    var data = "===========================================" + Environment.NewLine;
                    foreach (var statusEffect in buff.StatusEffects)
                    {
                        data += $"{statusEffect.Key} {statusEffect.Value}\n";
                    }

                    if (!string.IsNullOrEmpty(data))
                    {
                        File.AppendAllText(Path.Join(this.DllDirectory, "player_status_effect.txt"), data + Environment.NewLine);
                    }
                }
            }

            if (Core.GHSettings.EnableControllerMode)
            {
                // this is actually disabled in <see cref="MiscHelper.KeyUp"/> function.
                // follow is done just to provide debug msg to end users.
                this.debugMessage = "Controller mode enabled. this plugin doesn't support controllers";
                return;
            }

            if (string.IsNullOrEmpty(this.Settings.CurrentProfile))
            {
                this.debugMessage = "No Profile Selected.";
                return;
            }

            if (!this.Settings.Profiles.ContainsKey(this.Settings.CurrentProfile))
            {
                this.debugMessage = $"{this.Settings.CurrentProfile} not found.";
                return;
            }

            if (Core.States.InGameStateObject.GameUi.ChatParent.IsChatActive)
            {
                this.debugMessage = "Chat window is active, so can not drink flasks or trigger skills.";
                return;
            }

            foreach (var rule in this.Settings.Profiles[this.Settings.CurrentProfile].Rules)
            {
                rule.Execute(this.DebugLog);
            }
        }

        /// <summary>
        ///     Discovery helper: once per second, scans nearby Unique/boss monsters and logs any
        ///     Stats/Buffs entry that could indicate the monster is currently invulnerable. Logs
        ///     only when a given monster's marker set changes, so the damageable/invulnerable
        ///     transition is easy to spot. Use the output to identify the real "cannot be damaged"
        ///     signal, then wire it into a proper DynamicCondition.
        /// </summary>
        private void ScanUniqueInvulnMarkers()
        {
            var now = DateTime.Now;
            if ((now - this.lastInvulnScan).TotalSeconds < 1)
            {
                return;
            }

            this.lastInvulnScan = now;
            if (Core.States.GameCurrentState != GameStateTypes.InGameState)
            {
                return;
            }

            var area = Core.States.InGameStateObject.CurrentAreaInstance;
            var seen = new HashSet<uint>();
            foreach (var entity in area.AwakeEntities.Values)
            {
                if (entity.EntityType != EntityTypes.Monster)
                {
                    continue;
                }

                if (!entity.TryGetComponent<ObjectMagicProperties>(out var omp) ||
                    omp.Rarity != Rarity.Unique)
                {
                    continue;
                }

                seen.Add(entity.Id);
                var markers = new List<string>();
                if (entity.TryGetComponent<Stats>(out var stats))
                {
                    CollectInvulnStats(stats.StatsChangedByBuffAndActions, "stat", markers);
                    CollectInvulnStats(stats.StatsChangedByItems, "item-stat", markers);
                }

                if (entity.TryGetComponent<Buffs>(out var buffs))
                {
                    foreach (var name in buffs.StatusEffects.Keys)
                    {
                        var lower = name.ToLowerInvariant();
                        if (lower.Contains("invuln") || lower.Contains("immun") ||
                            lower.Contains("cannot") || lower.Contains("untarget") ||
                            lower.Contains("phase") || lower.Contains("damage_taken"))
                        {
                            markers.Add($"buff:{name}");
                        }
                    }
                }

                var report = markers.Count == 0 ? "no-invuln-markers" : string.Join(", ", markers);
                if (this.lastInvulnReport.TryGetValue(entity.Id, out var prev) && prev == report)
                {
                    continue;
                }

                this.lastInvulnReport[entity.Id] = report;
                var line = $"UNIQUE [{entity.Id}] {entity.Path} | state={entity.EntityState} | {report}";
                this.DebugLog(line);
                try
                {
                    File.AppendAllText(
                        Path.Join(this.DllDirectory, "unique_invuln_markers.txt"),
                        $"{now:HH:mm:ss} {line}{Environment.NewLine}");
                }
                catch (IOException)
                {
                    // best-effort logging; ignore transient file-access issues.
                }
            }

            // Forget monsters that are no longer awake so a re-encounter logs fresh.
            foreach (var goneId in this.lastInvulnReport.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                this.lastInvulnReport.Remove(goneId);
            }
        }

        private static void CollectInvulnStats(Dictionary<GameStats, int> stats, string prefix, List<string> markers)
        {
            if (stats == null)
            {
                return;
            }

            foreach (var kv in stats)
            {
                if (kv.Value == 0)
                {
                    continue;
                }

                var name = kv.Key.ToString();
                if (name.Contains("cannot_be_damaged") || name.Contains("invulnerable") ||
                    name.Contains("cannot_die") || name.Contains("immun"))
                {
                    markers.Add($"{prefix}:{name}={kv.Value}");
                }
            }
        }

        private void DebugLog(string logText)
        {
            if (this.Settings.DebugMode)
            {
                this.keyPressInfo.Add($"{DateTime.Now.TimeOfDay}: {logText}");
            }

            this.keyPressInfoAdded = true;
        }

        /// <inheritdoc />
        public override void OnDisable()
        {
            this.onAreaChange?.Cancel();
            this.onAreaChange = null;
        }

        /// <inheritdoc />
        public override void OnEnable(bool isGameOpened)
        {
            var jsonData2 = File.ReadAllText(this.DllDirectory + @"/StatusEffectGroup.json");
            JsonDataHelper.StatusEffectGroups = JsonConvert.DeserializeObject<
                Dictionary<string, List<string>>>(jsonData2)
                ?? new Dictionary<string, List<string>>();

            if (File.Exists(this.SettingPathname))
            {
                var content = File.ReadAllText(this.SettingPathname);
                this.Settings = JsonConvert.DeserializeObject<AutoHotKeyTriggerSettings>(
                    content,
                    new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.Auto
                    }) ?? new AutoHotKeyTriggerSettings();
            }
            else
            {
                this.CreateDefaultProfile();
            }

            this.onAreaChange = CoroutineHandler.Start(this.EnableAutoQuitWarningUiOnAreaChange());
        }

        /// <inheritdoc />
        public override void SaveSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.SettingPathname) ?? string.Empty);
            var settingsData = JsonConvert.SerializeObject(this.Settings,
                Formatting.Indented,
                new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.Auto
                });
            File.WriteAllText(this.SettingPathname, settingsData);
        }

        private bool ShouldExecutePlugin()
        {
            var cgs = Core.States.GameCurrentState;
            if (cgs != GameStateTypes.InGameState)
            {
                this.debugMessage = $"Current game state isn't InGameState, it's {cgs}.";
                return false;
            }

            if (!Core.Process.Foreground)
            {
                this.debugMessage = "Game is minimized.";
                return false;
            }

            var areaDetails = Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails;
            if (areaDetails.IsTown)
            {
                this.debugMessage = "Player is in town.";
                return false;
            }

            if (!this.Settings.ShouldRunInHideout && areaDetails.IsHideout)
            {
                this.debugMessage = "Player is in hideout & hideout execution is turned off.";
                return false;
            }

            if (Core.States.InGameStateObject.CurrentAreaInstance.Player.TryGetComponent<Life>(out var lifeComp))
            {
                if (lifeComp.Health.Current <= 0)
                {
                    this.debugMessage = "Player is dead.";
                    return false;
                }
            }
            else
            {
                this.debugMessage = "Can not find player Life component.";
                return false;
            }

            if (Core.States.InGameStateObject.CurrentAreaInstance.Player.TryGetComponent<Buffs>(out var buffComp))
            {
                if (buffComp.StatusEffects.ContainsKey("grace_period"))
                {
                    this.debugMessage = "Player has Grace Period.";
                    return false;
                }
            }
            else
            {
                this.debugMessage = "Can not find player PlayerBuffs component.";
                return false;
            }

            if (!Core.States.InGameStateObject.CurrentAreaInstance.Player.TryGetComponent<Actor>(out var _))
            {
                this.debugMessage = "Can not find player Actor component.";
                return false;
            }

            this.debugMessage = string.Empty;
            return true;
        }

        /// <summary>
        ///     Creates a default profile that is only valid for flasks on newly created character.
        /// </summary>
        private void CreateDefaultProfile()
        {
            Profile profile = new();
            foreach (var rule in Rule.CreateDefaultRules())
            {
                profile.Rules.Add(rule);
            }

            this.Settings.Profiles["LeagueStartDefaultProfile"] = profile;
            this.Settings.CurrentProfile = "LeagueStartDefaultProfile";
            this.Settings.Profiles["ProfileMidGame"] = new();
            this.Settings.Profiles["ProfileEndGame"] = new();
        }

        private void AutoQuitWarningUi()
        {

            if (!this.stopShowingAutoQuitWarning &&
                (Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails.IsTown ||
                Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails.IsHideout) &&
                this.ShouldExecuteAutoQuit)
            {
                ImGui.OpenPopup("AutoQuitWarningUi");
            }

            if (ImGui.BeginPopup("AutoQuitWarningUi"))
            {
                var warningMsg = this.PluginText.T("warning.autoquit_true", "The current condition you have put for AutoQuit is yielding true.\n" +
                    "This mean you will automatically logout as soon as you leave town/hideout.\n" +
                    "Please update your AutoQuit condition and/or disable it and/or fix your exile state.");
                ImGui.Text(warningMsg);
                if (ImGui.Button(this.PluginText.Label("button.i_understand", "I understand", "AhkAutoQuitUnderstand"), new Vector2(ImGui.CalcTextSize(warningMsg).X, 50f)))
                {
                    this.stopShowingAutoQuitWarning = true;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
        }

        private IEnumerator<Wait> EnableAutoQuitWarningUiOnAreaChange()
        {
            while (true)
            {
                yield return new Wait(RemoteEvents.AreaChanged);
                this.stopShowingAutoQuitWarning = false;
            }
        }
    }
}
