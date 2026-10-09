// <copyright file="TrackingLedger.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace LootTracker
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>One observed currency stack, detached from game memory.</summary>
    public sealed record CurrencyObservation(string Key, string Name, long Count);

    /// <summary>The server instance hash is paired with the area ID to identify return visits.</summary>
    public sealed record AreaIdentity(string Hash, string Id, string Name)
    {
        public string Key => this.Id + "|" + this.Hash;
    }

    public sealed class CurrencyTotal
    {
        public string Name { get; set; } = string.Empty;
        public long Count { get; set; }
    }

    public sealed class MapRun
    {
        public string AreaKey { get; set; } = string.Empty;
        public string AreaId { get; set; } = string.Empty;
        public string AreaHash { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTimeOffset FirstSeenUtc { get; set; }
        public DateTimeOffset LastSeenUtc { get; set; }
        public double ActiveSeconds { get; set; }
        public Dictionary<string, CurrencyTotal> Currency { get; set; } = new(StringComparer.Ordinal);
    }

    public sealed class TrackingSession
    {
        public int SchemaVersion { get; set; } = 1;
        public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
        public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;
        public bool Paused { get; set; }
        public double SessionSeconds { get; set; }
        public List<MapRun> Maps { get; set; } = new();
    }

    /// <summary>
    /// Counts positive changes in aggregate backpack currency. It does not infer ground drops or
    /// market value. Observation baselines and clock endpoints are deliberately never persisted.
    /// </summary>
    public sealed class TrackingLedger
    {
        private Dictionary<string, CurrencyObservation>? baseline;
        private string? previousAreaKey;
        private double? previousTimestamp;

        public TrackingLedger(TrackingSession? session = null)
        {
            this.Session = session ?? new TrackingSession();
            if (this.Session.SchemaVersion != 1 || this.Session.Maps == null ||
                !double.IsFinite(this.Session.SessionSeconds) || this.Session.SessionSeconds < 0 ||
                !Guid.TryParseExact(this.Session.SessionId, "N", out _) ||
                this.Session.Maps.Any(map => map == null || string.IsNullOrWhiteSpace(map.AreaKey) ||
                    !double.IsFinite(map.ActiveSeconds) || map.ActiveSeconds < 0 || map.Currency == null ||
                    map.Currency.Any(pair => pair.Value == null || pair.Value.Count < 0)) ||
                this.Session.Maps.Select(m => m.AreaKey).Distinct(StringComparer.Ordinal).Count() != this.Session.Maps.Count)
                throw new ArgumentException("Invalid or unsupported LootTracker session.", nameof(session));
        }

        public TrackingSession Session { get; }
        public MapRun? CurrentMap { get; private set; }
        public bool HasBaseline => this.baseline != null;
        public double TotalSeconds => this.Session.Maps.Sum(map => map.ActiveSeconds);

        public void SetPaused(bool paused)
        {
            this.Session.Paused = paused;
            this.Interrupt();
        }

        public void Interrupt()
        {
            this.baseline = null;
            this.previousTimestamp = null;
            this.previousAreaKey = null;
        }

        /// <summary>
        /// A null inventory is an unavailable/unsafe sample, never an empty backpack. Long gaps,
        /// area switches, pause and restart all require a fresh baseline. Timer uses monotonic time.
        /// </summary>
        public void Observe(AreaIdentity? area, bool timerEligible,
            IReadOnlyList<CurrencyObservation>? inventory, double timestamp, DateTimeOffset utcNow)
        {
            if (this.Session.Paused || !timerEligible || area == null ||
                string.IsNullOrWhiteSpace(area.Hash) || string.IsNullOrWhiteSpace(area.Id) ||
                !double.IsFinite(timestamp))
            {
                this.Interrupt();
                return;
            }

            var map = this.Session.Maps.FirstOrDefault(m => m.AreaKey == area.Key);
            if (map == null)
            {
                map = new MapRun
                {
                    AreaKey = area.Key, AreaId = area.Id, AreaHash = area.Hash,
                    Name = area.Name, FirstSeenUtc = utcNow,
                };
                this.Session.Maps.Add(map);
            }

            this.CurrentMap = map;
            map.Name = area.Name;
            map.LastSeenUtc = utcNow;
            var delta = this.previousTimestamp.HasValue ? timestamp - this.previousTimestamp.Value : 0;
            var continuous = this.previousAreaKey == area.Key && this.previousTimestamp.HasValue && delta >= 0 && delta <= 2;
            if (continuous) map.ActiveSeconds += delta;
            else this.baseline = null;
            this.previousTimestamp = timestamp;
            this.previousAreaKey = area.Key;

            if (inventory == null || inventory.Any(item => item.Count < 0 || string.IsNullOrWhiteSpace(item.Key)))
            {
                this.baseline = null;
                return;
            }

            var totals = inventory.GroupBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key,
                    group => new CurrencyObservation(group.Key, group.First().Name, group.Sum(item => item.Count)),
                    StringComparer.Ordinal);
            if (this.baseline != null)
            {
                foreach (var (key, item) in totals)
                {
                    var previous = this.baseline.TryGetValue(key, out var old) ? old.Count : 0;
                    var added = item.Count - previous;
                    if (added <= 0) continue;
                    if (!map.Currency.TryGetValue(key, out var total))
                    {
                        total = new CurrencyTotal();
                        map.Currency[key] = total;
                    }

                    total.Name = item.Name;
                    total.Count = checked(total.Count + added);
                }
            }

            this.baseline = totals;
        }

        public static Dictionary<string, CurrencyTotal> Totals(TrackingSession session)
        {
            var result = new Dictionary<string, CurrencyTotal>(StringComparer.Ordinal);
            foreach (var map in session.Maps)
            foreach (var (key, value) in map.Currency)
            {
                if (!result.TryGetValue(key, out var total))
                    result[key] = total = new CurrencyTotal();
                total.Name = value.Name;
                total.Count = checked(total.Count + value.Count);
            }

            return result;
        }
    }
}
