namespace GameOffsets.Objects.States.InGameState
{
    using System;
    using System.Runtime.InteropServices;
    using Natives;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct PreInventoryStruct
    {
        [FieldOffset(0x10)] public IntPtr ptr;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct InventoryStruct
    {
        //[FieldOffset(0x00)] public int InventoryType;
        //[FieldOffset(0x04)] public int InventorySlot;
        //[FieldOffset(0x08)] public byte ShouldShowBoxes; // there are some other useless flags right after this byte.
        [FieldOffset(0x14C)] public StdTuple2D<int> TotalBoxes; // X * Y * 8 = StdVector.ItemList.Length.

        //[FieldOffset(0x18)] public int CanPutItem0; // setting this to 1 will block user from putting items.
        //[FieldOffset(0x1C)] public int CanPutItem1; // same as above.
        [FieldOffset(0x170)] public StdVector ItemList;

        // use ItemList, reading StdMap requires more reads than reading StdVector
        // [FieldOffset(0x180)] public StdMap ItemsHashMap;

        // [FieldOffset(0x58)] public StdVector WierdUiElementsData;
        [FieldOffset(0x1E8)] public int ServerRequestCounter;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ItemHashKey
    {
        public int ServerRequestCounterSnapShot;
        public int PAD_8;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ItemHashValue
    {
        public IntPtr InventoryItemStructPtr;
        public IntPtr UselessItemPtr;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct InventoryItemStruct
    {
        [FieldOffset(0x00)] public IntPtr Item; // ItemStruct in EntityOffsets.cs
        [FieldOffset(0x08)] public StdTuple2D<int> SlotStart;
        [FieldOffset(0x10)] public StdTuple2D<int> SlotEnd;
        [FieldOffset(0x18)] public int ServerRequestCounterSnapShot;
    }
}