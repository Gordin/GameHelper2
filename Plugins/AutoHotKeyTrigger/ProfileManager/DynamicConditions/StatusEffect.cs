// <copyright file="StatusEffect.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.DynamicConditions
{
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions.Interface;

    /// <summary>
    ///     Information about a status effect
    /// </summary>
    /// <param name="Exists">Whether it exists on the player currently</param>
    /// <param name="TimeLeft">Time left in seconds</param>
    /// <param name="TotalTime">Total time the effect will last</param>
    /// <param name="Charges">Amount of stacks of the effect</param>
    /// <param name="Effectiveness">Effectiveness of effects like elusive.</param>
    public record StatusEffect(bool Exists, double TimeLeft, double TotalTime, int Charges, int Effectiveness) : IStatusEffect
    {
        /// <summary>
        ///     Time left in percent from total time
        /// </summary>
        public double PercentTimeLeft =>
            this.Exists
                ? double.IsPositiveInfinity(this.TimeLeft)
                      ? 100
                      : 100 * (this.TimeLeft / this.TotalTime)
                : 0;
    }
}
