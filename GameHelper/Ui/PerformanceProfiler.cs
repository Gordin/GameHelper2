// <copyright file="PerformanceProfiler.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Ui;

using System;
using System.Threading;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using Coroutine;
using CoroutineEvents;
using ImGuiNET;

/// <summary>
///     Performance profiler for optimization purposes.
/// </summary>
public static class PerformanceProfiler
{
    internal static readonly double NsPerTick = 1000000000.0 / Stopwatch.Frequency;
    private static readonly ConcurrentDictionary<string, ProfileData> ProfileData = new();
    private static readonly ConcurrentDictionary<string, double> CurrentFrameNs = new();
    private static readonly ConcurrentDictionary<string, int> CurrentFrameCounts = new();
    
    private static DateTime lastUpdate = DateTime.MinValue;
    private static List<ProfileRow> cachedRows = [];
    private static bool showCurrentFrameOnly = true;

    internal static void InitializeCoroutines()
    {
        CoroutineHandler.Start(RenderWindow());
    }

    public static IDisposable? Profile(string namespaceName, string methodName)
    {
        if (!Core.GHSettings.ShowPerfProfiler)
        {
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        return new ProfileDisposable($"{namespaceName}.{methodName}", stopwatch, ProfileData, CurrentFrameNs, CurrentFrameCounts);
    }

    private static IEnumerator<Wait> RenderWindow()
    {
        while (true)
        {
            yield return new Wait(GameHelperEvents.OnPostRender);
            if (!Core.GHSettings.ShowPerfProfiler)
            {
                continue;
            }

            ImGui.SetNextWindowSize(new Vector2(700, 500), ImGuiCond.FirstUseEver);
            if (ImGui.Begin("Performance Profiler", ref Core.GHSettings.ShowPerfProfiler, ImGuiWindowFlags.MenuBar))
            {
                if (ImGui.BeginMenuBar())
                {
                    if (ImGui.MenuItem("Reset"))
                    {
                        ProfileData.Clear();
                    }
                    ImGui.Checkbox("Current Frame Only", ref showCurrentFrameOnly);
                    ImGui.EndMenuBar();
                }
                
                EndFrame();
                
                var now = DateTime.Now;
                if ((now - lastUpdate).TotalMilliseconds >= 500 || cachedRows.Count == 0)
                {
                    lastUpdate = now;
                    var currentProfileData = ProfileData.ToList();
                    var tempRows = new List<ProfileRow>();
                    foreach (var kvp in currentProfileData)
                    {
                        var key = kvp.Key;
                        var pd = kvp.Value;
                        int count;
                        double avgPerCallNs;
                        double avgPerFrameNs;
                        if (showCurrentFrameOnly)
                        {
                            if (!CurrentFrameNs.TryGetValue(key, out double currentFrameContrib) || currentFrameContrib == 0) continue;
                            if (!CurrentFrameCounts.TryGetValue(key, out int currentFrameCount) || currentFrameCount == 0) continue;
                            count = currentFrameCount;
                            avgPerCallNs = currentFrameContrib / currentFrameCount;
                            avgPerFrameNs = currentFrameContrib;
                        }
                        else
                        {
                            count = pd.Count;
                            avgPerCallNs = pd.AverageTicks * NsPerTick;
                            avgPerFrameNs = pd.AverageFrameNs;
                        }
                        tempRows.Add(new ProfileRow(key, count, avgPerCallNs, avgPerFrameNs));
                    }
                    cachedRows = tempRows;
                }
                if (ImGui.BeginTable("profilerTable", 4,
                        ImGuiTableFlags.Sortable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp,
                        ImGui.GetContentRegionAvail()))
                {
                    ImGui.TableSetupColumn("Count");
                    ImGui.TableSetupColumn("Name");
                    ImGui.TableSetupColumn("Avg (Call)");
                    ImGui.TableSetupColumn("Avg (Frame)", ImGuiTableColumnFlags.DefaultSort);
                    
                    ImGui.TableSetupScrollFreeze(0, 1);
                    ImGui.TableHeadersRow();

                    var sortSpecs = ImGui.TableGetSortSpecs();
                    List<ProfileRow> sortedRows = cachedRows.OrderByDescending(r => r.AvgPerFrameNs).ToList(); // Default
                    if (sortSpecs.SpecsCount > 0)
                    {
                        var spec = sortSpecs.Specs;
                        int col = spec.ColumnIndex;
                        bool ascending = spec.SortDirection == ImGuiSortDirection.Ascending;
                        
                        sortedRows = col switch
                        {
                            0 => ascending ? cachedRows.OrderBy(r => r.Count).ToList() : cachedRows.OrderByDescending(r => r.Count).ToList(), // Count
                            1 => ascending ? cachedRows.OrderBy(r => r.Name).ToList() : cachedRows.OrderByDescending(r => r.Name).ToList(), // Name
                            2 => ascending ? cachedRows.OrderBy(r => r.AvgPerCallNs).ToList() : cachedRows.OrderByDescending(r => r.AvgPerCallNs).ToList(), // Avg (Call)
                            3 => ascending ? cachedRows.OrderBy(r => r.AvgPerFrameNs).ToList() : cachedRows.OrderByDescending(r => r.AvgPerFrameNs).ToList(), // Avg (Frame)
                            _ => cachedRows.OrderByDescending(r => r.AvgPerFrameNs).ToList()
                        };
                    }

                    foreach (var row in sortedRows)
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn();
                        ImGui.Text(row.Count.ToString());

                        ImGui.TableNextColumn();
                        ImGui.Text(row.Name);

                        ImGui.TableNextColumn();
                        ImGui.Text(FormatTime(row.AvgPerCallNs));
                            
                        ImGui.TableNextColumn();
                        ImGui.Text(FormatTime(row.AvgPerFrameNs));
                    }

                    ImGui.EndTable();
                }
            }
            ImGui.End();
        }
    }
        
    public static void StartFrame()
    {
        if (!Core.GHSettings.ShowPerfProfiler)
        {
            return;
        }
            
        CurrentFrameNs.Clear();
        CurrentFrameCounts.Clear();
    }
        
    public static void EndFrame()
    {
        if (!Core.GHSettings.ShowPerfProfiler)
        {
            return;
        }

        // Add frame samples for each profiled method
        foreach (var kvp in CurrentFrameNs)
        {
            var key = kvp.Key;
            var frameNs = kvp.Value;
            ProfileData.AddOrUpdate(key,
                _ => {
                    var pd = new ProfileData();
                    pd.AddFrameSample(frameNs);
                    return pd;
                },
                (k, existing) => {
                    existing.AddFrameSample(frameNs);
                    return existing;
                });
        }
    }
        
    private static string FormatTime(double ns)
    {
        return ns switch
        {
            >= 1000000000.0 => $"{ns / 1000000000.0:F2} s",
            >= 1000000.0 => $"{ns / 1000000.0:F2} ms",
            >= 1000.0 => $"{ns / 1000.0:F2} us",
            _ => $"{ns:F2} ns"
        };
    }
}

internal class ProfileData
{
    private const int WindowSize = 100;
    private int totalCount;
    private long sumTicks;
    private double sumFrameNs;
    private readonly ConcurrentQueue<long> recentTicks = new();
    private readonly ConcurrentQueue<double> recentFrameNs = new();
    public int Count => totalCount;

    public double AverageTicks => !recentTicks.IsEmpty ? (double)sumTicks / recentTicks.Count : 0.0;
    public double AverageFrameNs => !recentFrameNs.IsEmpty ? sumFrameNs / recentFrameNs.Count : 0.0;

    public void AddSample(long ticks)
    {
        Interlocked.Increment(ref totalCount);
        recentTicks.Enqueue(ticks);
        Interlocked.Add(ref sumTicks, ticks);
        while (recentTicks.Count > WindowSize)
        {
            if (recentTicks.TryDequeue(out var old))
            {
                Interlocked.Add(ref sumTicks, -old);
            }
        }
    }

    public void AddFrameSample(double ns)
    {
        recentFrameNs.Enqueue(ns);
        // F-182: atomic add - was raw `+=`, racy on parallel ProfileDisposable.Dispose
        // calls that update CurrentFrameNs from worker threads. Interlocked has no
        // double Add overload, so use the standard CompareExchange loop pattern.
        InterlockedAddDouble(ref sumFrameNs, ns);
        while (recentFrameNs.Count > WindowSize)
        {
            if (recentFrameNs.TryDequeue(out double old))
            {
                InterlockedAddDouble(ref sumFrameNs, -old);
            }
        }
    }

    private static double InterlockedAddDouble(ref double location, double value)
    {
        double current, computed;
        do
        {
            current = location;
            computed = current + value;
        }
        while (Interlocked.CompareExchange(ref location, computed, current) != current);
        return computed;
    }
}

internal class ProfileRow(string name, int count, double avgPerCallNs, double avgPerFrameNs)
{
    public string Name { get; } = name;
    public int Count { get; } = count;
    public double AvgPerCallNs { get; } = avgPerCallNs;
    public double AvgPerFrameNs { get; } = avgPerFrameNs;
}

internal class ProfileDisposable(
    string methodName,
    Stopwatch stopwatch,
    ConcurrentDictionary<string, ProfileData> profileData,
    ConcurrentDictionary<string, double> currentFrameNs,
    ConcurrentDictionary<string, int> currentFrameCounts)
    : IDisposable
{
    public void Dispose()
    {
        stopwatch.Stop();
        var elapsedTicks = stopwatch.ElapsedTicks;
        var elapsedNs = elapsedTicks * PerformanceProfiler.NsPerTick;
        profileData.AddOrUpdate(methodName,
            _ => { var pd = new ProfileData(); pd.AddSample(elapsedTicks); return pd; },
            (key, existing) => { existing.AddSample(elapsedTicks); return existing; });
        currentFrameNs.AddOrUpdate(methodName,
            _ => elapsedNs,
            (key, existing) => existing + elapsedNs);
        currentFrameCounts.AddOrUpdate(methodName,
            _ => 1,
            (key, existing) => existing + 1);
    }
}