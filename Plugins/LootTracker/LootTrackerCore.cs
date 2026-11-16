// <copyright file="LootTrackerCore.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace LootTracker
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using GameHelper;
    using GameHelper.Plugin;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using ImGuiNET;
    using Newtonsoft.Json;

    /// <summary>Area timers and observed backpack currency increases, with durable session history.</summary>
    public sealed partial class LootTrackerCore : PCore<LootTrackerSettings>
    {
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private TrackingLedger ledger = new();
        private double lastSample = -1;
        private double lastSave;
        private bool showHistory;
        private string statusKey = "status.waiting";
        private string statusFallback = "Waiting for a combat area";
        private string lastMessage = string.Empty;
        private string[] historyFiles = Array.Empty<string>();
        private TrackingSession? viewedSession;
        private bool canSaveActive = true;

        private string ConfigDirectory => Path.Combine(this.DllDirectory, "config");
        private string SettingsPath => Path.Combine(this.ConfigDirectory, "settings.json");
        private string ActivePath => Path.Combine(this.ConfigDirectory, "active.json");
        private string HistoryDirectory => Path.Combine(this.ConfigDirectory, "sessions");

        public override void OnEnable(bool isGameOpened)
        {
            this.canSaveActive = true;
            try
            {
                if (File.Exists(this.SettingsPath))
                    this.Settings = JsonConvert.DeserializeObject<LootTrackerSettings>(File.ReadAllText(this.SettingsPath)) ?? new();
                this.Settings.MaximumOverlayRows = Math.Clamp(this.Settings.MaximumOverlayRows, 1, 30);
            }
            catch (Exception ex) { this.ReportError(ex); }

            try
            {
                this.ledger = new TrackingLedger(File.Exists(this.ActivePath) ? SessionStore.Read(this.ActivePath) : null);
            }
            catch (Exception ex)
            {
                // Preserve the exact unreadable file before allowing subsequent auto-saves.
                var originalPreserved = false;
                try
                {
                    File.Copy(this.ActivePath, this.ActivePath + ".unreadable-" + Guid.NewGuid().ToString("N"));
                    originalPreserved = true;
                    this.ledger = new TrackingLedger(File.Exists(this.ActivePath + ".bak")
                        ? SessionStore.Read(this.ActivePath + ".bak") : null);
                }
                catch (Exception recoveryError)
                {
                    // If the unreadable original could not be preserved, do not overwrite it.
                    this.canSaveActive = originalPreserved;
                    this.ReportError(recoveryError);
                    this.ledger = new TrackingLedger();
                }

                this.ReportError(ex);
            }

            this.ledger.Interrupt();
            this.lastSample = -1;
            this.lastSave = this.clock.Elapsed.TotalSeconds;
            Core.States.InGameStateObject.CurrentAreaInstance.ServerDataObject.BackpackInventory.AutomaticUpdatesEnabled = true;
            this.RefreshHistory();
            this.lastSessionTick = -1;
            this.RefreshPrices();
        }

        public override void OnDisable()
        {
            this.ledger.Interrupt();
            Core.States.InGameStateObject.CurrentAreaInstance.ServerDataObject.BackpackInventory.AutomaticUpdatesEnabled = false;
            this.SaveSettings();
        }

        public override void SaveSettings()
        {
            try
            {
                SessionStore.AtomicWrite(this.SettingsPath, JsonConvert.SerializeObject(this.Settings, Formatting.Indented));
                if (this.canSaveActive) SessionStore.Write(this.ActivePath, this.ledger.Session);
            }
            catch (Exception ex) { this.ReportError(ex); }
        }

        public override void DrawSettings()
        {
            ImGui.Checkbox(this.PluginText.Label("settings.overlay", "Show tracking overlay", "LootTrackerOverlay"), ref this.Settings.ShowOverlay);
            ImGui.Checkbox(this.PluginText.Label("settings.town", "Show summary in towns and hideouts", "LootTrackerTown"), ref this.Settings.ShowInTown);
            this.DrawHudSettings();
            ImGui.SliderInt(this.PluginText.Label("settings.rows", "Maximum currency rows in overlay", "LootTrackerRows"), ref this.Settings.MaximumOverlayRows, 1, 30);
            ImGui.TextWrapped(this.PluginText.T("settings.scope", "Counts positive changes in total backpack currency while sampled in combat areas. Existing items, towns, hideouts, loading, pause, lost focus and large panels are excluded from counting. The first safe sample after a gap establishes a new baseline."));
            ImGui.TextWrapped(this.PluginText.T("settings.limits", "This is observed backpack income, not total ground drops or profit. Spending between samples can hide income; dropping and picking up currency again can count again. Items gained while excluded are not recovered later. Gold and other wallet balances are not backpack items. Names come from the game client."));
            ImGui.TextWrapped(this.PluginText.T("settings.timer", "The timer excludes towns, hideouts, loading, Escape, manual pause and lost focus. Returning to the same area instance continues its timer. Campaign combat areas are also recorded. Restarting never counts offline time."));
            ImGui.TextWrapped(this.PluginText.T("settings.save", "Sessions save automatically every 10 seconds and when disabled. New session archives the current record. History is kept until you remove its files yourself; CSV exports contain per-area currency quantities."));
            this.DrawControls();
            this.DrawSummary(this.ledger.Session, false);
            if (!string.IsNullOrEmpty(this.lastMessage)) ImGui.TextWrapped(this.lastMessage);
        }

        public override void DrawUI()
        {
            var now = this.clock.Elapsed.TotalSeconds;
            this.TickSessionClock(now);
            if (now - this.lastPriceRefresh >= 10) this.RefreshPrices();
            if (this.lastSample < 0 || now - this.lastSample >= 0.25)
            {
                this.lastSample = now;
                try { this.Sample(now); }
                catch (Exception ex)
                {
                    this.ledger.Interrupt();
                    this.SetStatus("status.invalid", "Waiting for a consistent backpack sample");
                    this.ReportError(ex);
                }
            }

            if (this.canSaveActive && now - this.lastSave >= 10)
            {
                this.lastSave = now;
                try { SessionStore.Write(this.ActivePath, this.ledger.Session); }
                catch (Exception ex) { this.ReportError(ex); }
            }

            this.DrawHistoryWindow();
            if (this.Settings.CompactHud && this.Settings.PreviewHud)
            {
                this.DrawCompactOverlay();
                return;
            }
            if (!this.Settings.ShowOverlay || !Core.Process.Foreground ||
                Core.States.GameCurrentState is not (GameStateTypes.InGameState or GameStateTypes.EscapeState)) return;
            var area = Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails;
            if (!this.Settings.ShowInTown && (area.IsTown || area.IsHideout)) return;

            if (this.Settings.CompactHud)
            {
                this.DrawCompactOverlay();
                return;
            }

            ImGui.SetNextWindowPos(new Vector2(30, 140), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSize(new Vector2(470, 0), ImGuiCond.FirstUseEver);
            if (ImGui.Begin(this.PluginText.Title("window.title", "Area timer and currency", "LootTrackerOverlay"), ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextUnformatted(this.PluginText.T(this.statusKey, this.statusFallback));
                if (this.ledger.CurrentMap is { } map)
                    ImGui.TextUnformatted(this.PluginText.F("summary.area", "Latest area: {0} · {1}", map.Name, Duration(map.ActiveSeconds)));
                this.DrawControls();
                this.DrawSummary(this.ledger.Session, true);
                if (this.ledger.CurrentMap is { } currentMap && ImGui.CollapsingHeader(this.PluginText.Title("summary.current_area", "Latest area currency", "TrackerLatestArea")))
                {
                    ImGui.PushID("CurrentMapCurrency");
                    this.DrawCurrency(currentMap.Currency, currentMap.ActiveSeconds, this.Settings.MaximumOverlayRows);
                    ImGui.PopID();
                }
            }

            ImGui.End();
        }

        private void Sample(double now)
        {
            var state = Core.States.InGameStateObject;
            var area = state.CurrentWorldInstance.AreaDetails;
            var instance = state.CurrentAreaInstance;
            if (this.ledger.Session.Paused)
            {
                this.ledger.Interrupt();
                this.SetStatus("status.paused", "Manually paused");
                return;
            }

            if (Core.States.GameCurrentState != GameStateTypes.InGameState || !Core.Process.Foreground)
            {
                this.ledger.Interrupt();
                this.SetStatus("status.inactive", "Paused: loading, game menu or lost focus");
                return;
            }

            if (area.Address == IntPtr.Zero || instance.Address == IntPtr.Zero ||
                area.IsTown || area.IsHideout || string.IsNullOrWhiteSpace(area.Id) ||
                string.IsNullOrWhiteSpace(instance.AreaHash) || instance.AreaHash == "0" || !instance.Player.IsValid)
            {
                this.ledger.Interrupt();
                this.SetStatus("status.waiting", "Waiting for a combat area");
                return;
            }

            IReadOnlyList<CurrencyObservation>? currency = null;
            IReadOnlyList<Inventory.CurrencyStackSnapshot> snapshot = Array.Empty<Inventory.CurrencyStackSnapshot>();
            var panelOpen = state.GameUi.IsAnyLargePanelOpen;
            var consistent = !panelOpen && state.GameUi.Address != IntPtr.Zero &&
                instance.ServerDataObject.BackpackInventory.TryGetCurrencySnapshot(out snapshot);
            if (consistent)
            {
                // A second call is deliberately avoided: keep one coherent snapshot per observation.
                currency = snapshot.Select(item => new CurrencyObservation(item.Metadata, item.Name, item.Count)).ToArray();
            }

            var hadBaseline = this.ledger.HasBaseline;
            this.ledger.Observe(new AreaIdentity(instance.AreaHash, area.Id, area.Name), true, currency, now, DateTimeOffset.UtcNow);
            if (panelOpen) this.SetStatus("status.panel", "Timer running; currency sampling paused while a panel is open");
            else if (!consistent) this.SetStatus("status.invalid", "Waiting for a consistent backpack sample");
            else if (!hadBaseline) this.SetStatus("status.baseline", "Backpack baseline established");
            else this.SetStatus("status.running", "Recording observed backpack currency increases");
        }

        private void DrawControls()
        {
            var pause = this.ledger.Session.Paused;
            if (ImGui.Button(pause
                    ? this.PluginText.Label("action.resume", "Resume", "TrackerPause")
                    : this.PluginText.Label("action.pause", "Pause", "TrackerPause")))
                this.ledger.SetPaused(!pause);
            ImGui.SameLine();
            if (ImGui.Button(this.PluginText.Label("action.new", "New session", "TrackerNew"))) this.NewSession();
            ImGui.SameLine();
            if (ImGui.Button(this.PluginText.Label("action.history", "History", "TrackerHistory")))
            {
                this.showHistory = true;
                this.RefreshHistory();
            }

            ImGui.SameLine();
            if (ImGui.Button(this.PluginText.Label("action.export", "Export CSV", "TrackerExport"))) this.Export(this.ledger.Session);
        }

        private void DrawSummary(TrackingSession session, bool compact)
        {
            ImGui.TextUnformatted(this.PluginText.F("summary.session", "Session: {0} · Recorded areas: {1}", Duration(session.Maps.Sum(map => map.ActiveSeconds)), session.Maps.Count));
            ImGui.TextUnformatted(this.PluginText.T("summary.income", "Observed backpack currency increases"));
            var totals = TrackingLedger.Totals(session);
            this.DrawCurrency(totals, session.Maps.Sum(map => map.ActiveSeconds), compact ? this.Settings.MaximumOverlayRows : int.MaxValue);
        }

        private void DrawCurrency(Dictionary<string, CurrencyTotal> totals, double activeSeconds, int limit)
        {
            if (totals.Count == 0)
            {
                ImGui.TextUnformatted(this.PluginText.T("summary.empty", "No currency increases recorded yet"));
                return;
            }

            if (ImGui.BeginTable("##Currency", 3, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg))
            {
                ImGui.TableSetupColumn(this.PluginText.T("column.currency", "Currency"));
                ImGui.TableSetupColumn(this.PluginText.T("column.count", "Quantity"));
                ImGui.TableSetupColumn(this.PluginText.T("column.rate", "Per active hour"));
                ImGui.TableHeadersRow();
                foreach (var item in totals.OrderByDescending(pair => pair.Value.Count).Take(limit))
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(item.Value.Name);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(item.Value.Count.ToString("N0"));
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(activeSeconds > 0 ? (item.Value.Count * 3600d / activeSeconds).ToString("N1") : "—");
                }

                ImGui.EndTable();
            }

            if (totals.Count > limit)
                ImGui.TextUnformatted(this.PluginText.F("summary.more", "{0} more currency types in history", totals.Count - limit));
        }

        private void DrawHistoryWindow()
        {
            if (!this.showHistory) return;
            ImGui.SetNextWindowSize(new Vector2(680, 580), ImGuiCond.FirstUseEver);
            if (ImGui.Begin(this.PluginText.Title("history.title", "Currency session history", "LootTrackerHistory"), ref this.showHistory))
            {
                if (ImGui.Button(this.PluginText.Label("action.refresh", "Refresh", "TrackerRefresh"))) this.RefreshHistory();
                ImGui.SameLine();
                if (ImGui.Button(this.PluginText.Label("history.current", "Current session", "TrackerCurrent"))) this.viewedSession = null;
                ImGui.TextUnformatted(this.PluginText.F("history.count", "Archived sessions: {0}", this.historyFiles.Length));
                foreach (var path in this.historyFiles)
                {
                    if (ImGui.Selectable(Path.GetFileNameWithoutExtension(path), false))
                    {
                        try { this.viewedSession = SessionStore.Read(path); }
                        catch (Exception ex) { this.ReportError(ex); }
                    }
                }

                ImGui.Separator();
                var selected = this.viewedSession ?? this.ledger.Session;
                ImGui.TextUnformatted(selected.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
                this.DrawSummary(selected, false);
                if (ImGui.Button(this.PluginText.Label("history.export", "Export selected session", "TrackerExportSelected"))) this.Export(selected);
                foreach (var map in selected.Maps.AsEnumerable().Reverse())
                {
                    if (ImGui.CollapsingHeader($"{map.Name} · {Duration(map.ActiveSeconds)}###{map.AreaKey}"))
                    {
                        ImGui.PushID(map.AreaKey);
                        ImGui.TextUnformatted(map.FirstSeenUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
                        this.DrawCurrency(map.Currency, map.ActiveSeconds, int.MaxValue);
                        ImGui.PopID();
                    }
                }

                if (!string.IsNullOrEmpty(this.lastMessage)) ImGui.TextWrapped(this.lastMessage);
            }

            ImGui.End();
        }

        private void NewSession()
        {
            try
            {
                var state = this.ledger.Session;
                var filename = state.StartedUtc.ToLocalTime().ToString("yyyyMMdd_HHmmss") + "_" + state.SessionId + ".json";
                SessionStore.Write(Path.Combine(this.HistoryDirectory, filename), state);
                var next = new TrackingLedger();
                if (!this.canSaveActive)
                {
                    // A fresh session is safe only after preserving the unreadable active file.
                    File.Copy(this.ActivePath, this.ActivePath + ".unreadable-" + Guid.NewGuid().ToString("N"));
                }

                SessionStore.Write(this.ActivePath, next.Session);
                this.canSaveActive = true;
                this.ledger = next;
                this.lastSessionTick = -1;
                this.viewedSession = null;
                this.RefreshHistory();
                this.lastMessage = this.PluginText.T("message.archived", "Previous session archived; new session started.");
            }
            catch (Exception ex) { this.ReportError(ex); }
        }

        private void RefreshHistory()
        {
            try
            {
                this.historyFiles = Directory.Exists(this.HistoryDirectory)
                    ? Directory.GetFiles(this.HistoryDirectory, "*.json").OrderByDescending(path => path, StringComparer.Ordinal).ToArray()
                    : Array.Empty<string>();
            }
            catch (Exception ex) { this.ReportError(ex); }
        }

        private void Export(TrackingSession session)
        {
            try
            {
                var path = Path.Combine(this.ConfigDirectory, "exports", "currency_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".csv");
                SessionStore.ExportCsv(path, session, this.PluginText.T("csv.header", "Session start,Area,Area ID,Instance hash,Active seconds,Currency,Metadata,Quantity"));
                this.lastMessage = this.PluginText.F("message.exported", "Exported: {0}", path);
            }
            catch (Exception ex) { this.ReportError(ex); }
        }

        private void SetStatus(string key, string fallback)
        {
            this.statusKey = key;
            this.statusFallback = fallback;
        }

        private void ReportError(Exception ex)
        {
            this.lastMessage = this.PluginText.F("message.error", "Tracking file/read error: {0}", ex.Message);
            Console.WriteLine("[LootTracker] " + ex);
        }

        private static string Duration(double seconds)
        {
            var time = TimeSpan.FromSeconds(seconds);
            return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
        }
    }
}
