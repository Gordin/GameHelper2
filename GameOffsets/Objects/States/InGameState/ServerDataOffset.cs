namespace GameOffsets.Objects.States.InGameState
{
    using System;
    using System.Runtime.InteropServices;
    using Natives;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct ServerDataOffsets
    {
        // Layout not consumed yet; keep the recovered location without guessing its type.
        public const int AreaMarkersOffset = 0x21C8;
        [FieldOffset(0x48)] public StdVector PlayerServerDataPtr;
    }
    
    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct ServerDataStructure
    {
        public const int WornMtxSlotsOffset = 0x7B50;
        [FieldOffset(0x320)] public StdVector PlayerInventories; // InventoryArrayStruct
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct InventoryArrayStruct
    {
        [FieldOffset(0x00)] public int InventoryId;
        [FieldOffset(0x04)] public int PAD_0;
        [FieldOffset(0x08)] public IntPtr InventoryPtr0; // InventoryStruct
        [FieldOffset(0x10)] public IntPtr InventoryPtr1; // this points to 0x10 bytes before InventoryPtr0
    }
}
