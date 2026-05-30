// <copyright file="ConditionType.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.Enums
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;

    /// <summary>
    ///     Conditions supported by this plugin.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ConditionType
    {
        /// <summary>
        ///     Condition based on player PlayerVitals.
        /// </summary>
        VITALS,

        /// <summary>
        ///     Condition based on what player is doing.
        /// </summary>
        ANIMATION,

        /// <summary>
        ///     Condition based on player PlayerBuffs/Debuffs.
        /// </summary>
        STATUS_EFFECT,

        /// <summary>
        ///     Condition based on flask mod active on player or not.
        /// </summary>
        FLASK_IS_ACTIVE,

        /// <summary>
        ///     Condition based on number of charges flask has.
        /// </summary>
        FLASK_CHARGES,

        /// <summary>
        ///     Condition based on flask can be used or not.
        /// </summary>
        FLASK_IS_USEABLE,

        /// <summary>
        ///     Condition based on Ailment on the player.
        /// </summary>
        AILMENT,

        /// <summary>
        ///     Condition based on Player skill useability.
        /// </summary>
        IS_SKILL_USEABLE,

        /// <summary>
        ///     Condition based on deployed object count.
        /// </summary>
        DEPLOYED_OBJECT_COUNT,

        /// <summary>
        ///     Condition based on the nearby monster count.
        /// </summary>
        NEARBY_MONSTER_COUNT,

        /// <summary>
        ///     Condition based on the user pressing of mouse/keyboard key.
        /// </summary>
        ON_KEY_PRESSED_FOR_ACTION,

        /// <summary>
        ///     Condition based on the Player currently active weapon set.
        /// </summary>
        WEAPON_SET_ACTIVE,

        /// <summary>
        ///     Condition based on the Player current shape and form.
        /// </summary>
        SHAPESHIFTED,

        /// <summary>
        ///     Condition base on user code
        /// </summary>
        DYNAMIC,

    }
}
