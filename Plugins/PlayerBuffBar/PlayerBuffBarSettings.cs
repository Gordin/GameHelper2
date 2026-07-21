namespace PlayerBuffBar
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using GameHelper.Plugin;

    public enum BuffBarDisplayMode
    {
        Icons,
        Text,
        IconsAndText,
    }

    public sealed class BuffBarSlotSettings
    {
        public bool Enabled = true;

        public bool AnchorToHealthBar = true;

        public bool ShowPositionDummy;

        public float IconSize = 28f;

        public float IconSpacing = 4f;

        public Vector2 ScreenOffset = new(0f, 36f);

        public Vector2 FixedPosition = new(40f, 520f);

        public List<string> Watchlist = new();
    }

    public sealed class PlayerBuffBarSettings : IPSettings
    {
        public const int MaxBuffBars = 4;

        public int SettingsVersion = 9;

        public bool ShowOverlay = true;

        public BuffBarDisplayMode DisplayMode = BuffBarDisplayMode.Icons;

        public bool HideInTownOrHideout = true;

        public bool HideWhenGameInBackground = true;

        public bool ShowInactiveWatchlist = true;

        public bool ShowCharges = true;

        public bool ShowRage = true;

        public bool HideEmptyResources = true;

        public bool ShowResourceCountBackground = true;

        public bool ShowDurations = true;

        public bool ShowStacks = true;

        public bool AutoDownloadWikiIcons = true;

        public float FontScale = 1f;

        public float MaxBarWidth = 260f;

        public float InactiveIconAlpha = 0.35f;

        public Vector4 ActiveColor = new(0.2f, 0.85f, 0.35f, 0.92f);

        public Vector4 InactiveColor = new(0.45f, 0.45f, 0.45f, 0.55f);

        public Vector4 ChargeTextColor = new(1f, 0.92f, 0.35f, 1f);

        public Vector4 BuffTextColor = new(1f, 1f, 1f, 1f);

        public Vector4 RageTextColor = new(1f, 0.45f, 0.2f, 1f);

        public bool SettingsResourceBarSectionOpen;

        public bool SettingsBuffBarsSectionOpen;

        public bool SettingsBuffDisplaySectionOpen;

        public bool SettingsIconsToolsSectionOpen;

        public int SelectedBuffBarTab;

        public List<BuffBarSlotSettings> BuffBars = new();

        // Resource row (power / frenzy / endurance charges + rage stat)
        public bool ResourceAnchorToHealthBar = true;

        public bool ResourceShowPositionDummy;

        public float ResourceIconSize = 28f;

        public float ResourceIconSpacing = 4f;

        public Vector2 ResourceScreenOffset = new(0f, 8f);

        public Vector2 ResourceFixedPosition = new(40f, 480f);

        // Legacy (migrated into BuffBars[0] on load)
        public bool SettingsBuffBarSectionOpen;

        public bool SettingsWatchlistSectionOpen;

        public bool BuffAnchorToHealthBar = true;

        public bool BuffShowPositionDummy;

        public float BuffIconSize = 28f;

        public float BuffIconSpacing = 4f;

        public Vector2 BuffScreenOffset = new(0f, 36f);

        public Vector2 BuffFixedPosition = new(40f, 520f);

        public bool AnchorToHealthBar = true;

        public float IconSize = 28f;

        public float IconSpacing = 4f;

        public Vector2 ScreenOffset = new(0f, 36f);

        public Vector2 FixedPosition = new(40f, 520f);

        public bool WatchlistUserConfigured;

        public List<string> Watchlist = new();

        public void EnsureBuffBarSlots()
        {
            while (this.BuffBars.Count < MaxBuffBars)
            {
                var index = this.BuffBars.Count;
                this.BuffBars.Add(CreateDefaultBuffBarSlot(index));
            }

            if (this.BuffBars.Count > MaxBuffBars)
            {
                this.BuffBars = this.BuffBars.Take(MaxBuffBars).ToList();
            }
        }

        public IEnumerable<string> GetAllWatchIds() =>
            this.BuffBars
                .SelectMany(bar => bar.Watchlist)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Where(id => !BuffIconCatalog.IsReservedResourceWatchId(id))
                .Distinct(StringComparer.OrdinalIgnoreCase);

        public static BuffBarSlotSettings CreateDefaultBuffBarSlot(int index) => new()
        {
            Enabled = index == 0,
            AnchorToHealthBar = true,
            IconSize = 28f,
            IconSpacing = 4f,
            ScreenOffset = new Vector2(0f, 36f + index * 36f),
            FixedPosition = new Vector2(40f, 520f + index * 36f),
            Watchlist = index == 0 ? CreateDefaultWatchlist().ToList() : new List<string>(),
        };

        public static IReadOnlyList<string> CreateDefaultWatchlist() => DefaultWatchlist;

        private static readonly string[] DefaultWatchlist =
        {
            "blood_rage",
            "fortify",
            "adrenaline",
            "berserk",
            "molten",
            "granite",
            "grace",
            "haste",
            "herald",
        };

        public static bool IsDefaultWatchlistPrefix(IReadOnlyList<string> watchlist)
        {
            if (watchlist.Count < DefaultWatchlist.Length)
            {
                return false;
            }

            for (var i = 0; i < DefaultWatchlist.Length; i++)
            {
                if (!string.Equals(watchlist[i], DefaultWatchlist[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        public static List<string> StripDefaultWatchlistPrefix(IReadOnlyList<string> watchlist)
        {
            if (!IsDefaultWatchlistPrefix(watchlist))
            {
                return watchlist.ToList();
            }

            return watchlist.Skip(DefaultWatchlist.Length).ToList();
        }
    }
}
