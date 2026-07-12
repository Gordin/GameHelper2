// <copyright file="MonsterCategories.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using GameHelper.RemoteEnums;

    /// <summary>
    ///     Maps a monster's metadata path (its <c>MonsterVarieties</c> Id, e.g.
    ///     "Metadata/Monsters/BaneSapling/BaneSapling") to its <see cref="MonsterCategory" /> flags.
    ///     <para>
    ///     The mapping is loaded once from the embedded <c>Data/MonsterCategories.tsv</c> resource,
    ///     generated from the game's <c>MonsterVarieties</c> Tags column. To refresh it after a game
    ///     patch, regenerate that file and rebuild (see the <c>beast-detection</c> Claude memory).
    ///     </para>
    /// </summary>
    public static class MonsterCategories
    {
        private const string ResourceSuffix = "Data.MonsterCategories.tsv";

        private static readonly Lazy<IReadOnlyDictionary<string, MonsterCategory>> Map = new(Load);

        /// <summary>
        ///     Gets the number of known path -> category mappings.
        /// </summary>
        public static int Count => Map.Value.Count;

        /// <summary>
        ///     Gets the <see cref="MonsterCategory" /> flags for a monster metadata path. Any
        ///     <c>@variant</c> suffix on the path is ignored (the base equals the MonsterVariety Id).
        ///     Returns <see cref="MonsterCategory.None" /> for unknown / non-monster paths.
        /// </summary>
        /// <param name="monsterPath">the entity's metadata path (e.g. <c>Entity.Path</c>).</param>
        /// <returns>the category flags, or <see cref="MonsterCategory.None" /> when unmapped.</returns>
        public static MonsterCategory Get(string monsterPath)
        {
            if (string.IsNullOrEmpty(monsterPath))
            {
                return MonsterCategory.None;
            }

            var path = monsterPath;
            var at = path.IndexOf('@');
            if (at >= 0)
            {
                path = path[..at];
            }

            return Map.Value.TryGetValue(path, out var category) ? category : MonsterCategory.None;
        }

        private static IReadOnlyDictionary<string, MonsterCategory> Load()
        {
            var result = new Dictionary<string, MonsterCategory>(StringComparer.Ordinal);
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith(ResourceSuffix, StringComparison.Ordinal));
                if (resourceName == null)
                {
                    Console.WriteLine($"[MonsterCategories] Embedded resource '{ResourceSuffix}' not found.");
                    return result;
                }

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    return result;
                }

                using var reader = new StreamReader(stream);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0 || line[0] == '#')
                    {
                        continue;
                    }

                    var tab = line.IndexOf('\t');
                    if (tab <= 0)
                    {
                        continue;
                    }

                    var path = line[..tab];
                    if (int.TryParse(line[(tab + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags))
                    {
                        result[path] = (MonsterCategory)flags;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MonsterCategories] Failed to load mapping: {ex}");
            }

            return result;
        }
    }
}
