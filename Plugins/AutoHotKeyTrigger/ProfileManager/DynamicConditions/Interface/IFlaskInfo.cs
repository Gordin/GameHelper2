// <copyright file="IFlaskInfo.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.DynamicConditions.Interface
{
    /// <summary>
    ///     The structure describing a flask state
    /// </summary>
    public interface IFlaskInfo
    {
        /// <summary>
        ///     Whether the flask effect is active now
        /// </summary>
        bool Active { get; init; }

        /// <summary>
        ///     Current charge amount of a flask
        /// </summary>
        int Charges { get; init; }

        /// <summary>
        ///     Does flask have enough charges for use.
        /// </summary>
        bool IsUsable { get; init; }
    }
}
