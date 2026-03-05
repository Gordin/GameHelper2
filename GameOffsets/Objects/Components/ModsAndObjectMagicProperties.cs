namespace GameOffsets.Objects.Components
{
    using System;
    using System.Runtime.InteropServices;
    using GameOffsets.Natives;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct ModsOffsets
    {
        [FieldOffset(0x000)] public ComponentHeader Header;
        [FieldOffset(0x000)] public ModsAndObjectMagicProperties Details0;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct ObjectMagicPropertiesOffsets
    {
        [FieldOffset(0x000)] public ComponentHeader Header;
        [FieldOffset(0x0B0)] public ModsAndObjectMagicProperties Details1;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct ModsAndObjectMagicProperties
    {
        [FieldOffset(0x94)] public int Rarity;
        [FieldOffset(0xA0)] public AllModsType Mods;
        [FieldOffset(0x148)] public StdVector StatsFromMods;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct AllModsType
    {
        public StdVector ImplicitMods; // ModArrayStruct
        public StdVector ExplicitMods; // ModArrayStruct
        public StdVector EnchantMods; // ModArrayStruct
        public StdVector HellscapeMods; // ModArrayStruct
        public StdVector CrucibleMods; // ModArrayStruct
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct ModArrayStruct
    {
        [FieldOffset(0x00)] public StdVector Values;
        [FieldOffset(0x18)] public int Value0;
        [FieldOffset(0x28)] public IntPtr ModsPtr; //// Mods.DAT file row
        [FieldOffset(0x30)] public IntPtr UselessPtr;
        [FieldOffset(0x38)] public IntPtr UselessPtr2;
    }
}
