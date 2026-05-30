// <copyright file="IFlasksInfo.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.DynamicConditions.Interface
{
    /// <summary>
    ///     Information about a set of flasks
    /// </summary>
    public interface IFlasksInfo
    {
        /// <summary>
        ///     Provides access to the flask array
        /// </summary>
        /// <param name="i">The flask index (0-based)</param>
        IFlaskInfo this[int i] { get; }

        /// <summary>
        ///     The flask in the first slot;
        /// </summary>
        IFlaskInfo Flask1 { get; }

        /// <summary>
        ///     The flask in the second slot;
        /// </summary>
        IFlaskInfo Flask2 { get; }

        /// <summary>
        ///     The flask in the third slot;
        /// </summary>
        IFlaskInfo Flask3 { get; }

        /// <summary>
        ///     The flask in the fourth slot;
        /// </summary>
        IFlaskInfo Flask4 { get; }

        /// <summary>
        ///     The flask in the fifth slot;
        /// </summary>
        IFlaskInfo Flask5 { get; }
    }
}
