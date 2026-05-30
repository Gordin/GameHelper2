namespace GameOffsets.Objects.Components
{
    using System;
    using System.Runtime.InteropServices;
    using GameOffsets.Natives;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct StatsOffsets // component is 0x40 bytes long
    {
        [FieldOffset(0x000)] public ComponentHeader Header;
        [FieldOffset(0x160)] public IntPtr StatsChangedByItemsPtr; // StatsStructInternal
        [FieldOffset(0x168)] public int CurrentWeaponIndex; // 0 or 1 when we change weapons.
        [FieldOffset(0x170)] public IntPtr ShapeshiftFormsRowPtr; // Data/ShapeshiftForms.dat ~ 0x00 if Player isn't shape shifted
        [FieldOffset(0x198)] public IntPtr StatsChangedByBuffAndActions; // StatsStructInternal
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct StatsStructInternal
    {
        [FieldOffset(0xF8)] public StdVector Stats; // type StatArrayStruct
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct StatArrayStruct
    {
        public int key;
        public int value;
    }
}
