// <copyright file="KeyActionMode.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.Enums
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;

    /// <summary>
    ///     Action mode for key presses in a rule.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum KeyActionMode
    {
        /// <summary>
        ///     Tap (press and release immediately via KeyUp).
        /// </summary>
        Tap,

        /// <summary>
        ///     Hold (press down via KeyDown, wait for HoldDuration, then release via KeyUp).
        /// </summary>
        Hold,
    }
}
