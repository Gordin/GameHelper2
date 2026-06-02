namespace GameOffsets.Objects.States.InGameState
{
    using System;
    using System.Runtime.InteropServices;
    using Natives;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct ItemStruct
    {
        [FieldOffset(0x00)] public IntPtr VTablePtr;
        [FieldOffset(0x08)] public IntPtr EntityDetailsPtr;
        [FieldOffset(0x10)] public StdVector ComponentListPtr;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct EntityOffsets
    {
        [FieldOffset(0x00)] public ItemStruct ItemBase;
        [FieldOffset(0x30)] public StdVector UnknownListPtr;
        // [FieldOffset(0x70)] public IntPtr CurrentAreaInstanceOwner;
        [FieldOffset(0x88)] public uint Id;
        [FieldOffset(0x8C)] public byte IsValid; // 0x0C = Valid, 0x03 = Invalid
    }

    public static class EntityHelper
    {
        // 0th bit set = invalid. test byte ptr [rcx+5C],01
        // pass IsValid byte to this function.
        public static Func<byte, bool> IsValidEntity = param => { return !Util.isBitSetByte(param, 0); };

        public static Func<byte, bool> IsFriendly = param => { return (param & 0x7F) == 0x01; };
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct EntityDetails
    {
        [FieldOffset(0x08)] public StdWString name;
        [FieldOffset(0x28)] public IntPtr ComponentLookUpPtr;
    }

    // DRY this with LoadedFilesOffset
    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct ComponentLookUpStruct
    {
        [FieldOffset(0x00)] public IntPtr Unknown0;
        [FieldOffset(0x08)] public IntPtr Unknown1;
        [FieldOffset(0x10)] public StdVector Unknown2;
        [FieldOffset(0x28)] public StdBucket ComponentsNameAndIndex;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ComponentNameAndIndexStruct
    {
        public IntPtr NamePtr;
        public int Index;
        public int PAD_0xC;
    }
}
