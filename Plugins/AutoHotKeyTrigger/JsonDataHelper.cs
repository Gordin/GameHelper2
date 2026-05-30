// <copyright file="JsonDataHelper.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger
{
    using System.Collections.Generic;

    /// <summary>
    ///     A helper class that reads JSON data and provides useful functions.
    /// </summary>
    public static class JsonDataHelper
    {
        /// <summary>
        ///     Gets or sets the status effects in a group and the group name.
        /// </summary>
        public static Dictionary<string, List<string>> StatusEffectGroups { get; set; } = new();
    }
}