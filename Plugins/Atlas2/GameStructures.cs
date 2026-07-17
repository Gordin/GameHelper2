namespace Atlas2
{
    using GameOffsets.Natives;
    using GameOffsets.Objects.UiElement;
    using System;
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct UiElement
    {
        [FieldOffset(0)] public UiElementBaseOffset UiElementBase;
        public readonly uint Flags => UiElementBase.Flags;
        public readonly IntPtr GetChildAddress(int index)
        {
            var vector = UiElementBase.ChildrensPtr;
            long count = vector.First == IntPtr.Zero ? 0 : (vector.Last.ToInt64() - vector.First.ToInt64()) / IntPtr.Size;
            return index < 0 || index >= count ? IntPtr.Zero : Atlas2.Read<IntPtr>(vector.First + index * IntPtr.Size);
        }
    }

    /// <summary>
    ///     Enum AtlasNodeState — encodes the observable states a node can have on the
    ///     endgame atlas overlay.
    /// </summary>
    public enum AtlasNodeState : ushort
    {
        /// <summary>Not unlocked yet (path not cleared, behind a quest / gate).</summary>
        None = 0x0000,

        /// <summary>Unlocked but not completed.</summary>
        AccessibleNow = 0x0001,

        /// <summary>Completed at least once.</summary>
        CompletedBase = 0x0002,
    }
}
