namespace GameOffsets.Objects.Components
{
    using System;
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct ChargesOffsets
    {
        [FieldOffset(0x0000)] public ComponentHeader Header;
        [FieldOffset(0x0010)] public IntPtr ChargesInternalPtr; // ChargesInternalStruct
        [FieldOffset(0x0018)] public int current;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct ChargesInternalStruct
    {
        [FieldOffset(0x0018)] public int PerUseCharges;
    }
}