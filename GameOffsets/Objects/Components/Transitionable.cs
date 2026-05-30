namespace GameOffsets.Objects.Components
{
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct TransitionableOffsets
    {

        [FieldOffset(0x000)] public ComponentHeader Header;
        [FieldOffset(0x120)] public short CurrentStateEnum;
    }
}
