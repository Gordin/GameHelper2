namespace GameOffsets.Objects.FilesStructures
{
    using System;
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct BuffDefinitionsOffset
    {
        [FieldOffset(0x00)] public IntPtr Name;
        [FieldOffset(0x67)] public byte BuffType;
    }
}
