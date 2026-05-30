// <copyright file="Rarity.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteEnums
{
    using Newtonsoft.Json;
    using Newtonsoft.Json.Converters;

    /// <summary>
    ///     Read Rarity.dat file for Rarity to integer mapping.
    /// </summary>
    [JsonConverter(typeof(StringEnumConverter))]
    public enum Rarity
    {
        /// <summary>
        ///     Normal Item/Monster.
        /// </summary>
        Normal,

        /// <summary>
        ///     Magic Item/Monster.
        /// </summary>
        Magic,

        /// <summary>
        ///     Rare Item/Monster.
        /// </summary>
        Rare,

        /// <summary>
        ///     Unique Item/Monster.
        /// </summary>
        Unique
    }
}