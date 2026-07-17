// <copyright file="MonsterCategory.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteEnums
{
    using System;

    /// <summary>
    ///     The Monster Category tag(s) a monster carries in PoE2 (Humanoid, Beast, Undead,
    ///     Construct, Demon, Eldritch). A monster may carry more than one — e.g. a werewolf is
    ///     <see cref="Humanoid" /> | <see cref="Beast" />, and a skeletal snake is
    ///     <see cref="Beast" /> | <see cref="Undead" /> — so this is a <see cref="FlagsAttribute" />
    ///     set rather than a single value. Resolved from the shipped <c>Data/MonsterCategories.tsv</c>
    ///     by <see cref="GameHelper.Utils.MonsterCategories" /> (that file's tags come straight from
    ///     the game's <c>MonsterVarieties</c> data table).
    /// </summary>
    [Flags]
    public enum MonsterCategory
    {
        /// <summary>No known category (non-monster, or an uncategorised monster variety).</summary>
        None = 0,

        /// <summary>Humanoid category.</summary>
        Humanoid = 1,

        /// <summary>Beast category (Spirit Walker / Tame Beast targets).</summary>
        Beast = 2,

        /// <summary>Undead category.</summary>
        Undead = 4,

        /// <summary>Construct category.</summary>
        Construct = 8,

        /// <summary>Demon category.</summary>
        Demon = 16,

        /// <summary>Eldritch category.</summary>
        Eldritch = 32,
    }
}
