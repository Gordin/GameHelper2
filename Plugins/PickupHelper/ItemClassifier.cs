// <copyright file="ItemClassifier.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace PickupHelper
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    ///     Derives a coarse item category from the item's metadata path. GameHelper ships no
    ///     item-class table, so we categorize by the first path segment under <c>Metadata/Items/</c>.
    /// </summary>
    internal static class ItemClassifier
    {
        private const string ItemPathPrefix = "Metadata/Items/";

        /// <summary>
        ///     Renames raw path segments to friendlier category names (e.g. the in-game "Tablets"
        ///     live under <c>Metadata/Items/TowerAugment/</c>).
        /// </summary>
        private static readonly Dictionary<string, string> CategoryAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["TowerAugment"] = "Tablets",
        };

        /// <summary>
        ///     A best-effort curated list of common categories shown in the settings UI even
        ///     before any item has been hovered. The real, observed categories are discovered
        ///     at runtime and merged with this list.
        /// </summary>
        internal static readonly string[] CuratedCategories =
        {
            "Currency", "Gems", "Waystones", "Tablets", "SoulCores", "Sanctum", "Expedition",
            "Armours", "Weapons", "Rings", "Amulets", "Belts", "Flasks", "Jewels", "Quivers",
        };

        /// <summary>
        ///     Returns the first path segment under <c>Metadata/Items/</c>, or an empty string
        ///     when the path is not an item path.
        /// </summary>
        /// <param name="path">the item entity's metadata path.</param>
        /// <returns>the category segment (e.g. "Currency"), or empty.</returns>
        internal static string CategoryOf(string? path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith(ItemPathPrefix, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            var rest = path.Substring(ItemPathPrefix.Length);
            var slash = rest.IndexOf('/');
            var segment = slash > 0 ? rest.Substring(0, slash) : rest;
            return CategoryAliases.TryGetValue(segment, out var alias) ? alias : segment;
        }
    }
}
