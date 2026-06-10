using GameHelper.Plugin;
using System.Collections.Generic;
using System.Numerics;

namespace Atlas
{
    public sealed class AtlasSettings : IPSettings
    {
        public Vector4 DefaultBackgroundColor = new(0f, 0f, 0f, 0.85f);
        public Vector4 DefaultFontColor = new(1f, 1f, 1f, 1.0f);

        public bool ControllerMode = false;

        public string SearchQuery = string.Empty;

        public bool DrawLinesToCitadel = false;
        public Vector4 CitadelPathColor = new(0f, 0f, 1f, 50f / 255f);
        public int CitadelMaxHops = 100;
        public bool DrawLinesToTowers = false;
        public Vector4 TowerPathColor = new(0.78f, 0.76f, 0.05f, 50f / 255f);
        public int TowerMaxHops = 100;
        public bool DrawLinesToSearch = true;
        public Vector4 SearchPathColor = new(1f, 1f, 1f, 50f / 255f);
        public int SearchMaxHops = 100;
        public bool DrawLinesToUniqueMaps = false;
        public Vector4 UniquePathColor = new(1f, 143f / 255f, 0f, 50f / 255f);
        public int UniqueMaxHops = 100;
        public bool DrawLinesToLineageMaps = false;
        public Vector4 LineagePathColor = new(0f, 0.88f, 0f, 50f / 255f);
        public int LineageMaxHops = 100;
        public bool DrawLinesToArbiterMaps = false;
        public Vector4 ArbiterPathColor = new(1f, 0f, 0f, 50f / 255f);
        public int ArbiterMaxHops = 100;

        public bool HideCompletedMaps = true;
        public bool HideNotAccessibleMaps = false;
        public bool ShowAtlasGraph = false;
        public Vector4 AtlasGraphLineColor = new(1f, 1f, 1f, 0.35f);
        public float AtlasGraphOffsetX = 0f;
        public float AtlasGraphOffsetY = 0f;
        public bool ShowMapBadges = true;
        public bool ShowMapCounts = false;
        public bool ShowBiomeBorder = true;
        public float BiomeBorderThickness = 2.5f;

        public float PathLineThickness = 6f;

        public float BaseWidth = 1920f;
        public float BaseHeight = 1080f;
        public Vector2 AnchorNudge = Vector2.Zero;
        public float ScaleMultiplier = 1.1f;

        public List<MapGroupSettings> MapGroups = [];
        public string GroupNameInput = string.Empty;

        public Dictionary<string, ContentOverride> ContentOverrides = [];
        public Dictionary<byte, ContentOverride> BiomeOverrides = [];

        public AtlasSettings()
        {
            var citadels = new MapGroupSettings("Citadels", new Vector4(1f, 1f, 1f, 0.85f), new Vector4(1f, 0f, 0f, 1f));
            citadels.Maps.Add("The Copper Citadel");
            citadels.Maps.Add("The Iron Citadel");
            citadels.Maps.Add("The Stone Citadel");

            var pinnacleBosses = new MapGroupSettings("Pinnacle Boss", new Vector4(0.471f, 0.196f, 0.471f, 0.85f), new Vector4(1f, 1f, 1f, 1f));
            pinnacleBosses.Maps.Add("The Burning Monolith");

            var special = new MapGroupSettings("Special", new Vector4(0.737f, 0.376f, 0.145f, 0.85f), new Vector4(0f, 0f, 0f, 1f));
            special.Maps.Add("Untainted Paradise");
            special.Maps.Add("Vaults of Kamasa");
            special.Maps.Add("Moment of Zen");
            special.Maps.Add("The Ezomyte Megaliths");
            special.Maps.Add("Derelict Mansion");
            special.Maps.Add("The Viridian Wildwood");
            special.Maps.Add("The Jade Isles");
            special.Maps.Add("Castaway");
            special.Maps.Add("The Fractured Lake");
            special.Maps.Add("Ice Cave");

            var good = new MapGroupSettings("Good", new Vector4(0.157f, 0.157f, 0f, 0.85f), new Vector4(1f, 1f, 0f, 1f));
            good.Maps.Add("Burial Bog");
            good.Maps.Add("Creek");
            good.Maps.Add("Rustbowl");
            good.Maps.Add("Sandspit");
            good.Maps.Add("Savannah");
            good.Maps.Add("Steaming Springs");
            good.Maps.Add("Steppe");
            good.Maps.Add("Wetlands");
            good.Maps.Add("Willow");

            var towers = new MapGroupSettings("Towers", new Vector4(0.863f, 0f, 0.882f, 0.85f), new Vector4(0f, 0f, 0f, 1f));
            towers.Maps.Add("Bluff");
            towers.Maps.Add("Lost Towers");
            towers.Maps.Add("Mesa");
            towers.Maps.Add("Sinking Spire");
            towers.Maps.Add("Alpine Ridge");

            MapGroups.Add(citadels);
            MapGroups.Add(towers);
            MapGroups.Add(pinnacleBosses);
            MapGroups.Add(good);
            MapGroups.Add(special);
        }
    }

    public class MapGroupSettings(string name, Vector4 backgroundColor, Vector4 fontColor)
    {
        public string Name = name;
        public Vector4 BackgroundColor = backgroundColor;
        public Vector4 FontColor = fontColor;
        public List<string> Maps = [];
        public string MapNameInput = string.Empty;
    }

    public class ContentOverride
    {
        public Vector4? BackgroundColor { get; set; }
        public Vector4? BorderColor { get; set; }
        public Vector4? FontColor { get; set; }
        public bool? Show { get; set; }
        public string Abbrev { get; set; }
    }
}