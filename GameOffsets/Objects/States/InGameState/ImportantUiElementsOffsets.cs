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
        [FieldOffset(0x5C0)] public IntPtr ChatParentPtr;
        [FieldOffset(0x6B0)] public IntPtr PassiveSkillTreePanel;
        [FieldOffset(0x748)] public IntPtr MapParentPtr;
        [FieldOffset(0xAA8)] public IntPtr ControllerModeMapParentPtr;
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
