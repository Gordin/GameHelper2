// <copyright file="AtlasMapNode.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.States.InGameStateObjects
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using GameHelper.Utils;
    using GameOffsets.Natives;

    /// <summary>
    ///     Snapshot of one endgame Atlas map node exposed for plugins.
    /// </summary>
    public sealed class AtlasMapNode
    {
        internal AtlasMapNode(
            int index,
            IntPtr address,
            string mapId,
            StdTuple2D<int> gridPosition,
            byte biomeId,
            AtlasMapNodeState state,
            IReadOnlyList<string> contentNames,
            IReadOnlyList<IntPtr> badgeAddresses,
            IReadOnlyList<uint> contentTokens,
            IReadOnlyList<uint> badgeContentIds,
            IReadOnlyList<StdTuple2D<int>> connectedGridPositions)
        {
            this.Index = index;
            this.Address = address;
            this.MapId = mapId;
            this.GridPosition = gridPosition;
            this.BiomeId = biomeId;
            this.State = state;
            this.ContentNames = new ReadOnlyCollection<string>(new List<string>(contentNames));
            this.BadgeAddresses = new ReadOnlyCollection<IntPtr>(new List<IntPtr>(badgeAddresses));
            this.ContentTokens = new ReadOnlyCollection<uint>(new List<uint>(contentTokens));
            this.BadgeContentIds = new ReadOnlyCollection<uint>(new List<uint>(badgeContentIds));
            this.ConnectedGridPositions = new ReadOnlyCollection<StdTuple2D<int>>(new List<StdTuple2D<int>>(connectedGridPositions));
        }

        /// <summary>
        ///     Gets the child index under <see cref="ImportantUiElements.Atlas" />.
        /// </summary>
        public int Index { get; }

        /// <summary>
        ///     Gets the underlying Atlas node UiElement address.
        /// </summary>
        public IntPtr Address { get; }

        /// <summary>
        ///     Gets the internal map id/name read from the Atlas node metadata.
        /// </summary>
        public string MapId { get; }

        /// <summary>
        ///     Gets the internal map id/name read from the Atlas node metadata.
        /// </summary>
        public string Name => this.MapId;

        /// <summary>
        ///     Gets the human-readable in-game name resolved from <see cref="MapId" />,
        ///     falling back to the raw id when no mapping is known.
        /// </summary>
        public string DisplayName => WorldAreaNames.GetDisplayName(this.MapId);

        /// <summary>
        ///     Gets the map type classification ("normal" or "unique"),
        ///     resolved from <see cref="MapId" /> via the embedded tags database.
        /// </summary>
        public string Type => WorldAreaTags.GetMeta(this.MapId)?.Type ?? "normal";

        /// <summary>
        ///     Gets feature tags (e.g. "lineage", "arbiter") for this map,
        ///     resolved from <see cref="MapId" /> via the embedded tags database.
        /// </summary>
        public IReadOnlyList<string> Tags => WorldAreaTags.GetMeta(this.MapId)?.Tags ?? Array.Empty<string>();

        /// <summary>
        ///     Gets the node's Atlas grid position.
        /// </summary>
        public StdTuple2D<int> GridPosition { get; }

        /// <summary>
        ///     Gets the biome id read from the Atlas node metadata.
        /// </summary>
        public byte BiomeId { get; }

        /// <summary>
        ///     Gets the discovered completion/accessibility state.
        /// </summary>
        public AtlasMapNodeState State { get; }

        /// <summary>
        ///     Gets raw content/badge names attached to this node, when known.
        /// </summary>
        public IReadOnlyList<string> ContentNames { get; }

        /// <summary>
        ///     Gets badge UiElement addresses under Atlas node child path [0][0].
        /// </summary>
        public IReadOnlyList<IntPtr> BadgeAddresses { get; }

        /// <summary>
        ///     Gets the number of badge UiElements under Atlas node child path [0][0].
        /// </summary>
        public int BadgeCount => this.BadgeAddresses.Count;

        /// <summary>
        ///     Gets the raw per-node content tokens (class-1 content: the StdVector&lt;u32&gt; living
        ///     on the Atlas node UiElement at element+0x350). Populated only for visible/rendered nodes.
        ///     Resolution of a token to a content name is left to consumers.
        /// </summary>
        public IReadOnlyList<uint> ContentTokens { get; }

        /// <summary>
        ///     Gets the raw class-2 (badge) content ids — the u32 at badge+0x188 of each
        ///     node[0][0] child. The high word is a constant category; the content type is the
        ///     low 16 bits. Resolution of an id to a content name is left to consumers.
        /// </summary>
        public IReadOnlyList<uint> BadgeContentIds { get; }

        /// <summary>
        ///     Gets connected Atlas grid positions read from the Atlas connection data, when available.
        /// </summary>
        public IReadOnlyList<StdTuple2D<int>> ConnectedGridPositions { get; }

        // Content-token / badge model (verified live for PoE2 0.5.x):
        //   * A content TOKEN (see ContentTokens) is one effect line. Its low 16 bits are the effect id
        //     and its high 16 bits normally encode the magnitude as (magnitude × 64) — i.e. magnitude =
        //     high16/64 (1 for plain effects, the number in the text for "N additional"/"N% …", 100 for
        //     binary "always"/"doubles" effects). The Delirious token (0x685A) is an exception: its top
        //     three high-word bits are a counter, so its magnitude is (high16 & 0x1FFF)/64. So the same
        //     effect at a different magnitude is a different full u32; we key on the low 16 bits and
        //     substitute the magnitude into a "{0}" template.
        //   * A BADGE (see BadgeContentIds) is the named content (the bold tooltip title). Its high word
        //     is a constant 0x0002 category tag; the content id is the low 16 bits, which we key on.
        // A node's tooltip = its badge (title) plus one token per effect line.

        /// <summary>
        ///     Known effect-token id (low 16 bits of a value in <see cref="ContentTokens" />) → effect-text
        ///     template. A "{0}" placeholder is replaced with the token's magnitude (high16 / 64) at
        ///     resolve time. Unmapped ids have no entry (callers fall back to the raw hex value).
        /// </summary>
        private static readonly Dictionary<uint, string> ContentTokenEffects = new()
        {
            [0x65F4] = "{0} Atlas Point",
            [0x686E] = "Delirium",
            [0x4C58] = "Powerful Map Boss",
            [0x4C59] = "Powerful Map Boss",
            [0x6870] = "Ritual Altars",
            [0x6873] = "Ritual Altars",
            [0x686F] = "Abysses",
            [0x6872] = "Area contains Abysses",
            [0x6875] = "Area contains Breaches",
            [0x60C1] = "Breach Stronghold",
            [0x60C4] = "Breach Stronghold",
            [0x3A5D] = "Hive Fortress",
            [0x3A5E] = "Breach Hive Fortress",
            [0x6760] = "Map Boss drops a Djinn Barya",
            [0x0963] = "Contains {0} additional Shrines",
            [0x3897] = "Map Boss is Possessed",
            [0x127B] = "Map Boss drops a Unique item",
            [0x0A8C] = "Contains {0} additional Azmeri Spirits",
            [0x6762] = "Currency found is replaced with rarer varieties",
            [0x6157] = "Contains a reflection of the Map Boss",
            [0x615A] = "Grand Mirror",
            [0x6714] = "Use the Grand Mirror to access",
            [0x6503] = "Also counts as a Grass Area",
            [0x6505] = "Also counts as a Swamp Area",
            [0x6502] = "Also counts as a Mountain Area",
            [0x6506] = "Also counts as a Desert Area",
            [0x6504] = "Also counts as a Forest Area",
            [0x4E88] = "Doubles Effect of Tablets used on Area",
            [0x634A] = "Contains {0} additional Map Bosses throughout the area",
            [0x320F] = "All Monsters are at least Magic",
            [0x1282] = "Area has {0} additional random Waystone Modifiers",
            [0x04D8] = "{0}% increased Rarity of items found in area",
            [0x5E28] = "Contains {0} additional Summoning Circles",
            [0x61C7] = "Summoning Circles always summon an additional Boss",
            [0x1247] = "Contains {0} additional Essence",
            [0x634D] = "Essences transfer to a random Unique Monster on death",
            [0x6871] = "Area contains Vaal Beacons",
            [0x6874] = "Vaal Beacons",
            [0x6638] = "Elemental Shrines do not appear in area",
            [0x3E16] = "Shrine Duration increased by {0}%",
            [0x6244] = "Shrines release an Azmeri Spirit when activated",
        };

        /// <summary>
        ///     Known content id (low 16 bits of a value in <see cref="BadgeContentIds" />) → named-content
        ///     title (the bold tooltip header). Unmapped ids have no entry (callers fall back to hex).
        /// </summary>
        private static readonly Dictionary<uint, string> BadgeContentNames = new()
        {
            [0x0064] = "Powerful Map Boss",
            [0x008E] = "Sekhema's Student",
            [0x007D] = "Power of Faith",
            [0x008F] = "Azmeri Champion",
            [0x0088] = "Breach Hive",
            [0x008C] = "Monstrous Treasure",
            [0x0094] = "Swarming Spirits",
            [0x0091] = "Glimmering Mutation",
            [0x0070] = "Essence Trove",
            [0x03E8] = "Corruption",
            [0x6157] = "Grand Mirror",
            [0x009A] = "Mountain Influence",
            [0x009B] = "Grass Influence",
            [0x009C] = "Forest Influence",
            [0x009D] = "Swamp Influence",
            [0x009E] = "Desert Influence",
            [0x0097] = "Energized Ley Lines",
            [0x0095] = "Power Struggle",
            [0x0073] = "Arcane Hordes",
            [0x0096] = "Corrupted Mirage",
            [0x008B] = "Affluent Armies",
            [0x0085] = "Scattered Stones",
            [0x0084] = "Twinned Terrors",
            [0x0077] = "Indomitable Essence",
            [0x007F] = "Zealous Reverence",
            [0x0075] = "Nature Shrines",
        };

        /// <summary>
        ///     Resolves a content token to its effect text, or <c>null</c> when the token is unmapped.
        ///     The token's low 16 bits select the effect; a "{0}" in the template is replaced with the
        ///     magnitude (high 16 bits / 64).
        /// </summary>
        /// <param name="token">raw content-token value (see <see cref="ContentTokens" />).</param>
        /// <returns>the resolved effect text, or <c>null</c> if the effect id is unknown.</returns>
        public static string? GetContentTokenName(uint token)
        {
            if (!ContentTokenEffects.TryGetValue(token & 0xFFFFu, out var template))
            {
                return null;
            }

            if (template.Contains("{0}", StringComparison.Ordinal))
            {
                var magnitude = (token >> 16) / 64;
                return template.Replace("{0}", magnitude.ToString());
            }

            return template;
        }

        /// <summary>
        ///     Resolves a badge content id to its named-content title, or <c>null</c> when unmapped.
        ///     Keys on the low 16 bits (the high word is the constant 0x0002 category tag).
        /// </summary>
        /// <param name="id">raw badge-content value (see <see cref="BadgeContentIds" />).</param>
        /// <returns>the resolved content title, or <c>null</c> if the content id is unknown.</returns>
        public static string? GetBadgeContentName(uint id) =>
            BadgeContentNames.TryGetValue(id & 0xFFFFu, out var name) ? name : null;

        /// <summary>
        ///     Returns this node's content as a merged, de-duplicated display list: each badge resolves to
        ///     its content title and each token to its effect text (raw hex when unmapped). Titles
        ///     (from <see cref="BadgeContentIds" />) come first, then the effect lines (from
        ///     <see cref="ContentTokens" />), mirroring the in-game tooltip; duplicates are removed.
        /// </summary>
        /// <param name="includeUnmapped">
        ///     when <c>true</c>, every raw value is shown alongside its mapped name (and unmapped
        ///     values are shown as raw hex); when <c>false</c>, only resolved names are returned.
        /// </param>
        /// <returns>the merged display names, badge-titles-then-token-effects, with duplicates removed.</returns>
        public IReadOnlyList<string> GetContentDisplayNames(bool includeUnmapped = true)
        {
            var result = new List<string>();
            foreach (var id in this.BadgeContentIds)
            {
                var name = GetBadgeContentName(id);
                if (name == null)
                {
                    if (!includeUnmapped)
                    {
                        continue;
                    }

                    name = $"0x{id:X8}";
                }
                else if (includeUnmapped)
                {
                    name = $"{name} [0x{id:X8}]";
                }

                if (!result.Contains(name))
                {
                    result.Add(name);
                }
            }

            var deliriousTokens = new List<uint>();
            var deliriousPercent = 0u;
            foreach (var token in this.ContentTokens)
            {
                if ((token & 0xFFFFu) == 0x685Au)
                {
                    deliriousTokens.Add(token);
                    deliriousPercent = Math.Max(deliriousPercent, ((token >> 16) & 0x1FFFu) / 64u);
                    continue;
                }

                var name = GetContentTokenName(token);
                if (name == null)
                {
                    if (!includeUnmapped)
                    {
                        continue;
                    }

                    name = $"0x{token:X8}";
                }
                else if (includeUnmapped)
                {
                    name = $"{name} [0x{token:X8}]";
                }

                if (!result.Contains(name))
                {
                    result.Add(name);
                }
            }

            if (deliriousTokens.Count > 0)
            {
                if (includeUnmapped)
                {
                    foreach (var token in deliriousTokens)
                    {
                        var percent = ((token >> 16) & 0x1FFFu) / 64u;
                        var name = percent > 0 ? $"{percent}% Delirious" : "Delirious";
                        var debugName = $"{name} [0x{token:X8}]";
                        if (!result.Contains(debugName))
                        {
                            result.Add(debugName);
                        }
                    }
                }
                else
                {
                    result.Add(deliriousPercent > 0 ? $"{deliriousPercent}% Delirious" : "Delirious");
                }
            }

            return result;
        }
    }

    /// <summary>
    ///     Observable Atlas map node states.
    /// </summary>
    public enum AtlasMapNodeState : ushort
    {
        None = 0x0000,
        AccessibleNow = 0x0001,
        CompletedBase = 0x0002,
    }
}
