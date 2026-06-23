namespace GameOffsets.Objects.Components
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    ///     Native layout of the <c>WorldItem</c> component — present on a dropped/ground item entity.
    ///     A ground drop is a wrapper entity; the actual item entity (the one carrying
    ///     <c>Mods</c> / <c>Base</c> / <c>RenderItem</c>) is referenced here.
    /// </summary>
    /// <remarks>
    ///     Recovered from the POE2Radar project: <c>+0x28</c> is a pointer to the inner item Entity.
    /// </remarks>
    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct WorldItemOffsets
    {
        [FieldOffset(0x0000)] public ComponentHeader Header;

        // → the inner item Entity (carries Mods / Base / RenderItem / Stack).
        [FieldOffset(0x0028)] public IntPtr ItemEntityPtr;
    }
}
