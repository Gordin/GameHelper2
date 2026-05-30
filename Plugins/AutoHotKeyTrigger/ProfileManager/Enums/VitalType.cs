// <copyright file="VitalType.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.Enums
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;

    /// <summary>
    ///     Different type of player PlayerVitals.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum VitalType
    {
        /// <summary>
        ///     Condition based on player Mana.
        /// </summary>
        MANA_CURRENT,

        /// <summary>
        ///     Condition based on player Mana.
        /// </summary>
        MANA_PERCENT,

        /// <summary>
        ///     Condition based on player Mana.
        /// </summary>
        MANA_RESERVED,

        /// <summary>
        ///     Condition based on player Life.
        /// </summary>
        HP_CURRENT,

        /// <summary>
        ///     Condition based on player Life.
        /// </summary>
        HP_PERCENT,

        /// <summary>
        ///     Condition based on player Life.
        /// </summary>
        HP_RESERVED,

        /// <summary>
        ///     Condition based on player Energy Shield.
        /// </summary>
        ES_CURRENT,

        /// <summary>
        ///     Condition based on player Energy Shield.
        /// </summary>
        ES_PERCENT,

        /// <summary>
        ///     Condition based on player Energy Shield.
        /// </summary>
        ES_RESERVED,
    }
}
