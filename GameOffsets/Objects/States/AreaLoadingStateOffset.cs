namespace GameOffsets.Objects.States
{
    using System;
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct AreaLoadingStateOffset
    {
        [FieldOffset(0x770)] public int IsLoading; // save the structure data before, during and after area change loading screen
        [FieldOffset(0xE20)] public uint TotalLoadingScreenTimeMs; // increases when area changes
        [FieldOffset(0xEA0)] public IntPtr CurrentAreaDetailsPtr; // WorldArea.dat row.
    }
}
