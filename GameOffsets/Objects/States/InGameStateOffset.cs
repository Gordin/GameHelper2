namespace GameOffsets.Objects.States
{
    using System;
    using System.Runtime.InteropServices;

    // Ghidra function ref: search for "Abnormal disconnect: "
    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct InGameStateOffset
    {
        [FieldOffset(0x290)] public IntPtr AreaInstanceData; // contains area level
        [FieldOffset(0x368)] public IntPtr WorldData; // contains area name
        [FieldOffset(0x2F0)] public IntPtr UiRootStructPtr; // UiRootStruct
        [FieldOffset(0x318)] public IntPtr GamepadUiRootStructPtr; // UiRootStruct for controller mode
    }
    
    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct UiRootStruct
    {
        [FieldOffset(0x340)] public IntPtr UiRootPtr; // contains self pointer
        [FieldOffset(0xBE0)] public IntPtr GameUiPtr; // contains self pointer
        [FieldOffset(0xBE8)] public IntPtr GameUiControllerPtr; // contains self pointer
    }
}
