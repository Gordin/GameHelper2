// <copyright file="FlaskInfo.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.DynamicConditions
{
    using System;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.RemoteObjects.States;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions.Interface;
    using System.Collections.Generic;
    using GameHelper.RemoteEnums;

    /// <summary>
    ///     The structure describing a flask state
    /// </summary>
    /// <param name="Active">Whether the flask effect is active now</param>
    /// <param name="Charges">Current charge amount of a flask</param>
    /// <param name="IsUsable">Is the flask useable or not</param>
    public record FlaskInfo(bool Active, int Charges, bool IsUsable) : IFlaskInfo
    {
        private static IntPtr[] flaskAddressesCache  = new IntPtr[5];
        private static int[] flaskPerUseChargesCache = new int[5];

        /// <summary>
        ///     Create a new instance from the state and flask data
        /// </summary>
        /// <param name="state">State to build the structure from</param>
        /// <param name="flaskItem">The flask entity</param>
        /// <param name="slot">flask slot in the flask inventory</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static FlaskInfo From(InGameState state, Item flaskItem, int slot)
        {
            if (flaskItem.Address == IntPtr.Zero)
            {
                return new FlaskInfo(false, 0, false);
            }

            if (flaskAddressesCache[slot] != flaskItem.Address)
            {
                if (TryCacheFlaskPerUseCharges(state, flaskItem, slot))
                {
                    flaskAddressesCache[slot] = flaskItem.Address;
                }
                else
                {
                    return new FlaskInfo(false, 0, false);
                }
            }

            var active = state.CurrentAreaInstance.Player.TryGetComponent<Buffs>(out var playerBuffs) &&
                playerBuffs.FlaskActive[slot];
            var charges = flaskItem.TryGetComponent<Charges>(out var chargesComponent) ? chargesComponent.Current : 0;
            return new FlaskInfo(active, charges, charges >= flaskPerUseChargesCache[slot]);
        }

        private static bool TryCacheFlaskPerUseCharges(InGameState state, Item flask, int slot)
        {
            int chargesChangePercent = state.CurrentAreaInstance.Player.TryGetComponent<Stats>(out var statComp) ?
                statComp.StatsChangedByItems.GetValueOrDefault(GameStats.flask_charges_used_positive_percentage) : default;
            float change = (100 + chargesChangePercent) / 100f;
            if (flask.TryGetComponent<Mods>(out var modComp))
            {
                foreach (var (name, values) in modComp.ExplicitMods)
                {
                    if (name.ToLower().Contains("flaskchargesused"))
                    {
                        change *= (100 + values.value0) / 100f; // stack multiplicatively
                    }
                }
            }

            if (flask.TryGetComponent<Charges>(out var chargeComp))
            {
                flaskPerUseChargesCache[slot] = (int)(chargeComp.PerUseCharge * change);
            }

            return flaskPerUseChargesCache[slot] > 0;
        }
    }
}
