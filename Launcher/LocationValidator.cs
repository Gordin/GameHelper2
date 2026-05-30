// <copyright file="LocationValidator.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace Launcher
{
    using System;
    using System.IO;
    using System.Reflection;
    using System.Text.RegularExpressions;

    /// <summary>
    ///     A helper class to validate location.
    /// </summary>
    public static class LocationValidator
    {
        private static readonly Regex BadNameRegex = new(
            @"poe|path\s*of\s*exile|overlay|helper|hud|desktop",
            RegexOptions.IgnoreCase);

        /// <summary>
        ///     Checks the GameHelper tool path name to make sure it doesn't have any bad information.
        ///     If it does, print the issue to the stdout.
        /// </summary>
        /// <returns>returns true in case of good path otherwise false.</returns>
        public static bool IsGameHelperLocationGood(out string? message)
        {
            message = null;
            var currentProcessPath = Assembly.GetExecutingAssembly().Location;
            var directoryPath = Path.GetDirectoryName(currentProcessPath);
            if (string.IsNullOrEmpty(directoryPath))
            {
                return true;
            }

            var pathMatch = BadNameRegex.Match(directoryPath);
            if (pathMatch.Success)
            {
                message = $"You have downloaded GameHelper in a bad folder/pathname. " +
                    $"Your folder/pathname contains \"{pathMatch.Value}\". " +
                    $"This is bad for your account. Please rename/move GameHelper to a better folder/pathname.";
                return false;
            }

            return true;
        }
    }
}
