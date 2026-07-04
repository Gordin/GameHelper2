// <copyright file="PickupHelperSettings.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace PickupHelper
{
    using System;
    using System.Collections.Generic;
    using ClickableTransparentOverlay.Win32;
    using GameHelper.Plugin;

    /// <summary>
    ///     Settings for the <see cref="PickupHelperCore" /> plugin.
    /// </summary>
    public sealed class PickupHelperSettings : IPSettings
    {
        /// <summary>
        ///     Gets or sets a value indicating whether the pickup key must be held for clicks to happen.
        ///     When false, matching items are auto-clicked whenever the cursor is over them.
        /// </summary>
        public bool RequireHoldKey = true;

        /// <summary>
        ///     Gets or sets the key held (when <see cref="RequireHoldKey" />) to enable clicking.
        /// </summary>
        public VK PickupHoldKey = VK.F5;

        /// <summary>
        ///     Gets or sets the key that adds the currently hovered item to the whitelist.
        /// </summary>
        public VK AddHoveredKey = VK.INSERT;

        /// <summary>
        ///     Gets or sets a value indicating whether pickup is blocked while a large panel
        ///     (inventory, stash, character screen, etc.) is open.
        /// </summary>
        public bool BlockWhenLargePanelOpen = true;

        /// <summary>
        ///     Gets or sets the minimum grid distance from the player within which items are
        ///     eligible for pickup. Items farther than <see cref="MaxPickupDistance" /> are ignored.
        /// </summary>
        public int MaxPickupDistance = 50;

        /// <summary>
        ///     Gets or sets the minimum random delay (ms) between detecting a matching item and clicking it.
        /// </summary>
        public int MinPickupDelayMs = 5;

        /// <summary>
        ///     Gets or sets the maximum random delay (ms) between detecting a matching item and clicking it.
        /// </summary>
        public int MaxPickupDelayMs = 150;

        /// <summary>
        ///     Gets or sets the minimum milliseconds between any two automated clicks.
        /// </summary>
        public int ClickCooldownMs = 120;

        /// <summary>
        ///     Gets or sets the minimum milliseconds before the same item can be clicked again.
        /// </summary>
        public int SameItemCooldownMs = 600;

        /// <summary>
        ///     Gets or sets a value indicating whether normal-rarity items are picked up.
        /// </summary>
        public bool PickupNormal = false;

        /// <summary>
        ///     Gets or sets a value indicating whether magic-rarity items are picked up.
        /// </summary>
        public bool PickupMagic = false;

        /// <summary>
        ///     Gets or sets a value indicating whether rare-rarity items are picked up.
        /// </summary>
        public bool PickupRare = false;

        /// <summary>
        ///     Gets or sets a value indicating whether unique-rarity items are picked up.
        /// </summary>
        public bool PickupUnique = false;

        /// <summary>
        ///     Gets or sets the set of enabled item categories (path-derived top-level segment
        ///     under <c>Metadata/Items/</c>, e.g. "Currency", "Maps").
        /// </summary>
        public HashSet<string> EnabledCategories = new(StringComparer.OrdinalIgnoreCase)
        {
            "Currency", "Gems", "Tablets", "Waystones",
        };

        /// <summary>
        ///     Gets or sets the categories discovered from hovered items at runtime. Accumulated
        ///     so the settings UI can list categories that actually appear in-game.
        /// </summary>
        public HashSet<string> KnownCategories = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        ///     Gets or sets the explicit item whitelist: stable internal name -> display name.
        /// </summary>
        public Dictionary<string, string> Whitelist = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        ///     Gets or sets a value indicating whether the debug window is shown.
        /// </summary>
        public bool ShowDebugWindow = false;
    }
}
