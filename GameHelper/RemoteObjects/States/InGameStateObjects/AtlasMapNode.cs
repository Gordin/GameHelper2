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
        ///     Gets connected Atlas grid positions read from the Atlas connection data, when available.
        /// </summary>
        public IReadOnlyList<StdTuple2D<int>> ConnectedGridPositions { get; }
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
