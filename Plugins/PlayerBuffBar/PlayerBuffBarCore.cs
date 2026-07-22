namespace PlayerBuffBar
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using System.Runtime.InteropServices;
    using System.Text;
    using GameHelper;
    using GameHelper.Plugin;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using GameHelper.Utils;
    using GameOffsets.Objects.Components;
    using ImGuiNET;
    using Newtonsoft.Json;

    public sealed class PlayerBuffBarCore : PCore<PlayerBuffBarSettings>
    {
        private readonly BuffIconLoader iconLoader = new();

        private string SettingsPath => Path.Join(this.DllDirectory, "config", "settings.txt");
        private readonly string[] watchlistBuffers = new string[PlayerBuffBarSettings.MaxBuffBars];
        private readonly bool[] watchlistEditorDirtyPerBar = new bool[PlayerBuffBarSettings.MaxBuffBars];
        private string dumpStatusLine = string.Empty;
        private bool iconsQueuedOnEnable;

        public override void OnEnable(bool isGameOpened)
        {
            if (File.Exists(this.SettingsPath))
            {
                try
                {
                    var content = File.ReadAllText(this.SettingsPath);
                    this.Settings = JsonConvert.DeserializeObject<PlayerBuffBarSettings>(content) ?? new PlayerBuffBarSettings();
                }
                catch
                {
                    this.Settings = new PlayerBuffBarSettings();
                }
            }

            var migrated = this.MigrateSettingsIfNeeded();
            Array.Clear(this.watchlistEditorDirtyPerBar);
            this.iconLoader.Initialize(this.DllDirectory);
            this.iconsQueuedOnEnable = false;
            this.QueueIconDownloads(force: false);
            this.SyncAllWatchlistBuffersFromSettings();
            if (migrated)
            {
                this.SaveSettings();
            }
        }

        public override void OnDisable()
        {
            this.iconsQueuedOnEnable = false;
        }

        public override void SaveSettings()
        {
            this.ApplyWatchlistBufferIfNeeded();

            var dir = Path.GetDirectoryName(this.SettingsPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(this.SettingsPath, JsonConvert.SerializeObject(this.Settings, Formatting.Indented));
        }

        public override void DrawSettings()
        {
            this.DrawGeneralSettings();

            if (this.DrawSettingsSection(
                "section.resource_bar",
                "Resource bar (charges / rage)",
                "resource",
                ref this.Settings.SettingsResourceBarSectionOpen))
            {
                this.DrawResourceBarSettings();
            }

            if (this.DrawSettingsSection(
                "section.buff_bars",
                "Buff bars (up to 4)",
                "buffbars",
                ref this.Settings.SettingsBuffBarsSectionOpen))
            {
                this.DrawBuffBarsSettings();
            }

            if (this.DrawSettingsSection(
                "section.buff_display",
                "Buff display",
                "buffdisplay",
                ref this.Settings.SettingsBuffDisplaySectionOpen))
            {
                this.DrawBuffDisplaySettings();
            }

            if (this.DrawSettingsSection(
                "section.icons_tools",
                "Icons & tools",
                "icons",
                ref this.Settings.SettingsIconsToolsSectionOpen))
            {
                this.DrawIconsToolsSettings();
            }
        }

        private void DrawResourceBarSettings()
        {
            var fieldWidth = ImGui.GetContentRegionAvail().X;

            ImGui.Checkbox(this.PluginText.Label("settings.show_charges", "Show charges (P/F/E)", "PlayerBuffBarShowCharges"), ref this.Settings.ShowCharges);
            ImGui.Checkbox(this.PluginText.Label("settings.show_rage", "Show rage", "PlayerBuffBarShowRage"), ref this.Settings.ShowRage);
            ImGui.Checkbox(this.PluginText.Label("settings.hide_empty_resources", "Hide empty charges / rage", "PlayerBuffBarHideEmptyResources"), ref this.Settings.HideEmptyResources);
            ImGui.Checkbox(this.PluginText.Label("settings.anchor_resource_bar", "Anchor resource bar to health bar", "PlayerBuffBarAnchorResourceBar"), ref this.Settings.ResourceAnchorToHealthBar);
            if (!this.Settings.ResourceAnchorToHealthBar)
            {
                ImGui.Checkbox(this.PluginText.Label("settings.show_position_dummy", "Show position dummy (drag, then disable)", "PlayerBuffBarResourcePositionDummy"), ref this.Settings.ResourceShowPositionDummy);
            }
            else
            {
                this.Settings.ResourceShowPositionDummy = false;
            }

            ImGui.Text(this.PluginText.T("settings.resource_icon_size", "Resource icon size"));
            ImGui.SetNextItemWidth(fieldWidth);
            ImGui.DragFloat("##resource_icon_size", ref this.Settings.ResourceIconSize, 1f, 16f, 72f, "%.0f");

            ImGui.Text(this.PluginText.T("settings.resource_icon_spacing", "Resource icon spacing"));
            ImGui.SetNextItemWidth(fieldWidth);
            ImGui.DragFloat("##resource_icon_spacing", ref this.Settings.ResourceIconSpacing, 0.5f, 0f, 24f, "%.1f");

            ImGui.Text(this.PluginText.T("settings.resource_offset", "Resource offset X / Y"));
            ImGui.SetNextItemWidth(fieldWidth);
            ImGui.DragFloat2("##resource_screen_offset", ref this.Settings.ResourceScreenOffset, 1f, -400f, 400f, "%.0f");

            ImGui.Text(this.PluginText.T("settings.resource_fixed_position", "Resource fixed position"));
            ImGui.SetNextItemWidth(fieldWidth);
            ImGui.DragFloat2("##resource_fixed_position", ref this.Settings.ResourceFixedPosition, 1f, 0f, 4000f, "%.0f");

            ImGui.Separator();
            ImGui.TextUnformatted(this.PluginText.T("section.colors", "Colors"));
            ImGui.ColorEdit4(this.PluginText.Label("settings.charge_text_color", "Charge text color", "PlayerBuffBarChargeTextColor"), ref this.Settings.ChargeTextColor);
            ImGui.ColorEdit4(this.PluginText.Label("settings.rage_text_color", "Rage text color", "PlayerBuffBarRageTextColor"), ref this.Settings.RageTextColor);

            ImGui.TextDisabled(
                this.PluginText.T("settings.resource_help", "P/F/E charge icons load from poe2db. Rage stays numeric. Do not add charges/rage to the watchlist."));

            ImGui.Spacing();
        }

        private void DrawBuffBarsSettings()
        {
            this.Settings.EnsureBuffBarSlots();
            ImGui.TextWrapped(
                this.PluginText.T("settings.buff_bars_help", "Each bar has its own watchlist and position. Resource charges/rage stay on the resource bar."));

            if (ImGui.BeginTabBar("##PlayerBuffBarTabs"))
            {
                for (var i = 0; i < PlayerBuffBarSettings.MaxBuffBars; i++)
                {
                    var bar = this.Settings.BuffBars[i];
                    var tabLabel = this.PluginText.F("tab.bar", "Bar {0}", i + 1);
                    if (!bar.Enabled)
                    {
                        tabLabel += this.PluginText.T("tab.off_suffix", " (off)");
                    }

                    if (ImGui.BeginTabItem($"{tabLabel}##buff_bar_tab_{i}"))
                    {
                        this.Settings.SelectedBuffBarTab = i;
                        this.DrawSingleBuffBarSettings(i, bar);
                        ImGui.EndTabItem();
                    }
                }

                ImGui.EndTabBar();
            }

            ImGui.Spacing();
        }

        private void DrawSingleBuffBarSettings(int barIndex, BuffBarSlotSettings bar)
        {
            var fieldWidth = ImGui.GetContentRegionAvail().X;

            ImGui.Checkbox(this.PluginText.Label("settings.enable_this_bar", "Enable this bar", $"PlayerBuffBarEnable_{barIndex}"), ref bar.Enabled);
            ImGui.Checkbox(this.PluginText.Label("settings.anchor_to_health_bar", "Anchor to health bar", $"PlayerBuffBarAnchor_{barIndex}"), ref bar.AnchorToHealthBar);
            if (!bar.AnchorToHealthBar)
            {
                ImGui.Checkbox(this.PluginText.Label("settings.show_position_dummy", "Show position dummy (drag, then disable)", $"PlayerBuffBarPositionDummy_{barIndex}"), ref bar.ShowPositionDummy);
            }
            else
            {
                bar.ShowPositionDummy = false;
            }

            ImGui.Text(this.PluginText.T("settings.icon_size", "Icon size"));
            ImGui.SetNextItemWidth(fieldWidth);
            ImGui.DragFloat($"##buff_bar_{barIndex}_icon_size", ref bar.IconSize, 1f, 16f, 72f, "%.0f");

            ImGui.Text(this.PluginText.T("settings.icon_spacing", "Icon spacing"));
            ImGui.SetNextItemWidth(fieldWidth);
            ImGui.DragFloat($"##buff_bar_{barIndex}_icon_spacing", ref bar.IconSpacing, 0.5f, 0f, 24f, "%.1f");

            ImGui.Text(this.PluginText.T("settings.offset", "Offset X / Y"));
            ImGui.SetNextItemWidth(fieldWidth);
            ImGui.DragFloat2($"##buff_bar_{barIndex}_screen_offset", ref bar.ScreenOffset, 1f, -400f, 400f, "%.0f");
            ImGui.TextDisabled(this.PluginText.T("settings.offset_help", "Positive Y = below character, negative Y = above."));

            ImGui.Text(this.PluginText.T("settings.fixed_position", "Fixed position"));
            ImGui.SetNextItemWidth(fieldWidth);
            ImGui.DragFloat2($"##buff_bar_{barIndex}_fixed_position", ref bar.FixedPosition, 1f, 0f, 4000f, "%.0f");

            ImGui.Separator();
            ImGui.TextUnformatted(this.PluginText.T("section.watchlist", "Watchlist"));
            ImGui.TextWrapped(
                this.PluginText.T("settings.watchlist_help", "One buff id substring per line. Not for charges/rage. Examples: puppet_master, refutation, archon_undeath."));

            if (!this.watchlistEditorDirtyPerBar[barIndex])
            {
                this.SyncWatchlistBufferFromSettings(barIndex);
            }

            ImGui.InputTextMultiline(
                $"###PlayerBuffBarWatchlist_{barIndex}",
                ref this.watchlistBuffers[barIndex],
                4096,
                new Vector2(-1f, 100f));
            if (ImGui.IsItemDeactivatedAfterEdit())
            {
                this.ApplyWatchlistBuffer(barIndex);
                this.watchlistEditorDirtyPerBar[barIndex] = false;
                this.QueueIconDownloads(force: true);
            }
            else if (ImGui.IsItemEdited())
            {
                this.watchlistEditorDirtyPerBar[barIndex] = true;
            }

            if (ImGui.Button(this.PluginText.Label("button.apply_watchlist", "Apply watchlist", $"bar_{barIndex}")))
            {
                this.ApplyWatchlistBuffer(barIndex);
                this.watchlistEditorDirtyPerBar[barIndex] = false;
                this.QueueIconDownloads(force: true);
            }

            if (barIndex == 0)
            {
                ImGui.SameLine();
                if (ImGui.Button(this.PluginText.Label("button.reset_defaults", "Reset defaults", "PlayerBuffBarResetDefaults")))
                {
                    this.Settings = new PlayerBuffBarSettings();
                    Array.Clear(this.watchlistEditorDirtyPerBar);
                    this.SyncAllWatchlistBuffersFromSettings();
                    this.QueueIconDownloads(force: false);
                }
            }
        }

        private void DrawBuffDisplaySettings()
        {
            var fieldWidth = ImGui.GetContentRegionAvail().X;

            ImGui.Text(this.PluginText.T("settings.inactive_icon_alpha", "Inactive icon alpha"));
            ImGui.SetNextItemWidth(fieldWidth);
            ImGui.DragFloat("##inactive_icon_alpha", ref this.Settings.InactiveIconAlpha, 0.02f, 0.05f, 1f, "%.2f");

            ImGui.Text(this.PluginText.T("settings.font_scale", "Font scale"));
            ImGui.SetNextItemWidth(fieldWidth);
            ImGui.DragFloat("##font_scale", ref this.Settings.FontScale, 0.02f, 0.7f, 1.6f, "%.2f");

            ImGui.Text(this.PluginText.T("settings.max_bar_width", "Max bar width"));
            ImGui.SetNextItemWidth(fieldWidth);
            ImGui.DragFloat("##max_bar_width", ref this.Settings.MaxBarWidth, 2f, 120f, 480f, "%.0f");

            ImGui.Separator();
            ImGui.TextUnformatted(this.PluginText.T("section.colors", "Colors"));
            ImGui.ColorEdit4(this.PluginText.Label("settings.active_buff_color", "Active buff color", "PlayerBuffBarActiveBuffColor"), ref this.Settings.ActiveColor);
            ImGui.ColorEdit4(this.PluginText.Label("settings.inactive_buff_color", "Inactive buff color", "PlayerBuffBarInactiveBuffColor"), ref this.Settings.InactiveColor);
            ImGui.ColorEdit4(this.PluginText.Label("settings.buff_text_color", "Buff text color", "PlayerBuffBarBuffTextColor"), ref this.Settings.BuffTextColor);

            ImGui.Spacing();
        }

        private void DrawIconsToolsSettings()
        {
            if (ImGui.Button(this.PluginText.Label("button.dump_buffs", "Dump my buffs", "PlayerBuffBarDumpBuffs")))
            {
                this.DumpPlayerBuffs();
            }

            ImGui.SameLine();
            if (ImGui.Button(this.PluginText.Label("button.download_missing_icons", "Download missing icons", "PlayerBuffBarDownloadMissingIcons")))
            {
                this.QueueIconDownloads(force: true);
            }

            ImGui.SameLine();
            if (ImGui.Button(this.PluginText.Label("button.reload_icons", "Reload icons", "PlayerBuffBarReloadIcons")))
            {
                this.iconLoader.ReloadIconMap(this.DllDirectory);
                this.iconLoader.ReloadTextures();
                this.QueueIconDownloads(force: false);
            }

            ImGui.TextDisabled(this.PluginText.F("status.cached_icons", "Cached icons: {0}", this.iconLoader.CachedIconCount));
            if (!string.IsNullOrEmpty(this.iconLoader.LastLogLine))
            {
                ImGui.TextDisabled(this.iconLoader.LastLogLine);
            }

            if (!string.IsNullOrEmpty(this.dumpStatusLine))
            {
                ImGui.TextDisabled(this.dumpStatusLine);
            }

            ImGui.TextDisabled(
                this.PluginText.T("settings.icons_help", "Icons load from poe2db.tw (PoE2). Cache: Plugins/PlayerBuffBar/icons/."));
        }

        public override void DrawUI()
        {
            if (!this.Settings.ShowOverlay)
            {
                return;
            }

            this.iconLoader.PollReload();

            var gameState = Core.States.GameCurrentState;
            if (gameState is not (GameStateTypes.InGameState or GameStateTypes.EscapeState))
            {
                return;
            }

            var positioningDummyActive = this.Settings.ResourceShowPositionDummy || this.AnyBuffBarPositionDummyActive();

            if (gameState == GameStateTypes.EscapeState && !positioningDummyActive)
            {
                return;
            }

            if (this.Settings.HideWhenGameInBackground && !IsGameOrOverlayForeground())
            {
                return;
            }

            var inGame = Core.States.InGameStateObject;
            var areaDetails = inGame.CurrentWorldInstance.AreaDetails;
            if (this.Settings.HideInTownOrHideout && (areaDetails.IsTown || areaDetails.IsHideout) && !positioningDummyActive)
            {
                return;
            }

            if (inGame.GameUi.IsAnyLargePanelOpen && !positioningDummyActive)
            {
                return;
            }

            if (WorldSpaceBarVisibility.ShouldHide(inGame.GameUi) && !positioningDummyActive)
            {
                return;
            }

            var player = inGame.CurrentAreaInstance.Player;
            if (!player.IsValid || !player.TryGetComponent<Render>(out var render, true))
            {
                return;
            }

            var healthAnchor = this.ResolveHealthBarAnchor(render, inGame.CurrentWorldInstance);
            if (healthAnchor == null)
            {
                return;
            }

            if (this.Settings.AutoDownloadWikiIcons && !this.iconsQueuedOnEnable)
            {
                this.QueueIconDownloads(force: false);
                this.iconsQueuedOnEnable = true;
            }

            player.TryGetComponent<Buffs>(out var buffs, true);
            player.TryGetComponent<Stats>(out var stats, true);

            var activeLookup = this.BuildActiveBuffLookup(buffs);

            if (this.Settings.DisplayMode != BuffBarDisplayMode.Text)
            {
                this.DrawIconHud(healthAnchor.Value, activeLookup, stats);
            }

            if (this.Settings.DisplayMode != BuffBarDisplayMode.Icons)
            {
                var resourceLine = this.BuildResourceLine(stats);
                this.DrawTextHud(healthAnchor.Value, resourceLine, activeLookup);
            }
        }

        private bool AnyBuffBarPositionDummyActive()
        {
            this.Settings.EnsureBuffBarSlots();
            foreach (var bar in this.Settings.BuffBars)
            {
                if (bar.ShowPositionDummy)
                {
                    return true;
                }
            }

            return false;
        }

        private void QueueIconDownloads(bool force)
        {
            this.iconLoader.RequestDownloads(this.Settings.GetAllWatchIds(), force);
            this.iconLoader.RequestResourceIcons(force);
        }

        private bool MigrateSettingsIfNeeded()
        {
            var migrated = false;
            if (this.Settings.SettingsVersion < 2)
            {
                this.Settings.BuffIconSize = this.Settings.IconSize;
                this.Settings.BuffIconSpacing = this.Settings.IconSpacing;
                this.Settings.BuffScreenOffset = this.Settings.ScreenOffset;
                this.Settings.BuffFixedPosition = this.Settings.FixedPosition;
                this.Settings.BuffAnchorToHealthBar = this.Settings.AnchorToHealthBar;

                this.Settings.ResourceIconSize = this.Settings.IconSize;
                this.Settings.ResourceIconSpacing = this.Settings.IconSpacing;
                this.Settings.ResourceScreenOffset = new Vector2(
                    this.Settings.ScreenOffset.X,
                    this.Settings.ScreenOffset.Y - this.Settings.IconSize - this.Settings.IconSpacing - 6f);
                this.Settings.ResourceFixedPosition = new Vector2(
                    this.Settings.FixedPosition.X,
                    this.Settings.FixedPosition.Y - this.Settings.IconSize - 8f);
                this.Settings.ResourceAnchorToHealthBar = this.Settings.AnchorToHealthBar;

                this.Settings.Watchlist = this.Settings.Watchlist
                    .Where(id => !BuffIconCatalog.IsReservedResourceWatchId(id))
                    .ToList();

                this.Settings.SettingsVersion = 2;
                migrated = true;
            }

            if (this.Settings.SettingsVersion < 3)
            {
                this.Settings.ResourceShowPositionDummy = false;
                this.Settings.BuffShowPositionDummy = false;
                this.Settings.SettingsVersion = 3;
                migrated = true;
            }

            if (this.Settings.SettingsVersion < 4)
            {
                this.Settings.Watchlist = this.Settings.Watchlist
                    .Where(id => !BuffIconCatalog.IsReservedResourceWatchId(id))
                    .ToList();
                this.Settings.SettingsVersion = 4;
                migrated = true;
            }

            if (this.Settings.SettingsVersion < 5)
            {
                if (this.Settings.Watchlist.Count == 0)
                {
                    this.Settings.Watchlist = PlayerBuffBarSettings.CreateDefaultWatchlist().ToList();
                }
                else
                {
                    var defaults = PlayerBuffBarSettings.CreateDefaultWatchlist();
                    var isExactDefaultWatchlist = this.Settings.Watchlist.Count == defaults.Count &&
                        PlayerBuffBarSettings.IsDefaultWatchlistPrefix(this.Settings.Watchlist);
                    if (!isExactDefaultWatchlist)
                    {
                        this.Settings.WatchlistUserConfigured = true;
                    }
                }

                this.Settings.SettingsVersion = 5;
                migrated = true;
            }

            if (this.Settings.SettingsVersion < 6)
            {
                this.Settings.SettingsResourceBarSectionOpen = false;
                this.Settings.SettingsBuffBarSectionOpen = false;
                this.Settings.SettingsWatchlistSectionOpen = false;
                this.Settings.SettingsIconsToolsSectionOpen = false;
                this.Settings.SettingsVersion = 6;
                migrated = true;
            }

            if (this.Settings.SettingsVersion < 8)
            {
                this.Settings.SettingsResourceBarSectionOpen = false;
                this.Settings.SettingsBuffBarSectionOpen = false;
                this.Settings.SettingsWatchlistSectionOpen = false;
                this.Settings.SettingsIconsToolsSectionOpen = false;
                this.Settings.SettingsVersion = 8;
                migrated = true;
            }

            if (this.Settings.SettingsVersion < 9)
            {
                this.MigrateToMultiBuffBars();
                this.Settings.SettingsResourceBarSectionOpen = false;
                this.Settings.SettingsBuffBarsSectionOpen = false;
                this.Settings.SettingsBuffDisplaySectionOpen = false;
                this.Settings.SettingsIconsToolsSectionOpen = false;
                this.Settings.SettingsVersion = 9;
                migrated = true;
            }

            this.Settings.EnsureBuffBarSlots();
            this.EnsureWatchlistDefaults();

            return migrated;
        }

        private void MigrateToMultiBuffBars()
        {
            if (this.Settings.BuffBars.Count > 0)
            {
                this.Settings.EnsureBuffBarSlots();
                return;
            }

            var legacyWatchlist = this.Settings.Watchlist
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Where(id => !BuffIconCatalog.IsReservedResourceWatchId(id))
                .ToList();

            this.Settings.BuffBars.Add(new BuffBarSlotSettings
            {
                Enabled = true,
                AnchorToHealthBar = this.Settings.BuffAnchorToHealthBar,
                ShowPositionDummy = this.Settings.BuffShowPositionDummy,
                IconSize = this.Settings.BuffIconSize,
                IconSpacing = this.Settings.BuffIconSpacing,
                ScreenOffset = this.Settings.BuffScreenOffset,
                FixedPosition = this.Settings.BuffFixedPosition,
                Watchlist = legacyWatchlist,
            });

            for (var i = 1; i < PlayerBuffBarSettings.MaxBuffBars; i++)
            {
                this.Settings.BuffBars.Add(PlayerBuffBarSettings.CreateDefaultBuffBarSlot(i));
                this.Settings.BuffBars[i].Enabled = false;
            }
        }

        private void EnsureWatchlistDefaults()
        {
            this.Settings.EnsureBuffBarSlots();
            if (this.Settings.WatchlistUserConfigured)
            {
                return;
            }

            var bar0 = this.Settings.BuffBars[0];
            if (bar0.Watchlist.Count == 0)
            {
                bar0.Watchlist = PlayerBuffBarSettings.CreateDefaultWatchlist().ToList();
            }
        }

        private Vector2? ResolveHealthBarAnchor(Render render, WorldData world)
        {
            if (world.Address == IntPtr.Zero)
            {
                return null;
            }

            var curPos = render.WorldPosition;
            curPos.Z -= render.ModelBounds.Z;
            return world.WorldToScreen(curPos, curPos.Z);
        }

        private Vector2 ResolveBarTopLeft(Vector2 healthAnchor, bool anchorToHealthBar, Vector2 screenOffset, Vector2 fixedTopLeft, float rowWidth)
        {
            if (!anchorToHealthBar)
            {
                return fixedTopLeft;
            }

            var center = healthAnchor + screenOffset;
            return new Vector2(center.X - rowWidth * 0.5f, center.Y);
        }

        private int GetResourceSlotCount()
        {
            var slots = 0;
            if (this.Settings.ShowCharges)
            {
                slots += 3;
            }

            if (this.Settings.ShowRage)
            {
                slots += 1;
            }

            return slots;
        }

        private void DrawIconHud(Vector2 healthAnchor, Dictionary<string, ActiveBuffInfo> activeLookup, Stats? stats)
        {
            var draw = GetHudDrawList();
            var resourceRow = this.BuildChargeIconEntries(stats);
            var resourceSlots = this.GetResourceSlotCount();

            if (resourceSlots > 0)
            {
                if (this.Settings.ResourceShowPositionDummy && !this.Settings.ResourceAnchorToHealthBar)
                {
                    this.DrawPositionDummy(
                        "ResourceBarDummy",
                        ref this.Settings.ResourceFixedPosition,
                        this.Settings.ResourceIconSize,
                        this.Settings.ResourceIconSpacing,
                        this.BuildResourcePreviewEntries(),
                        resourcePreview: true);
                }
                else if (resourceRow.Count > 0)
                {
                    var rowWidth = this.GetRowWidth(resourceRow.Count, this.Settings.ResourceIconSize, this.Settings.ResourceIconSpacing);
                    var topLeft = this.ResolveBarTopLeft(
                        healthAnchor,
                        this.Settings.ResourceAnchorToHealthBar,
                        this.Settings.ResourceScreenOffset,
                        this.Settings.ResourceFixedPosition,
                        rowWidth);
                    this.DrawIconRow(draw, resourceRow, topLeft, this.Settings.ResourceIconSize, this.Settings.ResourceIconSpacing, resourceOnly: true);
                }
            }

            this.Settings.EnsureBuffBarSlots();
            for (var barIndex = 0; barIndex < this.Settings.BuffBars.Count; barIndex++)
            {
                var bar = this.Settings.BuffBars[barIndex];
                if (!bar.Enabled && !bar.ShowPositionDummy)
                {
                    continue;
                }

                var entries = this.BuildDisplayEntriesForBar(bar.Watchlist, activeLookup);
                var buffRow = entries.Where(e => e.IsActive || this.Settings.ShowInactiveWatchlist).ToList();
                if (bar.ShowPositionDummy && !bar.AnchorToHealthBar)
                {
                    this.DrawPositionDummy(
                        $"BuffBarDummy_{barIndex}",
                        ref bar.FixedPosition,
                        bar.IconSize,
                        bar.IconSpacing,
                        buffRow.Count > 0 ? buffRow : this.BuildBuffPreviewEntries(bar.Watchlist),
                        resourcePreview: false);
                }
                else if (bar.Enabled && buffRow.Count > 0)
                {
                    var rowWidth = this.GetRowWidth(buffRow.Count, bar.IconSize, bar.IconSpacing);
                    var topLeft = this.ResolveBarTopLeft(
                        healthAnchor,
                        bar.AnchorToHealthBar,
                        bar.ScreenOffset,
                        bar.FixedPosition,
                        rowWidth);
                    this.DrawIconRow(draw, buffRow, topLeft, bar.IconSize, bar.IconSpacing, resourceOnly: false);
                }
            }
        }

        private float GetRowWidth(int count, float iconSize, float spacing) =>
            count * iconSize + Math.Max(0, count - 1) * spacing;

        private void DrawIconRow(
            ImDrawListPtr draw,
            List<BuffDisplayEntry> row,
            Vector2 topLeft,
            float iconSize,
            float spacing,
            bool resourceOnly)
        {
            var x = topLeft.X;
            var y = topLeft.Y;

            foreach (var entry in row)
            {
                if (resourceOnly)
                {
                    this.DrawResourceIconCell(draw, new Vector2(x, y), entry, iconSize);
                }
                else
                {
                    this.DrawBuffIconCell(draw, new Vector2(x, y), entry, iconSize);
                }

                x += iconSize + spacing;
            }
        }

        private List<BuffDisplayEntry> BuildResourcePreviewEntries()
        {
            var row = new List<BuffDisplayEntry>();
            if (this.Settings.ShowCharges)
            {
                row.Add(new BuffDisplayEntry { WatchId = "power_charge", IsActive = true, ChargeCount = 3 });
                row.Add(new BuffDisplayEntry { WatchId = "frenzy_charge", IsActive = true, ChargeCount = 7 });
                row.Add(new BuffDisplayEntry { WatchId = "endurance_charge", IsActive = true, ChargeCount = 4 });
            }

            if (this.Settings.ShowRage)
            {
                row.Add(new BuffDisplayEntry { WatchId = "rage", IsActive = true, ChargeCount = 25 });
            }

            return row;
        }

        private List<BuffDisplayEntry> BuildBuffPreviewEntries(IReadOnlyList<string> watchlist)
        {
            if (watchlist.Count > 0)
            {
                return watchlist
                    .Where(id => !BuffIconCatalog.IsReservedResourceWatchId(id))
                    .Select(id => new BuffDisplayEntry
                    {
                        WatchId = id,
                        Display = PrettyName(id),
                        IsActive = true,
                        Stacks = 6,
                        TimeLeft = 12f,
                        HasDuration = true,
                    })
                    .ToList();
            }

            return new List<BuffDisplayEntry>
            {
                new()
                {
                    WatchId = "fortify",
                    Display = "Fortify",
                    IsActive = true,
                    Stacks = 6,
                    TimeLeft = 12f,
                    HasDuration = true,
                },
            };
        }

        private float GetBarRowHeight(bool resourcePreview, float iconSize, List<BuffDisplayEntry> entries)
        {
            var height = iconSize;
            if (!resourcePreview && this.Settings.ShowDurations &&
                entries.Any(e => e.IsActive && e.HasDuration))
            {
                height += ImGui.GetTextLineHeight() + 2f;
            }

            return height;
        }

        private void DrawPositionDummy(
            string windowId,
            ref Vector2 fixedTopLeft,
            float iconSize,
            float spacing,
            List<BuffDisplayEntry> previewEntries,
            bool resourcePreview)
        {
            if (previewEntries.Count == 0)
            {
                return;
            }

            var rowWidth = this.GetRowWidth(previewEntries.Count, iconSize, spacing);
            var rowHeight = this.GetBarRowHeight(resourcePreview, iconSize, previewEntries);
            var draw = ImGui.GetForegroundDrawList();

            this.DrawIconRow(draw, previewEntries, fixedTopLeft, iconSize, spacing, resourcePreview);

            var outlineMin = fixedTopLeft - new Vector2(2f, 2f);
            var outlineMax = fixedTopLeft + new Vector2(rowWidth, rowHeight) + new Vector2(2f, 2f);
            draw.AddRect(
                outlineMin,
                outlineMax,
                ImGuiHelper.Color(new Vector4(0.4f, 0.85f, 1f, 0.85f)),
                4f,
                ImDrawFlags.None,
                1.5f);

            var hint = this.PluginText.T("hud.drag_hint", "Drag to move. Disable dummy in settings when done.");
            var hintSize = ImGui.CalcTextSize(hint);
            var hintPos = new Vector2(fixedTopLeft.X, fixedTopLeft.Y - hintSize.Y - 4f);
            draw.AddText(hintPos + Vector2.One, ImGuiHelper.Color(new Vector4(0f, 0f, 0f, 0.9f)), hint);
            draw.AddText(hintPos, ImGuiHelper.Color(new Vector4(0.75f, 0.9f, 1f, 0.95f)), hint);

            ImGui.SetNextWindowPos(fixedTopLeft, ImGuiCond.Always);
            ImGui.SetNextWindowSize(new Vector2(rowWidth, rowHeight), ImGuiCond.Always);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);

            if (!ImGui.Begin(
                    $"###PlayerBuffBarDummy_{windowId}",
                    ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                    ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoBackground))
            {
                ImGui.PopStyleColor();
                ImGui.PopStyleVar(2);
                ImGui.End();
                return;
            }

            ImGui.InvisibleButton("##drag", new Vector2(rowWidth, rowHeight));
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                fixedTopLeft += ImGui.GetIO().MouseDelta;
            }

            var saveSettings = ImGui.IsItemDeactivated();
            ImGui.End();
            ImGui.PopStyleColor();
            ImGui.PopStyleVar(2);

            if (saveSettings)
            {
                this.SaveSettings();
            }
        }

        private void DrawResourceIconCell(ImDrawListPtr draw, Vector2 pos, BuffDisplayEntry entry, float size)
        {
            var rectMin = pos;
            var rectMax = pos + new Vector2(size, size);
            var alpha = entry.IsActive ? 1f : this.Settings.InactiveIconAlpha;
            var borderColor = this.GetResourceBorderColor(entry.WatchId, alpha);
            var tint = ImGuiHelper.Color(new Vector4(1f, 1f, 1f, alpha));

            draw.AddRectFilled(rectMin, rectMax, ImGuiHelper.Color(new Vector4(0f, 0f, 0f, 0.55f * alpha)), 4f);
            draw.AddRect(rectMin, rectMax, ImGuiHelper.Color(borderColor), 4f, ImDrawFlags.None, entry.IsActive ? 2f : 1f);

            if (this.iconLoader.TryGetTexture(entry.WatchId, out var ptr, out _, out _))
            {
                draw.AddImage(ptr, rectMin, rectMax, Vector2.Zero, Vector2.One, tint);
            }
            else
            {
                var shortLabel = entry.WatchId switch
                {
                    "power_charge" => "P",
                    "frenzy_charge" => "F",
                    "endurance_charge" => "E",
                    "rage" => "R",
                    _ => "?",
                };

                var textSize = ImGui.CalcTextSize(shortLabel);
                draw.AddText(
                    rectMin + new Vector2((size - textSize.X) * 0.5f, (size - textSize.Y) * 0.5f),
                    tint,
                    shortLabel);
            }

            var countLabel = Math.Max(0, entry.ChargeCount).ToString();
            var countSize = ImGui.CalcTextSize(countLabel);
            var textColor = entry.WatchId == "rage" ? this.Settings.RageTextColor : this.Settings.ChargeTextColor;
            var textPos = new Vector2(
                rectMin.X + (size - countSize.X) * 0.5f,
                rectMax.Y - countSize.Y - 1f);

            if (this.Settings.ShowResourceCountBackground)
            {
                var badgeHeight = MathF.Max(countSize.Y + 2f, 12f);
                var badgeMin = new Vector2(rectMin.X, rectMax.Y - badgeHeight);
                draw.AddRectFilled(badgeMin, rectMax, ImGuiHelper.Color(new Vector4(0f, 0f, 0f, 0.88f * alpha)), 2f);
                textPos = new Vector2(
                    rectMin.X + (size - countSize.X) * 0.5f,
                    badgeMin.Y + (badgeHeight - countSize.Y) * 0.5f);
            }
            else
            {
                draw.AddText(textPos + Vector2.One, ImGuiHelper.Color(new Vector4(0f, 0f, 0f, 0.9f * alpha)), countLabel);
            }

            draw.AddText(
                textPos,
                ImGuiHelper.Color(new Vector4(textColor.X, textColor.Y, textColor.Z, alpha)),
                countLabel);
        }

        private Vector4 GetResourceBorderColor(string watchId, float alpha) => watchId switch
        {
            "power_charge" => new Vector4(0.35f, 0.55f, 1f, alpha),
            "frenzy_charge" => new Vector4(0.35f, 0.95f, 0.45f, alpha),
            "endurance_charge" => new Vector4(0.95f, 0.35f, 0.3f, alpha),
            "rage" => new Vector4(this.Settings.RageTextColor.X, this.Settings.RageTextColor.Y, this.Settings.RageTextColor.Z, alpha),
            _ => new Vector4(0.6f, 0.6f, 0.6f, alpha),
        };

        private void DrawBuffIconCell(ImDrawListPtr draw, Vector2 pos, BuffDisplayEntry entry, float size)
        {
            var rectMin = pos;
            var rectMax = pos + new Vector2(size, size);
            var alpha = entry.IsActive ? 1f : this.Settings.InactiveIconAlpha;
            var tint = ImGuiHelper.Color(new Vector4(1f, 1f, 1f, alpha));

            draw.AddRectFilled(rectMin, rectMax, ImGuiHelper.Color(new Vector4(0f, 0f, 0f, 0.45f * alpha)), 4f);

            if (this.iconLoader.TryGetTexture(entry.WatchId, out var ptr, out _, out _))
            {
                draw.AddImage(ptr, rectMin, rectMax, Vector2.Zero, Vector2.One, tint);
            }
            else
            {
                var fallback = entry.WatchId.Length >= 2 ? entry.WatchId[..2].ToUpperInvariant() : "?";
                var textSize = ImGui.CalcTextSize(fallback);
                draw.AddText(
                    rectMin + new Vector2((size - textSize.X) * 0.5f, (size - textSize.Y) * 0.5f),
                    tint,
                    fallback);
            }

            if (entry.IsActive)
            {
                draw.AddRect(rectMin, rectMax, ImGuiHelper.Color(this.Settings.ActiveColor), 4f, ImDrawFlags.None, 2f);
            }
            else
            {
                draw.AddRect(rectMin, rectMax, ImGuiHelper.Color(new Vector4(0.4f, 0.4f, 0.4f, 0.8f)), 4f, ImDrawFlags.None, 1f);
            }

            if (entry.IsActive && this.Settings.ShowStacks && entry.Stacks > 1)
            {
                this.DrawStackBadge(draw, rectMin, rectMax, entry.Stacks, alpha);
            }

            if (entry.IsActive && this.Settings.ShowDurations && entry.HasDuration)
            {
                var label = FormatSeconds(entry.TimeLeft);
                var textSize = ImGui.CalcTextSize(label);
                var textPos = new Vector2(pos.X + (size - textSize.X) * 0.5f, pos.Y + size + 1f);
                draw.AddText(textPos + Vector2.One, ImGuiHelper.Color(new Vector4(0f, 0f, 0f, 0.9f)), label);
                draw.AddText(textPos, ImGuiHelper.Color(this.GetBuffTextColor(alpha)), label);
            }
        }

        private void DrawStackBadge(ImDrawListPtr draw, Vector2 rectMin, Vector2 rectMax, int stacks, float alpha)
        {
            var label = stacks.ToString();
            var textSize = ImGui.CalcTextSize(label);
            var badgeHeight = MathF.Max(textSize.Y + 2f, 12f);
            var badgeMin = new Vector2(rectMin.X, rectMax.Y - badgeHeight);
            var textPos = this.Settings.ShowResourceCountBackground
                ? new Vector2(
                    rectMin.X + (rectMax.X - rectMin.X - textSize.X) * 0.5f,
                    badgeMin.Y + (badgeHeight - textSize.Y) * 0.5f)
                : new Vector2(
                    rectMin.X + (rectMax.X - rectMin.X - textSize.X) * 0.5f,
                    rectMax.Y - textSize.Y - 1f);

            if (this.Settings.ShowResourceCountBackground)
            {
                draw.AddRectFilled(badgeMin, rectMax, ImGuiHelper.Color(new Vector4(0f, 0f, 0f, 0.88f * alpha)), 2f);
            }
            else
            {
                draw.AddText(textPos + Vector2.One, ImGuiHelper.Color(new Vector4(0f, 0f, 0f, 0.9f * alpha)), label);
            }

            draw.AddText(textPos, ImGuiHelper.Color(this.GetBuffTextColor(alpha)), label);
        }

        private Vector4 GetBuffTextColor(float alpha) =>
            new(this.Settings.BuffTextColor.X, this.Settings.BuffTextColor.Y, this.Settings.BuffTextColor.Z, alpha);

        private List<BuffDisplayEntry> BuildChargeIconEntries(Stats? stats)
        {
            var row = new List<BuffDisplayEntry>();
            if (stats == null)
            {
                return row;
            }

            if (this.Settings.ShowCharges)
            {
                this.AddResourceEntryIfVisible(row, "power_charge", GetStat(stats, GameStats.current_power_charges));
                this.AddResourceEntryIfVisible(row, "frenzy_charge", GetStat(stats, GameStats.current_frenzy_charges));
                this.AddResourceEntryIfVisible(row, "endurance_charge", GetStat(stats, GameStats.current_endurance_charges));
            }

            if (this.Settings.ShowRage)
            {
                this.AddResourceEntryIfVisible(row, "rage", GetStat(stats, GameStats.current_rage));
            }

            return row;
        }

        private void AddResourceEntryIfVisible(List<BuffDisplayEntry> row, string watchId, int count)
        {
            if (this.Settings.HideEmptyResources && count <= 0)
            {
                return;
            }

            row.Add(this.CreateChargeEntry(watchId, count));
        }

        private BuffDisplayEntry CreateChargeEntry(string watchId, int count) => new()
        {
            WatchId = watchId,
            IsActive = count > 0,
            ChargeCount = count,
            Stacks = count,
        };

        private void DrawTextHud(Vector2 healthAnchor, string resourceLine, Dictionary<string, ActiveBuffInfo> activeLookup)
        {
            var draw = GetHudDrawList();
            var maxWidth = this.Settings.MaxBarWidth;
            var pad = 4f;

            if (!string.IsNullOrEmpty(resourceLine) && (this.Settings.ShowCharges || this.Settings.ShowRage))
            {
                var size = ImGui.CalcTextSize(resourceLine);
                var rowWidth = size.X + pad * 2f;
                var topLeft = this.ResolveBarTopLeft(
                    healthAnchor,
                    this.Settings.ResourceAnchorToHealthBar,
                    this.Settings.ResourceScreenOffset,
                    this.Settings.ResourceFixedPosition,
                    rowWidth);
                var bgMin = topLeft;
                var bgMax = new Vector2(topLeft.X + MathF.Min(size.X + pad * 2f, maxWidth), topLeft.Y + size.Y + pad);
                draw.AddRectFilled(bgMin, bgMax, ImGuiHelper.Color(new Vector4(0f, 0f, 0f, 0.55f)), 4f);
                draw.AddText(new Vector2(topLeft.X + pad, topLeft.Y + pad * 0.5f), ImGuiHelper.Color(this.Settings.ChargeTextColor), resourceLine);
            }

            this.Settings.EnsureBuffBarSlots();
            for (var barIndex = 0; barIndex < this.Settings.BuffBars.Count; barIndex++)
            {
                var bar = this.Settings.BuffBars[barIndex];
                if (!bar.Enabled)
                {
                    continue;
                }

                var entries = this.BuildDisplayEntriesForBar(bar.Watchlist, activeLookup);
                if (entries.Count == 0)
                {
                    continue;
                }

                var buffRowWidth = MathF.Max(80f, this.Settings.MaxBarWidth * 0.35f);
                var buffTopLeft = this.ResolveBarTopLeft(
                    healthAnchor,
                    bar.AnchorToHealthBar,
                    bar.ScreenOffset,
                    bar.FixedPosition,
                    buffRowWidth);
                var x = buffTopLeft.X;
                var y = buffTopLeft.Y;
                if (this.Settings.DisplayMode == BuffBarDisplayMode.IconsAndText)
                {
                    y += bar.IconSize + bar.IconSpacing + 6f;
                }

                foreach (var entry in entries)
                {
                    var text = entry.Display;
                    var size = ImGui.CalcTextSize(text);
                    var chipWidth = MathF.Min(size.X + pad * 2f, maxWidth);
                    var chipHeight = size.Y + pad;
                    var color = entry.IsActive ? this.Settings.ActiveColor : this.Settings.InactiveColor;

                    var bgMin = new Vector2(x, y);
                    var bgMax = new Vector2(x + chipWidth, y + chipHeight);
                    draw.AddRectFilled(bgMin, bgMax, ImGuiHelper.Color(color), 4f);
                    draw.AddRect(bgMin, bgMax, ImGuiHelper.Color(new Vector4(0f, 0f, 0f, 0.35f)), 4f);
                    draw.AddText(new Vector2(x + pad, y + pad * 0.5f), ImGuiHelper.Color(this.GetBuffTextColor(entry.IsActive ? 1f : 0.75f)), text);

                    y += chipHeight + 2f;
                }
            }
        }

        private Dictionary<string, ActiveBuffInfo> BuildActiveBuffLookup(Buffs? buffs)
        {
            var activeByWatchId = new Dictionary<string, ActiveBuffInfo>(StringComparer.OrdinalIgnoreCase);
            var watchIds = this.Settings.GetAllWatchIds().ToList();
            if (buffs == null || watchIds.Count == 0)
            {
                return activeByWatchId;
            }

            foreach (var kv in buffs.StatusEffects)
            {
                foreach (var watchId in watchIds)
                {
                    if (!BuffIconCatalog.MatchesBuffKey(kv.Key, watchId))
                    {
                        continue;
                    }

                    var info = this.ToActiveBuffInfo(watchId, kv.Key, kv.Value);
                    if (!activeByWatchId.TryGetValue(watchId, out var existing) ||
                        info.Stacks > existing.Stacks ||
                        (info.Stacks == existing.Stacks && info.TimeLeft > existing.TimeLeft))
                    {
                        activeByWatchId[watchId] = info;
                    }
                }
            }

            return activeByWatchId;
        }

        private List<BuffDisplayEntry> BuildDisplayEntriesForBar(
            IReadOnlyList<string> watchlist,
            IReadOnlyDictionary<string, ActiveBuffInfo> activeByWatchId)
        {
            var result = new List<BuffDisplayEntry>();
            foreach (var watchId in watchlist)
            {
                if (string.IsNullOrWhiteSpace(watchId) || BuffIconCatalog.IsReservedResourceWatchId(watchId))
                {
                    continue;
                }

                var trimmed = watchId.Trim();
                if (activeByWatchId.TryGetValue(trimmed, out var active))
                {
                    result.Add(new BuffDisplayEntry
                    {
                        WatchId = trimmed,
                        IsActive = true,
                        Display = this.FormatActiveLabel(trimmed, active),
                        Stacks = active.Stacks,
                        TimeLeft = active.TimeLeft,
                        HasDuration = active.HasDuration,
                        ChargeCount = -1,
                    });
                }
                else if (this.Settings.ShowInactiveWatchlist)
                {
                    result.Add(new BuffDisplayEntry
                    {
                        WatchId = trimmed,
                        IsActive = false,
                        Display = this.FormatInactiveLabel(trimmed),
                        ChargeCount = -1,
                    });
                }
            }

            return result;
        }

        private ActiveBuffInfo ToActiveBuffInfo(string watchId, string rawKey, StatusEffectStruct effect)
        {
            var stacks = Math.Max(0, (int)effect.Charges);
            var timeLeft = effect.TimeLeft;
            var total = effect.TotalTime;
            var timeLeftFinite = !(float.IsNaN(timeLeft) || float.IsInfinity(timeLeft));
            var totalFinite = !(float.IsNaN(total) || float.IsInfinity(total));
            float? duration = timeLeftFinite && totalFinite && timeLeft > 0f ? timeLeft : null;

            return new ActiveBuffInfo
            {
                WatchId = watchId,
                RawKey = rawKey,
                Stacks = stacks,
                TimeLeft = duration ?? 0f,
                HasDuration = duration.HasValue,
            };
        }

        private string FormatActiveLabel(string watchId, ActiveBuffInfo info)
        {
            var label = PrettyName(watchId);
            var parts = new List<string> { label };

            if (this.Settings.ShowStacks && info.Stacks > 1)
            {
                parts.Add($"x{info.Stacks}");
            }

            if (this.Settings.ShowDurations && info.HasDuration)
            {
                parts.Add(FormatSeconds(info.TimeLeft));
            }

            return string.Join(" ", parts);
        }

        private string FormatInactiveLabel(string watchId) =>
            this.PluginText.F("hud.inactive_suffix", "{0} - off", PrettyName(watchId));

        private string BuildResourceLine(Stats? stats)
        {
            if (stats == null)
            {
                return string.Empty;
            }

            var parts = new List<string>();

            if (this.Settings.ShowCharges)
            {
                var power = GetStat(stats, GameStats.current_power_charges);
                var frenzy = GetStat(stats, GameStats.current_frenzy_charges);
                var endurance = GetStat(stats, GameStats.current_endurance_charges);
                if (!this.Settings.HideEmptyResources || power > 0 || frenzy > 0 || endurance > 0)
                {
                    if (!this.Settings.HideEmptyResources || power > 0)
                    {
                        parts.Add($"P:{power}");
                    }

                    if (!this.Settings.HideEmptyResources || frenzy > 0)
                    {
                        parts.Add($"F:{frenzy}");
                    }

                    if (!this.Settings.HideEmptyResources || endurance > 0)
                    {
                        parts.Add($"E:{endurance}");
                    }
                }
            }

            if (this.Settings.ShowRage)
            {
                var rage = GetStat(stats, GameStats.current_rage);
                if (!this.Settings.HideEmptyResources || rage > 0)
                {
                    parts.Add(this.PluginText.F("hud.rage_count", "Rage:{0}", rage));
                }
            }

            return parts.Count == 0 ? string.Empty : string.Join("  |  ", parts);
        }

        private void DumpPlayerBuffs()
        {
            var dumpPath = Path.Join(this.DllDirectory, "player_buff_dump.txt");
            try
            {
                if (Core.States.GameCurrentState is not (GameStateTypes.InGameState or GameStateTypes.EscapeState))
                {
                    this.dumpStatusLine = this.PluginText.T("status.dump_failed_state", "Dump failed: enter a map or town first.");
                    return;
                }

                var player = Core.States.InGameStateObject.CurrentAreaInstance.Player;
                if (!player.TryGetComponent<Buffs>(out var buffs, true))
                {
                    this.dumpStatusLine = this.PluginText.T("status.dump_failed_buffs", "Dump failed: player buffs not readable.");
                    return;
                }

                var sb = new StringBuilder();
                sb.AppendLine("=== Player buff dump ===");
                foreach (var kv in buffs.StatusEffects.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
                {
                    sb.AppendLine($"{kv.Key} | charges={kv.Value.Charges} timeLeft={kv.Value.TimeLeft} total={kv.Value.TotalTime}");
                }

                File.WriteAllText(dumpPath, sb.ToString());
                var count = buffs.StatusEffects.Count;
                this.dumpStatusLine =
                    this.PluginText.F("status.dump_saved", "Dump saved ({0} buffs): Plugins/PlayerBuffBar/player_buff_dump.txt", count);
                Console.WriteLine($"[PlayerBuffBar] Wrote {count} buffs to {dumpPath}");
            }
            catch (Exception ex)
            {
                this.dumpStatusLine = this.PluginText.F("status.dump_failed_error", "Dump failed: {0}", ex.Message);
                Console.WriteLine($"[PlayerBuffBar] Buff dump failed: {ex}");
            }
        }

        private void SyncAllWatchlistBuffersFromSettings()
        {
            this.Settings.EnsureBuffBarSlots();
            for (var i = 0; i < PlayerBuffBarSettings.MaxBuffBars; i++)
            {
                this.SyncWatchlistBufferFromSettings(i);
            }
        }

        private void SyncWatchlistBufferFromSettings(int barIndex)
        {
            this.Settings.EnsureBuffBarSlots();
            this.watchlistBuffers[barIndex] = string.Join(
                Environment.NewLine,
                this.Settings.BuffBars[barIndex].Watchlist.Where(id => !BuffIconCatalog.IsReservedResourceWatchId(id)));
        }

        private void ApplyWatchlistBufferIfNeeded()
        {
            for (var i = 0; i < PlayerBuffBarSettings.MaxBuffBars; i++)
            {
                if (!this.watchlistEditorDirtyPerBar[i] && !this.WatchlistBufferDiffersFromSettings(i))
                {
                    continue;
                }

                this.ApplyWatchlistBuffer(i);
                this.watchlistEditorDirtyPerBar[i] = false;
            }
        }

        private bool WatchlistBufferDiffersFromSettings(int barIndex)
        {
            var parsed = this.ParseWatchlistBuffer(barIndex);
            var barWatchlist = this.Settings.BuffBars[barIndex].Watchlist;
            if (parsed.Count != barWatchlist.Count)
            {
                return true;
            }

            for (var i = 0; i < parsed.Count; i++)
            {
                if (!string.Equals(parsed[i], barWatchlist[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private List<string> ParseWatchlistBuffer(int barIndex) => this.watchlistBuffers[barIndex]
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Where(s => !BuffIconCatalog.IsReservedResourceWatchId(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        private void ApplyWatchlistBuffer(int barIndex)
        {
            this.Settings.EnsureBuffBarSlots();
            if (!this.watchlistEditorDirtyPerBar[barIndex] &&
                string.IsNullOrWhiteSpace(this.watchlistBuffers[barIndex]) &&
                this.Settings.BuffBars[barIndex].Watchlist.Count > 0)
            {
                return;
            }

            this.Settings.BuffBars[barIndex].Watchlist = this.ParseWatchlistBuffer(barIndex);
            this.Settings.WatchlistUserConfigured = true;
        }

        private static int GetStat(Stats stats, GameStats stat)
        {
            if (stats.StatsChangedByBuffAndActions.TryGetValue(stat, out var value))
            {
                return value;
            }

            if (stats.StatsChangedByItems.TryGetValue(stat, out value))
            {
                return value;
            }

            return 0;
        }

        private static string FormatSeconds(float seconds) =>
            seconds >= 60f ? $"{(int)MathF.Ceiling(seconds / 60f)}m" : $"{MathF.Ceiling(seconds):0}s";

        private static string PrettyName(string raw) =>
            string.Join(' ', raw.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

        private void DrawGeneralSettings()
        {
            ImGui.SeparatorText(this.PluginText.T("section.general", "General"));

            var fieldWidth = ImGui.GetContentRegionAvail().X;

            ImGui.Checkbox(this.PluginText.Label("settings.show_overlay", "Show overlay", "PlayerBuffBarShowOverlay"), ref this.Settings.ShowOverlay);

            ImGui.Text(this.PluginText.T("settings.display_mode", "Display mode"));
            ImGui.SetNextItemWidth(fieldWidth);
            var displayMode = (int)this.Settings.DisplayMode;
            var displayModePreview = displayMode switch
            {
                0 => this.PluginText.T("display_mode.icons", "Icons"),
                1 => this.PluginText.T("display_mode.text", "Text"),
                _ => this.PluginText.T("display_mode.icons_and_text", "Icons + text"),
            };
            if (ImGui.BeginCombo("##display_mode", displayModePreview))
            {
                if (ImGui.Selectable(this.PluginText.T("display_mode.icons", "Icons"), displayMode == 0))
                {
                    displayMode = 0;
                }

                if (ImGui.Selectable(this.PluginText.T("display_mode.text", "Text"), displayMode == 1))
                {
                    displayMode = 1;
                }

                if (ImGui.Selectable(this.PluginText.T("display_mode.icons_and_text", "Icons + text"), displayMode == 2))
                {
                    displayMode = 2;
                }

                ImGui.EndCombo();
            }

            if (displayMode != (int)this.Settings.DisplayMode)
            {
                this.Settings.DisplayMode = (BuffBarDisplayMode)displayMode;
            }

            ImGui.Checkbox(this.PluginText.Label("settings.hide_town_hideout", "Hide in town / hideout", "PlayerBuffBarHideTownHideout"), ref this.Settings.HideInTownOrHideout);
            ImGui.Checkbox(this.PluginText.Label("settings.hide_background", "Hide when game in background", "PlayerBuffBarHideBackground"), ref this.Settings.HideWhenGameInBackground);
            ImGui.Checkbox(this.PluginText.Label("settings.show_inactive_watchlist", "Show inactive watchlist buffs", "PlayerBuffBarShowInactiveWatchlist"), ref this.Settings.ShowInactiveWatchlist);
            ImGui.Checkbox(this.PluginText.Label("settings.show_durations", "Show durations", "PlayerBuffBarShowDurations"), ref this.Settings.ShowDurations);
            ImGui.Checkbox(this.PluginText.Label("settings.show_stacks", "Show stacks", "PlayerBuffBarShowStacks"), ref this.Settings.ShowStacks);
            ImGui.Checkbox(this.PluginText.Label("settings.auto_download_icons", "Auto-download poe2db icons", "PlayerBuffBarAutoDownloadIcons"), ref this.Settings.AutoDownloadWikiIcons);
            ImGui.Checkbox(this.PluginText.Label("settings.count_badge_background", "Count badge background", "PlayerBuffBarCountBadgeBackground"), ref this.Settings.ShowResourceCountBackground);
            ImGuiHelper.ToolTip(
                this.PluginText.T("settings.count_badge_background.tooltip", "When off, charge/rage counts and buff stack numbers are drawn as text only (no black bar on the icon)."));

            ImGui.Spacing();
        }

        private bool DrawSettingsSection(string key, string english, string id, ref bool isOpen)
        {
            var flags = ImGuiTreeNodeFlags.CollapsingHeader | ImGuiTreeNodeFlags.SpanFullWidth;
            if (isOpen)
            {
                flags |= ImGuiTreeNodeFlags.DefaultOpen;
            }

            var opened = ImGui.TreeNodeEx($"{this.PluginText.T(key, english)}##PlayerBuffBar_{id}", flags);
            isOpen = opened;
            return opened;
        }

        /// <summary>Above map overlays (e.g. Wraedar).</summary>
        private static ImDrawListPtr GetHudDrawList() => ImGui.GetForegroundDrawList();

        /// <summary>PoE or GameHelper overlay/settings focused â€” not e.g. Discord.</summary>
        private static bool IsGameOrOverlayForeground() =>
            Core.Process.Foreground ||
            Process.GetCurrentProcess().MainWindowHandle == GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private sealed class BuffDisplayEntry
        {
            public string WatchId = string.Empty;
            public bool IsActive;
            public string Display = string.Empty;
            public int Stacks;
            public float TimeLeft;
            public bool HasDuration;
            public int ChargeCount = -1;
        }

        private struct ActiveBuffInfo
        {
            public string WatchId;
            public string RawKey;
            public int Stacks;
            public float TimeLeft;
            public bool HasDuration;
        }
    }
}
