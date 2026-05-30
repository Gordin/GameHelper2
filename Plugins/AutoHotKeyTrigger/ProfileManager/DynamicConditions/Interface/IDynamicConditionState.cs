// <copyright file="IDynamicConditionState.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.DynamicConditions.Interface
{
    using System.Collections.Generic;
    using ClickableTransparentOverlay.Win32;
    using GameHelper.RemoteEnums;
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
        ///   The player skill details are in this structure.
        /// </summary>
        Dictionary<string, ActiveSkillDetails> ActiveSkills { get; }

        /// <summary>
        ///     The objects deployed by the player with Object type as key and Object Counter as value.
        /// </summary>
        int[] DeployedObjectsCount { get; }

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
    }
}
