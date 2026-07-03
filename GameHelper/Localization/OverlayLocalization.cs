// <copyright file="OverlayLocalization.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Localization
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using Newtonsoft.Json;

    /// <summary>
    ///     Languages supported by the main overlay UI.
    /// </summary>
    public enum OverlayLanguage
    {
        /// <summary>
        ///     English UI text.
        /// </summary>
        English,

        /// <summary>
        ///     Traditional Chinese UI text.
        /// </summary>
        ChineseTraditional = 1,

        /// <summary>
        ///     French UI text.
        /// </summary>
        French,

        /// <summary>
        ///     German UI text.
        /// </summary>
        German,

        /// <summary>
        ///     Spanish (Spain) UI text.
        /// </summary>
        SpanishSpain,

        /// <summary>
        ///     Japanese UI text.
        /// </summary>
        Japanese,

        /// <summary>
        ///     Korean UI text.
        /// </summary>
        Korean,

        /// <summary>
        ///     Portuguese (Brazil) UI text.
        /// </summary>
        PortugueseBrazil,

        /// <summary>
        ///     Russian UI text.
        /// </summary>
        Russian,

        /// <summary>
        ///     Thai UI text.
        /// </summary>
        Thai,

        /// <summary>
        ///     Simplified Chinese UI text.
        /// </summary>
        ChineseSimplified,
    }

    /// <summary>
    ///     Localizes main overlay UI text and keeps the legacy plugin shim intact.
    /// </summary>
    public static class OverlayLocalization
    {
        private static readonly object SyncRoot = new();
        private static readonly Dictionary<OverlayLanguage, IReadOnlyDictionary<string, string>> Cache = new();

        /// <summary>
        ///     Languages exposed in the main settings window.
        /// </summary>
        public static readonly OverlayLanguage[] SupportedLanguages =
        {
            OverlayLanguage.English,
            OverlayLanguage.French,
            OverlayLanguage.German,
            OverlayLanguage.SpanishSpain,
            OverlayLanguage.Japanese,
            OverlayLanguage.Korean,
            OverlayLanguage.PortugueseBrazil,
            OverlayLanguage.Russian,
            OverlayLanguage.Thai,
            OverlayLanguage.ChineseSimplified,
            OverlayLanguage.ChineseTraditional,
        };

        /// <summary>
        ///     Gets the language currently selected for overlay UI text.
        /// </summary>
        public static OverlayLanguage CurrentLanguage => Core.GHSettings.UiLanguage;

        /// <summary>
        ///     Gets a value indicating whether the overlay language is German.
        ///     Kept for compatibility with plugins ported from older localized forks.
        /// </summary>
        public static bool IsGerman => CurrentLanguage == OverlayLanguage.German;

        /// <summary>
        ///     Returns the localized string for compatibility with older plugins.
        ///     The main UI should use keyed resources through <see cref="T"/>.
        /// </summary>
        /// <param name="english">The English string (always returned).</param>
        /// <param name="german">The German string.</param>
        /// <returns>The localized legacy string.</returns>
        public static string L(string english, string german) => IsGerman ? german : english;

        /// <summary>
        ///     Resolves a localized string by key, falling back to English and then
        ///     <paramref name="fallback"/> when a resource is missing.
        /// </summary>
        public static string T(string key, string fallback)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return fallback;
            }

            if (TryGet(CurrentLanguage, key, out var value))
            {
                return value;
            }

            if (CurrentLanguage != OverlayLanguage.English &&
                TryGet(OverlayLanguage.English, key, out value))
            {
                return value;
            }

            return fallback;
        }

        /// <summary>
        ///     Resolves and formats a localized string using the current culture.
        /// </summary>
        public static string F(string key, string fallback, params object[] args) =>
            string.Format(CultureInfo.CurrentCulture, T(key, fallback), args);

        /// <summary>
        ///     Resolves a localized label and appends a stable ImGui hidden ID.
        /// </summary>
        public static string Label(string key, string fallback, string id) =>
            $"{T(key, fallback)}##{id}";

        /// <summary>
        ///     Resolves a localized visible title while keeping the ImGui window/item ID stable.
        /// </summary>
        public static string Title(string key, string fallback, string id) =>
            $"{T(key, fallback)}###{id}";

        /// <summary>
        ///     Gets the current-language display name for a supported language option.
        /// </summary>
        public static string DisplayName(OverlayLanguage language) => language switch
        {
            OverlayLanguage.ChineseSimplified => T("ui.language.chinese_simplified", "Simplified Chinese"),
            OverlayLanguage.ChineseTraditional => T("ui.language.chinese_traditional", "Traditional Chinese"),
            OverlayLanguage.French => T("ui.language.french", "French"),
            OverlayLanguage.German => T("ui.language.german", "German"),
            OverlayLanguage.SpanishSpain => T("ui.language.spanish_spain", "Spanish (Spain)"),
            OverlayLanguage.Japanese => T("ui.language.japanese", "Japanese"),
            OverlayLanguage.Korean => T("ui.language.korean", "Korean"),
            OverlayLanguage.PortugueseBrazil => T("ui.language.portuguese_brazil", "Portuguese (Brazil)"),
            OverlayLanguage.Russian => T("ui.language.russian", "Russian"),
            OverlayLanguage.Thai => T("ui.language.thai", "Thai"),
            _ => T("ui.language.english", "English"),
        };

        private static bool TryGet(OverlayLanguage language, string key, out string value)
        {
            var resources = GetResources(language);
            return resources.TryGetValue(key, out value!) && !string.IsNullOrEmpty(value);
        }

        private static IReadOnlyDictionary<string, string> GetResources(OverlayLanguage language)
        {
            lock (SyncRoot)
            {
                if (Cache.TryGetValue(language, out var resources))
                {
                    return resources;
                }

                resources = LoadResources(language);
                Cache[language] = resources;
                return resources;
            }
        }

        private static IReadOnlyDictionary<string, string> LoadResources(OverlayLanguage language)
        {
            foreach (var languageCode in LanguageCodes(language))
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Localization", $"{languageCode}.json");
                if (!File.Exists(path))
                {
                    continue;
                }

                try
                {
                    var content = File.ReadAllText(path);
                    return JsonConvert.DeserializeObject<Dictionary<string, string>>(content) ??
                           new Dictionary<string, string>(StringComparer.Ordinal);
                }
                catch (IOException ex)
                {
                    Console.WriteLine($"[OverlayLocalization] Failed to read {path}: {ex.Message}");
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"[OverlayLocalization] Failed to parse {path}: {ex.Message}");
                }
            }

            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        /// <summary>
        ///     Gets the resource file language code for a supported overlay language.
        /// </summary>
        public static string LanguageCode(OverlayLanguage language) => language switch
        {
            OverlayLanguage.ChineseSimplified => "zh-CN",
            OverlayLanguage.ChineseTraditional => "zh-Hant",
            OverlayLanguage.French => "fr-FR",
            OverlayLanguage.German => "de-DE",
            OverlayLanguage.SpanishSpain => "es-ES",
            OverlayLanguage.Japanese => "ja-JP",
            OverlayLanguage.Korean => "ko-KR",
            OverlayLanguage.PortugueseBrazil => "pt-BR",
            OverlayLanguage.Russian => "ru-RU",
            OverlayLanguage.Thai => "th-TH",
            _ => "en-US",
        };

        /// <summary>
        ///     Gets resource file language codes in fallback order.
        /// </summary>
        public static IReadOnlyList<string> LanguageCodes(OverlayLanguage language) => language switch
        {
            _ => new[] { LanguageCode(language) },
        };
    }
}
