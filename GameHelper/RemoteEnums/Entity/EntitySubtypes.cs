// <copyright file="EntitySubtypes.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteEnums.Entity
{
    /// <summary>
    ///     Enum for entity Sub Categorization system.
    /// </summary>
    public enum EntitySubtypes
    {
        /// <summary>
        ///     All <see cref="EntityTypes"/> that are not sub-categorized yet.
        /// </summary>
        Unidentified,

        /// <summary>
        ///     All <see cref="EntityTypes"/> that doesn't require a sub-type.
        /// </summary>
        None,

        /// <summary>
        ///     A <see cref="EntityTypes.Player"/> that is controlled by the current user.
        /// </summary>
        PlayerSelf,

        /// <summary>
        ///     A <see cref="EntityTypes.Player"/> that is controlled by the other user.
        /// </summary>
        PlayerOther,

        /// <summary>
        ///     A <see cref="EntityTypes.Chest"/> that have magic or above rarity on them.
        /// </summary>
        ChestWithMagicRarity,

        /// <summary>
        ///     A <see cref="EntityTypes.Chest"/> that have magic or above rarity on them.
        /// </summary>
        ChestWithRareRarity,

        /// <summary>
        ///     A <see cref="EntityTypes.Chest"/> you find in expedition encounter.
        /// </summary>
        ExpeditionChest,

        /// <summary>
        ///     A <see cref="EntityTypes.Chest"/> you find in breach encounter.
        /// </summary>
        BreachChest,

        /// <summary>
        ///     A <see cref="EntityTypes.Chest"/> that is a Strongbox.
        /// </summary>
        Strongbox,

        /// <summary>
        ///     A <see cref="EntityTypes.NPC"/> that is very important to the user.
        /// </summary>
        SpecialNPC,

        /// <summary>
        ///     A monster that is point of interest for the user.
        /// </summary>
        POIMonster,

        /// <summary>
        ///    Atlas pinnacle boss.
        /// </summary>
        PinnacleBoss,

        /// <summary>
        ///     A <see cref="EntityTypes.Item"/> that is on the ground.
        /// </summary>
        WorldItem,

        /// <summary>
        ///      A <see cref="EntityTypes.Item"/> that is in the inventory.
        /// </summary>
        InventoryItem,
    }
}
