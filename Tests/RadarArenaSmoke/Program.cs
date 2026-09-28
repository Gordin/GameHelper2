// <copyright file="Program.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

// dotnet run --project Tests/RadarArenaSmoke -c Release
using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using GameHelper;
using GameHelper.RemoteObjects.Components;
using GameHelper.RemoteObjects.States.InGameStateObjects;

static void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
}

const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
var classify = typeof(Radar.Radar).GetMethod("ClassifyTrackedPath", BindingFlags.NonPublic | BindingFlags.Static);
const string Marker = "Metadata/MiscellaneousObjects/BossLeagueContentMarkerMain";
Check(classify.Invoke(null, new object[] { Marker }) is ValueTuple<string, string> kind && kind == ("boss", "Boss Arena"),
    "The shared arena marker must be recognized without a terrain whitelist.");
Check(classify.Invoke(null, new object[] { "Metadata/MiscellaneousObjects/BossArenaBlocker" }) == null,
    "Arena walls must not become duplicate markers.");

// Read a captured component layout through the real process reader, using this process as the source.
// Plantation, 2026-09-27: Positioned grid ints at 0x444/0x448 = (1766,649), no Render component.
var handleProperty = typeof(GameProcess).GetProperty("Handle", PrivateInstance);
using var handle = (IDisposable)Activator.CreateInstance(handleProperty.PropertyType, PrivateInstance,
    null, new object[] { Environment.ProcessId }, null);
handleProperty.SetValue(Core.Process, handle);
var memory = Marshal.AllocHGlobal(0x450);
try
{
    Marshal.Copy(new byte[0x450], 0, memory, 0x450);
    Marshal.WriteInt32(memory, 0x444, 1766);
    Marshal.WriteInt32(memory, 0x448, 649);
    var entity = (Entity)Activator.CreateInstance(typeof(Entity), PrivateInstance, null, new object[] { IntPtr.Zero }, null);
    typeof(Entity).GetProperty("Path").SetValue(entity, Marker);
    var components = (ConcurrentDictionary<string, ComponentBase>)typeof(Entity)
        .GetField("componentCache", PrivateInstance).GetValue(entity);
    components["Positioned"] = new Positioned(memory);
    Check(!entity.TryGetComponent<Render>(out _), "Fixture must reproduce the missing Render component.");

    var heights = new float[650][];
    for (var i = 0; i < heights.Length; i++) heights[i] = Array.Empty<float>();
    heights[649] = new float[1767];
    heights[649][1766] = 42;
    var collect = typeof(Radar.Radar).GetMethod("TryGetTrackedNode", BindingFlags.NonPublic | BindingFlags.Static);
    object[] invocation = { entity, heights, null };
    Check((bool)collect.Invoke(null, invocation), "A non-rendered arena must produce a tracked icon/path target.");
    var node = ((Vector2 gridPos, float height, string category, string iconKey))invocation[2];
    Check(node == (new Vector2(1766, 649), 42, "boss", "Boss Arena"), "Wrong marker position or icon.");
    Marshal.WriteInt32(memory, 0x444, -1);
    Check(!(bool)collect.Invoke(null, invocation), "Negative memory coordinates must be rejected.");
    Marshal.WriteInt32(memory, 0x444, 1767);
    Check(!(bool)collect.Invoke(null, invocation), "Out-of-bounds memory coordinates must be rejected.");
    components.Clear();
    Check(!(bool)collect.Invoke(null, invocation), "A marker without Positioned must be ignored.");
    Console.WriteLine("PASS: non-rendered boss marker produces the correct icon/path target; invalid positions and arena walls are rejected.");
}
finally
{
    Marshal.FreeHGlobal(memory);
}

