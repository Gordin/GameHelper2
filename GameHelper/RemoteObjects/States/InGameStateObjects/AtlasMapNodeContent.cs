// <copyright file="AtlasMapNodeContent.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.States.InGameStateObjects
{
    using System;
    using System.Collections.Generic;

    /// <summary>Describes one known Atlas badge value.</summary>
    public sealed class AtlasMapNodeBadge
    {
        internal AtlasMapNodeBadge(uint id, string name, string description, string? icon)
        {
            this.Id = id;
            this.Name = name;
            this.Description = description;
            this.Icon = icon;
        }

        public uint Id { get; }
        public string Name { get; }
        public string Description { get; }
        public string? Icon { get; }

        public static IReadOnlyList<AtlasMapNodeBadge> Known { get; } = new AtlasMapNodeBadge[]
        {
            new(0x0064, "Powerful Map Boss", "Area contains a Powerful Map Boss", "AtlasIconContentMapBoss"),
            new(0x008E, "Sekhema's Student", "Map Boss drops a Djinn Barya", "SorceressSandDjinnCorpseBeetles"),
            new(0x007D, "Power of Faith", "Contains 3 additional Shrines", "Shrines"),
            new(0x008F, "Azmeri Champion", "Map Boss is Possessed", "BossNotableAzmeriSpirit"),
            new(0x0088, "Breach Hive", "Contains a Breach Hive", "BreachNotable4"),
            new(0x008C, "Monstrous Treasure", "Contains many extra Strongboxes with monsters waiting in ambush", "AtlasIconContentStrongBox"),
            new(0x0094, "Swarming Spirits", "Contains 5 additional Azmeri Spirits", "EnduranceFrenzyPowerChargeNode"),
            new(0x0091, "Glimmering Mutation", "Currency found is replaced with rarer varieties", "CurrencyNode"),
            new(0x0070, "Essence Trove", "All Rare monsters are Essence monsters", "AtlasIconContentEssence"),
            new(0x03E8, "Corruption", "This map is Corrupted", "AtlasIconContentCorruption"),
            new(0x6157, "Grand Mirror", "Contains a reflection of the Map Boss", "AtlasIconContentGigaMirror"),
            new(0x009A, "Mountain Influence", "Also counts as a Mountain Area", "MountainBiome"),
            new(0x009B, "Grass Influence", "Also counts as a Grass Area", "GrassBiome"),
            new(0x009C, "Forest Influence", "Also counts as a Forest Area", "ForestBiome"),
            new(0x009D, "Swamp Influence", "Also counts as a Swamp Area", "SwampBiome"),
            new(0x009E, "Desert Influence", "Also counts as a Desert Area", "DesertBiome"),
            new(0x0097, "Energized Ley Lines", "Doubles Effect of Tablets used on Area", "CaptivatedInterestKeystone"),
            new(0x0095, "Power Struggle", "Contains 3 additional Map Bosses throughout the area", "BossNotableSpawnBeyondMonsters"),
            new(0x0073, "Arcane Hordes", "All Monsters are at least Magic", "ItemQuantityandRarity"),
            new(0x0096, "Corrupted Mirage", "Area has 2 additional random Waystone Modifiers", "CorruptedDefences"),
            new(0x008B, "Affluent Armies", "50% increased Rarity of items found in area", "BossMapDrops"),
            new(0x0085, "Scattered Stones", "Contains 3 additional Summoning Circles", "StoneCirclesNode"),
            new(0x0084, "Twinned Terrors", "Summoning Circles always summon an additional Boss", "StoneCircles"),
            new(0x0077, "Indomitable Essence", "Essences transfer to a random Unique Monster on death", "EssenceNotable2"),
            new(0x007F, "Zealous Reverence", "Elemental Shrines do not appear in area", "BossNotableSpawnAdditionalShrine"),
            new(0x0075, "Nature Shrines", "Shrines release an Azmeri Spirit when activated", "HybridShrineAzmeriSpirit"),
        };
    }

    /// <summary>Describes one known Atlas effect-token value.</summary>
    public sealed class AtlasMapNodeEffect
    {
        internal AtlasMapNodeEffect(uint id, string description, string? icon = null)
        {
            this.Id = id;
            this.Description = description;
            this.Icon = icon;
        }

        public uint Id { get; }
        public string Description { get; }
        public string? Icon { get; }

        public static IReadOnlyList<AtlasMapNodeEffect> Known { get; } = new AtlasMapNodeEffect[]
        {
            new(0x65F4, "{0} Atlas Point"),
            new(0x686E, "Delirium", "AtlasIconContentDelirium"),
            new(0x4C58, "Powerful Map Boss", "AtlasIconContentMapBoss"), new(0x4C59, "Powerful Map Boss", "AtlasIconContentMapBoss"),
            new(0x6870, "Ritual Altars", "AtlasIconContentRitual"), new(0x6873, "Ritual Altars", "AtlasIconContentRitual"),
            new(0x686F, "Abysses", "AtlasIconContentAbyss"), new(0x6872, "Area contains Abysses", "AtlasIconContentAbyss"),
            new(0x6875, "Area contains Breaches", "AtlasIconContentBreach"),
            new(0x60C1, "Breach Stronghold", "AtlasIconContentBreach"), new(0x60C4, "Breach Stronghold", "AtlasIconContentBreach"),
            new(0x3A5D, "Hive Fortress", "AtlasIconContentBreach"), new(0x3A5E, "Breach Hive Fortress", "AtlasIconContentBreach"),
            new(0x6760, "Map Boss drops a Djinn Barya", "SorceressSandDjinnCorpseBeetles"),
            new(0x0963, "Contains {0} additional Shrines", "Shrines"), new(0x3897, "Map Boss is Possessed", "BossNotableAzmeriSpirit"),
            new(0x127B, "Map Boss drops a Unique item"), new(0x0A8C, "Contains {0} additional Azmeri Spirits", "AtlasIconContentAzmeriSpirit"),
            new(0x6762, "Currency found is replaced with rarer varieties", "CurrencyNode"),
            new(0x6157, "Contains a reflection of the Map Boss", "AtlasIconContentGigaMirror"), new(0x615A, "Grand Mirror", "AtlasIconContentGigaMirror"),
            new(0x6714, "Use the Grand Mirror to access", "AtlasIconContentGigaMirror"),
            new(0x6503, "Also counts as a Grass Area", "GrassBiome"), new(0x6505, "Also counts as a Swamp Area", "SwampBiome"),
            new(0x6502, "Also counts as a Mountain Area", "MountainBiome"), new(0x6506, "Also counts as a Desert Area", "DesertBiome"),
            new(0x6504, "Also counts as a Forest Area", "ForestBiome"), new(0x4E88, "Doubles Effect of Tablets used on Area", "CaptivatedInterestKeystone"),
            new(0x634A, "Contains {0} additional Map Bosses throughout the area", "BossNotableSpawnBeyondMonsters"),
            new(0x320F, "All Monsters are at least Magic", "ItemQuantityandRarity"), new(0x1282, "Area has {0} additional random Waystone Modifiers", "CorruptedDefences"),
            new(0x04D8, "{0}% increased Rarity of items found in area", "BossMapDrops"), new(0x5E28, "Contains {0} additional Summoning Circles", "StoneCirclesNode"),
            new(0x61C7, "Summoning Circles always summon an additional Boss", "StoneCircles"), new(0x1247, "Contains {0} additional Essence", "AtlasIconContentEssence"),
            new(0x634D, "Essences transfer to a random Unique Monster on death", "EssenceNotable2"),
            new(0x6871, "Area contains a Mirror of Delirium", "AtlasIconContentDelirium"), new(0x6874, "Vaal Beacons", "AtlasIconContentIncursion"),
            new(0x6638, "Elemental Shrines do not appear in area", "BossNotableSpawnAdditionalShrine"),
            new(0x3E16, "Shrine Duration increased by {0}%", "BossNotableSpawnAdditionalShrine"),
            new(0x6244, "Shrines release an Azmeri Spirit when activated", "HybridShrineAzmeriSpirit"),
            new(0x685A, "{0}% Delirious", "AtlasIconContentDelirium"),
        };
    }
}
