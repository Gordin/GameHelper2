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
            new(0x0065, "Breach", "Area contains an Otherworldly Breach", "AtlasIconContentBreach"),
            new(0x0066, "Expedition", "Area contains a Kalguuran Expedition", "AtlasIconContentExpedition"),
            new(0x0067, "Delirium", "Area contains a Delirium Mirror", "AtlasIconContentDelirium"),
            new(0x0068, "Ritual", "Area contains Ritual Altars", "AtlasIconContentRitual"),
            new(0x0069, "Irradiated", "Area has +1 to Monster Level", "AtlasIconContentIrradiated"),
            new(0x006A, "Overrun by the Abyssal", "Area contains many extra Abysses", "AtlasIconContentAbyssOverrun"),
            new(0x006B, "Vaal Beacons", "Area contains Vaal Beacons", "AtlasIconContentIncursion"),
            new(0x006C, "Abyss", "Area contains Abysses", "AtlasIconContentAbyss"),
            new(0x006D, "Notable Location", "Area contains an important objective", "AtlasMasteryBiome"),
            new(0x006E, "[DNT] Breach City - Not Shown to Players", "DNT No visual identity = not shown", null),
            new(0x006F, "Great Beast", "Slay the Great Beast to earn Hilda's Favour", "CompanionsNotable1"),
            new(0x0071, "Monstrous Treasure", "Contains many extra Strongboxes with monsters waiting in ambush", "AtlasIconContentStrongBox"),
            new(0x0072, "Spirit Guide", "Contains an Azmeri Spirit that will be released when Possessed Monsters are slain", "AtlasIconContentAzmeriSpirit"),
            new(0x0074, "Hunting Grounds", "Contains 2 additional Rogue Exiles and 5 additional Rare Beasts", "Hunter"),
            new(0x0076, "Crystalised Twinning", "Contains 3 additional Essence Packs Essence Packs have an additional Rare Monster", "EssenceNotable1"),
            new(0x0078, "Azmeri Energisation", "Contains 2 additional Azmeri Spirits Azmeri Spirits have 1000% increased maximum Empowerment", "MoreWildWisps"),
            new(0x0079, "Spirit Migration", "An Azmeri Spirit moves to a nearby map on completion, eventually ascending to a Sacred Spirit", "VividPrimalWildWisps"),
            new(0x007A, "Sacred Spirit", "The Azmeri Spirit has ascended to a Sacred Spirit", "MoreSacredWisps"),
            new(0x007B, "Ancient Trove", "Contains a Unique Strongbox", "StrongboxNotable2"),
            new(0x007C, "Twice-Locked Boxes", "Contains 3 additional Strongboxes Strongboxes are openable twice", "StrongboxNotable1"),
            new(0x007E, "Large Congregation", "Contains 3 additional Shrines Shrines have 2 additional packs of Worshippers", "ShrinesNode"),
            new(0x0080, "Persistent Devotion", "Shrine Buffs are reapplied when entering the Map Boss Arena", "GreedShrinenoteble"),
            new(0x0081, "Rites of the Rogues", "Contains 2 additional Shrines Shrines are Worshipped by a Rogue Exile", "Anarchy5"),
            new(0x0082, "Surprising Alliances", "Contains 2 additional Rogue Exiles Rogue Exiles appear in Pairs", "AnarchyNode1"),
            new(0x0083, "Azmeri Bloodline", "Contains an additional Rogue Exile and 2 additional Azmeri Spirits Rogue Exiles are Possessed when a Possessed Monster is killed in Area", "Anarchy4"),
            new(0x0086, "Map Area Modified", "World Area has been manipulated and cannot be manipulated again", "Mapnode"),
            new(0x0087, "Fleeing Exile", "", "AnarchyNotable2"),
            new(0x0089, "Simulacrum", "Contains a manifestation of Delirium", "DeliriumNotable7"),
            new(0x008A, "Chaotic Cacophony", "Contains an extra of each type of content", "ElderShaperNotable1"),
            new(0x008D, "Trialmaster's Trainee", "Map Boss drops an Inscribed Ultimatum", "VaalNotable1"),
            new(0x0090, "Gigantic Uprising", "Monsters are Gigantic, have 50% reduced pack size and drop 50% increased items", "MinionsandManaNotable"),
            new(0x0092, "Stolen Power", "Contains an additional Summoning Circle Summoning Circle Bosses have increased difficulty and reward per power of enemy slain", "ScorchTheEarth"),
            new(0x0093, "Headhunters", "When Players Kill a Rare Monster they will gain 1 of its Modifiers for 20 seconds", "skullcracking"),
            new(0x0098, "Exceptional Find", "1000% increased Exceptional Items found in Area Monster may drop anyExceptional Items", "ExceptionalItemsBodyArmour"),
            new(0x0099, "Water Influence", "Also counts as a Water Area", "WaterBiome"),
            new(0x009F, "Immured Fury", "Doryani has spotted The Immured Fury in this Area", "AtlasIconContentSanctificationBoss"),
            new(0x00A0, "Mirage of Riches", "Equipment dropped by monsters is replaced with other items", "Currency2"),
            new(0x00A1, "Wisdom's Teachings", "Monsters grant 100% increased Experience", "BossNotableGrantMoreExperience"),
            new(0x00A2, "Tight Pockets", "Gold dropped by monsters is replaced with other items", "BossNotableDropMoreItems"),
            new(0x00A3, "Fragment of Immortality", "Players have unlimited Revivals in area Monsters have 100% increased Effectiveness", "IncreaseMinionLifeNode"),
            new(0x00A4, "Prosperous Populous", "100% increased Rarity of items found in area", "ItemQuantity"),
            new(0x00A5, "Echoes of Power", "5 Rare Monsters are Duplicated", "GenericMinionNotable"),
            new(0x00A6, "Grand Expedition", "Area contains a Grand Expedition", "ExpeditionNode1"),
            // Do not map 0x03E9: the full UI value 0x000203E9 is a shared special-border badge
            // marker, observed on both Grand Expeditions and Simulacrums. Grand Expedition is
            // identified by its persistent EndgameMapContent id 0x00A6 instead.
            // ATLAS_CONTENT_PORT_INSERT
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
            // Observed as full token 0x1F916290 on Simulacrum nodes. The low word selects the
            // content effect; the high word is token payload and must not be used as an identity.
            new(0x6290, "Simulacrum", "DeliriumNotable7"),
            new(0x685A, "{0}% Delirious", "AtlasIconContentDelirium"),
        };
    }
}
