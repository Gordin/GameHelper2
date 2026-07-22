// <copyright file="LootValueCore.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace LootValue
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using System.Reflection;
    using System.Text.RegularExpressions;
    using GameHelper;
    using GameHelper.Plugin;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using GameOffsets.Natives;
    using GameOffsets.Objects.UiElement;
    using ImGuiNET;
    using Newtonsoft.Json;
    using Newtonsoft.Json.Linq;

    /// <summary>
    ///     LootValue plugin — prices ground, stash, and inventory items and draws their values in context.
    ///     Unidentified uniques are revealed by name via their icon art (same bridge as RitualHelper).
    /// </summary>
    public sealed class LootValueCore : PCore<LootValueSettings>
    {
        private const string ItemPathPrefix = "Metadata/Items";
        private const int UiElementItemAddressOffset = 0x4F8;
        private static readonly int[] CurrencyExchangeRootPath = { 114, 20, 6 };

        private readonly List<LootLabel> cachedLabels = new();
        private readonly Dictionary<uint, Tracked> trackWorld = new();
        private DateTime nextRecomputeUtc = DateTime.MinValue;

        private readonly List<string> diagSamples = new();
        private string diagSummary = string.Empty;
        private DateTime nextDiagUtc = DateTime.MinValue;

        // Loot-tag mode (anchors chips to the game's loot labels via a throttled UI-tree scan).
        private const int UiElementTextOffset = 0x390;
        private readonly List<TagChip> cachedTagChips = new();
        private readonly Dictionary<IntPtr, Tracked> trackTag = new();
        private DateTime nextTagScanUtc = DateTime.MinValue;
        private object? handleObj;
        private object? uiParentsObj;
        private MethodInfo? readUiOffsetMethod;
        private MethodInfo? readStdVectorMethod;
        private MethodInfo? readStdWStringStructMethod;
        private MethodInfo? readStdWStringMethod;
        private MethodInfo? readIntPtrMethod;
        private readonly HashSet<string> groundTagNames = new(StringComparer.OrdinalIgnoreCase);
        private SlotScanReport leftSlotReport = new(IntPtr.Zero);
        private SlotScanReport rightSlotReport = new(IntPtr.Zero);
        private List<SlotInfo> cachedLeftSlots = new();
        private List<SlotInfo> cachedRightSlots = new();
        private IntPtr cachedLeftPanelAddress;
        private IntPtr cachedRightPanelAddress;
        private DateTime nextSlotScanUtc = DateTime.MinValue;
        private readonly List<ExchangePriceLabel> cachedExchangeLabels = new();
        private DateTime nextExchangeScanUtc = DateTime.MinValue;

        private string SettingPathname => Path.Join(this.DllDirectory, "config", "settings.txt");

        /// <inheritdoc/>
        public override void OnEnable(bool isGameOpened)
        {
            var shouldMigrateStashSettings = true;
            if (File.Exists(this.SettingPathname))
            {
                try
                {
                    var settingsJson = File.ReadAllText(this.SettingPathname);
                    shouldMigrateStashSettings = JObject.Parse(settingsJson)[nameof(LootValueSettings.ShowStashOverlay)] == null;
                    this.Settings = JsonConvert.DeserializeObject<LootValueSettings>(settingsJson) ?? new LootValueSettings();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LootValue] Failed to load settings: {ex.Message}");
                    this.Settings = new LootValueSettings();
                }
            }

            if (shouldMigrateStashSettings && this.TryMigrateStashValueSettings())
            {
                this.SaveSettings();
            }

            PoeNinjaPriceFetcher.Configure(this.Settings.PriceSource, this.Settings.League ?? string.Empty, this.Settings.RefreshIntervalMin);
            PoeNinjaPriceFetcher.Initialize(this.DllDirectory);
        }

        private bool TryMigrateStashValueSettings()
        {
            var pluginsDirectory = Directory.GetParent(this.DllDirectory)?.FullName;
            if (pluginsDirectory == null) return false;

            foreach (var pluginName in new[] { "StashValueByZx0", "StashValue" })
            {
                var legacyPath = Path.Join(pluginsDirectory, pluginName, "config", "settings.txt");
                if (!File.Exists(legacyPath)) continue;

                try
                {
                    var legacy = JObject.Parse(File.ReadAllText(legacyPath));
                    this.Settings.ShowStashOverlay = legacy.Value<bool?>("ShowOverlay") ?? this.Settings.ShowStashOverlay;
                    this.Settings.ShowInventoryOverlay = legacy.Value<bool?>("ShowInventoryOverlay") ?? this.Settings.ShowInventoryOverlay;
                    this.Settings.HideSlotPricesOnHover = legacy.Value<bool?>("HidePriceOnHover") ?? this.Settings.HideSlotPricesOnHover;
                    this.Settings.ShowSlotDebugInfo = legacy.Value<bool?>("ShowDebugInfo") ?? this.Settings.ShowSlotDebugInfo;
                    this.Settings.SlotFontScale = legacy.Value<float?>("PriceFontScale") ?? this.Settings.SlotFontScale;
                    this.Settings.SlotOffsetX = legacy.Value<float?>("PriceOffsetX") ?? this.Settings.SlotOffsetX;
                    this.Settings.SlotOffsetY = legacy.Value<float?>("PriceOffsetY") ?? this.Settings.SlotOffsetY;
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LootValue] Failed to migrate {pluginName} settings: {ex.Message}");
                }
            }

            return false;
        }

        /// <inheritdoc/>
        public override void OnDisable()
        {
            this.cachedLabels.Clear();
            this.cachedTagChips.Clear();
            this.trackWorld.Clear();
            this.trackTag.Clear();
            this.nextRecomputeUtc = DateTime.MinValue;
            this.nextTagScanUtc = DateTime.MinValue;
            this.handleObj = null;
            this.uiParentsObj = null;
            this.readUiOffsetMethod = null;
            this.readStdVectorMethod = null;
            this.readStdWStringStructMethod = null;
            this.readStdWStringMethod = null;
            this.readIntPtrMethod = null;
            this.groundTagNames.Clear();
            this.cachedLeftSlots.Clear();
            this.cachedRightSlots.Clear();
            this.cachedLeftPanelAddress = IntPtr.Zero;
            this.cachedRightPanelAddress = IntPtr.Zero;
            this.nextSlotScanUtc = DateTime.MinValue;
            this.cachedExchangeLabels.Clear();
            this.nextExchangeScanUtc = DateTime.MinValue;
        }

        /// <inheritdoc/>
        public override void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(this.SettingPathname) ?? string.Empty);
                File.WriteAllText(this.SettingPathname, JsonConvert.SerializeObject(this.Settings, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LootValue] Failed to save settings: {ex.Message}");
            }
        }

        /// <inheritdoc/>
        public override void DrawSettings()
        {
            ImGui.Checkbox(this.PluginText.Label("settings.show_overlay", "Show value over ground items", "LootValueShowOverlay"), ref this.Settings.ShowOverlay);
            ImGui.Checkbox(this.PluginText.Label("settings.anchor_to_loot_tags", "Anchor to loot labels (no overlap when items pile up)", "LootValueAnchorToLootTags"), ref this.Settings.AnchorToLootTags);
            ImGui.Checkbox(this.PluginText.Label("settings.show_stash_overlay", "Show value over stash items", "LootValueShowStashOverlay"), ref this.Settings.ShowStashOverlay);
            ImGui.Checkbox(this.PluginText.Label("settings.show_inventory_overlay", "Show value over inventory items", "LootValueShowInventoryOverlay"), ref this.Settings.ShowInventoryOverlay);
            ImGui.Checkbox(this.PluginText.Label("settings.show_currency_exchange_overlay", "Show owned-stack values in Currency Exchange", "LootValueShowCurrencyExchangeOverlay"), ref this.Settings.ShowCurrencyExchangeOverlay);
            ImGui.Checkbox(this.PluginText.Label("settings.hide_slot_prices_on_hover", "Hide stash/inventory values while hovering an item", "LootValueHideSlotPricesOnHover"), ref this.Settings.HideSlotPricesOnHover);
            ImGui.Checkbox(this.PluginText.Label("settings.reveal_unidentified_uniques", "Reveal unidentified uniques (by art)", "LootValueRevealUnidentifiedUniques"), ref this.Settings.RevealUnidentifiedUniques);
            ImGui.Checkbox(this.PluginText.Label("settings.diagnostics_window", "Diagnostics window", "LootValueDiagnosticsWindow"), ref this.Settings.DiagnosticsMode);
            ImGui.Checkbox(this.PluginText.Label("settings.slot_diagnostics", "Stash/inventory slot diagnostics", "LootValueSlotDiagnostics"), ref this.Settings.ShowSlotDebugInfo);

            ImGui.Separator();
            ImGui.Text(this.PluginText.T("section.display", "Display"));
            if (ImGui.RadioButton(this.PluginText.Label("currency.chaos", "Chaos", "LootValueCurrencyChaos"), this.Settings.DisplayCurrency == 2)) this.Settings.DisplayCurrency = 2;
            ImGui.SameLine();
            if (ImGui.RadioButton(this.PluginText.Label("currency.exalted", "Exalted", "LootValueCurrencyExalted"), this.Settings.DisplayCurrency == 1)) this.Settings.DisplayCurrency = 1;
            ImGui.SameLine();
            if (ImGui.RadioButton(this.PluginText.Label("currency.divine", "Divine", "LootValueCurrencyDivine"), this.Settings.DisplayCurrency == 0)) this.Settings.DisplayCurrency = 0;

            ImGui.SliderFloat(this.PluginText.Label("settings.min_value_to_show", "Min value to show (ex)", "LootValueMinValueToShow"), ref this.Settings.MinValueEx, 0f, 50f, "%.2f");
            ImGui.SliderFloat(this.PluginText.Label("settings.highlight_from", "Highlight from (ex)", "LootValueHighlightFrom"), ref this.Settings.HighlightMinEx, 0f, 200f, "%.1f");
            ImGui.SliderFloat(this.PluginText.Label("settings.font_size", "Font size", "LootValueFontSize"), ref this.Settings.FontSize, 8f, 48f, "%.0f");
            ImGui.SliderFloat(this.PluginText.Label("settings.highlight_font_size", "Highlight font size", "LootValueHighlightFontSize"), ref this.Settings.HighlightFontSize, 8f, 64f, "%.0f");
            ImGui.Checkbox(this.PluginText.Label("settings.highlight_bold", "Highlight bold", "LootValueHighlightBold"), ref this.Settings.HighlightBold);
            ImGui.SliderFloat(this.PluginText.Label("settings.vertical_offset", "Vertical offset", "LootValueVerticalOffset"), ref this.Settings.OffsetY, -50f, 50f);
            ImGui.SliderFloat(this.PluginText.Label("settings.slot_font_scale", "Stash/inventory font scale", "LootValueSlotFontScale"), ref this.Settings.SlotFontScale, 0.5f, 2f, "%.2f");
            ImGui.SliderFloat(this.PluginText.Label("settings.slot_horizontal_offset", "Stash/inventory horizontal offset", "LootValueSlotOffsetX"), ref this.Settings.SlotOffsetX, -50f, 50f);
            ImGui.SliderFloat(this.PluginText.Label("settings.slot_vertical_offset", "Stash/inventory vertical offset", "LootValueSlotOffsetY"), ref this.Settings.SlotOffsetY, -50f, 50f);
            ImGui.Checkbox(this.PluginText.Label("settings.smooth_label_motion", "Smooth label motion (velocity tracking)", "LootValueSmoothLabelMotion"), ref this.Settings.InterpolatePosition);
            if (this.Settings.InterpolatePosition)
            {
                ImGui.SliderInt(this.PluginText.Label("settings.jitter_filter", "Jitter filter (lower=stronger, no lag)", "LootValueJitterFilter"), ref this.Settings.InterpolationRate, 1, 1000);
            }

            ImGui.SliderInt(this.PluginText.Label("settings.rescan_interval", "Rescan interval (ms)", "LootValueRescanInterval"), ref this.Settings.RescanIntervalMs, 16, 1000);
            ImGui.TextDisabled(this.PluginText.T("settings.rescan_interval.tooltip", "Positions redraw every frame; rescan only re-detects items/prices."));
            ImGui.SliderInt(this.PluginText.Label("settings.slot_rescan_interval", "Stash/inventory rescan interval (ms)", "LootValueSlotRescanInterval"), ref this.Settings.SlotRescanIntervalMs, 100, 2000);
            ImGui.TextDisabled(this.PluginText.T("settings.slot_rescan_interval.tooltip", "Cached slot values draw every frame; panel traversal and pricing run at this interval."));

            ImGui.ColorEdit4(this.PluginText.Label("settings.text_color", "Text color", "LootValueTextColor"), ref this.Settings.TextColor);
            ImGui.ColorEdit4(this.PluginText.Label("settings.highlight_color", "Highlight color", "LootValueHighlightColor"), ref this.Settings.HighlightColor);

            ImGui.Separator();
            ImGui.Text(this.PluginText.T("section.price_source", "Price source"));
            if (ImGui.RadioButton("poe2scout", this.Settings.PriceSource == PoeNinjaPriceFetcher.SourcePoe2Scout))
                this.Settings.PriceSource = PoeNinjaPriceFetcher.SourcePoe2Scout;
            ImGui.SameLine();
            if (ImGui.RadioButton("poe.ninja", this.Settings.PriceSource == PoeNinjaPriceFetcher.SourcePoeNinja))
                this.Settings.PriceSource = PoeNinjaPriceFetcher.SourcePoeNinja;

            ImGui.InputText(this.PluginText.Label("settings.league", "League", "LootValueLeague"), ref this.Settings.League, 64);
            ImGui.SliderInt(this.PluginText.Label("settings.refresh_interval", "Refresh interval (min)", "LootValueRefreshInterval"), ref this.Settings.RefreshIntervalMin, 1, 120);
            if (ImGui.Button(this.PluginText.Label("button.refresh_prices_now", "Refresh prices now", "LootValueRefreshPricesNow")))
            {
                PoeNinjaPriceFetcher.Configure(this.Settings.PriceSource, this.Settings.League ?? string.Empty, this.Settings.RefreshIntervalMin);
                PoeNinjaPriceFetcher.ForceRefresh(this.DllDirectory, ignoreCooldown: true);
            }

            ImGui.SameLine();
            if (PoeNinjaPriceFetcher.IsFetching)
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.2f, 1f), this.PluginText.T("status.loading", "Loading..."));
            }
            else if (PoeNinjaPriceFetcher.LastFetchUtc > DateTime.MinValue)
            {
                var mins = Math.Max(0, (int)(DateTime.UtcNow - PoeNinjaPriceFetcher.LastFetchUtc).TotalMinutes);
                ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.5f, 1f), this.PluginText.F("status.loaded_items", "{0} items | {1} min ago", PoeNinjaPriceFetcher.LoadedItemCount, mins));
            }
        }

        /// <inheritdoc/>
        public override void DrawUI()
        {
            if (Core.States.GameCurrentState != GameStateTypes.InGameState) return;

            PoeNinjaPriceFetcher.Configure(this.Settings.PriceSource, this.Settings.League ?? string.Empty, this.Settings.RefreshIntervalMin);
            PoeNinjaPriceFetcher.RefreshIfNeeded();

            if (this.Settings.DiagnosticsMode)
            {
                this.RunDiagnostics();
                this.DrawDiagnosticsWindow();
            }

            var now = DateTime.UtcNow;
            if (this.Settings.ShowOverlay && this.Settings.AnchorToLootTags)
            {
                if (this.EnsureReflection())
                {
                    if (now >= this.nextTagScanUtc)
                    {
                        this.nextTagScanUtc = now.AddMilliseconds(Math.Max(16, this.Settings.RescanIntervalMs));
                        this.ScanLootTags();
                    }

                    this.DrawTagChips();
                }
            }
            else if (this.Settings.ShowOverlay)
            {
                if (now >= this.nextRecomputeUtc)
                {
                    this.nextRecomputeUtc = now.AddMilliseconds(Math.Max(16, this.Settings.RescanIntervalMs));
                    this.RecomputeLabels();
                }

                this.DrawLabels();
            }

            if (this.Settings.ShowStashOverlay || this.Settings.ShowInventoryOverlay || this.Settings.ShowSlotDebugInfo)
            {
                this.DrawItemSlotValues();
            }

            if (this.Settings.ShowCurrencyExchangeOverlay)
            {
                this.DrawCurrencyExchangeValues();
            }
        }

        /// <summary>Re-reads + reprices every ground item; throttled. The drawn position is updated live each frame.</summary>
        private void RecomputeLabels()
        {
            this.cachedLabels.Clear();

            var area = Core.States.InGameStateObject.CurrentAreaInstance;
            foreach (var entity in area.AwakeEntities.Values)
            {
                // Ground drops are identified by the WorldItem component (path-independent — the wrapper
                // entity's own path is not "Metadata/Items"; that's the inner item).
                if (!entity.TryGetComponent<WorldItem>(out var worldItem) || worldItem.ItemEntityAddress == IntPtr.Zero) continue;
                if (!entity.TryGetComponent<Render>(out var render)) continue;

                var item = ReadFreshItem(worldItem.ItemEntityAddress);
                if (item == null) continue;

                if (!this.TryPriceItem(item, out var valueEx, out var label)) continue;
                if (valueEx < this.Settings.MinValueEx) continue;

                var highlight = valueEx >= this.Settings.HighlightMinEx;
                var color = ImGui.ColorConvertFloat4ToU32(highlight ? this.Settings.HighlightColor : this.Settings.TextColor);
                this.cachedLabels.Add(new LootLabel(entity.Id, render, label, color, highlight));
            }

            // Drop tracker state for items no longer present (picked up / left the area).
            if (this.trackWorld.Count > 0)
            {
                var live = new HashSet<uint>(this.cachedLabels.Count);
                foreach (var l in this.cachedLabels) live.Add(l.EntityId);
                this.trackWorld.Keys.Where(k => !live.Contains(k)).ToList().ForEach(k => this.trackWorld.Remove(k));
            }
        }

        private void DrawLabels()
        {
            if (this.cachedLabels.Count == 0) return;

            var fg = ImGui.GetForegroundDrawList();
            var font = ImGui.GetFont();
            var baseSize = ImGui.GetFontSize();
            var world = Core.States.InGameStateObject.CurrentWorldInstance;

            foreach (var label in this.cachedLabels)
            {
                // Anchor to the GROUND (stable TerrainHeight), not WorldPosition.Z — that Z is the item's
                // animated/bobbing model height, which makes the projected point oscillate. TerrainHeight is
                // constant for a stationary drop, so the only moving input becomes the camera (smoothed below).
                var screen = world.WorldToScreen(label.Render.WorldPosition, label.Render.TerrainHeight);
                if (screen == Vector2.Zero) continue;

                // Velocity-tracking filter: GH samples the camera at 120Hz from a 90Hz source, so the raw
                // projected point of a STATIC item beats ~1-2px along the path. Tracking screen velocity and
                // advancing by it each frame removes that without the lag a plain low-pass would add.
                if (this.Settings.InterpolatePosition)
                {
                    screen = Track(this.trackWorld, label.EntityId, screen, this.Settings.InterpolationRate);
                }

                var fontSize = label.Highlight ? this.Settings.HighlightFontSize : this.Settings.FontSize;
                var textWidth = ImGui.CalcTextSize(label.Text).X * (fontSize / baseSize);
                var pos = new Vector2(screen.X - (textWidth / 2f), screen.Y + this.Settings.OffsetY);
                this.DrawValueLabel(fg, font, baseSize, pos, label.Text, label.Color, label.Highlight);
            }
        }

        /// <summary>Draws one value label (background chip + shadowed text, faux-bold when highlighted)
        /// at the given top-left screen position. Shared by world-space and loot-tag modes.</summary>
        private void DrawValueLabel(ImDrawListPtr fg, ImFontPtr font, float baseSize, Vector2 pos, string text, uint color, bool highlight)
        {
            const uint shadow = 0xCC000000u;
            var fontSize = highlight ? this.Settings.HighlightFontSize : this.Settings.FontSize;
            var bold = highlight && this.Settings.HighlightBold;
            var textWidth = ImGui.CalcTextSize(text).X * (fontSize / baseSize);

            fg.AddRectFilled(pos - new Vector2(3f, 1f), pos + new Vector2(textWidth + 3f, fontSize + 1f), 0xB0000000u, 3f);
            fg.AddText(font, fontSize, pos + new Vector2(1f, 1f), shadow, text);
            fg.AddText(font, fontSize, pos, color, text);
            if (bold)
            {
                // Faux-bold: redraw offset by 1px so the glyphs thicken.
                fg.AddText(font, fontSize, pos + new Vector2(1f, 0f), color, text);
            }
        }

        // ---- Loot-tag mode: anchor value chips to the game's loot labels (found via a UI-tree scan) ----

        private bool EnsureReflection()
        {
            if (this.handleObj != null) return true;
            var handleProp = typeof(GameProcess).GetProperty("Handle", BindingFlags.Instance | BindingFlags.NonPublic);
            this.handleObj = handleProp?.GetValue(Core.Process);
            if (this.handleObj == null) return false;

            var methods = this.handleObj.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var readMem = methods.First(m => m.Name == "ReadMemory" && m.IsGenericMethod && m.GetParameters().Length == 1);
            var readVec = methods.First(m => m.Name == "ReadStdVector" && m.IsGenericMethod);
            this.readUiOffsetMethod = readMem.MakeGenericMethod(typeof(UiElementBaseOffset));
            this.readStdVectorMethod = readVec.MakeGenericMethod(typeof(IntPtr));
            this.readStdWStringStructMethod = readMem.MakeGenericMethod(typeof(StdWString));
            this.readStdWStringMethod = methods.First(m => m.Name == "ReadStdWString" && m.GetParameters().Length == 1);
            this.readIntPtrMethod = readMem.MakeGenericMethod(typeof(IntPtr));
            return true;
        }

        private string ReadUiElementText(IntPtr element)
        {
            try
            {
                var ws = this.readStdWStringStructMethod!.Invoke(this.handleObj, new object[] { element + UiElementTextOffset });
                if (ws == null) return string.Empty;
                return this.readStdWStringMethod!.Invoke(this.handleObj, new object[] { ws }) as string ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>BFS the visible UI tree; any text element that prices as a loot drop becomes a chip
        /// anchored to that element. Throttled; the element's live rect is re-read each frame when drawing.</summary>
        private void ScanLootTags()
        {
            this.cachedTagChips.Clear();
            this.RefreshGroundTagNames();
            var gameUi = Core.States.InGameStateObject.GameUi;
            var root = gameUi.Address;
            var leftPanel = gameUi.LeftPanel.Address;
            var rightPanel = gameUi.RightPanel.Address;
            if (root == IntPtr.Zero || this.readUiOffsetMethod == null || this.readStdVectorMethod == null) return;

            var queue = new Queue<IntPtr>();
            var visited = new HashSet<IntPtr>();
            queue.Enqueue(root);
            while (queue.Count > 0 && visited.Count < 20000)
            {
                var el = queue.Dequeue();
                if (el == IntPtr.Zero || !visited.Add(el)) continue;
                // Stash, inventory, vendor, and other large-panel text cannot be a ground loot label.
                // Do not traverse those potentially enormous subtrees when a panel is open.
                if (el != root && (el == leftPanel || el == rightPanel)) continue;
                if (this.readUiOffsetMethod.Invoke(this.handleObj, new object[] { el }) is not UiElementBaseOffset off) continue;
                if (el != root && !UiElementBaseFuncs.IsVisibleChecker(off.Flags)) continue;

                if (this.readStdVectorMethod.Invoke(this.handleObj, new object[] { off.ChildrensPtr }) is IntPtr[] kids)
                {
                    foreach (var k in kids) queue.Enqueue(k);
                }

                var text = this.ReadUiElementText(el);
                if (text.Length < 3) continue;
                var firstLine = text.Split('\n')[0].Trim();
                if (firstLine.Length < 3) continue;

                if (this.TryPriceTagText(firstLine, out var chipText, out var color, out var highlight))
                {
                    this.cachedTagChips.Add(new TagChip(el, chipText, color, highlight));
                }
            }

            // Drop tracker state for labels that are gone (item picked up / left the area).
            if (this.trackTag.Count > 0)
            {
                var live = new HashSet<IntPtr>(this.cachedTagChips.Count);
                foreach (var c in this.cachedTagChips) live.Add(c.ElementAddress);
                this.trackTag.Keys.Where(k => !live.Contains(k)).ToList().ForEach(k => this.trackTag.Remove(k));
            }
        }

        private bool TryPriceTagText(string text, out string chipText, out uint color, out bool highlight)
        {
            chipText = string.Empty;
            color = 0;
            highlight = false;

            var count = 1;
            var name = text;
            var m = Regex.Match(text, @"^(\d+)\s*x\s+(.+)$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                int.TryParse(m.Groups[1].Value, out count);
                name = m.Groups[2].Value;
            }

            name = name.Trim();
            if (name.Length < 3) return false;
            if (!this.groundTagNames.Contains(name)) return false;

            var price = PoeNinjaPriceFetcher.GetPrice(name);
            if (price == null) return false;

            var priced = new PoeNinjaPrice { PriceChaos = price.PriceChaos * Math.Max(1, count) };
            var (exVal, _) = PoeNinjaPriceFetcher.GetDisplayPrice(priced, 1);
            if (exVal < this.Settings.MinValueEx) return false;

            var (disp, cur) = PoeNinjaPriceFetcher.GetDisplayPrice(priced, this.Settings.DisplayCurrency);
            chipText = FormatValue(disp, cur);
            highlight = exVal >= this.Settings.HighlightMinEx;
            color = ImGui.ColorConvertFloat4ToU32(highlight ? this.Settings.HighlightColor : this.Settings.TextColor);
            return true;
        }

        private void DrawTagChips()
        {
            if (this.cachedTagChips.Count == 0) return;
            this.uiParentsObj ??= PluginUiElementReflection.CreateParents();
            if (this.uiParentsObj == null) return;

            var fg = ImGui.GetForegroundDrawList();
            var font = ImGui.GetFont();
            var baseSize = ImGui.GetFontSize();

            foreach (var chip in this.cachedTagChips)
            {
                // Pre-validate the address with a cheap raw read BEFORE constructing the UiElement: a real
                // UI element is self-referential (Self == its own address). If the element was freed since
                // the scan (e.g. item picked up), this no longer holds — skip it so CreateUiElement (which
                // would THROW on an invalid address) is never reached. try/catch remains as a backstop.
                if (this.readUiOffsetMethod!.Invoke(this.handleObj, new object[] { chip.ElementAddress }) is not UiElementBaseOffset off) continue;
                if (off.Self != IntPtr.Zero && off.Self != chip.ElementAddress) continue; // exact inverse of the game's "not a Ui Element" guard
                if (!UiElementBaseFuncs.IsVisibleChecker(off.Flags)) continue;

                try
                {
                    var el = PluginUiElementReflection.CreateUiElement(chip.ElementAddress, this.uiParentsObj);
                    if (el == null) continue;

                    var pos = (Vector2)PluginUiElementReflection.UiElementPositionProperty!.GetValue(el)!;
                    var size = (Vector2)PluginUiElementReflection.UiElementSizeProperty!.GetValue(el)!;
                    if (size.X <= 0f || pos == Vector2.Zero) continue;

                    var fontSize = chip.Highlight ? this.Settings.HighlightFontSize : this.Settings.FontSize;
                    var chipPos = new Vector2(pos.X + size.X + 6f, pos.Y + ((size.Y - fontSize) / 2f));

                    // Same velocity-tracking filter as world mode (the read rect beats against the game's
                    // update rate the same way), keyed by the label element.
                    if (this.Settings.InterpolatePosition)
                    {
                        chipPos = Track(this.trackTag, chip.ElementAddress, chipPos, this.Settings.InterpolationRate);
                    }

                    this.DrawValueLabel(fg, font, baseSize, chipPos, chip.Text, chip.Color, chip.Highlight);
                }
                catch
                {
                    // Stale/freed loot label — drop it; the next scan rebuilds from live elements.
                }
            }
        }

        /// <summary>
        /// Restricts loot-label matching to names backed by live ground-item entities. The game UI contains
        /// many unrelated text nodes (stash search, vendor listings, tooltips) whose text can also be priced;
        /// those must not be mistaken for ground labels.
        /// </summary>
        private void RefreshGroundTagNames()
        {
            this.groundTagNames.Clear();
            var area = Core.States.InGameStateObject.CurrentAreaInstance;
            foreach (var entity in area.AwakeEntities.Values)
            {
                if (!entity.TryGetComponent<WorldItem>(out var worldItem) || worldItem.ItemEntityAddress == IntPtr.Zero) continue;
                var item = ReadFreshItem(worldItem.ItemEntityAddress);
                if (item == null) continue;

                if (item.TryGetComponent<Base>(out var baseComp) && !string.IsNullOrWhiteSpace(baseComp.BaseItemName))
                {
                    this.groundTagNames.Add(baseComp.BaseItemName.Trim());
                }

                if (!item.TryGetComponent<Mods>(out var mods) || mods.Rarity != Rarity.Unique ||
                    !item.TryGetComponent<RenderItem>(out var renderItem)) continue;

                foreach (var key in ArtKeyVariants(ExtractArtBasename(renderItem.ResourcePath)))
                {
                    if (PoeNinjaPriceFetcher.TryResolveDisplayName(key, out var uniqueName) &&
                        !PoeNinjaPriceFetcher.IsGenericLookupName(uniqueName))
                    {
                        this.groundTagNames.Add(uniqueName.Trim());
                    }
                }
            }
        }

        /// <summary>Draws cached owned-stack values in the Currency Exchange item browser.</summary>
        private void DrawCurrencyExchangeValues()
        {
            if (!this.EnsureReflection()) return;

            var now = DateTime.UtcNow;
            if (now >= this.nextExchangeScanUtc)
            {
                this.nextExchangeScanUtc = now.AddMilliseconds(Math.Clamp(this.Settings.SlotRescanIntervalMs, 100, 2000));
                this.ScanCurrencyExchange();
            }

            if (this.cachedExchangeLabels.Count == 0) return;
            var foreground = ImGui.GetForegroundDrawList();
            var font = ImGui.GetFont();
            var baseSize = ImGui.GetFontSize();
            foreach (var label in this.cachedExchangeLabels)
            {
                this.DrawValueLabel(
                    foreground,
                    font,
                    baseSize,
                    label.Position,
                    label.Text,
                    label.Color,
                    label.Highlight);
            }
        }

        private void ScanCurrencyExchange()
        {
            this.cachedExchangeLabels.Clear();
            var root = this.ResolveUiPath(Core.States.InGameStateObject.GameUi.Address, CurrencyExchangeRootPath);
            if (root == IntPtr.Zero || !this.TryGetVisibleChildren(root, out var rootChildren) || rootChildren.Length <= 1) return;

            // [114][20][6][1] is the complete item list. Its visibility is the reliable signal that
            // Currency Exchange is open; only categories enabled by the selected tab are visible below it.
            var listAddress = rootChildren[1];
            if (!this.TryGetVisibleChildren(listAddress, out var categoryAddresses)) return;
            if (!PluginUiElementReflection.TryGetAbsoluteRect(root, out var viewportPosition, out var viewportSize)) return;
            var viewportMax = viewportPosition + viewportSize;

            foreach (var categoryAddress in categoryAddresses)
            {
                if (!this.TryGetVisibleChildren(categoryAddress, out var groupAddresses)) continue;
                foreach (var groupAddress in groupAddresses)
                {
                    if (!this.TryGetVisibleChildren(groupAddress, out var rowAddresses)) continue;

                    // Child 0 is the group headline. Every following populated child is an item row:
                    // [0] name, [1] icon container, [1][0] owned amount.
                    for (var rowIndex = 1; rowIndex < rowAddresses.Length; rowIndex++)
                    {
                        var rowAddress = rowAddresses[rowIndex];
                        if (!this.TryGetVisibleChildren(rowAddress, out var rowChildren) || rowChildren.Length <= 1) continue;
                        var nameAddress = rowChildren[0];
                        var iconAddress = rowChildren[1];
                        if (!this.TryGetVisibleChildren(iconAddress, out var iconChildren) || iconChildren.Length == 0) continue;

                        var name = this.ReadUiElementText(nameAddress).Split('\n')[0].Trim();
                        var amountText = this.ReadUiElementText(iconChildren[0]);
                        if (name.Length < 2 || !TryParseOwnedAmount(amountText, out var amount) || amount <= 0) continue;
                        if (!this.TryPriceNamedStack(name, amount, out var text, out var color, out var highlight)) continue;
                        if (!PluginUiElementReflection.TryGetAbsoluteRect(iconAddress, out var iconPosition, out var iconSize)) continue;

                        var center = iconPosition + (iconSize * 0.5f);
                        if (center.X < viewportPosition.X || center.X > viewportMax.X ||
                            center.Y < viewportPosition.Y || center.Y > viewportMax.Y) continue;

                        var fontSize = highlight ? this.Settings.HighlightFontSize : this.Settings.FontSize;
                        var labelPosition = new Vector2(
                            iconPosition.X + this.Settings.SlotOffsetX,
                            iconPosition.Y + iconSize.Y - fontSize + this.Settings.SlotOffsetY);
                        this.cachedExchangeLabels.Add(new ExchangePriceLabel(labelPosition, text, color, highlight));
                    }
                }
            }
        }

        private bool TryGetVisibleChildren(IntPtr address, out IntPtr[] children)
        {
            return this.TryGetChildren(address, requireVisible: true, out children);
        }

        private bool TryGetChildren(IntPtr address, bool requireVisible, out IntPtr[] children)
        {
            children = Array.Empty<IntPtr>();
            if (address == IntPtr.Zero || this.readUiOffsetMethod == null || this.readStdVectorMethod == null ||
                this.readUiOffsetMethod.Invoke(this.handleObj, new object[] { address }) is not UiElementBaseOffset offset ||
                (requireVisible && !UiElementBaseFuncs.IsVisibleChecker(offset.Flags))) return false;

            children = this.readStdVectorMethod.Invoke(this.handleObj, new object[] { offset.ChildrensPtr }) as IntPtr[] ?? Array.Empty<IntPtr>();
            return true;
        }

        private IntPtr ResolveUiPath(IntPtr root, IReadOnlyList<int> path)
        {
            var current = root;
            foreach (var childIndex in path)
            {
                if (!this.TryGetChildren(current, requireVisible: false, out var children) ||
                    childIndex < 0 || childIndex >= children.Length) return IntPtr.Zero;
                current = children[childIndex];
            }

            return current;
        }

        private static bool TryParseOwnedAmount(string text, out long amount)
        {
            amount = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var digits = Regex.Replace(text, @"[^0-9]", string.Empty);
            return digits.Length > 0 && long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out amount);
        }

        private bool TryPriceNamedStack(
            string itemName,
            long amount,
            out string text,
            out uint color,
            out bool highlight)
        {
            text = string.Empty;
            color = 0;
            highlight = false;
            var price = PoeNinjaPriceFetcher.GetPrice(itemName);
            if (price == null) return false;

            var priced = new PoeNinjaPrice { PriceChaos = price.PriceChaos * amount };
            var (exValue, _) = PoeNinjaPriceFetcher.GetDisplayPrice(priced, 1);
            if (exValue < this.Settings.MinValueEx) return false;

            var (displayValue, displayCurrency) = PoeNinjaPriceFetcher.GetDisplayPrice(priced, this.Settings.DisplayCurrency);
            text = FormatValue(displayValue, displayCurrency);
            highlight = exValue >= this.Settings.HighlightMinEx;
            color = ImGui.ColorConvertFloat4ToU32(highlight ? this.Settings.HighlightColor : this.Settings.TextColor);
            return true;
        }

        /// <summary>Prices item slots in the open stash and inventory panels.</summary>
        private void DrawItemSlotValues()
        {
            var gameUi = Core.States.InGameStateObject.GameUi;
            if (gameUi.Address == IntPtr.Zero || !this.EnsureReflection()) return;

            var scanLeft = this.Settings.ShowStashOverlay || this.Settings.ShowSlotDebugInfo;
            var scanRight = this.Settings.ShowInventoryOverlay || this.Settings.ShowSlotDebugInfo;
            var leftAddress = scanLeft && gameUi.LeftPanel.IsVisible ? gameUi.LeftPanel.Address : IntPtr.Zero;
            var rightAddress = scanRight && gameUi.RightPanel.IsVisible ? gameUi.RightPanel.Address : IntPtr.Zero;

            if (leftAddress != this.cachedLeftPanelAddress || rightAddress != this.cachedRightPanelAddress)
            {
                this.cachedLeftPanelAddress = leftAddress;
                this.cachedRightPanelAddress = rightAddress;
                this.nextSlotScanUtc = DateTime.MinValue;
            }

            var now = DateTime.UtcNow;
            if (now >= this.nextSlotScanUtc)
            {
                this.nextSlotScanUtc = now.AddMilliseconds(Math.Clamp(this.Settings.SlotRescanIntervalMs, 100, 2000));
                if (leftAddress != IntPtr.Zero)
                {
                    this.cachedLeftSlots = this.ScanItemSlots(
                        leftAddress,
                        gameUi.LeftPanel.Position,
                        gameUi.LeftPanel.Size,
                        out this.leftSlotReport);
                }
                else
                {
                    this.cachedLeftSlots.Clear();
                    this.leftSlotReport = new SlotScanReport(IntPtr.Zero);
                }

                if (rightAddress != IntPtr.Zero)
                {
                    this.cachedRightSlots = this.ScanItemSlots(
                        rightAddress,
                        gameUi.RightPanel.Position,
                        gameUi.RightPanel.Size,
                        out this.rightSlotReport);
                }
                else
                {
                    this.cachedRightSlots.Clear();
                    this.rightSlotReport = new SlotScanReport(IntPtr.Zero);
                }
            }

            var leftScroll = GetScrollFrameState(this.cachedLeftSlots);
            var rightScroll = GetScrollFrameState(this.cachedRightSlots);
            var hidePrices = this.Settings.HideSlotPricesOnHover &&
                             (IsAnySlotHovered(this.cachedLeftSlots, leftScroll) ||
                              IsAnySlotHovered(this.cachedRightSlots, rightScroll));
            this.DrawItemSlots(this.cachedLeftSlots, this.Settings.ShowStashOverlay, hidePrices, leftScroll);
            this.DrawItemSlots(this.cachedRightSlots, this.Settings.ShowInventoryOverlay, hidePrices, rightScroll);
            if (this.Settings.ShowSlotDebugInfo)
            {
                this.DrawSlotDiagnosticsWindow();
            }
        }

        private List<SlotInfo> ScanItemSlots(
            IntPtr panelAddress,
            Vector2 panelPosition,
            Vector2 panelSize,
            out SlotScanReport report)
        {
            report = new SlotScanReport(panelAddress);
            var candidatesByItem = new Dictionary<IntPtr, List<SlotElementCandidate>>();
            if (panelAddress == IntPtr.Zero || this.readUiOffsetMethod == null ||
                this.readStdVectorMethod == null || this.readIntPtrMethod == null) return new List<SlotInfo>();

            var queue = new Queue<(IntPtr Address, IntPtr Parent, ScrollBinding Scroll)>();
            var visited = new HashSet<IntPtr>();
            queue.Enqueue((panelAddress, IntPtr.Zero, default));

            while (queue.Count > 0 && visited.Count < 5000)
            {
                var (element, parent, scroll) = queue.Dequeue();
                if (element == IntPtr.Zero || !visited.Add(element)) continue;
                report.VisitedElements++;
                if (this.readUiOffsetMethod.Invoke(this.handleObj, new object[] { element }) is not UiElementBaseOffset offset) continue;
                if (!UiElementBaseFuncs.IsVisibleChecker(offset.Flags)) continue;

                if (this.readStdVectorMethod.Invoke(this.handleObj, new object[] { offset.ChildrensPtr }) is IntPtr[] children)
                {
                    var hasScrollContainer = this.TryGetScrollContainer(
                        children,
                        out var scrollItemsAddress,
                        out var localScroll);
                    if (hasScrollContainer)
                    {
                        report.ScrollContainers++;
                        report.ScrollOffsetY = localScroll.ScanOffsetY;
                    }

                    foreach (var child in children)
                    {
                        if (hasScrollContainer && child == scrollItemsAddress)
                        {
                            queue.Enqueue((child, element, localScroll));
                        }
                        else
                        {
                            queue.Enqueue((child, element, scroll));
                        }
                    }
                }

                // Slot discovery/rendering adapted from StashValueByZx0 by zx0CF1.
                var pointerValue = this.readIntPtrMethod.Invoke(this.handleObj, new object[] { element + UiElementItemAddressOffset });
                var itemAddress = pointerValue is IntPtr pointer ? pointer : IntPtr.Zero;
                if (itemAddress == IntPtr.Zero) continue;
                report.NonZeroPointers++;

                if (!candidatesByItem.TryGetValue(itemAddress, out var itemCandidates))
                {
                    itemCandidates = new List<SlotElementCandidate>();
                    candidatesByItem[itemAddress] = itemCandidates;
                }

                itemCandidates.Add(new SlotElementCandidate(element, parent, scroll));
            }

            // Premium tabs expose many ghost UI copies. Deduplicate by item pointer before validating,
            // constructing components, reading mods, or pricing so each real item pays those costs once.
            report.UniquePointers = candidatesByItem.Count;
            var panelMax = panelPosition + panelSize;
            var slots = new List<SlotInfo>();
            foreach (var (itemAddress, itemCandidates) in candidatesByItem)
            {
                var hasVisibleRect = false;
                var position = Vector2.Zero;
                var size = Vector2.Zero;
                var selectedScroll = default(ScrollBinding);
                var diagnosticElement = itemCandidates[0].ElementAddress;
                foreach (var candidate in itemCandidates)
                {
                    if (!TryGetSlotRect(candidate, out var candidatePosition, out var candidateSize)) continue;
                    var center = candidatePosition + (candidateSize * 0.5f);
                    if (center.X < panelPosition.X || center.X > panelMax.X) continue;
                    if (!candidate.Scroll.IsActive &&
                        (center.Y < panelPosition.Y || center.Y > panelMax.Y)) continue;

                    diagnosticElement = candidate.ElementAddress;
                    position = candidatePosition;
                    size = candidateSize;
                    selectedScroll = candidate.Scroll;
                    hasVisibleRect = true;
                    break;
                }

                if (!hasVisibleRect) continue;
                if (!PluginUiElementReflection.TryValidateItemAddress(itemAddress, out _, out var failureReason))
                {
                    report.AddRejected(diagnosticElement, itemAddress, failureReason);
                    continue;
                }

                var item = ReadFreshItem(itemAddress);
                if (item == null || string.IsNullOrEmpty(item.Path) ||
                    !item.Path.StartsWith(ItemPathPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    report.AddRejected(diagnosticElement, itemAddress, "item changed after validation");
                    continue;
                }

                report.ValidItems++;
                if (!this.TryPriceItem(item, out var valueEx, out var valueText, includeUniqueName: false) ||
                    valueEx < this.Settings.MinValueEx) continue;
                report.PricedCandidates++;

                slots.Add(new SlotInfo(itemAddress, position, size, valueText, selectedScroll));
            }

            report.VisibleSlots = slots.Count;

            return slots;
        }

        private bool TryGetScrollContainer(
            IntPtr[] children,
            out IntPtr itemsAddress,
            out ScrollBinding scroll)
        {
            itemsAddress = IntPtr.Zero;
            scroll = default;
            if (children.Length <= 2 || children[1] == IntPtr.Zero || children[2] == IntPtr.Zero ||
                this.readUiOffsetMethod == null || this.readStdVectorMethod == null) return false;

            var contentAddress = children[1];
            var holderAddress = children[2];
            if (this.readUiOffsetMethod.Invoke(this.handleObj, new object[] { holderAddress }) is not UiElementBaseOffset holderOffset ||
                !UiElementBaseFuncs.IsVisibleChecker(holderOffset.Flags) ||
                this.readStdVectorMethod.Invoke(this.handleObj, new object[] { holderOffset.ChildrensPtr }) is not IntPtr[] holderChildren ||
                holderChildren.Length == 0 || holderChildren[0] == IntPtr.Zero) return false;

            var thumbAddress = holderChildren[0];
            if (!PluginUiElementReflection.TryGetAbsoluteRect(contentAddress, out var contentPosition, out var contentSize) ||
                !PluginUiElementReflection.TryGetAbsoluteRect(holderAddress, out var holderPosition, out var holderSize) ||
                !PluginUiElementReflection.TryGetAbsoluteRect(thumbAddress, out var thumbPosition, out var thumbSize)) return false;

            // Shape checks keep ordinary [1]/[2] child layouts from being mistaken for scroll views.
            if (holderSize.X < 4f || holderSize.X > 64f || holderSize.Y < 40f ||
                thumbSize.X < 2f || thumbSize.X > holderSize.X * 1.5f ||
                thumbSize.Y < 8f || thumbSize.Y >= holderSize.Y ||
                contentSize.Y <= holderSize.Y + 1f || holderPosition.X < contentPosition.X ||
                thumbPosition.Y < holderPosition.Y - 2f ||
                thumbPosition.Y + thumbSize.Y > holderPosition.Y + holderSize.Y + 2f) return false;

            var thumbTravel = holderSize.Y - thumbSize.Y;
            var contentOverflow = contentSize.Y - holderSize.Y;
            if (thumbTravel <= 0f || contentOverflow <= 0f) return false;

            var progress = Math.Clamp((thumbPosition.Y - holderPosition.Y) / thumbTravel, 0f, 1f);
            itemsAddress = contentAddress;
            scroll = new ScrollBinding(
                holderAddress,
                thumbAddress,
                contentSize.Y,
                progress * contentOverflow,
                holderPosition.Y,
                holderPosition.Y + holderSize.Y);
            return float.IsFinite(scroll.ScanOffsetY) && scroll.ClipBottom > scroll.ClipTop;
        }

        private static bool TryGetSlotRect(SlotElementCandidate candidate, out Vector2 position, out Vector2 size)
        {
            if (!PluginUiElementReflection.TryGetAbsoluteRect(candidate.ElementAddress, out position, out size)) return false;

            // Premium tabs can keep the item pointer on a small bookkeeping child while its parent
            // owns the visible cell rectangle.
            if (candidate.ParentAddress != IntPtr.Zero &&
                PluginUiElementReflection.TryGetAbsoluteRect(candidate.ParentAddress, out var parentPosition, out var parentSize) &&
                parentSize.X >= 20f && parentSize.Y >= 20f &&
                ((parentSize.X <= 160f && parentSize.Y <= 256f) ||
                 (parentSize.X <= 256f && parentSize.Y <= 160f)))
            {
                position = parentPosition;
                size = parentSize;
            }

            position.Y -= candidate.Scroll.ScanOffsetY;

            return true;
        }

        private static bool IsAnySlotHovered(IReadOnlyList<SlotInfo> slots, ScrollFrameState scroll)
        {
            var mousePosition = ImGui.GetIO().MousePos;
            foreach (var slot in slots)
            {
                var position = GetLiveSlotPosition(slot, scroll);
                var centerY = position.Y + (slot.Size.Y * 0.5f);
                if (centerY < scroll.ClipTop || centerY > scroll.ClipBottom) continue;
                if (mousePosition.X >= position.X && mousePosition.X <= position.X + slot.Size.X &&
                    mousePosition.Y >= position.Y && mousePosition.Y <= position.Y + slot.Size.Y)
                {
                    return true;
                }
            }

            return false;
        }

        private static ScrollFrameState GetScrollFrameState(IReadOnlyList<SlotInfo> slots)
        {
            foreach (var slot in slots)
            {
                var binding = slot.Scroll;
                if (!binding.IsActive) continue;
                if (!PluginUiElementReflection.TryGetAbsoluteRect(binding.HolderAddress, out var holderPosition, out var holderSize) ||
                    !PluginUiElementReflection.TryGetAbsoluteRect(binding.ThumbAddress, out var thumbPosition, out var thumbSize))
                {
                    return new ScrollFrameState(binding.HolderAddress, 0f, binding.ClipTop, binding.ClipBottom);
                }

                var thumbTravel = holderSize.Y - thumbSize.Y;
                var contentOverflow = binding.ContentHeight - holderSize.Y;
                if (thumbTravel <= 0f || contentOverflow <= 0f)
                {
                    return new ScrollFrameState(binding.HolderAddress, 0f, holderPosition.Y, holderPosition.Y + holderSize.Y);
                }

                var progress = Math.Clamp((thumbPosition.Y - holderPosition.Y) / thumbTravel, 0f, 1f);
                var currentOffset = progress * contentOverflow;
                return new ScrollFrameState(
                    binding.HolderAddress,
                    currentOffset - binding.ScanOffsetY,
                    holderPosition.Y,
                    holderPosition.Y + holderSize.Y);
            }

            return ScrollFrameState.None;
        }

        private static Vector2 GetLiveSlotPosition(SlotInfo slot, ScrollFrameState scroll)
        {
            if (!slot.Scroll.IsActive || slot.Scroll.HolderAddress != scroll.HolderAddress) return slot.Position;
            return slot.Position - new Vector2(0f, scroll.OffsetDeltaY);
        }

        private void DrawSlotDiagnosticsWindow()
        {
            ImGui.SetNextWindowSize(new Vector2(720f, 420f), ImGuiCond.FirstUseEver);
            if (ImGui.Begin(
                    this.PluginText.Title("diagnostics.slots.window_title", "LootValue Slot Diagnostics", "LootValueSlotDiagnostics"),
                    ref this.Settings.ShowSlotDebugInfo))
            {
                this.DrawSlotScanReport(this.PluginText.T("diagnostics.slots.left_panel", "Left panel (stash)"), this.leftSlotReport);
                ImGui.Separator();
                this.DrawSlotScanReport(this.PluginText.T("diagnostics.slots.right_panel", "Right panel (inventory)"), this.rightSlotReport);
            }

            ImGui.End();
        }

        private void DrawSlotScanReport(string label, SlotScanReport report)
        {
            ImGui.TextUnformatted($"{label}: 0x{report.PanelAddress.ToInt64():X}");
            ImGui.TextUnformatted(this.PluginText.F(
                "diagnostics.slots.summary",
                "UI elements={0}  non-zero +0x4F8={1}  unique pointers={2}  valid items={3}  priced={4}  visible={5}  scroll views={6}  scroll Y={7:0.0}",
                report.VisitedElements,
                report.NonZeroPointers,
                report.UniquePointers,
                report.ValidItems,
                report.PricedCandidates,
                report.VisibleSlots,
                report.ScrollContainers,
                report.ScrollOffsetY));
            ImGui.TextUnformatted(this.PluginText.F(
                "diagnostics.slots.rejected",
                "Rejected candidates={0} (showing up to {1})",
                report.RejectedCandidates,
                SlotScanReport.MaxSamples));
            foreach (var sample in report.RejectedSamples)
            {
                ImGui.TextUnformatted(sample);
            }
        }

        private void DrawItemSlots(
            IReadOnlyList<SlotInfo> slots,
            bool drawPrices,
            bool hidePrices,
            ScrollFrameState scroll)
        {
            var foreground = ImGui.GetForegroundDrawList();
            var font = ImGui.GetFont();
            var fontSize = ImGui.GetFontSize() * this.Settings.SlotFontScale;
            var color = ImGui.ColorConvertFloat4ToU32(this.Settings.TextColor);

            foreach (var slot in slots)
            {
                var position = GetLiveSlotPosition(slot, scroll);
                var centerY = position.Y + (slot.Size.Y * 0.5f);
                if (centerY < scroll.ClipTop || centerY > scroll.ClipBottom) continue;

                if (this.Settings.ShowSlotDebugInfo)
                {
                    foreground.AddRect(position, position + slot.Size, 0xFFFF00FFu, 0f, ImDrawFlags.None, 2f);
                    foreground.AddText(font, fontSize, position, 0xFFFFFFFFu, $"E: {slot.ItemAddress.ToInt64():X}");
                }

                if (!drawPrices || hidePrices) continue;
                var textWidth = ImGui.CalcTextSize(slot.ValueText).X * this.Settings.SlotFontScale;
                var drawPosition = new Vector2(
                    position.X + this.Settings.SlotOffsetX,
                    position.Y + slot.Size.Y - fontSize + this.Settings.SlotOffsetY);
                foreground.AddRectFilled(
                    drawPosition - new Vector2(3f, 1f),
                    drawPosition + new Vector2(textWidth + 3f, fontSize + 1f),
                    0xB0000000u,
                    3f);
                foreground.AddText(font, fontSize, drawPosition + new Vector2(1f, 1f), 0xCC000000u, slot.ValueText);
                foreground.AddText(font, fontSize, drawPosition, color, slot.ValueText);
            }
        }

        private static string FormatValue(double value, string currency) => currency switch
        {
            "divine" => value.ToString("0.00", CultureInfo.InvariantCulture) + " div",
            "chaos" => value.ToString("0.#", CultureInfo.InvariantCulture) + " c",
            _ => value.ToString("0.#", CultureInfo.InvariantCulture) + " ex",
        };

        /// <summary>Alpha-beta filter on a screen position (per tracked key). It estimates screen-space
        /// VELOCITY and advances by it each frame, then nudges toward the noisy measurement by alpha — so
        /// constant-velocity motion tracks with no lag while the per-frame sampling jitter is rejected.
        /// A large jump (teleport / zone change) resets the tracker. Velocity is in px/frame (assumes a
        /// roughly steady frame rate, which is fine for jitter rejection).</summary>
        private static Vector2 Track<TKey>(Dictionary<TKey, Tracked> dict, TKey key, Vector2 measure, int rate)
            where TKey : notnull
        {
            var alpha = Math.Clamp(rate / 1000f, 0.01f, 1f);
            var beta = alpha * alpha / (2f - alpha);
            if (dict.TryGetValue(key, out var t))
            {
                var predicted = t.Pos + t.Vel;
                var residual = measure - predicted;
                if (residual.LengthSquared() <= 150f * 150f)
                {
                    var pos = predicted + (residual * alpha);
                    var vel = t.Vel + (residual * beta);
                    dict[key] = new Tracked(pos, vel);
                    return pos;
                }
            }

            dict[key] = new Tracked(measure, Vector2.Zero);
            return measure;
        }

        /// <summary>Walks every awake entity and reports the ground-item detection funnel + sample reads,
        /// so we can see which stage drops items. Throttled. Independent of the overlay gates.</summary>
        private void RunDiagnostics()
        {
            var now = DateTime.UtcNow;
            if (now < this.nextDiagUtc) return;
            this.nextDiagUtc = now.AddMilliseconds(500);

            this.diagSamples.Clear();
            int total = 0, wiPath = 0, metaItemsPath = 0, wiComp = 0, innerOk = 0, priced = 0, belowFloor = 0;

            var area = Core.States.InGameStateObject.CurrentAreaInstance;
            foreach (var entity in area.AwakeEntities.Values)
            {
                total++;
                var p = entity.Path ?? string.Empty;
                if (p.Contains("WorldItem", StringComparison.Ordinal)) wiPath++;
                if (p.StartsWith(ItemPathPrefix, StringComparison.Ordinal)) metaItemsPath++;

                if (!entity.TryGetComponent<WorldItem>(out var wi) || wi.ItemEntityAddress == IntPtr.Zero) continue;
                wiComp++;

                var item = ReadFreshItem(wi.ItemEntityAddress);
                if (item == null) continue;
                innerOk++;

                var rarity = item.TryGetComponent<Mods>(out var m) ? m.Rarity : Rarity.Normal;
                var baseName = item.TryGetComponent<Base>(out var b) ? b.BaseItemName : string.Empty;
                var art = item.TryGetComponent<RenderItem>(out var ri) ? ExtractArtBasename(ri.ResourcePath) : string.Empty;
                var ok = this.TryPriceItem(item, out var ex, out var lbl);
                if (ok)
                {
                    priced++;
                    if (ex < this.Settings.MinValueEx) belowFloor++;
                }

                if (this.diagSamples.Count < 20)
                {
                    this.diagSamples.Add(ok
                        ? $"{rarity} {baseName} [art={art}] -> {lbl} ({ex:0.##} ex)"
                        : $"{rarity} {baseName} [art={art}] -> {this.PluginText.T("diagnostics.no_price", "NO PRICE")}");
                }
            }

            this.diagSummary =
                this.PluginText.F("diagnostics.summary.ingame", "InGame={0}  PanelOpen={1}", Core.States.GameCurrentState == GameStateTypes.InGameState, Core.States.InGameStateObject.GameUi.IsAnyLargePanelOpen) + "\n" +
                this.PluginText.F("diagnostics.summary.awake_entities", "AwakeEntities={0}", total) + "\n" +
                this.PluginText.F("diagnostics.summary.paths", "path contains 'WorldItem'={0}    path starts 'Metadata/Items'={1}", wiPath, metaItemsPath) + "\n" +
                this.PluginText.F("diagnostics.summary.components", "WorldItem component (inner!=0)={0}    inner item read OK={1}", wiComp, innerOk) + "\n" +
                this.PluginText.F("diagnostics.summary.pricing", "priced={0}    belowFloor(<{1}ex)={2}    would draw={3}", priced, this.Settings.MinValueEx, belowFloor, priced - belowFloor) + "\n" +
                this.PluginText.F("diagnostics.summary.price_db", "priceDB items={0}  fetching={1}", PoeNinjaPriceFetcher.LoadedItemCount, PoeNinjaPriceFetcher.IsFetching);
        }

        private void DrawDiagnosticsWindow()
        {
            ImGui.SetNextWindowSize(new Vector2(580, 440), ImGuiCond.FirstUseEver);
            if (ImGui.Begin(this.PluginText.Title("diagnostics.window_title", "LootValue Diagnostics", "LootValueDiagnostics"), ref this.Settings.DiagnosticsMode))
            {
                ImGui.TextUnformatted(this.diagSummary);
                ImGui.Separator();
                ImGui.TextUnformatted(this.PluginText.F("diagnostics.samples", "Samples ({0}):", this.diagSamples.Count));
                foreach (var s in this.diagSamples)
                {
                    ImGui.TextUnformatted(s);
                }
            }

            ImGui.End();
        }

        /// <summary>Resolve an item's display value + label text. Uniques price by icon art (revealing
        /// unidentified ones); everything else by base-type name. Mirrors RitualHelper's resolution.</summary>
        private bool TryPriceItem(Item item, out double valueEx, out string label, bool includeUniqueName = true)
        {
            valueEx = 0;
            label = string.Empty;

            var rarity = Rarity.Normal;
            if (item.TryGetComponent<Mods>(out var mods)) rarity = mods.Rarity;

            var baseName = item.TryGetComponent<Base>(out var baseComp) ? baseComp.BaseItemName?.Trim() ?? string.Empty : string.Empty;
            var artBasename = item.TryGetComponent<RenderItem>(out var renderItem) ? ExtractArtBasename(renderItem.ResourcePath) : string.Empty;
            var fullItemPath = item.Path ?? string.Empty;
            var internalName = fullItemPath.Contains('/') ? fullItemPath[(fullItemPath.LastIndexOf('/') + 1)..] : fullItemPath;

            var itemName = baseName;
            if (rarity == Rarity.Unique && !string.IsNullOrEmpty(artBasename))
            {
                foreach (var key in ArtKeyVariants(artBasename))
                {
                    if (PoeNinjaPriceFetcher.TryResolveDisplayName(key, out var uniqueName) &&
                        !PoeNinjaPriceFetcher.IsGenericLookupName(uniqueName))
                    {
                        itemName = uniqueName;
                        break;
                    }

                    if (PoeNinjaPriceFetcher.HasPriceDataForName(key))
                    {
                        itemName = key;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(itemName)) return false;

            var modLines = ItemModHelper.GetModLines(item);
            var price = PoeNinjaPriceFetcher.GetPrice(itemName, modLines, internalName, fullItemPath);
            if (price == null) return false;

            var stack = item.TryGetComponent<Stack>(out var stackComp) && stackComp.Count > 1 ? stackComp.Count : 1;
            var priceChaos = price.PriceChaos * stack;

            var priced = new PoeNinjaPrice { PriceChaos = priceChaos };
            var (displayValue, displayCurrency) = PoeNinjaPriceFetcher.GetDisplayPrice(priced, this.Settings.DisplayCurrency);

            // Value floor / highlight compare in Exalted, independent of the chosen display currency.
            var (exValue, _) = PoeNinjaPriceFetcher.GetDisplayPrice(priced, 1);
            valueEx = exValue;

            var valueText = FormatValue(displayValue, displayCurrency);

            // valueText is already the stack TOTAL; only uniques get a name prefix.
            var nameForLabel = includeUniqueName && rarity == Rarity.Unique && this.Settings.RevealUnidentifiedUniques ? $"{itemName} — " : string.Empty;
            label = $"{nameForLabel}{valueText}";
            return true;
        }

        private static Item? ReadFreshItem(IntPtr itemAddress)
        {
            if (itemAddress == IntPtr.Zero) return null;
            try
            {
                return Activator.CreateInstance(
                    typeof(Item),
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new object[] { itemAddress },
                    null) as Item;
            }
            catch
            {
                return null;
            }
        }

        // "Art/2DItems/.../Uniques/Deidbell.dds" -> "Deidbell".
        private static string ExtractArtBasename(string? artPath)
        {
            if (string.IsNullOrWhiteSpace(artPath)) return string.Empty;
            var slash = artPath.LastIndexOfAny(new[] { '/', '\\' });
            var file = slash >= 0 && slash < artPath.Length - 1 ? artPath[(slash + 1)..] : artPath;
            var dot = file.LastIndexOf('.');
            return dot > 0 ? file[..dot] : file;
        }

        // GGG art basenames and the price DB disagree on a leading "The" (both directions).
        private static IEnumerable<string> ArtKeyVariants(string artBasename)
        {
            if (string.IsNullOrWhiteSpace(artBasename)) yield break;
            yield return artBasename;
            if (artBasename.StartsWith("The", StringComparison.OrdinalIgnoreCase) && artBasename.Length > 3)
                yield return artBasename[3..];
            else
                yield return "The" + artBasename;
        }

        private readonly struct LootLabel
        {
            public LootLabel(uint entityId, Render render, string text, uint color, bool highlight)
            {
                this.EntityId = entityId;
                this.Render = render;
                this.Text = text;
                this.Color = color;
                this.Highlight = highlight;
            }

            public uint EntityId { get; }

            public Render Render { get; }

            public string Text { get; }

            public uint Color { get; }

            public bool Highlight { get; }
        }

        private readonly struct SlotInfo
        {
            public SlotInfo(
                IntPtr itemAddress,
                Vector2 position,
                Vector2 size,
                string valueText,
                ScrollBinding scroll)
            {
                this.ItemAddress = itemAddress;
                this.Position = position;
                this.Size = size;
                this.ValueText = valueText;
                this.Scroll = scroll;
            }

            public IntPtr ItemAddress { get; }

            public Vector2 Position { get; }

            public Vector2 Size { get; }

            public string ValueText { get; }

            public ScrollBinding Scroll { get; }
        }

        private readonly struct ExchangePriceLabel
        {
            public ExchangePriceLabel(Vector2 position, string text, uint color, bool highlight)
            {
                this.Position = position;
                this.Text = text;
                this.Color = color;
                this.Highlight = highlight;
            }

            public Vector2 Position { get; }

            public string Text { get; }

            public uint Color { get; }

            public bool Highlight { get; }
        }

        private readonly struct SlotElementCandidate
        {
            public SlotElementCandidate(
                IntPtr elementAddress,
                IntPtr parentAddress,
                ScrollBinding scroll)
            {
                this.ElementAddress = elementAddress;
                this.ParentAddress = parentAddress;
                this.Scroll = scroll;
            }

            public IntPtr ElementAddress { get; }

            public IntPtr ParentAddress { get; }

            public ScrollBinding Scroll { get; }
        }

        private readonly struct ScrollBinding
        {
            public ScrollBinding(
                IntPtr holderAddress,
                IntPtr thumbAddress,
                float contentHeight,
                float scanOffsetY,
                float clipTop,
                float clipBottom)
            {
                this.HolderAddress = holderAddress;
                this.ThumbAddress = thumbAddress;
                this.ContentHeight = contentHeight;
                this.ScanOffsetY = scanOffsetY;
                this.ClipTop = clipTop;
                this.ClipBottom = clipBottom;
            }

            public bool IsActive => this.HolderAddress != IntPtr.Zero && this.ThumbAddress != IntPtr.Zero;

            public IntPtr HolderAddress { get; }

            public IntPtr ThumbAddress { get; }

            public float ContentHeight { get; }

            public float ScanOffsetY { get; }

            public float ClipTop { get; }

            public float ClipBottom { get; }
        }

        private readonly struct ScrollFrameState
        {
            public static ScrollFrameState None { get; } = new(
                IntPtr.Zero,
                0f,
                float.NegativeInfinity,
                float.PositiveInfinity);

            public ScrollFrameState(IntPtr holderAddress, float offsetDeltaY, float clipTop, float clipBottom)
            {
                this.HolderAddress = holderAddress;
                this.OffsetDeltaY = offsetDeltaY;
                this.ClipTop = clipTop;
                this.ClipBottom = clipBottom;
            }

            public IntPtr HolderAddress { get; }

            public float OffsetDeltaY { get; }

            public float ClipTop { get; }

            public float ClipBottom { get; }
        }

        private sealed class SlotScanReport
        {
            public const int MaxSamples = 8;
            private readonly HashSet<IntPtr> sampledPointers = new();

            public SlotScanReport(IntPtr panelAddress)
            {
                this.PanelAddress = panelAddress;
            }

            public IntPtr PanelAddress { get; }

            public int VisitedElements { get; set; }

            public int NonZeroPointers { get; set; }

            public int UniquePointers { get; set; }

            public int ScrollContainers { get; set; }

            public float ScrollOffsetY { get; set; }

            public int ValidItems { get; set; }

            public int PricedCandidates { get; set; }

            public int VisibleSlots { get; set; }

            public int RejectedCandidates { get; private set; }

            public List<string> RejectedSamples { get; } = new();

            public void AddRejected(IntPtr elementAddress, IntPtr itemAddress, string reason)
            {
                this.RejectedCandidates++;
                if (this.RejectedSamples.Count >= MaxSamples || !this.sampledPointers.Add(itemAddress)) return;
                this.RejectedSamples.Add(
                    $"ui=0x{elementAddress.ToInt64():X}  candidate=0x{itemAddress.ToInt64():X}  {reason}");
            }
        }

        private readonly struct TagChip
        {
            public TagChip(IntPtr elementAddress, string text, uint color, bool highlight)
            {
                this.ElementAddress = elementAddress;
                this.Text = text;
                this.Color = color;
                this.Highlight = highlight;
            }

            public IntPtr ElementAddress { get; }

            public string Text { get; }

            public uint Color { get; }

            public bool Highlight { get; }
        }

        private readonly struct Tracked
        {
            public Tracked(Vector2 pos, Vector2 vel)
            {
                this.Pos = pos;
                this.Vel = vel;
            }

            public Vector2 Pos { get; }

            public Vector2 Vel { get; }
        }
    }
}
