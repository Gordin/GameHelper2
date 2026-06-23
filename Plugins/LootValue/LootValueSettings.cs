// <copyright file="LootValueSettings.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace LootValue
{
    using System.Numerics;
    using GameHelper.Plugin;

    /// <summary>
    /// <see cref="LootValue"/> plugin settings.
    /// </summary>
    public sealed class LootValueSettings : IPSettings
    {
        /// <summary>Draw value labels over dropped items on the ground.</summary>
        public bool ShowOverlay = true;

        /// <summary>Anchor value chips to the game's loot labels (avoids overlap when items pile up)
        /// instead of drawing free-floating world-space labels over each drop. Default mode.</summary>
        public bool AnchorToLootTags = true;

        /// <summary>Price source: <see cref="PoeNinjaPriceFetcher.SourcePoeNinja"/> or <see cref="PoeNinjaPriceFetcher.SourcePoe2Scout"/>.</summary>
        public int PriceSource = PoeNinjaPriceFetcher.SourcePoe2Scout;

        /// <summary>PoE2 league name for price lookups.</summary>
        public string League = "Runes of Aldur";

        /// <summary>Automatic price refresh interval in minutes.</summary>
        public int RefreshIntervalMin = 5;

        /// <summary>Display currency: 0 = Divine, 1 = Exalted, 2 = Chaos.</summary>
        public int DisplayCurrency = 1;

        /// <summary>Minimum value (in Exalted) for a drop to get a label. Hides chaff.</summary>
        public float MinValueEx = 1f;

        /// <summary>Value (in Exalted) at/above which a label is drawn in the highlight color.</summary>
        public float HighlightMinEx = 10f;

        /// <summary>Reveal unidentified uniques by name (resolved from their icon art).</summary>
        public bool RevealUnidentifiedUniques = true;

        /// <summary>Show a diagnostics window: the ground-item detection funnel + sample reads.</summary>
        public bool DiagnosticsMode = false;

        /// <summary>Font size (pixels) for normal value labels.</summary>
        public float FontSize = 16f;

        /// <summary>Font size (pixels) for highlighted (high-value) labels.</summary>
        public float HighlightFontSize = 22f;

        /// <summary>Render highlighted labels bold (faux-bold via offset double-draw).</summary>
        public bool HighlightBold = true;

        /// <summary>Vertical pixel offset of the label from the item's world anchor.</summary>
        public float OffsetY = -10f;

        /// <summary>Smooth label motion with a velocity-tracking (alpha-beta) filter so labels don't jitter
        /// while moving. Unlike a plain low-pass it has no steady-state lag during constant movement.
        /// Applies to both world-space and loot-label modes.</summary>
        public bool InterpolatePosition = true;

        /// <summary>Filter responsiveness, 1-1000 (= alpha x1000). Lower = smoother / more jitter rejection
        /// (still no motion lag thanks to velocity tracking); 1000 = effectively raw.</summary>
        public int InterpolationRate = 110;

        /// <summary>How often (ms) the item set + prices are re-detected. Positions still redraw every frame;
        /// this only controls how quickly new drops appear and how much CPU the scan uses.</summary>
        public int RescanIntervalMs = 200;

        /// <summary>Normal label text color (RGBA 0-1).</summary>
        public Vector4 TextColor = new Vector4(1f, 235f / 255f, 140f / 255f, 1f);

        /// <summary>Highlight label text color (RGBA 0-1) for high-value drops.</summary>
        public Vector4 HighlightColor = new Vector4(0.4f, 1f, 0.4f, 1f);
    }
}
