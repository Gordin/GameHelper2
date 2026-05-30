namespace GameOffsets.Objects
{
    using System;
    using System.Runtime.InteropServices;
    using System.Runtime.CompilerServices;
    using Natives;

    public static class GameStateHelper
    {
        public const int TOTAL_STATES = 12;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct GameStateStaticOffset
    {
        [FieldOffset(0x00)] public IntPtr GameState;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct GameStateOffset
    {
        [FieldOffset(0x08)] public StdVector CurrentStatePtr; // Used in RemoteObject -> CurrentState
        [FieldOffset(0x48)] public GameStateBuffer States;
    }

    [StructLayout (LayoutKind.Sequential, Pack = 1)]
    [InlineArray(GameStateHelper.TOTAL_STATES)]
    public struct GameStateBuffer
    {
        private StdTuple2D<IntPtr> _ptr;
    }
}