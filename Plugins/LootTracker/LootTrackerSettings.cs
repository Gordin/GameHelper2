// <copyright file="LootTrackerSettings.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace LootTracker
{
    using GameHelper.Plugin;

    public sealed class LootTrackerSettings : IPSettings
    {
        public bool ShowOverlay = true;
        public bool ShowInTown = true;
        public int MaximumOverlayRows = 10;
        public bool CompactHud = true;
        public bool PreviewHud;
        public bool LockHud = true;
        public float HudWidth = 780f;
        public float HudScale = 1f;
        public float HudOpacity = 0.65f;
        public float HudBottomOffset = 46f;
        public float HudHorizontalOffset;
        public bool ShowValuesInDivine;
    }
}
