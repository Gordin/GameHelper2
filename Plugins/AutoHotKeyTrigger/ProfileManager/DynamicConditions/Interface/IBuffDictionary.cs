// <copyright file="IBuffDictionary.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.DynamicConditions.Interface
{
    using System.Linq.Dynamic.Core.CustomTypeProviders;

    /// <summary>
    ///     Describes a set of buffs applied to the player
    /// </summary>
    [DynamicLinqType]
    public interface IBuffDictionary
    {
        /// <summary>
        ///     Returns a buff description
        /// </summary>
        /// <param name="id">The buff id</param>
        IStatusEffect this[string id] { get; }

        /// <summary>
        ///     Checks whether the buff is present
        /// </summary>
        bool Has(string id);
    }
}
