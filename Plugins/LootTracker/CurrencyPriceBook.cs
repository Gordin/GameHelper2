namespace LootTracker
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;

    /// <summary>A read-only snapshot of LootValue's cache. No guessed or default exchange rates.</summary>
    public sealed class CurrencyPriceBook
    {
        public sealed record Value(double Exalted, int UnpricedTypes, long UnpricedCount);
        private readonly Dictionary<string, double> prices;
        private readonly Dictionary<string, string> paths;
        private readonly Dictionary<string, double> translatedNames = new(StringComparer.Ordinal);
        public double ChaosPerExalted { get; }
        public double DivineToExalted { get; }
        public string League { get; }
        public DateTimeOffset UpdatedUtc { get; }

        public CurrencyPriceBook(string json, string expectedLeague, int expectedSource, DateTimeOffset now,
            IReadOnlyDictionary<string, string>? english = null, IReadOnlyDictionary<string, string>? localized = null)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            this.League = root.GetProperty("League").GetString() ?? string.Empty;
            this.UpdatedUtc = root.GetProperty("LastFetchUtc").GetDateTimeOffset();
            if (root.GetProperty("CacheVersion").GetInt32() != 2 ||
                root.GetProperty("PriceSource").GetInt32() != expectedSource ||
                !this.League.Equals(expectedLeague, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cache does not match the selected league/source.");
            if (now - this.UpdatedUtc > TimeSpan.FromHours(24) || this.UpdatedUtc - now > TimeSpan.FromMinutes(5))
                throw new InvalidOperationException("Price cache is stale or has an invalid timestamp.");
            this.ChaosPerExalted = root.GetProperty("ChaosPerExalted").GetDouble();
            var divine = root.GetProperty("ChaosPerDivine").GetDouble();
            if (!double.IsFinite(this.ChaosPerExalted) || this.ChaosPerExalted <= 0 || !double.IsFinite(divine) || divine <= 0)
                throw new InvalidOperationException("No valid exchange rate.");
            this.DivineToExalted = divine / this.ChaosPerExalted;
            this.prices = new(StringComparer.OrdinalIgnoreCase);
            foreach (var p in root.GetProperty("FlatPricesChaos").EnumerateObject())
                if (p.Value.TryGetDouble(out var price) && double.IsFinite(price) && price > 0) this.prices[p.Name] = price;
            this.paths = new(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("PathBasenameToItemName", out var names) && names.ValueKind == JsonValueKind.Object)
                foreach (var p in names.EnumerateObject())
                    if (p.Value.GetString() is { } name) this.paths[p.Name] = name;
            if (english != null && localized != null)
                foreach (var (key, text) in english)
                    if (key.StartsWith("terms.", StringComparison.Ordinal) && localized.TryGetValue(key, out var local) &&
                        this.prices.TryGetValue(Normalize(text), out var price)) this.translatedNames[local] = price;
        }

        public Value Estimate(IReadOnlyDictionary<string, CurrencyTotal> currency)
        {
            double value = 0;
            int unknown = 0;
            long unknownCount = 0;
            foreach (var (metadata, item) in currency)
            {
                if (item.Count <= 0) continue;
                var basename = Normalize(metadata.Split('/').Last());
                double chaos = 0;
                var found = this.paths.TryGetValue(basename, out var name) && this.prices.TryGetValue(Normalize(name), out chaos);
                if (!found) found = this.prices.TryGetValue(basename, out chaos);
                if (!found) found = this.prices.TryGetValue(Normalize(item.Name), out chaos);
                if (!found) found = this.translatedNames.TryGetValue(item.Name, out chaos);
                if (found) value += chaos / this.ChaosPerExalted * item.Count;
                else { unknown++; unknownCount = checked(unknownCount + item.Count); }
            }
            return new Value(value, unknown, unknownCount);
        }

        private static string Normalize(string name) => Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "");
    }
}
