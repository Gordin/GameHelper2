namespace GameOffsets.Objects.Components
{
    using System.Runtime.InteropServices;
    using Natives;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct PlayerOffsets
    {
        [FieldOffset(0x000)] public ComponentHeader Header;
        [FieldOffset(0x1B0)] public StdWString Name;
        [FieldOffset(0x1D8)] public int Xp;
        [FieldOffset(0x204)] public byte Level;
    }
}