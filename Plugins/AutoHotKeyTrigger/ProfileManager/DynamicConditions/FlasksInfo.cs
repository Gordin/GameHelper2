// <copyright file="FlasksInfo.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.DynamicConditions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameHelper.RemoteObjects.States;
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions.Interface;

    /// <summary>
    ///     Information about a set of flasks
    /// </summary>
    public class FlasksInfo : IFlasksInfo
    {
        private const int FlaskCount = 5;

        /// <summary>
        ///     Provides access to the flask array
        /// </summary>
        /// <param name="i">The flask index (0-based)</param>
        public IFlaskInfo this[int i]
        {
            get
            {
                if (i < 0 || i >= FlaskCount)
                {
                    throw new Exception($"Flask index is 0-based and must be in the range of 0-{FlaskCount - 1}");
                }

                return this.flasks[i];
            }
        }

        /// <summary>
        ///     The flask in the first slot;
        /// </summary>
        public IFlaskInfo Flask1 => this[0];

        /// <summary>
        ///     The flask in the second slot;
        /// </summary>
        public IFlaskInfo Flask2 => this[1];

        /// <summary>
        ///     The flask in the third slot;
        /// </summary>
        public IFlaskInfo Flask3 => this[2];

        /// <summary>
        ///     The flask in the fourth slot;
        /// </summary>
        public IFlaskInfo Flask4 => this[3];

        /// <summary>
        ///     The flask in the fifth slot;
        /// </summary>
        public IFlaskInfo Flask5 => this[4];

        private readonly IReadOnlyList<FlaskInfo> flasks;

        /// <summary>
        ///     Creates a new instance
        /// </summary>
        /// <param name="state">State to build the structure from</param>
        public FlasksInfo(InGameState state)
        {
            this.flasks = Enumerable.Range(0, FlaskCount)
                                    .Select(i => state.CurrentAreaInstance.ServerDataObject.FlaskInventory[0, i])
                                    .Select((f, k) => FlaskInfo.From(state, f, k))
                                    .ToList();
        }
    }
}
