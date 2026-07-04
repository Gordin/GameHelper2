namespace GameOffsets.Objects.Components
{
    using System;
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct StackOffsets
    {
        [FieldOffset(0x0000)] public ComponentHeader Header;
        [FieldOffset(0x0010)] public IntPtr StackSizeDataPtr;
        [FieldOffset(0x0018)] public int Count;
    }

    /// <summary>
    ///     The per-base-type stack descriptor that <see cref="StackOffsets.StackSizeDataPtr" />
    ///     (Stack + 0x10) points at. It is shared across every stack of the same base item
    ///     (e.g. two Scroll-of-Wisdom stacks resolve to the same pointer) and holds the
    ///     per-context stack caps (verified in-game 2026-07-04).
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct StackSizeDataOffsets
    {
        /// <summary>
        ///     Currency stash tab cap (e.g. Scroll of Wisdom = 5000).
        /// </summary>
        [FieldOffset(0x0020)] public int MaxStackTab;

        /// <summary>
        ///     Third cap field (observed = 100), unused for display.
        /// </summary>
        [FieldOffset(0x0024)] public int UnknownMaxStack;

        /// <summary>
        ///     Normal cap: backpack and normal stash tabs (e.g. Scroll of Wisdom = 40).
        /// </summary>
        [FieldOffset(0x0028)] public int MaxStack;
    }
}
