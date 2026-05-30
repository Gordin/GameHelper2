// <copyright file="PerformanceStats.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Ui
{
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Numerics;
    using Coroutine;
    using CoroutineEvents;
    using ImGuiNET;
    using Utils;

    /// <summary>
    ///     Visualize the co-routines stats.
    /// </summary>
    public static class PerformanceStats
    {
        private static readonly Dictionary<string, MovingAverage> MovingAverageValue = new();

        private static bool isPerformanceWindowHovered;

        /// <summary>
        ///     Initializes the co-routines.
        /// </summary>
        internal static void InitializeCoroutines()
        {
            CoroutineHandler.Start(PerformanceStatRenderCoRoutine());
        }

        /// <summary>
        ///     Draws the window to display the perf stats.
        /// </summary>
        /// <returns>co-routine IWait.</returns>
        private static IEnumerator<Wait> PerformanceStatRenderCoRoutine()
        {
            var pos = new Vector2(0, 100);
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnRender);
                if (Core.GHSettings.ShowPerfStats)
                {
                    if (Core.GHSettings.HidePerfStatsWhenBg && !Core.Process.Foreground)
                    {
                        continue;
                    }

                    ImGui.SetNextWindowPos(pos);
                    if (isPerformanceWindowHovered)
                    {
                        ImGui.PushStyleColor(ImGuiCol.WindowBg, Vector4.Zero);
                        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
                    }

                    ImGui.Begin("Perf Stats Window", ImGuiHelper.TransparentWindowFlags);

                    if (isPerformanceWindowHovered)
                    {
                        ImGui.PopStyleVar();
                        ImGui.PopStyleColor();
                    }

                    isPerformanceWindowHovered = ImGui.IsMouseHoveringRect(Vector2.Zero, ImGui.GetWindowSize());
                    if (isPerformanceWindowHovered)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
                    }

                    if (!Core.GHSettings.MinimumPerfStats)
                    {
                        ImGui.Text("Performance Related Stats");
                        using (var proc = Process.GetCurrentProcess())
                        {
                            // The proc.PrivateMemorySize64 will returns the private memory usage in byte.
                            // Would like to Convert it to Megabyte? divide it by 2^20
                            ImGui.Text($"Total Used Memory: {proc.PrivateMemorySize64 / (1024 * 1024)} (MB)");
                        }

                        ImGui.Text($"Total Event Coroutines: {CoroutineHandler.EventCount}");
                        ImGui.Text($"Total Tick Coroutines: {CoroutineHandler.TickingCount}");
                    }

                    var cAI = Core.States.InGameStateObject.CurrentAreaInstance;
                    ImGui.Text($"Total Entities: {cAI.AwakeEntities.Count}");
                    if (Core.GHSettings.DisableAllCounters == false)
                    {
                        ImGui.Text($"Awake Entities: {cAI.NetworkBubbleEntityCount}");
                        ImGui.Text($"Useless Awake:  {cAI.UselessAwakeEntities}");
                    }

                    var fps = ImGui.GetIO().Framerate;
                    ImGui.Text($"FPS: {fps}");

                    if (!Core.GHSettings.MinimumPerfStats)
                    {
                        ImGui.NewLine();
                        ImGui.Text($"==Average of last {(int)(1440 / fps)} seconds==");
                        for (var i = 0; i < Core.CoroutinesRegistrar.Count; i++)
                        {
                            var coroutine = Core.CoroutinesRegistrar[i];
                            if (coroutine.IsFinished)
                            {
                                Core.CoroutinesRegistrar.Remove(coroutine);
                            }

                            if (MovingAverageValue.TryGetValue(coroutine.Name, out var value))
                            {
                                value.ComputeAverage(
                                    coroutine.LastMoveNextTime.TotalMilliseconds,
                                    coroutine.MoveNextCount);
                                ImGui.Text($"{coroutine.Name}: {value.Average:0.00}(ms)");
                            }
                            else
                            {
                                MovingAverageValue[coroutine.Name] = new MovingAverage();
                            }
                        }
                    }

                    if (isPerformanceWindowHovered)
                    {
                        ImGui.PopStyleColor();
                    }

                    ImGui.End();
                }
            }
        }

        private class MovingAverage
        {
            private readonly Queue<double> samples = new();
            private readonly int windowSize = 144 * 10; // 10 seconds moving average @ 144 FPS.
            private int lastIterationNumber;
            private double sampleAccumulator;

            public double Average { get; private set; }

            /// <summary>
            ///     Computes a new windowed average each time a new sample arrives.
            /// </summary>
            /// <param name="newSample">new sample to add into the moving average.</param>
            /// <param name="iterationNumber">iteration number who's sample you are adding.</param>
            public void ComputeAverage(double newSample, int iterationNumber)
            {
                if (iterationNumber <= this.lastIterationNumber)
                {
                    return;
                }

                this.lastIterationNumber = iterationNumber;
                this.sampleAccumulator += newSample;
                this.samples.Enqueue(newSample);

                if (this.samples.Count > this.windowSize)
                {
                    this.sampleAccumulator -= this.samples.Dequeue();
                }

                this.Average = this.sampleAccumulator / this.samples.Count;
            }
        }
    }
}