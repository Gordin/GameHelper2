namespace GameOffsets.Objects.FilesStructures
{
    using System;
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct GrantedEffectsPerLevelDatOffset
    {
        [FieldOffset(0x00)] public IntPtr GrantedEffectDatPtr; // GrantedEffectsDatOffset
    }
}
