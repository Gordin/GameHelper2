// <copyright file="WorldDataOffset.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameOffsets.Objects.States.InGameState
{
    using System;
    using System.Numerics;
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct WorldDataOffset
    {
        [FieldOffset(0x98)] public IntPtr WorldAreaDetailsPtr;
        [FieldOffset(0xA0)] public CameraStructure CameraStructurePtr;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct WorldAreaDetailsStruct
    {
        [FieldOffset(0x98)] public IntPtr WorldAreaDetailsRowPtr; // WorldArea.dat Offsets
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct CameraStructure
    {
        [FieldOffset(0x00)] public IntPtr CodePtr;
        // [FieldOffset(0x8)] public int Width;
        // [FieldOffset(0xC)] public int Height;

        // First value is changing when we change the screen size (ratio)
        // 4 bytes before the matrix doesn't change
        // matrix is duplicated, meaning when first matrix finish, 2nd one start with exact same values.
        [FieldOffset(0x100)] public Matrix4x4 WorldToScreenMatrix;
        // [FieldOffset(0xF0)] public Vector3 Position;
        // [FieldOffset(0x1C4)] public float ZFar;
    }
}
