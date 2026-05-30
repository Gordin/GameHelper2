// <copyright file="EntityStates.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>


namespace GameHelper.RemoteEnums.Entity
{
    /// <summary>
    ///     Enum for entity states. These states do not translate to anything ingame.
    /// </summary>
    public enum EntityStates
    {
        /// <summary>
        ///     All <see cref="EntityTypes"/> whos entity state isn't identified yet.
        /// </summary>
        None,

        /// <summary>
        ///     A special entity state that will make sure that this entity
        ///     components are never updated again from the game memory.
        ///     This is to save the CPU cycles on such entity. All plugins
        ///     are expected to skip this entity. All <see cref="EntityTypes"/>
        ///     can have this state.
        ///
        ///     WARNING: if an entity reaches this state it can never go back to not-useless
        ///              unless current area/zone changes.
        /// </summary>
        Useless,

        /// <summary>
        ///     A <see cref="EntityTypes.Player"/> that is important to the player.
        /// </summary>
        PlayerLeader,

        /// <summary>
        ///     A <see cref="EntityTypes.Monster"/> that is friendly to the player.
        /// </summary>
        MonsterFriendly,

        /// <summary>
        ///    Pinnacle boss is hidden and not attackable
        /// </summary>
        PinnacleBossHidden,
    }
}
