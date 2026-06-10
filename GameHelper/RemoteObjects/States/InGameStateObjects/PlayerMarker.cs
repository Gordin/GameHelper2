// <copyright file="PlayerMarker.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.States.InGameStateObjects
{
    using System;

    /// <summary>
    ///     Represents the "you are here" marker on the Atlas — a child of the
    ///     Atlas node-list container that renders on the player's current map node.
    ///     It is not a real map node (no grid, no MapId, no content).
    /// </summary>
    public sealed class PlayerMarker
    {
        internal PlayerMarker(int index, IntPtr address, int mapNodeIndex)
        {
            this.Index = index;
            this.Address = address;
            this.MapNodeIndex = mapNodeIndex;
        }

        /// <summary>
        ///     Gets the child index under <see cref="ImportantUiElements.Atlas" />.
        /// </summary>
        public int Index { get; }

        /// <summary>
        ///     Gets the underlying UiElement address (for position/size reads).
        /// </summary>
        public IntPtr Address { get; }

        /// <summary>
        ///     Gets the index of the <see cref="AtlasMapNode" /> this marker sits on,
        ///     or -1 when no node could be matched. Resolved each cache-refresh frame
        ///     by finding the nearest node center after a downward Y offset (the
        ///     marker renders above the node it marks).
        /// </summary>
        public int MapNodeIndex { get; }
    }
}
