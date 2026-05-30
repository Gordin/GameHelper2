namespace GameOffsets.Objects.Components
{
    using System;
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct ChestOffsets
    {
        [FieldOffset(0x000)] public ComponentHeader Header;
        [FieldOffset(0x160)] public IntPtr ChestsDataPtr;
        [FieldOffset(0x168)] public bool IsOpened;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct ChestsStructInternal
    {
        [FieldOffset(0x21)] public bool IsLabelVisible;
        [FieldOffset(0x50)] public IntPtr StrongboxDatPtr;
    }
}
