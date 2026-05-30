namespace GameOffsets.Objects.Components
{
    using System;
    using System.Runtime.InteropServices;
    using GameOffsets.Natives;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct StateMachineComponentOffsets
    {
        [FieldOffset(0x000)]
        public ComponentHeader Header;

        // Pointer to the states array (raw)
        [FieldOffset(0x158)]
        public IntPtr StatesPtr;

        // Actual states stored as a StdVector
        [FieldOffset(0x160)]
        public StdVector StatesValues;
    }

    //[StructLayout(LayoutKind.Sequential, Pack = 1)]
    //public struct StateMachineStateStruct {
    //    public int StateId;     // Likely an enum or ID
    //    public int StateValue;  // Could be a bool, int, or timer depending on context
    //}
}