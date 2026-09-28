// <copyright file="Program.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

// Run after building the solution: dotnet run --project Tests/AtlasSearchSmoke -c Release
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

static void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
}

var plugin = new Atlas2.Atlas2();
// Exercise the same matcher used by DrawUI once a search has been entered.
var nodeType = typeof(Atlas2.Atlas2).GetNestedType("NodeData", BindingFlags.NonPublic);
var node = Activator.CreateInstance(nodeType);
nodeType.GetField("ContentDisplay").SetValue(node, new List<string> { "Ritual" });
nodeType.GetField("RawContents").SetValue(node, new List<string>());
var match = typeof(Atlas2.Atlas2).GetMethod("MatchesSearch", BindingFlags.Static | BindingFlags.NonPublic);
foreach (var (query, expected) in new[] { ("mesa", true), ("RITUAL", true), ("Bluff", false) })
    Check((bool)match.Invoke(null, new[] { node, "Mesa", query }) == expected, $"Search mismatch: {query}");

Check(!plugin.Settings.ShowOnlySelectedCategories, "Category filtering must be opt-in.");
plugin.Settings.ShowOnlySelectedCategories = true;
Check(JsonConvert.DeserializeObject<Atlas2.Atlas2Settings>(JsonConvert.SerializeObject(plugin.Settings)).ShowOnlySelectedCategories,
    "Category filtering must persist.");
var select = typeof(Atlas2.Atlas2).GetMethod("GetSelectedCategory", BindingFlags.Instance | BindingFlags.NonPublic);
object Selected(string mapName, bool active = false, bool matches = false) =>
    select.Invoke(plugin, new[] { node, mapName, (object)active, matches });
foreach (var category in plugin.Settings.MapGroups) category.DrawPath = false;
Check(Selected("Derelict Mansion") == null, "No selected categories must select no maps.");
var lineage = plugin.Settings.MapGroups.Single(category => category.BuiltInKey == "lineage");
lineage.DrawPath = true;
Check(ReferenceEquals(Selected("Derelict Mansion"), lineage), "Checked category must include its maps.");
Check(Selected("Mesa") == null, "Other maps must be excluded.");
Check(ReferenceEquals(Selected("Derelict Mansion", true, false), lineage), "Search must not exclude other selected categories.");
var search = plugin.Settings.MapGroups.Single(category => category.BuiltInKey == "search");
search.DrawPath = true;
Check(Selected("Mesa") == null, "Empty Search must not include every map.");
Check(ReferenceEquals(Selected("Mesa", true, true), search), "Search results must join the selected categories.");
search.DrawPath = false;
Check(Selected("Mesa", true, true) == null, "Unchecked Search must not include results.");
lineage.BuiltInTargets["Derelict Mansion"] = false;
Check(Selected("Derelict Mansion") == null, "Disabled category members must be excluded.");
var custom = new Atlas2.MapGroupSettings("Custom", default, default) { DrawPath = true };
custom.Maps.Add("Mesa");
plugin.Settings.MapGroups.Add(custom);
Check(ReferenceEquals(Selected("Mesa"), custom), "Custom categories must work too.");
Console.WriteLine("PASS: search visibility, settings persistence, selected category union, exclusions, and custom categories.");
