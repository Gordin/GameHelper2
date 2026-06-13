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
        [FieldOffset(0x6D8)] public IntPtr LeftPanelPtr;
        [FieldOffset(0x6E0)] public IntPtr RightPanelPtr;
        [FieldOffset(0x730)] public IntPtr PassiveSkillTreePanel;
        [FieldOffset(0x7C8)] public IntPtr MapParentPtr;
        [FieldOffset(0x988)] public IntPtr WorldMapPanelPtr;
        [FieldOffset(0xB98)] public IntPtr ControllerModeMapParentPtr;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct MapParentStruct
    {
        [FieldOffset(0x28)] public IntPtr LargeMapPtr; // 1st child ~ reading from cache location
        [FieldOffset(0x30)] public IntPtr MiniMapPtr; // 2nd child ~ reading from cache location
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
