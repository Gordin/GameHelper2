using LootTracker;

var passed = 0;
var area = new AreaIdentity("ABC", "MapTest", "測試地圖");
var now = new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);
CurrencyObservation[] Items(params long[] stacks) => stacks.Select(n => new CurrencyObservation("Metadata/Items/Currency/Exalted", "崇高石", n)).ToArray();
void Observe(TrackingLedger ledger, double second, CurrencyObservation[]? snapshot, AreaIdentity? where = null) =>
    ledger.Observe(where ?? area, true, snapshot, second, now.AddSeconds(second));
long Count(TrackingLedger ledger) => TrackingLedger.Totals(ledger.Session).Values.Sum(v => v.Count);
void Equal<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{label}: expected {expected}, got {actual}");
}
void Test(string name, Action body) { body(); passed++; Console.WriteLine("PASS " + name); }

Test("Existing backpack is a baseline; only later positive changes count", () =>
{
    var ledger = new TrackingLedger();
    Observe(ledger, 0, Items(20));
    Equal(0L, Count(ledger), "initial inventory");
    Observe(ledger, .25, Items(23));
    Equal(3L, Count(ledger), "new currency");
    Equal(.25, ledger.TotalSeconds, "active time");
});

Test("Split stacks, merges and slot reordering do not increase income", () =>
{
    var ledger = new TrackingLedger();
    Observe(ledger, 0, Items(20));
    Observe(ledger, .25, Items(10, 5, 5));
    Observe(ledger, .5, Items(5, 15));
    Observe(ledger, .75, Items(20));
    Equal(0L, Count(ledger), "aggregate equality");
});

Test("Spending does not subtract recorded income; future additions count", () =>
{
    var ledger = new TrackingLedger();
    Observe(ledger, 0, Items(10));
    Observe(ledger, .25, Items(15));
    Observe(ledger, .5, Items(7));
    Observe(ledger, .75, Items(9));
    Equal(7L, Count(ledger), "gross observed increases");
});

Test("Unreadable snapshot cannot masquerade as empty backpack", () =>
{
    var ledger = new TrackingLedger();
    Observe(ledger, 0, Items(100));
    Observe(ledger, .25, null);
    Observe(ledger, .5, Items(110));
    Equal(0L, Count(ledger), "gap rebaseline");
    Observe(ledger, .75, Items(112));
    Equal(2L, Count(ledger), "after recovery");
});

Test("Valid empty backpack can detect first picked-up currency", () =>
{
    var ledger = new TrackingLedger();
    Observe(ledger, 0, Items());
    Observe(ledger, .25, Items(1));
    Equal(1L, Count(ledger), "first currency");
});

Test("Town interruption and map reentry resume timer without counting transfers", () =>
{
    var ledger = new TrackingLedger();
    Observe(ledger, 0, Items(10));
    Observe(ledger, 1, Items(11));
    ledger.Observe(null, false, null, 2, now.AddSeconds(2));
    Observe(ledger, 50, Items(500));
    Observe(ledger, 51, Items(502));
    Equal(1, ledger.Session.Maps.Count, "same server instance");
    Equal(3L, Count(ledger), "town transfers ignored");
    Equal(2d, ledger.TotalSeconds, "town excluded");
});

Test("Manual pause ignores elapsed time and resets inventory baseline", () =>
{
    var ledger = new TrackingLedger();
    Observe(ledger, 0, Items(1));
    Observe(ledger, 1, Items(2));
    ledger.SetPaused(true);
    Observe(ledger, 2, Items(200));
    ledger.SetPaused(false);
    Observe(ledger, 20, Items(300));
    Observe(ledger, 21, Items(301));
    Equal(2L, Count(ledger), "paused currency ignored");
    Equal(2d, ledger.TotalSeconds, "paused time ignored");
});

Test("Instance and area IDs both separate runs; return visits reuse the original", () =>
{
    var ledger = new TrackingLedger();
    Observe(ledger, 0, Items(1));
    Observe(ledger, 1, Items(2));
    Observe(ledger, 2, Items(100), area with { Hash = "DEF" });
    Observe(ledger, 3, Items(102), area with { Hash = "DEF" });
    Observe(ledger, 4, Items(300), area with { Id = "MapOther" });
    Observe(ledger, 5, Items(400));
    Observe(ledger, 6, Items(401));
    Equal(3, ledger.Session.Maps.Count, "run identity");
    Equal(4L, Count(ledger), "per-instance totals");
    Equal(2d, ledger.Session.Maps[0].ActiveSeconds, "original timer resumed");
});

Test("Long render gaps and clock regression never fabricate time or loot", () =>
{
    var ledger = new TrackingLedger();
    Observe(ledger, 0, Items(1));
    Observe(ledger, 1, Items(2));
    Observe(ledger, 60, Items(200));
    Observe(ledger, 59, Items(300));
    Equal(1L, Count(ledger), "gap ignored");
    Equal(1d, ledger.TotalSeconds, "gap timer ignored");
});

Test("Negative inventory invalidates baseline", () =>
{
    var ledger = new TrackingLedger();
    Observe(ledger, 0, Items(10));
    Observe(ledger, .25, Items(-1));
    Observe(ledger, .5, Items(12));
    Equal(0L, Count(ledger), "negative sample");
});

var temporary = Path.Combine(Path.GetTempPath(), "LootTrackerTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
Test("Atomic save, backup and restart retain totals without persisting a live baseline", () =>
{
    var path = Path.Combine(temporary, "active.json");
    var ledger = new TrackingLedger();
    Observe(ledger, 0, Items(10));
    Observe(ledger, 1, Items(12));
    SessionStore.Write(path, ledger.Session);
    Observe(ledger, 2, Items(15));
    SessionStore.Write(path, ledger.Session);
    Equal(2L, Count(new TrackingLedger(SessionStore.Read(path + ".bak"))), "previous valid backup");
    var restored = new TrackingLedger(SessionStore.Read(path));
    Observe(restored, 10000, Items(999));
    Equal(5L, Count(restored), "restart does not recount backpack");
    Equal(2d, restored.TotalSeconds, "no offline time");
    Observe(restored, 10001, Items(1000));
    Equal(6L, Count(restored), "tracking resumes");
    Equal(0, Directory.GetFiles(temporary, "*.tmp").Length, "no temporary files");
});

Test("CSV preserves localized names and protects spreadsheet formula cells", () =>
{
    var ledger = new TrackingLedger();
    Observe(ledger, 0, Items());
    Observe(ledger, 1, new[] { new CurrencyObservation("=metadata", "=HYPERLINK(\"x\")", 2) });
    var path = Path.Combine(temporary, "export.csv");
    SessionStore.ExportCsv(path, ledger.Session, "session,area,id,hash,seconds,currency,key,count");
    var csv = File.ReadAllText(path);
    Equal(true, csv.Contains("測試地圖"), "UTF-8 Chinese");
    Equal(true, csv.Contains("\"'=HYPERLINK(\"\"x\"\")\""), "quoted non-formula cell");
    Equal(true, csv.Contains("\"'=metadata\""), "metadata non-formula cell");
});

var priceJson = $$$"""
{"CacheVersion":2,"PriceSource":1,"League":"TestLeague","LastFetchUtc":"{{{now:O}}}",
 "ChaosPerDivine":200,"ChaosPerExalted":2,
 "FlatPricesChaos":{"exaltedorb":2,"divineorb":200},
 "PathBasenameToItemName":{"currencyaddmodtorare":"Exalted Orb","currencyrerollvalues":"Divine Orb"}}
""";
var book = new CurrencyPriceBook(priceJson, "TestLeague", 1, now);
Test("Price uses currency quantity and cached exchange rates", () =>
{
    var estimate=book.Estimate(new Dictionary<string,CurrencyTotal> {
        ["Metadata/Items/Currency/CurrencyAddModToRare"] = new() {Name="崇高石",Count=3},
        ["Metadata/Items/Currency/CurrencyRerollValues"] = new() {Name="神聖石",Count=2},
    });
    Equal(203d,estimate.Exalted,"actual stack values, not stack count");
    Equal(100d,book.DivineToExalted,"divine/exalted rate");
    Equal(0,estimate.UnpricedTypes,"complete estimate");
});
Test("Unpriced currency is counted separately from known value", () =>
{
    var estimate=book.Estimate(new Dictionary<string,CurrencyTotal> {
        ["Metadata/Items/Currency/ExaltedOrb"] = new() {Name="崇高石",Count=3},
        ["Metadata/Items/Currency/NewUnknownCurrency"] = new() {Name="未定價",Count=5},
    });
    Equal(3d,estimate.Exalted,"known value retained");Equal(1,estimate.UnpricedTypes,"unknown type");Equal(5L,estimate.UnpricedCount,"unknown quantity");
});
Test("Traditional Chinese names can resolve through localized terminology", () =>
{
    var localBook=new CurrencyPriceBook(priceJson,"TestLeague",1,now,
        new Dictionary<string,string>{{"terms.exalted_orb","Exalted Orb"}},
        new Dictionary<string,string>{{"terms.exalted_orb","崇高石"}});
    Equal(4d,localBook.Estimate(new Dictionary<string,CurrencyTotal> { ["new/path"] = new(){Name="崇高石",Count=4}}).Exalted,"localized price");
});
void RejectPrice(string json,string league,int source,DateTimeOffset time)
{
    var failed=false;try { _=new CurrencyPriceBook(json,league,source,time); }catch {failed=true;}
    Equal(true,failed,"unusable cache rejected");
}
Test("Wrong league or source never supplies a valuation", () =>
{
    RejectPrice(priceJson,"DifferentLeague",1,now);RejectPrice(priceJson,"TestLeague",0,now);
});
Test("Stale, future and invalid exchange rates are rejected", () =>
{
    RejectPrice(priceJson,"TestLeague",1,now.AddDays(2));RejectPrice(priceJson,"TestLeague",1,now.AddHours(-1));
    RejectPrice(priceJson.Replace("\"ChaosPerExalted\":2,","\"ChaosPerExalted\":0,"),"TestLeague",1,now);
});
Test("Session time persists while old session files remain readable", () =>
{
    var ledger=new TrackingLedger();ledger.Session.SessionSeconds=3600;
    var path=Path.Combine(temporary,"wall-clock.json");SessionStore.Write(path,ledger.Session);
    Equal(3600d,SessionStore.Read(path).SessionSeconds,"persisted session clock");
    var old=File.ReadAllText(path).Replace("  \"SessionSeconds\": 3600,", "");
    File.WriteAllText(path,old);Equal(0d,SessionStore.Read(path).SessionSeconds,"schema 1 old session compatible");
});
Console.WriteLine($"{passed} tests passed. Persistence fixtures: {temporary}");
if (args.Length > 0)
{
    var runtime=Path.GetFullPath(args[0]);
    using var settings=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(runtime,"Plugins/LootValue/config/settings.txt")));
    Dictionary<string,string> Language(string lang)=>System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(runtime,"Localization",lang+".json")))!;
    var liveBook=new CurrencyPriceBook(File.ReadAllText(Path.Combine(runtime,"Plugins/LootValue/price_cache.json")),
        settings.RootElement.GetProperty("League").GetString()!,settings.RootElement.GetProperty("PriceSource").GetInt32(),DateTimeOffset.UtcNow,Language("en-US"),Language("zh-Hant"));
    var actual=SessionStore.Read(Path.Combine(runtime,"Plugins/LootTracker/config/active.json"));
    var estimate=liveBook.Estimate(TrackingLedger.Totals(actual));
    Console.WriteLine($"Read-only real-cache check: areas={actual.Maps.Count}, Exalted={estimate.Exalted:F2}, unpriced types={estimate.UnpricedTypes}; cache={liveBook.UpdatedUtc:O}");
}
