// <copyright file="IDynamicConditionState.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.DynamicConditions.Interface
{
    using System.Collections.Generic;
    using ClickableTransparentOverlay.Win32;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteObjects.Components;
    using GameOffsets.Objects.Components;

    /// <summary>
    ///     The structure that can be queried using DynamicCondition
    /// </summary>
    public interface IDynamicConditionState
    {
        /// <summary>
        ///     The ailment list
        /// </summary>
        IReadOnlyCollection<string> PlayerAilments { get; }

        /// <summary>
        ///     The current animation
        /// </summary>
        int PlayerAnimation { get; }

        /// <summary>
        ///     The player skill useability status.
        /// </summary>
        HashSet<string> PlayerSkillIsUseable { get; }

        /// <summary>
        ///     The names of minion "command" skills usable on at least one summoned minion.
        /// </summary>
        HashSet<string> MinionCommandSkillIsUsable { get; }

        /// <summary>
        ///   The player skill details are in this structure.
        /// </summary>
        Dictionary<string, ActiveSkillDetails> ActiveSkills { get; }

        /// <summary>
        ///     The objects deployed by the player, indexed by object-type id (absent ids read as 0).
        /// </summary>
        DeployedObjectCounter DeployedObjectsCount { get; }

        /// <summary>
        ///     The buff list
        /// </summary>
        IBuffDictionary PlayerBuffs { get; }

        /// <summary>
        ///     The flask information
        /// </summary>
        IFlasksInfo Flasks { get; }

        /// <summary>
        ///     The vitals information
        /// </summary>
        IVitalsInfo PlayerVitals { get; }

        /// <summary>
        ///     Number of friendly nearby monsters in inner circle.
        /// </summary>
        int InnerCircleFriendlyMonsterCount { get; }

        /// <summary>
        ///     Number of friendly nearby monsters in outer circle.
        /// </summary>
        int OuterCircleFriendlyMonsterCount { get; }

        /// <inheritdoc cref="OuterCircleFriendlyMonsterCount"/>
        int FriendlyMonsterCount { get; }

        /// <summary>
        ///     Calculates the number of nearby monsters given a rarity selector
        /// </summary>
        /// <param name="rarity">The rarity selector for monster search</param>
        /// <returns></returns>
        int MonsterCount(MonsterRarity rarity);

        /// <summary>
        ///     Calculates the number of nearby monsters given a rarity selector and the NearbyZone.
        /// </summary>
        /// <param name="rarity">The rarity selector for monster search</param>
        /// <param name="zone">circle in which the monster should exists</param>
        /// <returns></returns>
        int MonsterCount(MonsterRarity rarity, MonsterNearbyZones zone);

        /// <summary>
        ///     Counts nearby monsters of the given rarity that are currently undamageable
        ///     (in an invulnerability phase) in the outer circle.
        /// </summary>
        /// <param name="rarity">The rarity selector for monster search</param>
        int UndamageableMonsterCount(MonsterRarity rarity);

        /// <summary>
        ///     Counts nearby monsters of the given rarity that are currently undamageable
        ///     (in an invulnerability phase).
        /// </summary>
        /// <param name="rarity">The rarity selector for monster search</param>
        /// <param name="zone">circle in which the monster should exists</param>
        int UndamageableMonsterCount(MonsterRarity rarity, MonsterNearbyZones zone);

        /// <summary>
        ///     Counts nearby monsters of the given rarity that are currently damageable
        ///     (i.e. NOT in an invulnerability phase) in the outer circle.
        /// </summary>
        /// <param name="rarity">The rarity selector for monster search</param>
        int DamageableMonsterCount(MonsterRarity rarity);

        /// <summary>
        ///     Counts nearby monsters of the given rarity that are currently damageable
        ///     (i.e. NOT in an invulnerability phase).
        /// </summary>
        /// <param name="rarity">The rarity selector for monster search</param>
        /// <param name="zone">circle in which the monster should exists</param>
        int DamageableMonsterCount(MonsterRarity rarity, MonsterNearbyZones zone);

        /// <summary>
        ///     Counts nearby monsters of the given rarity within an explicit distance, ignoring the
        ///     configured inner/outer circle. Reaches up to the network bubble (~150).
        /// </summary>
        /// <param name="rarity">The rarity selector for monster search</param>
        /// <param name="maxDistance">Maximum distance from the player</param>
        int MonsterCountInRange(MonsterRarity rarity, int maxDistance);

        /// <summary>
        ///     Counts nearby monsters of the given rarity that are currently undamageable
        ///     (in an invulnerability phase) within an explicit distance.
        /// </summary>
        /// <param name="rarity">The rarity selector for monster search</param>
        /// <param name="maxDistance">Maximum distance from the player</param>
        int UndamageableMonsterCountInRange(MonsterRarity rarity, int maxDistance);

        /// <summary>
        ///     Counts nearby monsters of the given rarity that are currently damageable
        ///     (i.e. NOT in an invulnerability phase) within an explicit distance.
        /// </summary>
        /// <param name="rarity">The rarity selector for monster search</param>
        /// <param name="maxDistance">Maximum distance from the player</param>
        int DamageableMonsterCountInRange(MonsterRarity rarity, int maxDistance);

        /// <summary>
        ///     Counts nearby corpses (dead monsters) of the given rarity in the outer circle.
        /// </summary>
        /// <param name="rarity">The rarity selector for corpse search</param>
        int CorpseCount(MonsterRarity rarity);

        /// <summary>
        ///     Counts nearby corpses (dead monsters) of the given rarity.
        /// </summary>
        /// <param name="rarity">The rarity selector for corpse search</param>
        /// <param name="zone">circle in which the corpse should exists</param>
        int CorpseCount(MonsterRarity rarity, MonsterNearbyZones zone);

        /// <summary>
        ///     Counts nearby corpses (dead monsters) of the given rarity within an explicit distance.
        /// </summary>
        /// <param name="rarity">The rarity selector for corpse search</param>
        /// <param name="maxDistance">Maximum distance from the player</param>
        int CorpseCountInRange(MonsterRarity rarity, int maxDistance);

        /// <summary>
        ///     Detect a keypress event
        /// </summary>
        bool IsKeyPressedForAction(VK vk);

        /// <summary>
        ///     Gets the value indicating if first weapon set is active or not.
        /// </summary>
        bool PlayerFirstWeaponSetActive { get; }

        /// <summary>
        ///     Gets the value indicating if second weapon set is active or not.
        /// </summary>
        bool PlayerSecondWeaponSetActive { get; }

        /// <summary>
        ///     Gets the value indicating if player is shapeshifted or not.
        /// </summary>
        bool PlayerIsShapeShifted { get; }

        /// <summary>
        ///     Gets the stage of a skill/buff on a specific client-side effect entity.
        ///     Finds an entity whose Path contains skillNameOrPath, and extracts the stage counter of the specified buffName.
        /// </summary>
        int SkillStage(string skillNameOrPath, string buffName);

        /// <summary>
        ///     Gets the stage of a skill/buff on a specific client-side effect entity, defaulting to "spear_sandstorm" buffName.
        /// </summary>
        int SkillStage(string skillNameOrPath);
    }
}
