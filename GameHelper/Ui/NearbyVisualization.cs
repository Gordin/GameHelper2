// <copyright file="NearbyVisualization.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>


namespace GameHelper.Ui
{
    using Coroutine;
    using GameHelper.CoroutineEvents;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.Utils;
    using GameOffsets.Objects.States.InGameState;
    using ImGuiNET;
    using System;
    using System.Collections.Generic;
    using System.Numerics;

    public static class NearbyVisualization
    {
        /// <summary>
        ///     Initializes the co-routines.
        /// </summary>
        internal static void InitializeCoroutines()
        {
            CoroutineHandler.Start(NearbyVisualizationRenderCoRoutine());
        }

        /// <summary>
        ///     Draws the window for Data Visualization.
        /// </summary>
        /// <returns>co-routine IWait.</returns>
        private static IEnumerator<Wait> NearbyVisualizationRenderCoRoutine()
        {
            var totalLines = 40;
            var bigColor = ImGuiHelper.Color(255, 0, 0, 255);
            var smallColor = ImGuiHelper.Color(255, 255, 0, 255);
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnRender);
                try
                {
                    // F-179: snapshot the 4-level chain into locals; treat any null /
                    // NRE as "not in-game right now, skip this frame".
                    if (Core.States.GameCurrentState != GameStateTypes.InGameState)
                    {
                        continue;
                    }

                    var inGame = Core.States.InGameStateObject;
                    var area = inGame?.CurrentAreaInstance;
                    var player = area?.Player;
                    if (player == null || !player.TryGetComponent<Render>(out var r))
                    {
                        continue;
                    }

                    if (Core.GHSettings.OuterCircle.IsVisible)
                    {
                        DrawNearbyRange(totalLines, Core.GHSettings.OuterCircle.Meaning, r.GridPosition.X, r.GridPosition.Y, r.TerrainHeight, bigColor);
                    }

                    if (Core.GHSettings.InnerCircle.IsVisible)
                    {
                        DrawNearbyRange(totalLines, Core.GHSettings.InnerCircle.Meaning, r.GridPosition.X, r.GridPosition.Y, r.TerrainHeight, smallColor);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NearbyVisualization.NearbyVisualizationRenderCoRoutine] {ex}");
                }
            }
        }

        private static void DrawNearbyRange(int totalLines, int nearbyMeaning, float gX, float gY, float height, uint color)
        {
            var gridToWorld = TileStructure.TileToWorldConversion / TileStructure.TileToGridConversion;
            Span<Vector2> points = new Vector2[totalLines];
            var gap = 360f / totalLines;
            for (var i = 0; i < totalLines; i++)
            {
                points[i].X = gX + (float)(Math.Cos(Math.PI / 180 * i * gap) * nearbyMeaning);
                points[i].Y = gY + (float)(Math.Sin(Math.PI / 180 * i * gap) * nearbyMeaning);
                try
                {
                    height = Core.States.InGameStateObject.CurrentAreaInstance.GridHeightData[(int)points[i].Y][(int)points[i].X];
                }
                catch { }

                points[i] = Core.States.InGameStateObject.CurrentWorldInstance.WorldToScreen(points[i] * gridToWorld, height);
            }

            ImGui.GetBackgroundDrawList().AddPolyline(ref points[0], totalLines, color, ImDrawFlags.Closed, 5);
        }
    }
}
