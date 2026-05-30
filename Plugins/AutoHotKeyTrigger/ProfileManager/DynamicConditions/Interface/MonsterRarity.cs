// <copyright file="MonsterRarity.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.DynamicConditions.Interface
{
    using System;

    /// <summary>
    ///     different rarity of monster available in the game.
    /// </summary>
    [Flags]
    public enum MonsterRarity
    {
        /// <summary>
        ///     Monsters with red icons (i.e. white monsters).
        /// </summary>
        Normal = 1 << 0,

        /// <summary>
        ///     Monsters with blue icons (i.e. blue monsters).
        /// </summary>
        Magic = 1 << 1,

        /// <summary>
        ///     Monsters with yellow icons (i.e. yellow monsters).
        /// </summary>
        Rare = 1 << 2,

        /// <summary>
        ///     Monsters with big red icon (i.e. golden monsters).
        /// </summary>
        Unique = 1 << 3,

        /// <summary>
        ///     All rarity of monsters.
        /// </summary>
        Any = Normal | Magic | Rare | Unique,

        /// <summary>
        ///     Rare or above.
        /// </summary>
        AtLeastRare = Rare | Unique
    }
}
