// <copyright file="VitalsInfo.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.DynamicConditions
{
    using GameHelper.RemoteObjects.Components;
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions.Interface;

    /// <summary>
    ///     Information about player vitals
    /// </summary>
    public class VitalsInfo : IVitalsInfo
    {
        /// <summary>
        ///     Health information
        /// </summary>
        public IVital HP { get; }

        /// <summary>
        ///     Energy shield information
        /// </summary>
        public IVital ES { get; }

        /// <summary>
        ///     Mana information
        /// </summary>
        public IVital Mana { get; }

        /// <summary>
        ///     Creates a new instance
        /// </summary>
        /// <param name="lifeComponent">Source data for the structure</param>
        public VitalsInfo(Life lifeComponent)
        {
            this.HP = Vital.From(lifeComponent.Health);
            this.ES = Vital.From(lifeComponent.EnergyShield);
            this.Mana = Vital.From(lifeComponent.Mana);
        }
    }
}
