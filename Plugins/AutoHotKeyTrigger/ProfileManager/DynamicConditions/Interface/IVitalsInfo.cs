// <copyright file="IVitalsInfo.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.DynamicConditions.Interface
{
    /// <summary>
    ///     Information about player vitals
    /// </summary>
    public interface IVitalsInfo
    {
        /// <summary>
        ///     Energy shield information
        /// </summary>
        IVital ES { get; }

        /// <summary>
        ///     Health information
        /// </summary>
        IVital HP { get; }

        /// <summary>
        ///     Mana information
        /// </summary>
        IVital Mana { get; }
    }
}
