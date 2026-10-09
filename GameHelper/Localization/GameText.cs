// <copyright file="GameText.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>
namespace GameHelper.Localization
{
    using System;
    using System.Collections.Concurrent;
    using System.Linq;
    using System.Text.RegularExpressions;

    /// <summary>Localizes display values without changing game identifiers, filters, or saved keys.</summary>
    public static class GameText
    {
        private static readonly ConcurrentDictionary<string, string> Keys = new(StringComparer.Ordinal);

        public static string Display(string? value)
        {
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
            var key = Keys.GetOrAdd(value, text => "terms." + Regex.Replace(text.ToLowerInvariant(), @"[^\p{L}\p{N}]+", "_").Trim('_'));
            return OverlayLocalization.T(key, value);
        }

        public static string Lines(string value) => string.Join("\n",
            value.Split('\n').Select(line => Display(line.TrimEnd('\r'))));

        public static string Flags(string value) => string.Join(
            Core.GHSettings.UiLanguage is OverlayLanguage.ChineseTraditional or OverlayLanguage.ChineseSimplified ? "、" : ", ",
            value.Split(", ").Select(Display));
    }
}
