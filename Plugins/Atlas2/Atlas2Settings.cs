using GameHelper.Plugin;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Atlas2
{
    public sealed class Atlas2Settings : IPSettings
    {
        public Vector4 DefaultBackgroundColor = new(0f, 0f, 0f, 0.85f);
        public Vector4 DefaultFontColor = new(1f, 1f, 1f, 1.0f);

        public bool ControllerMode = false;

        public string SearchQuery = string.Empty;
        public int CategorySettingsVersion = 10;

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
        public bool DrawLinesToQuests = false;
        public Vector4 QuestsPathColor = new(0f, 1f, 1f, 1f); // cyan
        public int QuestsMaxHops = 100;

        // Named-map pathfinding categories (matched by exact display name; see Atlas2.*Maps sets).
        public bool DrawLinesToAtlasProgression = false;
        public Vector4 AtlasProgressionPathColor = new(0.55f, 0.27f, 0.07f, 1f); // brown
        public int AtlasProgressionMaxHops = 100;
        public bool DrawLinesToRitual = false;
        public Vector4 RitualPathColor = new(64f / 255f, 0f, 244f / 255f, 1f); // 64,0,244
        public int RitualMaxHops = 100;
        public bool DrawLinesToCorruptedNexus = false;
        public Vector4 CorruptedNexusPathColor = new(0.45f, 0f, 0f, 1f); // dark red
        public int CorruptedNexusMaxHops = 100;
        public bool DrawLinesToGrandMirror = false;
        public Vector4 GrandMirrorPathColor = new(0.7f, 0.9f, 1f, 1f); // pale blue
        public int GrandMirrorMaxHops = 100;
        public bool DrawLinesToBreach = false;
        public Vector4 BreachPathColor = new(255f / 255f, 51f / 255f, 189f / 255f, 1f); // 255,51,189
        public int BreachMaxHops = 100;
        public bool DrawLinesToExpedition = false;
        public Vector4 ExpeditionPathColor = new(91f / 255f, 193f / 255f, 237f / 255f, 1f); // 91,193,237
        public int ExpeditionMaxHops = 100;
        public bool DrawLinesToAbyss = false;
        public Vector4 AbyssPathColor = new(38f / 255f, 255f / 255f, 0f, 1f); // 38,255,0
        public int AbyssMaxHops = 100;
        public bool DrawLinesToTemple = false;
        public Vector4 TemplePathColor = new(222f / 255f, 167f / 255f, 0f, 1f); // 222,167,0
        public int TempleMaxHops = 100;

        public bool HideCompletedMaps = true;
        public bool HideNotAccessibleMaps = false;
        public bool ShowAtlasGraph = false;
        public Vector4 AtlasGraphLineColor = new(1f, 1f, 1f, 0.35f);
        public float AtlasGraphOffsetX = -10f;
        public float AtlasGraphOffsetY = -5f;
        public bool ShowUnchartedLeylines = false;
        public Vector4 UnchartedLeylineColor = new(0.2f, 0.85f, 1f, 0.9f);
        public float UnchartedLeylineThickness = 10f;
        public bool ShowShipsInFog = false;
        public float ShipIconSize = 46f;
        public bool ShowRitualPrediction = false;
        public bool LogRitualRolls = false;
        public bool ShowRitualPlanner = true;
        public string RitualRewardFilter = string.Empty;
        public float RitualPlannerFontScale = 1f;
        public Dictionary<string, int> RitualRewardWeights = [];
        public bool ShowMapBadges = true;
        public bool ShowMapCounts = false;
        public bool ShowContent = true;
        public bool ShowNodeIndex = false;
        public bool ShowContentIcons = false;
        public float ContentIconSize = 48f;
        public bool ShowContentDebug = false;
        public bool ShowBiomeBorder = true;
        public float BiomeBorderThickness = 2.5f;

        public float PathLineThickness = 6f;

        public float BaseWidth = 1920f;
        public float BaseHeight = 1080f;
        public Vector2 AnchorNudge = new(-8.5f, 45f);
        public float ScaleMultiplier = 0.5f;

        public List<MapGroupSettings> MapGroups = [];
        public string GroupNameInput = string.Empty;

        public Dictionary<string, ContentOverride> ContentOverrides = [];
        public Dictionary<byte, ContentOverride> BiomeOverrides = [];

        public Atlas2Settings()
        {
            AddBuiltIn("Search", "search", new(1f, 1f, 1f, 1f), new(0f, 0f, 0f, 0.85f), "Current search query");
            MapGroups[^1].DrawPath = true;
            AddNamed("Lineage Maps", "lineage", new(0f, 0f, 0f, 1f), "Derelict Mansion", "Sacred Reservoir", "Sealed Vault", "The Jade Isles");
            MapGroups[^1].BackgroundColor = new(0f, 181f / 255f, 33f / 255f, 1f);
            AddBuiltIn("Corrupted Nexus", "corrupted_nexus", new(0.45f, 0f, 0f, 1f), new(0f, 0f, 0f, 0.85f), "Corrupted Nexus content");
            AddBuiltIn("Grand Mirror", "grand_mirror", new(0f, 170f / 255f, 1f, 1f), new(0f, 0f, 0f, 0.85f), "Grand Mirror content");
            AddNamed("Atlas Progression", "atlas_progression", new(0.55f, 0.27f, 0.07f, 1f), "Precursor Tower", "Ancient Gateway", "The Burning Monolith", "Western Gateway", "Eastern Gateway", "Western Enigma Chamber", "Eastern Enigma Chamber", "The Origin Tower");
            AddNamed("Quests", "quests", new(0f, 1f, 1f, 1f), "The Withered Willow");
            AddNamed("Ritual", "ritual", new(0.25f, 0f, 0.96f, 1f), "Caer Tarth", "Crux of Nothingness");
            MapGroups[^1].BackgroundColor = new(1f, 1f, 1f, 0.85f);
            AddNamed("Breach", "breach", new(1f, 0.2f, 0.74f, 1f), "Hive Colony", "Hive Fortress");
            AddNamed("Expedition", "expedition", new(0.36f, 0.76f, 0.93f, 1f), "Barren Atoll", "Bleached Shoals", "Craggy Peninsula", "Exhumed Ruins", "Frigid Bluffs", "Grazed Prairie", "Lush Isle", "Moor of Fallen Skies", "Mournful Cliffside", "Obscure Island", "Scorched Cay", "Secluded Temple", "Sloughed Gully", "Sprawling Jungle", "Stagnant Basin", "The Chained Beast", "The Fallen Star", "Tomb of the Fallen Knight", "Ruins of Kingsmarch");
            var defaultExpeditionTargets = new HashSet<string>
            {
                "Moor of Fallen Skies", "Mournful Cliffside", "Obscure Island", "Secluded Temple",
                "The Chained Beast", "The Fallen Star", "Tomb of the Fallen Knight", "Ruins of Kingsmarch",
            };
            foreach (var target in MapGroups[^1].BuiltInTargets.Keys.ToList())
                MapGroups[^1].BuiltInTargets[target] = defaultExpeditionTargets.Contains(target);
            AddNamed("Abyss", "abyss", new(0.15f, 1f, 0f, 1f), "The Well of Souls");
            AddNamed("Temple", "temple", new(0.87f, 0.65f, 0f, 1f), "Vaal Ruins");
            AddNamed("Citadels", "arbiter", new(1f, 0f, 0f, 1f), "The Copper Citadel", "The Iron Citadel", "The Stone Citadel", "The Matriarch Halls", "The Patriarch Halls");
            MapGroups[^1].BackgroundColor = new(1f, 1f, 1f, 0.85f);
            AddNamed("Towers", "towers", new(0f, 0f, 0f, 0.85f), "Bluff", "Lost Towers", "Mesa", "Sinking Spire", "Alpine Ridge");
            MapGroups[^1].BackgroundColor = new(0.86f, 0f, 0.88f, 1f);
            AddNamed("Good", "good", new(1f, 1f, 0f, 1f), "Burial Bog", "Creek", "Rustbowl", "Sandspit", "Savannah", "Steaming Springs", "Steppe", "Wetlands", "Willow");
            AddNamed("Unique Maps", "unique", new(0f, 0f, 0f, 0.85f), "Ancient Gateway", "Castaway", "Eastern Gateway", "Freight", "Jado's Campsite", "Merchant's Campsite", "Moment of Zen", "Moor of Fallen Skies", "Precursor Tower", "Site of the Chosen", "The Ezomyte Megaliths", "The Fractured Lake", "The Silent Cave", "The Viridian Wildwood", "The Voyage", "Untainted Paradise", "Vaults of Kamasa", "Western Gateway");
            MapGroups[^1].BackgroundColor = new(1f, 0.56f, 0f, 1f);
            AddNamed("Special", "special", new(1f, 1f, 1f, 1f), "Ice Cave");
        }

        private void AddBuiltIn(string name, string key, Vector4 color, Vector4 background, params string[] targets)
        {
            var group = new MapGroupSettings(name, background, color) { BuiltInKey = key };
            foreach (var target in targets) group.BuiltInTargets[target] = true;
            MapGroups.Add(group);
        }

        private void AddNamed(string name, string key, Vector4 color, params string[] targets)
        {
            var group = new MapGroupSettings(name, new(0f, 0f, 0f, 0.85f), color) { BuiltInKey = key };
            foreach (var target in targets) group.BuiltInTargets[target] = true;
            MapGroups.Add(group);
        }
    }

    public class MapGroupSettings(string name, Vector4 backgroundColor, Vector4 fontColor)
    {
        public string Name = name;
        public Vector4 BackgroundColor = backgroundColor;
        public Vector4 FontColor = fontColor;
        public bool DrawPath = false;
        public int MaxHops = 100;
        public string BuiltInKey = string.Empty;
        public Dictionary<string, bool> BuiltInTargets = [];
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
