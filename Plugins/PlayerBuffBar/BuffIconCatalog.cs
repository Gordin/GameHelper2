namespace PlayerBuffBar
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Newtonsoft.Json.Linq;

    internal static class BuffIconCatalog
    {
        private const string Poe2DbPageBase = "https://poe2db.tw/us/";
        private const string Poe2DbCdnArtBase = "https://cdn.poe2db.tw/image/art/2dart/";
        private const string Poe2DbReferer = "https://poe2db.tw/";

        private static readonly Regex BuffIconRegex = new(
            @"BuffIcons/([^""'\s>]+\.webp)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex BuffIconRowRegex = new(
            @"<tr><td>BuffIcon</td><td><img[^>]+src=""[^""]*Art/2DArt/((?:BuffIcons|SkillIcons)/[^""]+\.webp)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex BuffIconContainerRegex = new(
            @"buff-icon-container[\s\S]*?src=""[^""]*Art/2DArt/(BuffIcons/[^""]+\.webp)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex PrimarySkillIconRegex = new(
            @"class=""(?:gemSkill|)""[^>]*src=""[^""]*Art/2DArt/(SkillIcons/(?!Support/)[^""]+\.webp)""",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SkillIconRegex = new(
            @"SkillIcons/(?!Support/)([^""'\s>/]+\.webp)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Dictionary<string, string> DefaultPageSlugs = new(StringComparer.OrdinalIgnoreCase)
        {
            ["blood_rage"] = "Blood_Rage",
            ["fortify"] = "Fortify",
            ["adrenaline"] = "Adrenaline",
            ["berserk"] = "Berserk",
            ["molten"] = "Molten_Shell",
            ["granite"] = "Granite",
            ["grace"] = "Grace",
            ["haste"] = "Haste",
            ["herald"] = "Herald_of_Ash",
            ["rage"] = "Berserk",
            ["unholy"] = "Unholy_Might",
            ["unholy_might"] = "Unholy_Might",
            ["phasing"] = "Phasing",
            ["onslaught"] = "Onslaught",
            ["puppet"] = "Puppet_Master",
            ["puppet_master"] = "Puppet_Master",
            ["charged_staff"] = "Charged_Staff",
            ["charged_staff_stack"] = "Charged_Staff",
            ["power_charge"] = "Power_charge",
            ["frenzy_charge"] = "Frenzy_charge",
            ["endurance_charge"] = "Endurance_charge",
            ["archon_undeath"] = "Archon_of_Undeath",
            ["archon_of_undeath"] = "Archon_of_Undeath",
            ["undeath"] = "Archon_of_Undeath",
            ["molten_shell"] = "Molten_Shell",
            ["herald_of_ash"] = "Herald_of_Ash",
            ["herald_of_ice"] = "Herald_of_Ice",
            ["herald_of_thunder"] = "Herald_of_Thunder",
            ["refutation"] = "Refutation",
            ["runic_fortress"] = "Refutation",
        };

        private static readonly Dictionary<string, string> DefaultDirectIcons = new(StringComparer.OrdinalIgnoreCase)
        {
            ["unholy"] = "BuffIcons/UnholyMightBuff.webp",
            ["unholy_might"] = "BuffIcons/UnholyMightBuff.webp",
            ["fortify"] = "BuffIcons/Fortify.webp",
            ["berserk"] = "BuffIcons/Beserk.webp",
            ["blood_rage"] = "BuffIcons/buffrage.webp",
            ["puppet"] = "BuffIcons/Puppeteer.webp",
            ["puppet_master"] = "BuffIcons/Puppeteer.webp",
            ["archon_undeath"] = "BuffIcons/ArchonUndeath.webp",
            ["archon_of_undeath"] = "BuffIcons/ArchonUndeath.webp",
            ["undeath"] = "BuffIcons/ArchonUndeath.webp",
            ["grace"] = "SkillIcons/auraevasion.webp",
            ["herald"] = "SkillIcons/HeraldOfAshSkill.webp",
            ["herald_of_ash"] = "SkillIcons/HeraldOfAshSkill.webp",
            ["herald_of_ice"] = "SkillIcons/HeraldOfIceSkill.webp",
            ["herald_of_thunder"] = "SkillIcons/HeraldOfThunderSkill.webp",
            ["molten"] = "SkillIcons/moltenshield.webp",
            ["molten_shell"] = "SkillIcons/moltenshield.webp",
            ["rage"] = "BuffIcons/Beserk.webp",
            ["haste"] = "SkillIcons/auraspeed.webp",
            ["charged_staff"] = "skillicons/4k/monkchargedstaff.webp",
            ["charged_staff_stack"] = "skillicons/4k/monkchargedstaff.webp",
            ["spear_sandstorm"] = "skillicons/4k/huntresswhirlingslash.webp",
            ["power_charge"] = "BuffIcons/chargeint.webp",
            ["frenzy_charge"] = "BuffIcons/chargedex.webp",
            ["endurance_charge"] = "BuffIcons/chargestr.webp",
            ["refutation"] = "SkillIcons/Refutation.webp",
            ["runic_fortress"] = "SkillIcons/Refutation.webp",
        };

        // Skill gem / display names whose in-game StatusEffects key differs from the watch id.
        private static readonly Dictionary<string, string[]> SkillBuffKeyAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["refutation"] = ["runic_fortress"],
        };

        internal static readonly string[] ResourceIconIds =
        {
            "power_charge",
            "frenzy_charge",
            "endurance_charge",
        };

        internal static string IconsDirectory(string pluginDirectory) => Path.Combine(pluginDirectory, "icons");

        internal static string IconMapPath(string pluginDirectory) => Path.Combine(pluginDirectory, "config", "icon_map.json");

        internal static string DownloadLogPath(string pluginDirectory) => Path.Combine(pluginDirectory, "icon_download.log");

        internal static string Poe2DbRefererHeader => Poe2DbReferer;

        internal static IconMapData LoadIconMap(string pluginDirectory)
        {
            var pageSlugs = new Dictionary<string, string>(DefaultPageSlugs, StringComparer.OrdinalIgnoreCase);
            var directIcons = new Dictionary<string, string>(DefaultDirectIcons, StringComparer.OrdinalIgnoreCase);
            var path = IconMapPath(pluginDirectory);
            if (!File.Exists(path))
            {
                return new IconMapData(pageSlugs, directIcons);
            }

            try
            {
                var root = JObject.Parse(File.ReadAllText(path));
                foreach (var prop in root.Properties())
                {
                    if (prop.Name.Equals("_icons", StringComparison.OrdinalIgnoreCase) && prop.Value is JObject iconsObj)
                    {
                        foreach (var iconProp in iconsObj.Properties())
                        {
                            var value = iconProp.Value?.ToString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(iconProp.Name) && !string.IsNullOrWhiteSpace(value))
                            {
                                directIcons[iconProp.Name.Trim()] = NormalizeIconPath(value);
                            }
                        }

                        continue;
                    }

                    var slug = prop.Value?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(prop.Name) && !string.IsNullOrWhiteSpace(slug))
                    {
                        pageSlugs[prop.Name.Trim()] = slug;
                    }
                }
            }
            catch
            {
            }

            return new IconMapData(pageSlugs, directIcons);
        }

        internal static bool TryResolvePageSlug(string watchId, IReadOnlyDictionary<string, string> pageSlugs, out string pageSlug)
        {
            pageSlug = string.Empty;
            if (string.IsNullOrWhiteSpace(watchId))
            {
                return false;
            }

            var trimmed = watchId.Trim();
            if (pageSlugs.TryGetValue(trimmed, out var exact) && !string.IsNullOrWhiteSpace(exact))
            {
                pageSlug = exact;
                return true;
            }

            foreach (var kv in pageSlugs)
            {
                if (ContainsInsensitive(trimmed, kv.Key) || ContainsInsensitive(kv.Key, trimmed))
                {
                    pageSlug = kv.Value;
                    return true;
                }
            }

            pageSlug = PrettyPageSlug(trimmed);
            return !string.IsNullOrWhiteSpace(pageSlug);
        }

        internal static bool TryResolveDirectIcon(
            string watchId,
            IReadOnlyDictionary<string, string> directIcons,
            out string relativeIconPath)
        {
            relativeIconPath = string.Empty;
            if (string.IsNullOrWhiteSpace(watchId))
            {
                return false;
            }

            var trimmed = watchId.Trim();
            if (directIcons.TryGetValue(trimmed, out var exact) && !string.IsNullOrWhiteSpace(exact))
            {
                relativeIconPath = exact;
                return true;
            }

            // Buff keys can include the active skill-gem id (for example,
            // spear_sandstorm_77). Use the most-specific matching base override.
            var match = directIcons
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value) &&
                    (ContainsInsensitive(trimmed, kv.Key) || ContainsInsensitive(kv.Key, trimmed)))
                .OrderByDescending(kv => kv.Key.Length)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(match.Key))
            {
                relativeIconPath = match.Value;
                return true;
            }

            return false;
        }

        internal static string BuildPageUrl(string pageSlug) => Poe2DbPageBase + Uri.EscapeDataString(pageSlug);

        internal static string BuildCdnUrl(string relativeIconPath)
        {
            var path = relativeIconPath.TrimStart('/').Replace('\\', '/');
            return Poe2DbCdnArtBase + path.ToLowerInvariant();
        }

        internal static bool TryParseBuffIconPath(string html, out string relativeIconPath)
        {
            relativeIconPath = string.Empty;
            if (string.IsNullOrWhiteSpace(html))
            {
                return false;
            }

            var rowMatch = BuffIconRowRegex.Match(html);
            if (rowMatch.Success)
            {
                relativeIconPath = NormalizeIconPath(rowMatch.Groups[1].Value);
                return true;
            }

            var containerMatch = BuffIconContainerRegex.Match(html);
            if (containerMatch.Success)
            {
                relativeIconPath = NormalizeIconPath(containerMatch.Groups[1].Value);
                return true;
            }

            var buffMatch = BuffIconRegex.Match(html);
            if (buffMatch.Success)
            {
                relativeIconPath = NormalizeIconPath("BuffIcons/" + buffMatch.Groups[1].Value);
                return true;
            }

            var primarySkillMatch = PrimarySkillIconRegex.Match(html);
            if (primarySkillMatch.Success)
            {
                relativeIconPath = NormalizeIconPath(primarySkillMatch.Groups[1].Value);
                return true;
            }

            foreach (Match match in SkillIconRegex.Matches(html))
            {
                relativeIconPath = NormalizeIconPath("SkillIcons/" + match.Groups[1].Value);
                return true;
            }

            return false;
        }

        internal static string BuildCacheFileName(string watchId, string extension = ".webp") =>
            SanitizeFileName(watchId.Trim().ToLowerInvariant()) + extension;

        private static readonly HashSet<string> ReservedResourceWatchIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "power_charge",
            "frenzy_charge",
            "endurance_charge",
            "rage",
        };

        internal static bool IsReservedResourceWatchId(string watchId) =>
            !string.IsNullOrWhiteSpace(watchId) && ReservedResourceWatchIds.Contains(watchId.Trim());

        internal static bool IsSupportedImage(byte[] bytes) =>
            IsPng(bytes) || IsWebp(bytes);

        internal static bool IsPng(byte[] bytes) =>
            bytes.Length >= 8 &&
            bytes[0] == 0x89 &&
            bytes[1] == 0x50 &&
            bytes[2] == 0x4E &&
            bytes[3] == 0x47;

        internal static bool IsWebp(byte[] bytes) =>
            bytes.Length >= 12 &&
            bytes[0] == (byte)'R' &&
            bytes[1] == (byte)'I' &&
            bytes[2] == (byte)'F' &&
            bytes[3] == (byte)'F' &&
            bytes[8] == (byte)'W' &&
            bytes[9] == (byte)'E' &&
            bytes[10] == (byte)'B' &&
            bytes[11] == (byte)'P';

        private static string NormalizeIconPath(string path)
        {
            path = path.Replace('\\', '/').Trim();
            const string artPrefix = "Art/2DArt/";
            if (path.StartsWith(artPrefix, StringComparison.OrdinalIgnoreCase))
            {
                path = path[artPrefix.Length..];
            }

            if (!path.StartsWith("BuffIcons/", StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith("SkillIcons/", StringComparison.OrdinalIgnoreCase))
            {
                path = "BuffIcons/" + path.TrimStart('/');
            }

            return path;
        }

        private static string PrettyPageSlug(string raw) =>
            string.Join('_', raw.Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

        private static string SanitizeFileName(string value)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }

            return value;
        }

        internal static bool MatchesBuffKey(string buffKey, string watchId)
        {
            if (string.IsNullOrWhiteSpace(buffKey) || string.IsNullOrWhiteSpace(watchId))
            {
                return false;
            }

            foreach (var candidate in ExpandWatchPatterns(watchId.Trim()))
            {
                if (ContainsInsensitive(buffKey, candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> ExpandWatchPatterns(string watchId)
        {
            yield return watchId;

            var underscored = watchId.Replace(' ', '_');
            if (!string.Equals(underscored, watchId, StringComparison.Ordinal))
            {
                yield return underscored;
            }

            var withoutOf = underscored
                .Replace("_of_", "_", StringComparison.OrdinalIgnoreCase)
                .Replace("of_", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("_of", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(withoutOf, underscored, StringComparison.OrdinalIgnoreCase))
            {
                yield return withoutOf;
            }

            if (SkillBuffKeyAliases.TryGetValue(watchId, out var aliases))
            {
                foreach (var alias in aliases)
                {
                    yield return alias;
                }
            }
        }

        private static bool ContainsInsensitive(string haystack, string needle) =>
            haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

        internal readonly record struct IconMapData(
            Dictionary<string, string> PageSlugs,
            Dictionary<string, string> DirectIcons);
    }
}
