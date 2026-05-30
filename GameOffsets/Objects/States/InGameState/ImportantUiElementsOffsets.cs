namespace GameOffsets.Objects.States.InGameState
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    ///     All offsets over here are UiElements.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct ImportantUiElementsOffsets
    {
        [FieldOffset(0x640)] public IntPtr ChatParentPtr;
        [FieldOffset(0x6B0)] public IntPtr PassiveSkillTreePanel;
        [FieldOffset(0x700)] public IntPtr LargeMapParentPtr;
        [FieldOffset(0x768)] public IntPtr LargeMapCenterRootPtr;
        [FieldOffset(0x7C8)] public IntPtr MiniMapParentPtr;
        [FieldOffset(0x7B8)] public IntPtr LargeMapVisibilityPtr;
        [FieldOffset(0xA50)] public IntPtr LargeMapCenterPtr;
        [FieldOffset(0xA50)] public IntPtr LargeMapVerticalCenterPtr;
        [FieldOffset(0xAA8)] public IntPtr ControllerModeMapParentPtr;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct LargeMapParentStruct
    {
        [FieldOffset(0x550)] public IntPtr LargeMapPtr;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct ControllerModeMapParentStruct
    {
        [FieldOffset(0x28)] public IntPtr LargeMapPtr;
        [FieldOffset(0x30)] public IntPtr MiniMapPtr;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct MiniMapParentStruct
    {
        [FieldOffset(0x390)] public IntPtr MiniMapPtr;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct PassiveSkillTreeStruct
    {
        // TODO/Update: cache location isn't working, wait for EA to be over to see if cache location start
        // working again...updated to use NonCache location.
        // [FieldOffset(0x5B0)] public IntPtr SkillTreeNodeUiElements; // 3nd child ~ reading from cache location
        public static int ChildNumber = (3 - 1) * 0x08;
    }
}
