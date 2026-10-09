namespace LootTracker
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using System.Text.Json;
    using GameHelper;
    using GameHelper.RemoteEnums;
    using ImGuiNET;

    public sealed partial class LootTrackerCore
    {
        private CurrencyPriceBook? priceBook;
        private double lastPriceRefresh = -100;
        private double lastSessionTick = -1;
        private HudModel? cachedHudModel;
        private double nextHudModelRefresh;

        private void TickSessionClock(double now)
        {
            var elapsed = this.lastSessionTick < 0 ? 0 : now - this.lastSessionTick;
            this.lastSessionTick = now;
            // Includes hideout/menu time while the game is foreground; never includes offline time.
            if (!this.ledger.Session.Paused && elapsed is >= 0 and <= 2 && Core.Process.Foreground &&
                Core.States.GameCurrentState is GameStateTypes.InGameState or GameStateTypes.EscapeState)
                this.ledger.Session.SessionSeconds += elapsed;
        }

        private void RefreshPrices()
        {
            this.lastPriceRefresh = this.clock.Elapsed.TotalSeconds;
            this.cachedHudModel = null;
            try
            {
                var valueDir = Path.Combine(Path.GetDirectoryName(this.DllDirectory)!, "LootValue");
                using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(valueDir, "config", "settings.txt")));
                var league = settings.RootElement.GetProperty("League").GetString() ?? string.Empty;
                var source = settings.RootElement.GetProperty("PriceSource").GetInt32();
                var languages = Path.Combine(AppContext.BaseDirectory, "Localization");
                Dictionary<string, string>? ReadLanguage(string file) => JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(languages, file)));
                this.priceBook = new CurrencyPriceBook(File.ReadAllText(Path.Combine(valueDir, "price_cache.json")), league, source,
                    DateTimeOffset.UtcNow, ReadLanguage("en-US.json"), ReadLanguage("zh-Hant.json"));
            }
            catch
            {
                // Missing/partial cache, stale data and wrong leagues must never look like zero-value loot.
                this.priceBook = null;
            }
        }

        private HudModel CreateHudModel()
        {
            var session = this.ledger.Session;
            var totals = TrackingLedger.Totals(session);
            var value = this.priceBook?.Estimate(totals);
            var hasItems = totals.Values.Any(v => v.Count > 0);
            var unknown = value?.UnpricedTypes > 0 || this.priceBook == null && hasItems;
            var sum = value?.Exalted ?? 0;
            var seconds = Math.Max(session.SessionSeconds, this.ledger.TotalSeconds);
            var unit = this.Settings.ShowValuesInDivine ? "Div" : "Ex";
            var divisor = this.Settings.ShowValuesInDivine ? this.priceBook?.DivineToExalted ?? 1 : 1;
            string Number(double ex, bool incomplete, bool hasCurrency) =>
                this.priceBook == null && hasCurrency ? "—" : (ex / divisor).ToString("0.0") + (incomplete ? "*" : "");
            var rows = session.Maps.AsEnumerable().Reverse().Select(map =>
            {
                var estimate = this.priceBook?.Estimate(map.Currency);
                return new HudRow(map.Name, ShortDuration(map.ActiveSeconds),
                    Number(estimate?.Exalted ?? 0, estimate?.UnpricedTypes > 0, map.Currency.Count > 0) + " " + unit);
            }).ToArray();
            return new HudModel
            {
                MapCount = session.Maps.Count,
                AverageTime = ShortDuration(session.Maps.Count == 0 ? 0 : this.ledger.TotalSeconds / session.Maps.Count),
                AverageValue = Number(session.Maps.Count == 0 ? 0 : sum / session.Maps.Count, unknown, hasItems) + " " + unit,
                Total = Number(sum, unknown, hasItems),
                Hourly = Number(seconds == 0 ? 0 : sum * 3600 / seconds, unknown, hasItems) + this.PluginText.T("hud.per_hour", " / h"),
                SessionTime = ShortDuration(seconds), Unit = unit, IncompletePrice = unknown, Rows = rows,
            };
        }

        private void DrawCompactOverlay()
        {
            var scale = Math.Clamp(this.Settings.HudScale, .65f, 1.8f);
            var display = ImGui.GetIO().DisplaySize;
            scale = Math.Min(scale, display.X / 680f);
            var width = Math.Clamp(this.Settings.HudWidth * scale, 680 * scale, Math.Max(680 * scale, display.X));
            var position = new Vector2(Math.Clamp((display.X - width) / 2 + this.Settings.HudHorizontalOffset, 0, Math.Max(0, display.X - width)),
                Math.Max(0, display.Y - 124 * scale - this.Settings.HudBottomOffset));
            if (this.cachedHudModel == null || this.clock.Elapsed.TotalSeconds >= this.nextHudModelRefresh)
            {
                this.cachedHudModel = this.CreateHudModel();
                this.nextHudModelRefresh = this.clock.Elapsed.TotalSeconds + .5;
            }
            CompactHud.Draw(this.cachedHudModel, position, width, scale, this.Settings.HudOpacity, this.Settings.LockHud,
                (key, fallback) => this.PluginText.T(key, fallback));
        }

        private void DrawHudSettings()
        {
            ImGui.Checkbox(this.PluginText.Label("settings.compact", "Reference-style compact bar", "TrackerCompact"), ref this.Settings.CompactHud);
            ImGui.Checkbox(this.PluginText.Label("settings.preview", "Preview bar outside the game (real data)", "TrackerPreview"), ref this.Settings.PreviewHud);
            ImGui.Checkbox(this.PluginText.Label("settings.lock", "Click-through bar (unlock to scroll map history)", "TrackerLock"), ref this.Settings.LockHud);
            ImGui.SliderFloat(this.PluginText.Label("settings.width", "Bar width", "TrackerWidth"), ref this.Settings.HudWidth, 680, 1200, "%.0f px");
            ImGui.SliderFloat(this.PluginText.Label("settings.scale", "Bar scale", "TrackerScale"), ref this.Settings.HudScale, .65f, 1.8f, "%.2f");
            ImGui.SliderFloat(this.PluginText.Label("settings.opacity", "Background opacity", "TrackerOpacity"), ref this.Settings.HudOpacity, .05f, 1f, "%.2f");
            ImGui.SliderFloat(this.PluginText.Label("settings.bottom", "Distance from bottom", "TrackerBottom"), ref this.Settings.HudBottomOffset, 0, 700, "%.0f px");
            ImGui.SliderFloat(this.PluginText.Label("settings.horizontal", "Horizontal offset", "TrackerHorizontal"), ref this.Settings.HudHorizontalOffset, -1000, 1000, "%.0f px");
            ImGui.Checkbox(this.PluginText.Label("settings.divine", "Display value in Divine instead of Exalted", "TrackerDivine"), ref this.Settings.ShowValuesInDivine);
            ImGui.TextWrapped(this.PluginText.T("settings.price_help", "Value uses LootValue's selected league/source cache (up to 24 hours old). Enable LootValue to refresh prices. A dash means no price data; * means an incomplete estimate. These are current estimated currency proceeds, not profit after map costs. Session time includes hideout time; average map time excludes it."));
            if (this.priceBook != null)
                ImGui.TextWrapped(this.PluginText.F("settings.price_status", "Prices: {0} · {1}", this.priceBook.League, this.priceBook.UpdatedUtc.ToLocalTime().ToString("MM-dd HH:mm")));
            else ImGui.TextWrapped(this.PluginText.T("settings.price_missing", "Waiting for matching, current LootValue prices"));
            if (ImGui.Button(this.PluginText.Label("action.read_prices", "Read price cache again", "TrackerReadPrices"))) this.RefreshPrices();
        }

        private static string ShortDuration(double seconds)
        {
            var value = TimeSpan.FromSeconds(seconds);
            return value.TotalHours >= 1 ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}" : $"{(int)value.TotalMinutes:00}:{value.Seconds:00}";
        }
    }
}
