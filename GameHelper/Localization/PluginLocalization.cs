// <copyright file="PluginLocalization.cs" company="None">
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
    ///     Loads localized UI text from a plugin-owned Localization directory.
    /// </summary>
    public sealed class PluginLocalization
    {
        private readonly object syncRoot = new();
        private readonly string localizationDirectory;
        private readonly Dictionary<OverlayLanguage, IReadOnlyDictionary<string, string>> cache = new();

        /// <summary>
        ///     Initializes a new instance of the <see cref="PluginLocalization"/> class.
        /// </summary>
        /// <param name="pluginRootDirectory">Plugin root directory passed through the plugin base DllDirectory field.</param>
        public PluginLocalization(string pluginRootDirectory)
        {
            this.localizationDirectory = Path.Combine(pluginRootDirectory, "Localization");
        }

        /// <summary>
        ///     Resolves a localized string by key, falling back to English and then
        ///     <paramref name="fallback"/> when a plugin resource is missing.
        /// </summary>
        public string T(string key, string fallback)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return fallback;
            }

            var currentLanguage = OverlayLocalization.CurrentLanguage;
            if (this.TryGet(currentLanguage, key, out var value))
            {
                return value;
            }

            if (currentLanguage != OverlayLanguage.English &&
                this.TryGet(OverlayLanguage.English, key, out value))
            {
                return value;
            }

            return fallback;
        }

        /// <summary>
        ///     Resolves and formats a localized string using the current culture.
        /// </summary>
        public string F(string key, string fallback, params object[] args) =>
            string.Format(CultureInfo.CurrentCulture, this.T(key, fallback), args);

        /// <summary>
        ///     Resolves a localized label and appends a stable ImGui hidden ID.
        /// </summary>
        public string Label(string key, string fallback, string id) =>
            $"{this.T(key, fallback)}##{id}";

        /// <summary>
        ///     Resolves a localized visible title while keeping the ImGui window/item ID stable.
        /// </summary>
        public string Title(string key, string fallback, string id) =>
            $"{this.T(key, fallback)}###{id}";

        private bool TryGet(OverlayLanguage language, string key, out string value)
        {
            var resources = this.GetResources(language);
            return resources.TryGetValue(key, out value!) && !string.IsNullOrEmpty(value);
        }

        private IReadOnlyDictionary<string, string> GetResources(OverlayLanguage language)
        {
            lock (this.syncRoot)
            {
                if (this.cache.TryGetValue(language, out var resources))
                {
                    return resources;
                }

                resources = this.LoadResources(language);
                this.cache[language] = resources;
                return resources;
            }
        }

        private IReadOnlyDictionary<string, string> LoadResources(OverlayLanguage language)
        {
            foreach (var languageCode in OverlayLocalization.LanguageCodes(language))
            {
                var path = Path.Combine(this.localizationDirectory, $"{languageCode}.json");
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
                    Console.WriteLine($"[PluginLocalization] Failed to read {path}: {ex.Message}");
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"[PluginLocalization] Failed to parse {path}: {ex.Message}");
                }
            }

            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}
