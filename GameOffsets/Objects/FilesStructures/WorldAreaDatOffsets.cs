namespace GameOffsets.Objects.FilesStructures
{
    using System;
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct WorldAreaDatOffsets
    {
        [FieldOffset(0x00)] public IntPtr IdPtr;
        [FieldOffset(0x08)] public IntPtr NamePtr;
        [FieldOffset(0x10)] public int Act;
        [FieldOffset(0x14)] public bool IsTown;
        [FieldOffset(0x15)] public bool HasWaypoint;
    }
}