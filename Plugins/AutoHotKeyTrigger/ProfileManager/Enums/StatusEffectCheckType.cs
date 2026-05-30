// <copyright file="StatusEffectCheckType.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.Enums
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;

    /// <summary>
    ///     Check type for the condition
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum StatusEffectCheckType
    {
        /// <summary>
        ///     Check remaining buff duration
        /// </summary>
        TimeLeft,

        /// <summary>
        ///     Check remaning buff duration in percent
        /// </summary>
        PercentTimeLeft,

        /// <summary>
        ///     Check buff charges
        /// </summary>
        Charges
    }
}
