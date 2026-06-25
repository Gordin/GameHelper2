namespace GameOffsets.Objects.Components
{
    using System;
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct AnimatedOffsets
    {
        [FieldOffset(0x0000)] public ComponentHeader Header;
        [FieldOffset(0x0280)] public IntPtr AnimatedEntityPtr;

        // Points to the model-info object that ultimately references the loaded .ao model file
        // (see AnimatedModelInfoOffsets). The .ao path is what distinguishes otherwise-identical
        // entities that share a metadata path (e.g. the different ExpeditionMarker flag models).
        [FieldOffset(0x0358)] public IntPtr ModelInfoPtr;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct AnimatedModelInfoOffsets
    {
        // Points to a file record (FileInfoValueStruct layout, StdWString Name at +0x08) for
        // the loaded .ao model file.
        [FieldOffset(0x0018)] public IntPtr ModelFileRecordPtr;
    }
}
