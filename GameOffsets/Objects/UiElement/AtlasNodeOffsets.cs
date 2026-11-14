// <copyright file="AtlasNodeOffsets.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameOffsets.Objects.UiElement
{
    using System;
    using System.Runtime.InteropServices;
    using GameOffsets.Natives;

    /// <summary>Atlas map-node widget layout (not region-action buttons).</summary>
    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct AtlasNodeOffsets
    {
        public const int DataOffset = 0x60;
        [FieldOffset(0x320)] public StdTuple2D<int> GridPosition;
        [FieldOffset(0x360)] public StdVector ContentVector;
        [FieldOffset(0x378)] public AtlasBadgeRange BadgeVector;
    }

    /// <summary>Known begin/end portion of the persistent badge byte vector.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct AtlasBadgeRange
    {
        public IntPtr Begin;
        public IntPtr End;
    }

    /// <summary>Node data relative to widget + 0x60.</summary>
    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct AtlasNodeDataOffsets
    {
        [FieldOffset(0x290)] public IntPtr MapDataPtr;
        [FieldOffset(0x2A0)] public IntPtr WorldAreasRowPtr;
        [FieldOffset(0x2A8)] public IntPtr WorldAreasTablePtr;
        [FieldOffset(0x2B0)] public IntPtr AtlasRowPtr;
        [FieldOffset(0x2CE)] public byte BiomeId;
        [FieldOffset(0x2CF)] public byte StatusBits;
    }
}
