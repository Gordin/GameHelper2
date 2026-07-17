// <copyright file="AtlasRegionButton.cs" company="None">
// Copyright (c) None. All rights reserved.
// Licensed under the GPL-3.0 license. See LICENSE file in the project root for full license information.
// </copyright>

namespace GameHelper.RemoteObjects.States.InGameStateObjects;

using GameOffsets.Natives;
using System;

/// <summary>Describes an atlas region action button such as an Uncharted Waters ship.</summary>
public sealed class AtlasRegionButton(
    int index,
    IntPtr address,
    StdTuple2D<int> gridPosition,
    bool isVisible)
{
    /// <summary>Gets the child index in the atlas panel.</summary>
    public int Index { get; } = index;

    /// <summary>Gets the remote widget address.</summary>
    public IntPtr Address { get; } = address;

    /// <summary>Gets the atlas grid position the button reveals.</summary>
    public StdTuple2D<int> GridPosition { get; } = gridPosition;

    /// <summary>Gets a value indicating whether the game currently renders the button.</summary>
    public bool IsVisible { get; } = isVisible;
}
