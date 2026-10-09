// <copyright file="PluginStrings.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>
namespace GameHelper.Localization
{
    using System;
    using System.Collections.Concurrent;
    using System.IO;

    /// <summary>Provides plugin-owned resources to static UI helpers without retaining plugin assemblies.</summary>
    public static class PluginStrings
    {
        private static readonly ConcurrentDictionary<string, PluginLocalization> Catalogs = new(StringComparer.Ordinal);

        /// <summary>Gets a named plugin's localization catalog.</summary>
        public static PluginLocalization For(string pluginName) => Catalogs.GetOrAdd(pluginName,
            name => new PluginLocalization(Path.Combine(AppContext.BaseDirectory, "Plugins", name)));
    }
}
