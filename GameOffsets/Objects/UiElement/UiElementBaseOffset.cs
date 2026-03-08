namespace GameOffsets.Objects.UiElement
{
    using System;
    using System.Runtime.InteropServices;
    using Natives;

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct UiElementBaseOffset
    {
        [FieldOffset(0x000)] public IntPtr Vtable;
        [FieldOffset(0x008)] public IntPtr Self;

        [FieldOffset(0x010)] public StdVector ChildrensPtr; // both points to same children UiElements.
        // 4 childrens are cached here
        // public StdVector ChildrensPtr; // both points to same children UiElements.
        // 4 childrens are cached here

        [FieldOffset(0xF0)] public StdTuple2D<float> PositionModifier;
        // Following Ptr is basically pointing to InGameState+0xXXX.
        // No idea what InGameState+0xXXX is pointing to
        // [FieldOffset(0x0C0)] public IntPtr UnknownPtr;

        [FieldOffset(0x0B8)] public IntPtr ParentPtr; // UiElement.
        [FieldOffset(0x118)] public StdTuple2D<float> RelativePosition; // variable
        [FieldOffset(0x130)] public float LocalScaleMultiplier;
        [FieldOffset(0x18A)] public byte ScaleIndex; // root = 3, child of root = 3 and first child of that child = 2

        [FieldOffset(0x140)] public StdWString StringIdPtr;
        // TODO: 0x08 byte gap....not sure what's there
        // [FieldOffset(0x108)] public float Scale; // !!do not use this!!, this scale provided by game is wrong.
        [FieldOffset(0x180)] public uint Flags; // variable

        [FieldOffset(0x288)] public StdTuple2D<float> UnscaledSize; // variable

        // public uint BorderColor; // BackgroundColor - 0x04
        [FieldOffset(0x2A4)] public uint BackgroundColor;
    }

    public static class UiElementBaseFuncs
    {
        private const int SHOULD_MODIFY_BINARY_POS = 0x0A;
        private const int IS_VISIBLE_BINARY_POS = 0x0B;

        /// <summary>
        ///     This value is extracted from GGPK -> Metadata -> UI -> UISettings.xml
        /// </summary>
        public static readonly StdTuple2D<double> BaseResolution = new(2560, 1600);

        public static Func<uint, bool> IsVisibleChecker = param =>
        {
            return Util.isBitSetUint(param, IS_VISIBLE_BINARY_POS);
        };

        public static Func<uint, bool> ShouldModifyPos = param =>
        {
            return Util.isBitSetUint(param, SHOULD_MODIFY_BINARY_POS);
        };
    }
}