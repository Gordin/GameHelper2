// <copyright file="MiscHelper.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace Launcher
{
    using System;
    using System.Linq;

    /// <summary>
    ///     A helper class containing misc functions.
    /// </summary>
    public static class MiscHelper
    {
        /// <summary>
        ///     Utility function that returns randomly generated string.
        /// </summary>
        /// <returns>randomly generated string.</returns>
        public static string GenerateRandomString()
        {
            //more common letters!
            const string characters = "qwertyuiopasdfghjklzxcvbnm" + "eioadfc";
            var random = new Random();

            char GetRandomCharacter()
            {
                return characters[random.Next(0, characters.Length)];
            }

            string GetWord()
            {
                return char.ToUpperInvariant(GetRandomCharacter()) +
                       new string(Enumerable.Range(0, random.Next(5, 10))
                                            .Select(_ => GetRandomCharacter())
                                            .ToArray());
            }

            return string.Join(' ', Enumerable.Range(0, random.Next(1, 4)).Select(_ => GetWord()));
        }
    }
}
