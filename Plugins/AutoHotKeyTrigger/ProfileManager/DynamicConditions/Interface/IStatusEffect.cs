// <copyright file="IStatusEffect.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.DynamicConditions.Interface
{
    /// <summary>
    ///     Information about a status effect
    /// </summary>
    public interface IStatusEffect
    {
        /// <summary>
        ///     Amount of stacks of the effect
        /// </summary>
        int Charges { get; init; }

        /// <summary>
        ///     Whether it exists on the player currently
        /// </summary>
        bool Exists { get; init; }

        /// <summary>
        ///     Time left in percent from total time
        /// </summary>
        double PercentTimeLeft { get; }

        /// <summary>
        ///     Time left in seconds
        /// </summary>
        double TimeLeft { get; init; }

        /// <summary>
        ///     Total time the effect will last
        /// </summary>
        double TotalTime { get; init; }

        /// <summary>
        ///     Effectiveness of the status effect like elusive
        ///     Value might change from effect to effect and might not be between 0 - 100.
        /// </summary>
        int Effectiveness { get; init; }
    }
}
