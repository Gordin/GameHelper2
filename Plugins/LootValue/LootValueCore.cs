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

    /// <summary>
    ///     LootValue plugin — prices dropped items and draws their value over the drop in the world.
    ///     Unidentified uniques are revealed by name via their icon art (same bridge as RitualHelper).
    /// </summary>
    public sealed class LootValueCore : PCore<LootValueSettings>
    {
        private const string ItemPathPrefix = "Metadata/Items";

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

        private string SettingPathname => Path.Join(this.DllDirectory, "config", "settings.txt");

        /// <inheritdoc/>
        public override void OnEnable(bool isGameOpened)
        {
            if (File.Exists(this.SettingPathname))
            {
                try
                {
                    this.Settings = JsonConvert.DeserializeObject<LootValueSettings>(File.ReadAllText(this.SettingPathname)) ?? new LootValueSettings();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LootValue] Failed to load settings: {ex.Message}");
                    this.Settings = new LootValueSettings();
                }
            }

            PoeNinjaPriceFetcher.Configure(this.Settings.PriceSource, this.Settings.League ?? string.Empty, this.Settings.RefreshIntervalMin);
            PoeNinjaPriceFetcher.Initialize(this.DllDirectory);
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
            ImGui.Checkbox("Show value over ground items", ref this.Settings.ShowOverlay);
            ImGui.Checkbox("Anchor to loot labels (no overlap when items pile up)", ref this.Settings.AnchorToLootTags);
            ImGui.Checkbox("Reveal unidentified uniques (by art)", ref this.Settings.RevealUnidentifiedUniques);
            ImGui.Checkbox("Diagnostics window", ref this.Settings.DiagnosticsMode);

            ImGui.Separator();
            ImGui.Text("Display");
            if (ImGui.RadioButton("Chaos", this.Settings.DisplayCurrency == 2)) this.Settings.DisplayCurrency = 2;
            ImGui.SameLine();
            if (ImGui.RadioButton("Exalted", this.Settings.DisplayCurrency == 1)) this.Settings.DisplayCurrency = 1;
            ImGui.SameLine();
            if (ImGui.RadioButton("Divine", this.Settings.DisplayCurrency == 0)) this.Settings.DisplayCurrency = 0;

            ImGui.SliderFloat("Min value to show (ex)", ref this.Settings.MinValueEx, 0f, 50f, "%.2f");
            ImGui.SliderFloat("Highlight from (ex)", ref this.Settings.HighlightMinEx, 0f, 200f, "%.1f");
            ImGui.SliderFloat("Font size", ref this.Settings.FontSize, 8f, 48f, "%.0f");
            ImGui.SliderFloat("Highlight font size", ref this.Settings.HighlightFontSize, 8f, 64f, "%.0f");
            ImGui.Checkbox("Highlight bold", ref this.Settings.HighlightBold);
            ImGui.SliderFloat("Vertical offset", ref this.Settings.OffsetY, -50f, 50f);
            ImGui.Checkbox("Smooth label motion (velocity tracking)", ref this.Settings.InterpolatePosition);
            if (this.Settings.InterpolatePosition)
            {
                ImGui.SliderInt("Jitter filter (lower=stronger, no lag)", ref this.Settings.InterpolationRate, 1, 1000);
            }

            ImGui.SliderInt("Rescan interval (ms)", ref this.Settings.RescanIntervalMs, 16, 1000);
            ImGui.TextDisabled("Positions redraw every frame; rescan only re-detects items/prices.");

            ImGui.ColorEdit4("Text color", ref this.Settings.TextColor);
            ImGui.ColorEdit4("Highlight color", ref this.Settings.HighlightColor);

            ImGui.Separator();
            ImGui.Text("Price source");
            if (ImGui.RadioButton("poe2scout", this.Settings.PriceSource == PoeNinjaPriceFetcher.SourcePoe2Scout))
                this.Settings.PriceSource = PoeNinjaPriceFetcher.SourcePoe2Scout;
            ImGui.SameLine();
            if (ImGui.RadioButton("poe.ninja", this.Settings.PriceSource == PoeNinjaPriceFetcher.SourcePoeNinja))
                this.Settings.PriceSource = PoeNinjaPriceFetcher.SourcePoeNinja;

            ImGui.InputText("League", ref this.Settings.League, 64);
            ImGui.SliderInt("Refresh interval (min)", ref this.Settings.RefreshIntervalMin, 1, 120);
            if (ImGui.Button("Refresh prices now"))
            {
                PoeNinjaPriceFetcher.Configure(this.Settings.PriceSource, this.Settings.League ?? string.Empty, this.Settings.RefreshIntervalMin);
                PoeNinjaPriceFetcher.ForceRefresh(this.DllDirectory, ignoreCooldown: true);
            }

            ImGui.SameLine();
            if (PoeNinjaPriceFetcher.IsFetching)
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.2f, 1f), "Loading...");
            }
            else if (PoeNinjaPriceFetcher.LastFetchUtc > DateTime.MinValue)
            {
                var mins = Math.Max(0, (int)(DateTime.UtcNow - PoeNinjaPriceFetcher.LastFetchUtc).TotalMinutes);
                ImGui.TextColored(new Vector4(0.5f, 0.8f, 0.5f, 1f), $"{PoeNinjaPriceFetcher.LoadedItemCount} items | {mins} min ago");
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

            if (!this.Settings.ShowOverlay) return;

            var now = DateTime.UtcNow;
            if (this.Settings.AnchorToLootTags)
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
            else
            {
                if (now >= this.nextRecomputeUtc)
                {
                    this.nextRecomputeUtc = now.AddMilliseconds(Math.Max(16, this.Settings.RescanIntervalMs));
                    this.RecomputeLabels();
                }

                this.DrawLabels();
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
            var root = Core.States.InGameStateObject.GameUi.Address;
            if (root == IntPtr.Zero || this.readUiOffsetMethod == null || this.readStdVectorMethod == null) return;

            var queue = new Queue<IntPtr>();
            var visited = new HashSet<IntPtr>();
            queue.Enqueue(root);
            while (queue.Count > 0 && visited.Count < 20000)
            {
                var el = queue.Dequeue();
                if (el == IntPtr.Zero || !visited.Add(el)) continue;
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
                        : $"{rarity} {baseName} [art={art}] -> NO PRICE");
                }
            }

            this.diagSummary =
                $"InGame={Core.States.GameCurrentState == GameStateTypes.InGameState}  PanelOpen={Core.States.InGameStateObject.GameUi.IsAnyLargePanelOpen}\n" +
                $"AwakeEntities={total}\n" +
                $"path contains 'WorldItem'={wiPath}    path starts 'Metadata/Items'={metaItemsPath}\n" +
                $"WorldItem component (inner!=0)={wiComp}    inner item read OK={innerOk}\n" +
                $"priced={priced}    belowFloor(<{this.Settings.MinValueEx}ex)={belowFloor}    would draw={priced - belowFloor}\n" +
                $"priceDB items={PoeNinjaPriceFetcher.LoadedItemCount}  fetching={PoeNinjaPriceFetcher.IsFetching}";
        }

        private void DrawDiagnosticsWindow()
        {
            ImGui.SetNextWindowSize(new Vector2(580, 440), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("LootValue Diagnostics", ref this.Settings.DiagnosticsMode))
            {
                ImGui.TextUnformatted(this.diagSummary);
                ImGui.Separator();
                ImGui.TextUnformatted($"Samples ({this.diagSamples.Count}):");
                foreach (var s in this.diagSamples)
                {
                    ImGui.TextUnformatted(s);
                }
            }

            ImGui.End();
        }

        /// <summary>Resolve an item's display value + label text. Uniques price by icon art (revealing
        /// unidentified ones); everything else by base-type name. Mirrors RitualHelper's resolution.</summary>
        private bool TryPriceItem(Item item, out double valueEx, out string label)
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
            var nameForLabel = rarity == Rarity.Unique && this.Settings.RevealUnidentifiedUniques ? $"{itemName} — " : string.Empty;
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
