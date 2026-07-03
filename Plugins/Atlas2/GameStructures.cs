namespace Atlas2
{
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
