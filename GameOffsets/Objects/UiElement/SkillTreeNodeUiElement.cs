namespace GameOffsets.Objects.UiElement
{
    using System;
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct SkillTreeNodeUiElementOffset
    {
        [FieldOffset(0x000)] public UiElementBaseOffset UiElementBase;
        [FieldOffset(0x2B8)] public IntPtr SkillInfo; // SkillInfoStruct
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct SkillInfoStruct
    {
        [FieldOffset(0x08)] public IntPtr PassiveSkillsDatRow; // PassiveSkillsDatStruct
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct PassiveSkillsDatStruct
    {
        [FieldOffset(0x30)] public short PassiveSkillGraphId;
    }
}
