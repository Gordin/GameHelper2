using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LootValue
{
    public class PoeNinjaPrice
    {
        public double Price { get; set; }
        public double PriceChaos { get; set; }
        public string Currency { get; set; } = "ex";
        public double? MaxVolumeRate { get; set; }
        public string MaxVolumeCurrency { get; set; } = string.Empty;
        public double? TotalChange { get; set; }
        public double? Volume { get; set; }
        public string ExchangeRateDisplay { get; set; } = string.Empty;
        public string ChangePercentDisplay { get; set; } = string.Empty;
        public string VolumeDisplay { get; set; } = string.Empty;
    }

    internal sealed class UniquePriceListing
    {
        public string Name { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string BaseType { get; set; } = string.Empty;
        public double PriceChaos { get; set; }
        public List<string> ExplicitMods { get; set; } = new();
    }

    internal sealed class PriceCacheSnapshot
    {
        /// <summary>Schema/semantics version. A cache written by a different version is discarded
        /// (deleted + refetched) rather than trusted. Bump when the cache shape or how it's built changes.</summary>
        public int CacheVersion { get; set; }
        public int PriceSource { get; set; }
        public string League { get; set; } = string.Empty;
        public DateTime LastFetchUtc { get; set; }
        public double ChaosPerDivine { get; set; }
        public double ChaosPerExalted { get; set; }
        public Dictionary<string, double> FlatPricesChaos { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<UniquePriceListing>> UniqueListings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> PathBasenameToItemName { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public static class PoeNinjaPriceFetcher
    {
        public const int SourcePoeNinja = 0;
        public const int SourcePoe2Scout = 1;

        // Bump whenever the cache shape or how the art->name index is built changes, so caches written
        // by an older plugin version are discarded instead of trusted. (v2: art index now built from
        // both poe.ninja + poe2scout icons.)
        private const int CacheSchemaVersion = 2;

        private static readonly string[] ScoutCurrencyCategories =
        {
            "currency", "ritual", "runes", "idol", "essences", "fragments", "abyss", "breach",
            "delirium", "expedition", "incursion", "ultimatum", "vaal", "vaultkeys", "verisium",
            "uncutgems", "lineagesupportgems",
        };

        private static readonly string[] ScoutUniqueCategories =
        {
            "weapon", "armour", "accessory", "flask", "jewel", "map", "sanctum",
        };

        private static readonly string[] NinjaExchangeTypes =
        {
            "Ritual", "Currency", "Runes", "Idols", "Essences", "Fragments", "Abyss", "Breach",
            "Delirium", "Expedition", "Ultimatum", "UncutGems",
        };

        private static readonly string[] NinjaStashTypes =
        {
            "UniqueArmours", "UniqueAccessories", "UniqueCharms", "UniqueWeapons",
        };

        private static readonly HashSet<string> GenericLookupNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Charm", "Ring", "Belt", "Wand", "Staff", "Bow", "Spear", "Gloves", "Boots", "Helmet",
            "Shield", "Quiver", "Amulet", "Focus", "Body Armour", "Quarterstaff", "Sceptre", "Mace",
            "Map", "Idol", "Omen", "Gem", "Flask", "Currency", "Rune",
        };

        private static readonly Dictionary<string, string> DefaultPathBasenames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["goldenuniquecharm"] = "Rite of Passage",
            ["silveruniquecharm"] = "The Fall of the Axe",
            ["stoneuniquecharm"] = "For Utopia",
            ["thawinguniquecharm"] = "Nascent Hope",
            ["dousinguniquecharm"] = "Beira's Anguish",
            ["topazuniquecharm"] = "Valako's Roar",
            ["staunchinguniquecharm"] = "Sanguis Heroum",
            ["groundinguniquecharm"] = "The Black Cat",
            ["rubyuniquecharm"] = "Ngamahu's Chosen",
            ["chaosuniquecharm"] = "Forsaken Bangle",
            ["antidoteuniquecharm"] = "Arakaali's Gift",
            ["sapphireuniquecharm"] = "Breath of the Mountains",
        };

        private static readonly HttpClient Http = CreateHttpClient();

        private static readonly object Gate = new();
        private static Dictionary<string, double> flatPricesChaos = new(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, List<UniquePriceListing>> uniqueListingsByName = new(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> pathBasenameToItemName = new(StringComparer.OrdinalIgnoreCase);

        private static bool isFetching;
        private static string pluginDir = string.Empty;
        private static string cacheFilePath = string.Empty;
        private static DateTime lastFetchTime = DateTime.MinValue;
        private static int configuredSource = SourcePoe2Scout;
        private static string configuredLeague = "Runes of Aldur";
        private static int configuredRefreshMinutes = 5;
        private static double chaosPerDivine = 12.0;
        private static double chaosPerExalted = 0.1;

        public static double DivineToExaltedRate { get; private set; } = 80.0;
        public static int LoadedItemCount { get; private set; }
        public static DateTime LastFetchUtc => lastFetchTime;
        public static bool IsFetching => isFetching;

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            client.DefaultRequestHeaders.Add("User-Agent", "RitualHelper-GameHelper-Plugin");
            return client;
        }

        public static void Configure(int priceSource, string league, int refreshIntervalMinutes)
        {
            configuredSource = priceSource;
            configuredLeague = string.IsNullOrWhiteSpace(league) ? "Runes of Aldur" : league.Trim();
            configuredRefreshMinutes = Math.Max(1, refreshIntervalMinutes);
        }

        public static void Initialize(string pluginDirectory)
        {
            pluginDir = pluginDirectory;
            cacheFilePath = Path.Combine(pluginDirectory, "price_cache.json");

            if (TryLoadCacheFromDisk())
            {
                if (NeedsPathIndexRebuild() ||
                    DateTime.UtcNow - lastFetchTime >= TimeSpan.FromMinutes(configuredRefreshMinutes))
                {
                    StartFetch();
                }

                return;
            }

            StartFetch();
        }

        public static void RefreshIfNeeded()
        {
            if (isFetching || pluginDir == null || pluginDir.Length == 0) return;
            if (DateTime.UtcNow - lastFetchTime < TimeSpan.FromMinutes(configuredRefreshMinutes)) return;
            StartFetch();
        }

        public static void ForceRefresh(string pluginDirectory, bool ignoreCooldown = false)
        {
            if (isFetching) return;
            if (!ignoreCooldown && DateTime.UtcNow - lastFetchTime < TimeSpan.FromSeconds(30)) return;
            pluginDir = pluginDirectory;
            cacheFilePath = Path.Combine(pluginDirectory, "price_cache.json");
            StartFetch();
        }

        public static bool TryResolveDisplayName(string internalPathBasename, out string displayName)
        {
            lock (Gate)
            {
                return TryResolveDisplayNameCore(internalPathBasename, out displayName);
            }
        }

        private static bool TryResolveDisplayNameCore(string internalPathBasename, out string displayName)
        {
            displayName = string.Empty;
            if (string.IsNullOrWhiteSpace(internalPathBasename)) return false;

            if (DefaultPathBasenames.TryGetValue(NormalizeKey(internalPathBasename), out var defaultName))
            {
                displayName = defaultName;
                return true;
            }

            if (pathBasenameToItemName.TryGetValue(NormalizeKey(internalPathBasename), out var resolvedName) &&
                !string.IsNullOrWhiteSpace(resolvedName))
            {
                displayName = resolvedName;
                return true;
            }

            return false;
        }

        public static bool IsGenericLookupName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            var trimmed = name.Trim();
            if (trimmed.Length < 4) return true;
            if (GenericLookupNames.Contains(trimmed)) return true;
            if (trimmed.StartsWith("Item ", StringComparison.Ordinal)) return true;
            return false;
        }

        public static bool HasPriceDataForName(string? name)
        {
            lock (Gate)
            {
                return HasPriceDataForNameUnlocked(name);
            }
        }

        private static bool HasPriceDataForNameUnlocked(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            var key = NormalizeKey(name);
            if (key.Length == 0)
            {
                return false;
            }

            return flatPricesChaos.ContainsKey(key) || uniqueListingsByName.ContainsKey(key);
        }

        public static double GetDivineValue(PoeNinjaPrice price)
        {
            if (price == null) return 0;
            if (chaosPerDivine <= 0) return 0;
            return price.PriceChaos / chaosPerDivine;
        }

        public static double GetChaosPerDivine()
        {
            lock (Gate)
            {
                return chaosPerDivine;
            }
        }

        public static (double Value, string Currency) GetDisplayPrice(PoeNinjaPrice price, int displayCurrency)
        {
            if (price == null) return (0, "divine");

            if (displayCurrency == 2)
                return (Math.Round(price.PriceChaos, 1), "chaos");

            if (displayCurrency == 1)
            {
                var ex = chaosPerExalted > 0 ? price.PriceChaos / chaosPerExalted : price.Price;
                return (Math.Round(ex, 1), "ex");
            }

            var div = chaosPerDivine > 0 ? price.PriceChaos / chaosPerDivine : price.Price;
            return (Math.Round(div, 3), "divine");
        }

        public static PoeNinjaPrice? GetPrice(
            string itemName,
            IReadOnlyList<string>? mods = null,
            string? internalPathBasename = null,
            string? fullItemPath = null,
            string? scoutText = null)
        {
            if (string.IsNullOrWhiteSpace(itemName) &&
                string.IsNullOrWhiteSpace(internalPathBasename) &&
                string.IsNullOrWhiteSpace(scoutText) &&
                (mods == null || mods.Count == 0))
            {
                return null;
            }

            lock (Gate)
            {
                foreach (var candidate in BuildNameCandidates(itemName, internalPathBasename, fullItemPath, scoutText))
                {
                    if (!HasPriceDataForNameUnlocked(candidate))
                    {
                        continue;
                    }

                    var direct = LookupPrice(candidate, mods);
                    if (direct != null) return direct;

                    if (mods != null && mods.Count > 0)
                    {
                        var byMods = LookupByModsForName(candidate, mods);
                        if (byMods != null) return byMods;
                    }
                }

                return null;
            }
        }

        private static List<string> BuildNameCandidates(
            string itemName,
            string? internalPathBasename,
            string? fullItemPath,
            string? scoutText)
        {
            var candidates = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void add(string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                var trimmed = value.Trim();
                if (IsGenericLookupName(trimmed)) return;
                if (seen.Add(trimmed)) candidates.Add(trimmed);
            }

            void addPathBasename(string? basename)
            {
                if (string.IsNullOrWhiteSpace(basename)) return;
                if (TryResolveDisplayNameCore(basename, out var mapped))
                    add(mapped);
                add(basename);
            }

            add(scoutText);
            addPathBasename(internalPathBasename);

            if (!string.IsNullOrWhiteSpace(fullItemPath))
            {
                foreach (var segment in fullItemPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
                    addPathBasename(segment);
            }

            add(itemName);
            return candidates;
        }

        private static PoeNinjaPrice? LookupPrice(string itemName, IReadOnlyList<string>? mods)
        {
            if (string.IsNullOrWhiteSpace(itemName)) return null;

            var chaosFromUnique = 0.0;
            if (mods != null && mods.Count > 0 &&
                uniqueListingsByName.TryGetValue(NormalizeKey(itemName), out var listings) && listings.Count > 0)
            {
                chaosFromUnique = ResolveUniquePrice(listings, mods);
            }

            flatPricesChaos.TryGetValue(NormalizeKey(itemName), out var chaosFromFlat);

            // Scout unique rows can be far below poe.ninja / trade (e.g. cheap uniques). Prefer the higher estimate.
            var chaos = Math.Max(chaosFromUnique, chaosFromFlat);
            if (chaos > 0) return FromChaos(chaos);

            return null;
        }

        private static PoeNinjaPrice? LookupByModsForName(string itemName, IReadOnlyList<string> mods)
        {
            if (string.IsNullOrWhiteSpace(itemName) || mods == null || mods.Count == 0) return null;
            if (!uniqueListingsByName.TryGetValue(NormalizeKey(itemName), out var listings) || listings.Count == 0)
                return null;

            var best = PickBestListingByMods(listings, mods);
            if (best == null) return null;

            flatPricesChaos.TryGetValue(NormalizeKey(itemName), out var chaosFromFlat);
            var chaos = Math.Max(best.PriceChaos, chaosFromFlat);
            return chaos > 0 ? FromChaos(chaos) : null;
        }

        private static double ResolveUniquePrice(List<UniquePriceListing> listings, IReadOnlyList<string>? mods)
        {
            if (listings == null || listings.Count == 0) return 0;

            if (mods != null && mods.Count > 0)
            {
                var best = PickBestListingByMods(listings, mods);
                if (best != null)
                    return best.PriceChaos;

                return GetMedianListingPrice(listings);
            }

            return GetMedianListingPrice(listings);
        }

        private static double GetMedianListingPrice(IEnumerable<UniquePriceListing> listings)
        {
            var prices = new List<double>();
            foreach (var listing in listings)
            {
                if (listing.PriceChaos > 0)
                    prices.Add(listing.PriceChaos);
            }

            if (prices.Count == 0)
                return 0;

            prices.Sort();
            return prices[prices.Count / 2];
        }

        private static UniquePriceListing? PickBestListingByMods(
            IEnumerable<UniquePriceListing> listings,
            IReadOnlyList<string> mods)
        {
            UniquePriceListing? best = null;
            var bestScore = 0;
            foreach (var listing in listings)
            {
                var score = ScoreModMatch(mods, listing.ExplicitMods);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = listing;
                }
            }

            var threshold = mods.Count >= 4 ? 2 : 3;
            return best != null && bestScore >= threshold ? best : null;
        }

        private static int ScoreModMatch(IReadOnlyList<string> itemMods, IReadOnlyList<string> listingMods)
        {
            if (itemMods == null || listingMods == null || itemMods.Count == 0 || listingMods.Count == 0)
                return 0;

            var score = 0;
            foreach (var itemMod in itemMods)
            {
                var itemNorm = NormalizeMod(itemMod);
                if (itemNorm.Length < 4) continue;

                foreach (var listingMod in listingMods)
                {
                    var listingNorm = NormalizeMod(listingMod);
                    if (listingNorm == itemNorm)
                    {
                        score += 3;
                        break;
                    }

                    if (listingNorm.Contains(itemNorm) || itemNorm.Contains(listingNorm))
                    {
                        score += 2;
                        break;
                    }

                    var itemNums = ExtractNumbers(itemMod);
                    var listNums = ExtractNumbers(listingMod);
                    if (itemNums.Count > 0 && itemNums.SequenceEqual(listNums))
                    {
                        score += 1;
                        break;
                    }
                }
            }

            return score;
        }

        private static List<int> ExtractNumbers(string text)
        {
            var nums = new List<int>();
            foreach (Match m in Regex.Matches(text ?? string.Empty, @"-?\d+"))
            {
                if (int.TryParse(m.Value, out var n)) nums.Add(n);
            }
            return nums;
        }

        private static string NormalizeMod(string mod)
        {
            if (string.IsNullOrWhiteSpace(mod)) return string.Empty;
            var s = mod.ToLowerInvariant();
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }

        private static PoeNinjaPrice? FromChaos(double chaosValue)
        {
            if (chaosValue <= 0) return null;

            var price = new PoeNinjaPrice { PriceChaos = chaosValue };

            if (chaosPerDivine > 0 && chaosValue >= chaosPerDivine)
            {
                price.Price = chaosValue / chaosPerDivine;
                price.Currency = "divine";
                return price;
            }

            if (chaosPerExalted > 0 && chaosValue >= chaosPerExalted)
            {
                price.Price = chaosValue / chaosPerExalted;
                price.Currency = "ex";
                return price;
            }

            price.Price = chaosValue;
            price.Currency = "chaos";
            return price;
        }

        private static bool NeedsPathIndexRebuild()
        {
            lock (Gate)
            {
                return pathBasenameToItemName.Count == 0 &&
                       (flatPricesChaos.Count > 0 || uniqueListingsByName.Count > 0);
            }
        }

        private static void StartFetch()
        {
            if (isFetching) return;
            isFetching = true;
            Task.Run(FetchPricesAsync);
        }

        private static async Task FetchPricesAsync()
        {
            try
            {
                var flat = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                var uniques = new Dictionary<string, List<UniquePriceListing>>(StringComparer.OrdinalIgnoreCase);
                var pathNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                double divChaos = chaosPerDivine;
                double exChaos = chaosPerExalted;

                if (configuredSource == SourcePoe2Scout)
                {
                    var rates = await FetchFromScoutAsync(flat, uniques, pathNames, divChaos, exChaos).ConfigureAwait(false);
                    divChaos = rates.DivChaos;
                    exChaos = rates.ExChaos;

                    // Scout unique prices are often too low; merge poe.ninja stash uniques as a floor/ceiling check.
                    // pathNames is shared so the art->name index is built from BOTH sources (union).
                    var ninjaStashRates = await FetchNinjaStashOverviewsAsync(flat, pathNames, divChaos, exChaos).ConfigureAwait(false);
                    divChaos = ninjaStashRates.DivChaos;
                    exChaos = ninjaStashRates.ExChaos;
                }
                else
                {
                    var rates = await FetchFromNinjaAsync(flat, pathNames, divChaos, exChaos).ConfigureAwait(false);
                    divChaos = rates.DivChaos;
                    exChaos = rates.ExChaos;
                }

                lock (Gate)
                {
                    flatPricesChaos = flat;
                    uniqueListingsByName = uniques;
                    pathBasenameToItemName = pathNames;
                    chaosPerDivine = divChaos > 0 ? divChaos : chaosPerDivine;
                    chaosPerExalted = exChaos > 0 ? exChaos : chaosPerExalted;
                    if (chaosPerExalted > 0)
                        DivineToExaltedRate = chaosPerDivine / chaosPerExalted;
                    LoadedItemCount = flat.Count + uniques.Values.Sum(v => v.Count);
                    lastFetchTime = DateTime.UtcNow;
                }

                SaveCacheToDisk();
            }
            catch { }
            finally { isFetching = false; }
        }

        private readonly struct RatePair
        {
            public RatePair(double divChaos, double exChaos)
            {
                DivChaos = divChaos;
                ExChaos = exChaos;
            }

            public double DivChaos { get; }
            public double ExChaos { get; }
        }

        private static async Task<RatePair> FetchFromScoutAsync(
            Dictionary<string, double> flat,
            Dictionary<string, List<UniquePriceListing>> uniques,
            Dictionary<string, string> pathNames,
            double divChaos,
            double exChaos)
        {
            var league = Uri.EscapeDataString(configuredLeague);
            var rates = await UpdateScoutRatesAsync(league, divChaos, exChaos).ConfigureAwait(false);
            divChaos = rates.DivChaos;
            exChaos = rates.ExChaos;

            foreach (var category in ScoutCurrencyCategories)
            {
                await FetchScoutCurrencyCategoryAsync(league, category, flat, pathNames).ConfigureAwait(false);
            }

            foreach (var category in ScoutUniqueCategories)
            {
                await FetchScoutUniqueCategoryAsync(league, category, uniques, pathNames).ConfigureAwait(false);
            }

            return new RatePair(divChaos, exChaos);
        }

        private static async Task<RatePair> UpdateScoutRatesAsync(string leagueEscaped, double divChaos, double exChaos)
        {
            try
            {
                var json = await Http.GetStringAsync("https://poe2scout.com/api/poe2/Leagues").ConfigureAwait(false);
                var root = JObject.Parse(json);
                var leagues = root["value"] as JArray ?? root["Value"] as JArray;
                if (leagues == null) return new RatePair(divChaos, exChaos);

                foreach (var league in leagues)
                {
                    if (!string.Equals(league["Value"]?.ToString(), configuredLeague, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var chaosDiv = league["ChaosDivinePrice"]?.Value<double?>() ?? 0;
                    if (chaosDiv > 0) divChaos = chaosDiv;

                    var divEx = league["DivinePrice"]?.Value<double?>() ?? 0;
                    if (divEx > 0 && chaosDiv > 0)
                        exChaos = chaosDiv / divEx;
                    break;
                }
            }
            catch { }

            try
            {
                var url = $"https://poe2scout.com/api/poe2/Leagues/{leagueEscaped}/Currencies/ByCategory?Category=currency&ReferenceCurrency=chaos&PerPage=250&Page=1";
                var json = await Http.GetStringAsync(url).ConfigureAwait(false);
                var items = JObject.Parse(json)["Items"] as JArray;
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        var text = item["Text"]?.ToString();
                        var price = item["CurrentPrice"]?.Value<double?>() ?? 0;
                        if (string.IsNullOrEmpty(text) || price <= 0) continue;

                        if (text.Contains("Divine Orb", StringComparison.OrdinalIgnoreCase))
                            divChaos = price;
                        if (text.Contains("Exalted Orb", StringComparison.OrdinalIgnoreCase))
                            exChaos = price;
                    }
                }
            }
            catch { }

            return new RatePair(divChaos, exChaos);
        }

        private static async Task FetchScoutCurrencyCategoryAsync(
            string leagueEscaped,
            string category,
            Dictionary<string, double> flat,
            Dictionary<string, string> pathNames)
        {
            var page = 1;
            var pages = 1;
            while (page <= pages)
            {
                try
                {
                    var url = $"https://poe2scout.com/api/poe2/Leagues/{leagueEscaped}/Currencies/ByCategory?Category={category}&ReferenceCurrency=chaos&PerPage=250&Page={page}";
                    var json = await Http.GetStringAsync(url).ConfigureAwait(false);
                    var data = JObject.Parse(json);
                    pages = data["Pages"]?.Value<int?>() ?? 1;

                    if (data["Items"] is not JArray items) break;

                    foreach (var item in items)
                    {
                        var price = item["CurrentPrice"]?.Value<double?>() ?? 0;
                        if (price <= 0) continue;

                        var text = item["Text"]?.ToString();
                        AddFlatPrice(flat, text, price);
                        AddFlatPrice(flat, item["ApiId"]?.ToString(), price);
                        AddFlatPrice(flat, item["ItemMetadata"]?["name"]?.ToString(), price);
                        AddFlatPrice(flat, item["ItemMetadata"]?["base_type"]?.ToString(), price);
                        IndexPathName(pathNames, item["ApiId"]?.ToString(), text);
                        IndexPathName(pathNames, ExtractIconBasename(item["IconUrl"]?.ToString()), text);
                    }
                }
                catch { break; }

                page++;
            }
        }

        private static async Task FetchScoutUniqueCategoryAsync(
            string leagueEscaped,
            string category,
            Dictionary<string, List<UniquePriceListing>> uniques,
            Dictionary<string, string> pathNames)
        {
            var page = 1;
            var pages = 1;
            while (page <= pages)
            {
                try
                {
                    var url = $"https://poe2scout.com/api/poe2/Leagues/{leagueEscaped}/Uniques/ByCategory?Category={category}&ReferenceCurrency=chaos&PerPage=250&Page={page}";
                    var json = await Http.GetStringAsync(url).ConfigureAwait(false);
                    var data = JObject.Parse(json);
                    pages = data["Pages"]?.Value<int?>() ?? 1;
                    if (data["Items"] is not JArray items) break;

                    foreach (var item in items)
                    {
                        var price = item["CurrentPrice"]?.Value<double?>() ?? 0;
                        if (price <= 0) continue;

                        var listing = new UniquePriceListing
                        {
                            Name = item["Name"]?.ToString() ?? string.Empty,
                            Text = item["Text"]?.ToString() ?? string.Empty,
                            BaseType = item["Type"]?.ToString() ?? item["ItemMetadata"]?["base_type"]?.ToString() ?? string.Empty,
                            PriceChaos = price,
                            ExplicitMods = CombineModLists(
                                item["ItemMetadata"]?["implicit_mods"]?.ToObject<List<string>>(),
                                item["ItemMetadata"]?["explicit_mods"]?.ToObject<List<string>>()),
                        };

                        AddUniqueListing(uniques, listing);
                        IndexPathName(pathNames, ExtractIconBasename(item["IconUrl"]?.ToString()), listing.Name);
                        IndexPathName(pathNames, listing.Name, listing.Name);
                    }
                }
                catch { break; }

                page++;
            }
        }

        private static List<string> CombineModLists(IReadOnlyList<string>? first, IReadOnlyList<string>? second)
        {
            var mods = new List<string>();
            if (first != null) mods.AddRange(first);
            if (second != null) mods.AddRange(second);
            return mods;
        }

        private static void IndexPathName(Dictionary<string, string> pathNames, string? pathBasename, string? displayName)
        {
            if (string.IsNullOrWhiteSpace(pathBasename) || string.IsNullOrWhiteSpace(displayName)) return;
            pathNames[NormalizeKey(pathBasename)] = displayName.Trim();
        }

        private static string ExtractIconBasename(string? iconUrl)
        {
            if (string.IsNullOrWhiteSpace(iconUrl)) return string.Empty;

            var withoutQuery = iconUrl.Split('?')[0];
            var file = withoutQuery.Split('/').LastOrDefault();
            if (string.IsNullOrWhiteSpace(file)) return string.Empty;

            var dot = file.LastIndexOf('.');
            return dot > 0 ? file[..dot] : file;
        }

        private static void AddFlatPrice(Dictionary<string, double> flat, string? key, double price)
        {
            if (string.IsNullOrWhiteSpace(key) || price <= 0) return;
            var norm = NormalizeKey(key);
            if (!flat.ContainsKey(norm) || flat[norm] < price)
                flat[norm] = price;
        }

        private static void AddUniqueListing(Dictionary<string, List<UniquePriceListing>> uniques, UniquePriceListing listing)
        {
            if (string.IsNullOrWhiteSpace(listing.Name)) return;

            void add(string key)
            {
                if (string.IsNullOrWhiteSpace(key)) return;
                var norm = NormalizeKey(key);
                if (!uniques.TryGetValue(norm, out var list))
                {
                    list = new List<UniquePriceListing>();
                    uniques[norm] = list;
                }
                list.Add(listing);
            }

            add(listing.Name);
            add(listing.Text);
            if (!string.IsNullOrWhiteSpace(listing.BaseType))
                add($"{listing.Name} {listing.BaseType}");
        }

        private static async Task<RatePair> FetchFromNinjaAsync(Dictionary<string, double> flat, Dictionary<string, string> pathNames, double divChaos, double exChaos)
        {
            var leagueParam = Uri.EscapeDataString(configuredLeague).Replace("%20", "+");

            foreach (var type in NinjaExchangeTypes)
            {
                var url = $"https://poe.ninja/poe2/api/economy/exchange/current/overview?league={leagueParam}&type={type}";
                var rates = await FetchNinjaExchangeApi(url, flat, pathNames, divChaos, exChaos).ConfigureAwait(false);
                divChaos = rates.DivChaos;
                exChaos = rates.ExChaos;
            }

            return await FetchNinjaStashOverviewsAsync(flat, pathNames, divChaos, exChaos).ConfigureAwait(false);
        }

        private static async Task<RatePair> FetchNinjaStashOverviewsAsync(
            Dictionary<string, double> flat,
            Dictionary<string, string> pathNames,
            double divChaos,
            double exChaos)
        {
            var leagueParam = Uri.EscapeDataString(configuredLeague).Replace("%20", "+");

            foreach (var type in NinjaStashTypes)
            {
                var url = $"https://poe.ninja/poe2/api/economy/stash/current/item/overview?league={leagueParam}&type={type}";
                exChaos = await FetchNinjaStashApi(url, flat, pathNames, divChaos, exChaos).ConfigureAwait(false);
            }

            return new RatePair(divChaos, exChaos);
        }

        private static async Task<RatePair> FetchNinjaExchangeApi(string url, Dictionary<string, double> flat, Dictionary<string, string> pathNames, double divChaos, double exChaos)
        {
            try
            {
                var response = await Http.GetStringAsync(url).ConfigureAwait(false);
                var data = JObject.Parse(response);

                var primaryCurrency = data["core"]?["primary"]?.ToString() ?? "divine";
                var rateToken = data["core"]?["rates"]?["exalted"];
                if (rateToken != null)
                {
                    var r = rateToken.Value<double>();
                    if (r > 0) DivineToExaltedRate = r;
                }

                var idToName = new Dictionary<string, string>();
                var idToIcon = new Dictionary<string, string>();
                if (data["items"] is JArray itemsArray)
                {
                    foreach (var item in itemsArray)
                    {
                        var id = item["id"]?.ToString();
                        if (id == null) continue;
                        var name = item["name"]?.ToString();
                        if (name != null) idToName[id] = name;
                        var icon = item["image"]?.ToString() ?? item["icon"]?.ToString();
                        if (!string.IsNullOrEmpty(icon)) idToIcon[id] = icon;
                    }
                }

                if (data["lines"] is JArray lines)
                {
                    foreach (var line in lines)
                    {
                        var id = line["id"]?.ToString();
                        if (id == null || !idToName.TryGetValue(id, out var name)) continue;

                        var pval = line["primaryValue"]?.Value<double>() ?? 0.0;
                        if (pval <= 0) continue;

                        var chaos = PrimaryValueToChaos(pval, primaryCurrency, divChaos, exChaos);
                        AddFlatPrice(flat, name, chaos);
                        if (idToIcon.TryGetValue(id, out var iconUrl))
                            IndexPathName(pathNames, ExtractIconBasename(iconUrl), name);

                        if (name.Contains("Divine", StringComparison.OrdinalIgnoreCase))
                            divChaos = chaos;
                        if (name.Contains("Exalted", StringComparison.OrdinalIgnoreCase))
                            exChaos = chaos;
                    }
                }
            }
            catch { }

            return new RatePair(divChaos, exChaos);
        }

        private static async Task<double> FetchNinjaStashApi(string url, Dictionary<string, double> flat, Dictionary<string, string> pathNames, double divChaos, double exChaos)
        {
            try
            {
                var response = await Http.GetStringAsync(url).ConfigureAwait(false);
                var data = JObject.Parse(response);

                var primaryCurrency = data["core"]?["primary"]?.ToString() ?? "exalted";
                var rateToken = data["core"]?["rates"]?["exalted"];
                if (rateToken != null)
                {
                    var r = rateToken.Value<double>();
                    if (r > 0) DivineToExaltedRate = r;
                }

                if (data["lines"] is JArray lines)
                {
                    foreach (var line in lines)
                    {
                        var name = line["name"]?.ToString();
                        var baseType = line["baseType"]?.ToString() ?? string.Empty;
                        var pval = line["primaryValue"]?.Value<double>() ?? 0.0;
                        if (string.IsNullOrEmpty(name) || pval <= 0) continue;

                        var chaos = PrimaryValueToChaos(pval, primaryCurrency, divChaos, exChaos);
                        var cacheKey = BuildStashCacheKey(name, baseType);
                        AddFlatPrice(flat, cacheKey, chaos);
                        var icon = line["icon"]?.ToString() ?? line["image"]?.ToString();
                        IndexPathName(pathNames, ExtractIconBasename(icon), name);
                    }
                }
            }
            catch { }

            return exChaos;
        }

        private static double PrimaryValueToChaos(double value, string primaryCurrency, double divChaos, double exChaos)
        {
            if (primaryCurrency.Equals("divine", StringComparison.OrdinalIgnoreCase))
                return value * (divChaos > 0 ? divChaos : 1.0);

            return value * (exChaos > 0 ? exChaos : 0.1);
        }

        private static string BuildStashCacheKey(string name, string baseType)
        {
            if (baseType.Contains("Runeforged", StringComparison.OrdinalIgnoreCase))
                return $"{name} Runeforged";

            if (baseType.Contains("Runemastered", StringComparison.OrdinalIgnoreCase))
                return $"{name} Runemastered";

            return name;
        }

        private static string NormalizeKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return string.Empty;
            return Regex.Replace(key.Trim().ToLowerInvariant(), @"[^a-z0-9]+", "");
        }

        private static bool TryLoadCacheFromDisk()
        {
            if (string.IsNullOrEmpty(cacheFilePath) || !File.Exists(cacheFilePath)) return false;

            try
            {
                var snapshot = JsonConvert.DeserializeObject<PriceCacheSnapshot>(File.ReadAllText(cacheFilePath));

                // Written by a different (older) plugin version, or missing required data: discard it
                // entirely so we never trust a stale-schema cache. A fresh fetch rebuilds it.
                if (snapshot == null || snapshot.CacheVersion != CacheSchemaVersion || snapshot.FlatPricesChaos == null)
                {
                    DeleteCacheFromDisk();
                    return false;
                }

                // Valid cache, but for a different source/league than currently selected — leave the file
                // (the next fetch overwrites it) and just fall through to refetch.
                if (snapshot.PriceSource != configuredSource) return false;
                if (!string.Equals(snapshot.League, configuredLeague, StringComparison.OrdinalIgnoreCase)) return false;

                lock (Gate)
                {
                    flatPricesChaos = new Dictionary<string, double>(snapshot.FlatPricesChaos, StringComparer.OrdinalIgnoreCase);
                    uniqueListingsByName = snapshot.UniqueListings != null
                        ? new Dictionary<string, List<UniquePriceListing>>(snapshot.UniqueListings, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, List<UniquePriceListing>>(StringComparer.OrdinalIgnoreCase);
                    pathBasenameToItemName = snapshot.PathBasenameToItemName != null
                        ? new Dictionary<string, string>(snapshot.PathBasenameToItemName, StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    chaosPerDivine = snapshot.ChaosPerDivine > 0 ? snapshot.ChaosPerDivine : chaosPerDivine;
                    chaosPerExalted = snapshot.ChaosPerExalted > 0 ? snapshot.ChaosPerExalted : chaosPerExalted;
                    if (chaosPerExalted > 0)
                        DivineToExaltedRate = chaosPerDivine / chaosPerExalted;
                    lastFetchTime = snapshot.LastFetchUtc;
                    LoadedItemCount = flatPricesChaos.Count + uniqueListingsByName.Values.Sum(v => v.Count);
                }

                return true;
            }
            catch
            {
                // Corrupt / unreadable / incompatible cache from an older version: remove it so it can't
                // keep failing, and fall back to a fresh fetch.
                DeleteCacheFromDisk();
                return false;
            }
        }

        private static void DeleteCacheFromDisk()
        {
            try
            {
                if (!string.IsNullOrEmpty(cacheFilePath) && File.Exists(cacheFilePath))
                {
                    File.Delete(cacheFilePath);
                }
            }
            catch { }
        }

        private static void SaveCacheToDisk()
        {
            if (string.IsNullOrEmpty(cacheFilePath)) return;

            try
            {
                PriceCacheSnapshot snapshot;
                lock (Gate)
                {
                    snapshot = new PriceCacheSnapshot
                    {
                        CacheVersion = CacheSchemaVersion,
                        PriceSource = configuredSource,
                        League = configuredLeague,
                        LastFetchUtc = lastFetchTime,
                        ChaosPerDivine = chaosPerDivine,
                        ChaosPerExalted = chaosPerExalted,
                        FlatPricesChaos = new Dictionary<string, double>(flatPricesChaos, StringComparer.OrdinalIgnoreCase),
                        UniqueListings = new Dictionary<string, List<UniquePriceListing>>(uniqueListingsByName, StringComparer.OrdinalIgnoreCase),
                        PathBasenameToItemName = new Dictionary<string, string>(pathBasenameToItemName, StringComparer.OrdinalIgnoreCase),
                    };
                }

                File.WriteAllText(cacheFilePath, JsonConvert.SerializeObject(snapshot, Formatting.Indented));
            }
            catch { }
        }
    }
}
