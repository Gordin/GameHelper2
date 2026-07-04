// <copyright file="PickupHelperCore.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace PickupHelper
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using System.Reflection;
    using ClickableTransparentOverlay.Win32;
    using GameHelper;
    using GameHelper.Plugin;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using GameHelper.Utils;
    using ImGuiNET;
    using Newtonsoft.Json;

    /// <summary>
    ///     A hover-to-pickup helper: when the item under the cursor matches the configured
    ///     whitelist / categories / rarity, it left-clicks in place (the cursor is never moved).
    /// </summary>
    public sealed class PickupHelperCore : PCore<PickupHelperSettings>
    {
        private long lastClickMs;
        private long lastClickedKey;

        // Pending randomized click: after a matching item is detected we wait a random delay
        // (tracked per target key) before clicking, without blocking the render thread.
        private long pendingClickKey;
        private long pendingClickAtMs;

        private string SettingPathname => Path.Join(this.DllDirectory, "config", "settings.txt");

        /// <inheritdoc />
        public override void OnEnable(bool isGameOpened)
        {
            if (File.Exists(this.SettingPathname))
            {
                var content = File.ReadAllText(this.SettingPathname);
                this.Settings = JsonConvert.DeserializeObject<PickupHelperSettings>(content) ?? new PickupHelperSettings();
            }
        }

        /// <inheritdoc />
        public override void OnDisable()
        {
        }

        /// <inheritdoc />
        public override void SaveSettings()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.SettingPathname) ?? string.Empty);
            var data = JsonConvert.SerializeObject(this.Settings, Formatting.Indented);
            File.WriteAllText(this.SettingPathname, data);
        }

        /// <inheritdoc />
        public override void DrawUI()
        {
            var haveItem = this.TryReadHoveredItem(out var info);
            if (haveItem && !string.IsNullOrEmpty(info.Category))
            {
                this.Settings.KnownCategories.Add(info.Category);
            }

            // Add-to-whitelist hotkey works regardless of the pickup trigger.
            if (Utils.IsKeyPressedAndNotTimeout(this.Settings.AddHoveredKey, 250) &&
                haveItem && !string.IsNullOrEmpty(info.InternalName))
            {
                this.Settings.Whitelist[info.InternalName] = info.DisplayName;
            }

            if (this.Settings.ShowDebugWindow)
            {
                this.DrawDebugWindow(haveItem, info);
            }

            if (!this.CanAttemptPickup(haveItem, info))
            {
                this.pendingClickKey = 0;
                return;
            }

            var nowMs = Environment.TickCount64;

            // Schedule a randomized delay the first frame this item becomes the pickup target.
            if (this.pendingClickKey != info.Key)
            {
                this.pendingClickKey = info.Key;
                this.pendingClickAtMs = nowMs + this.NextRandomDelayMs();
                return;
            }

            // Wait out the random detection->click delay.
            if (nowMs < this.pendingClickAtMs)
            {
                return;
            }

            // Rate limits.
            if (nowMs - this.lastClickMs < this.Settings.ClickCooldownMs)
            {
                return;
            }

            if (info.Key == this.lastClickedKey && nowMs - this.lastClickMs < this.Settings.SameItemCooldownMs)
            {
                this.pendingClickKey = 0;
                return;
            }

            Native.LeftClick();
            this.lastClickMs = nowMs;
            this.lastClickedKey = info.Key;
            this.pendingClickKey = 0;
        }

        /// <inheritdoc />
        public override void DrawSettings()
        {
            ImGui.Checkbox(
                this.PluginText.Label("settings.require_hold_key", "Require holding key to pick up", "PHReqHold"),
                ref this.Settings.RequireHoldKey);
            ImGuiHelper.ToolTip(this.PluginText.T(
                "settings.require_hold_key.tooltip",
                "When on, items are only clicked while the pickup key is held.\nWhen off, matching items are auto-clicked whenever your cursor is over them."));

            ImGui.Text(this.PluginText.T("settings.pickup_key", "Pickup key (hold)"));
            ImGui.SameLine();
            ImGuiHelper.NonContinuousEnumComboBox("##PHPickupKey", ref this.Settings.PickupHoldKey);

            ImGui.Text(this.PluginText.T("settings.add_key", "Add hovered item to whitelist key"));
            ImGui.SameLine();
            ImGuiHelper.NonContinuousEnumComboBox("##PHAddKey", ref this.Settings.AddHoveredKey);

            ImGui.Checkbox(
                this.PluginText.Label("settings.block_panel", "Block pickup while a large panel is open", "PHBlockPanel"),
                ref this.Settings.BlockWhenLargePanelOpen);

            ImGui.SliderInt(
                this.PluginText.Label("settings.max_distance", "Max pickup distance", "PHMaxDist"),
                ref this.Settings.MaxPickupDistance, 0, 300);
            ImGuiHelper.ToolTip(this.PluginText.T(
                "settings.max_distance.tooltip",
                "Items farther than this grid distance from your character are ignored.\n0 = no limit."));

            ImGui.SliderInt(
                this.PluginText.Label("settings.min_delay", "Min pickup delay (ms)", "PHMinDelay"),
                ref this.Settings.MinPickupDelayMs, 0, 500);
            ImGui.SliderInt(
                this.PluginText.Label("settings.max_delay", "Max pickup delay (ms)", "PHMaxDelay"),
                ref this.Settings.MaxPickupDelayMs, 0, 500);
            ImGuiHelper.ToolTip(this.PluginText.T(
                "settings.delay.tooltip",
                "A random delay in this range is waited between detecting a matching item and clicking it."));

            ImGui.SliderInt(
                this.PluginText.Label("settings.click_cooldown", "Click cooldown (ms)", "PHCd"),
                ref this.Settings.ClickCooldownMs, 20, 500);
            ImGui.SliderInt(
                this.PluginText.Label("settings.same_item_cooldown", "Same-item cooldown (ms)", "PHSameCd"),
                ref this.Settings.SameItemCooldownMs, 100, 2000);

            ImGui.Checkbox(
                this.PluginText.Label("settings.debug_window", "Show debug window", "PHDbg"),
                ref this.Settings.ShowDebugWindow);

            if (ImGui.CollapsingHeader(this.PluginText.Title("settings.rarity", "Rarity", "PHRarity")))
            {
                ImGui.Checkbox(this.PluginText.Label("settings.rarity.normal", "Normal", "PHRarN"), ref this.Settings.PickupNormal);
                ImGui.Checkbox(this.PluginText.Label("settings.rarity.magic", "Magic", "PHRarM"), ref this.Settings.PickupMagic);
                ImGui.Checkbox(this.PluginText.Label("settings.rarity.rare", "Rare", "PHRarR"), ref this.Settings.PickupRare);
                ImGui.Checkbox(this.PluginText.Label("settings.rarity.unique", "Unique", "PHRarU"), ref this.Settings.PickupUnique);
            }

            if (ImGui.CollapsingHeader(this.PluginText.Title("settings.categories", "Categories", "PHCats")))
            {
                foreach (var cat in this.AllCategories())
                {
                    var enabled = this.Settings.EnabledCategories.Contains(cat);
                    if (ImGui.Checkbox($"{cat}##cat_{cat}", ref enabled))
                    {
                        if (enabled)
                        {
                            this.Settings.EnabledCategories.Add(cat);
                        }
                        else
                        {
                            this.Settings.EnabledCategories.Remove(cat);
                        }
                    }
                }

                ImGui.TextDisabled(this.PluginText.T(
                    "settings.categories.hint",
                    "Hover an item in-game to auto-discover its category here."));
            }

            if (ImGui.CollapsingHeader(this.PluginText.Title("settings.whitelist", "Whitelist", "PHWl")))
            {
                ImGui.TextDisabled(this.PluginText.F(
                    "settings.whitelist.count",
                    "{0} item(s). Hover an item in-game and press the add key.",
                    this.Settings.Whitelist.Count));

                string? toRemove = null;
                foreach (var kv in this.Settings.Whitelist)
                {
                    if (ImGui.SmallButton(this.PluginText.Label("button.delete", "X", kv.Key)))
                    {
                        toRemove = kv.Key;
                    }

                    ImGui.SameLine();
                    ImGui.Text($"{kv.Value}  ({kv.Key})");
                }

                if (toRemove != null)
                {
                    this.Settings.Whitelist.Remove(toRemove);
                }
            }
        }

        private static Item? ReadFreshItem(IntPtr itemAddress)
        {
            if (itemAddress == IntPtr.Zero)
            {
                return null;
            }

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

        private bool CanAttemptPickup(bool haveItem, in HoveredItem info)
        {
            if (!haveItem)
            {
                return false;
            }

            if (this.Settings.RequireHoldKey && !Native.IsKeyDown((int)this.Settings.PickupHoldKey))
            {
                return false;
            }

            if (this.Settings.BlockWhenLargePanelOpen &&
                Core.States.InGameStateObject.GameUi.IsAnyLargePanelOpen)
            {
                return false;
            }

            if (this.Settings.MaxPickupDistance > 0 && info.Distance > this.Settings.MaxPickupDistance)
            {
                return false;
            }

            return this.ShouldPickup(info, out _);
        }

        private int NextRandomDelayMs()
        {
            var min = Math.Max(0, this.Settings.MinPickupDelayMs);
            var max = Math.Max(min, this.Settings.MaxPickupDelayMs);
            return min == max ? min : Random.Shared.Next(min, max + 1);
        }

        private IEnumerable<string> AllCategories()
        {
            return ItemClassifier.CuratedCategories
                .Concat(this.Settings.KnownCategories)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase);
        }

        private bool RarityEnabled(Rarity rarity)
        {
            return rarity switch
            {
                Rarity.Normal => this.Settings.PickupNormal,
                Rarity.Magic => this.Settings.PickupMagic,
                Rarity.Rare => this.Settings.PickupRare,
                Rarity.Unique => this.Settings.PickupUnique,
                _ => false,
            };
        }

        private bool ShouldPickup(in HoveredItem info, out string reason)
        {
            if (!string.IsNullOrEmpty(info.InternalName) && this.Settings.Whitelist.ContainsKey(info.InternalName))
            {
                reason = "whitelist";
                return true;
            }

            if (!string.IsNullOrEmpty(info.Category) && this.Settings.EnabledCategories.Contains(info.Category))
            {
                reason = $"category:{info.Category}";
                return true;
            }

            if (this.RarityEnabled(info.Rarity))
            {
                reason = $"rarity:{info.Rarity}";
                return true;
            }

            reason = string.Empty;
            return false;
        }

        private bool TryReadHoveredItem(out HoveredItem result)
        {
            result = default;
            var hovered = Core.States.InGameStateObject.MouseOverEntity;
            if (!hovered.IsValid)
            {
                return false;
            }

            Entity? itemEntity;
            long dedupKey;
            if (hovered.TryGetComponent<WorldItem>(out var worldItem) && worldItem.ItemEntityAddress != IntPtr.Zero)
            {
                itemEntity = ReadFreshItem(worldItem.ItemEntityAddress);
                dedupKey = hovered.Id != 0 ? hovered.Id : worldItem.ItemEntityAddress.ToInt64();
            }
            else if (hovered.TryGetComponent<Base>(out _))
            {
                // Fallback: the hovered entity is itself the item (no WorldItem wrapper).
                itemEntity = hovered;
                dedupKey = hovered.Id != 0 ? hovered.Id : hovered.Address.ToInt64();
            }
            else
            {
                return false;
            }

            if (itemEntity == null)
            {
                return false;
            }

            var internalName = itemEntity.TryGetComponent<Base>(out var baseComp) ? baseComp.InternalName : string.Empty;
            var displayName = baseComp != null && !string.IsNullOrEmpty(baseComp.BaseItemName)
                ? baseComp.BaseItemName
                : internalName;
            var rarity = itemEntity.TryGetComponent<Mods>(out var mods) ? mods.Rarity : Rarity.Normal;
            var stack = itemEntity.TryGetComponent<Stack>(out var stackComp) && stackComp.Count > 1 ? stackComp.Count : 1;
            var path = itemEntity.Path ?? string.Empty;

            // Distance is measured from the player to the ground (wrapper) entity, which carries
            // the world position — the inner item entity has none.
            var player = Core.States.InGameStateObject.CurrentAreaInstance.Player;
            var distance = player.IsValid ? player.DistanceFrom(hovered) : 0;

            result = new HoveredItem
            {
                Valid = true,
                Key = dedupKey,
                InternalName = internalName,
                DisplayName = displayName,
                Path = path,
                Category = ItemClassifier.CategoryOf(path),
                Rarity = rarity,
                Stack = stack,
                Distance = distance,
            };
            return true;
        }

        private void DrawDebugWindow(bool haveItem, HoveredItem info)
        {
            ImGui.SetNextWindowSize(new Vector2(380, 320), ImGuiCond.FirstUseEver);
            if (ImGui.Begin(
                this.PluginText.Title("window.debug", "PickupHelper Debug", "PHDebugWindow"),
                ref this.Settings.ShowDebugWindow))
            {
                var hovered = Core.States.InGameStateObject.MouseOverEntity;
                ImGui.Text($"MouseOverEntity valid: {hovered.IsValid}");
                if (hovered.IsValid)
                {
                    ImGui.Text($"Hovered entity path: {hovered.Path}");
                }

                ImGui.Text($"Large panel open: {Core.States.InGameStateObject.GameUi.IsAnyLargePanelOpen}");

                ImGui.Separator();
                if (!haveItem)
                {
                    ImGui.Text("No ground item under cursor.");
                }
                else
                {
                    ImGui.Text($"Display: {info.DisplayName}");
                    ImGui.Text($"Internal: {info.InternalName}");
                    ImGui.Text($"Path: {info.Path}");
                    ImGui.Text($"Category: {info.Category}");
                    ImGui.Text($"Rarity: {info.Rarity}");
                    ImGui.Text($"Stack: {info.Stack}");
                    ImGui.Text($"Distance: {info.Distance}");
                    var match = this.ShouldPickup(info, out var reason);
                    ImGui.Text($"Filter match: {match}  ({reason})");
                    ImGui.Text($"Eligible now: {this.CanAttemptPickup(haveItem, info)}");
                    ImGui.Separator();
                    ImGui.TextDisabled(this.PluginText.F(
                        "debug.press_to_add", "Press {0} to add to whitelist", this.Settings.AddHoveredKey));
                }
            }

            ImGui.End();
        }

        private struct HoveredItem
        {
            public bool Valid;
            public long Key;
            public string InternalName;
            public string DisplayName;
            public string Path;
            public string Category;
            public Rarity Rarity;
            public int Stack;
            public int Distance;
        }
    }
}
