// <copyright file="OverlayKiller.cs" company="None">
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
    using L = GameHelper.Localization.OverlayLocalization;

    /// <summary>
    ///     Kills the overlay.
    /// </summary>
    public static class OverlayKiller
    {
        private static readonly Stopwatch Sw = Stopwatch.StartNew();
        private static readonly int Timelimit = 20;
        private static readonly Vector2 Size = new(400);

        /// <summary>
        ///     Initializes the co-routines.
        /// </summary>
        internal static void InitializeCoroutines()
        {
            CoroutineHandler.Start(OverlayKillerCoRoutine());
            CoroutineHandler.Start(OnAreaChange());
        }

        private static IEnumerator<Wait> OverlayKillerCoRoutine()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnRender);
                if (!Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails.IsBattleRoyale)
                {
                    Sw.Restart();
                    continue;
                }

                ImGui.SetNextWindowSize(Size);
                ImGui.Begin(L.Title("overlay_killer.title", "Player Vs Player (PVP) Detected", "PvpDetected"));
                ImGui.TextWrapped(
                    L.F(
                        "overlay_killer.message",
                        "Please don't cheat in PvP mode. GameHelper was not created for PvP cheating. Overlay will close in {0} seconds.",
                        Timelimit - (int)Sw.Elapsed.TotalSeconds));
                ImGui.End();

                if (Sw.Elapsed.TotalSeconds > Timelimit)
                {
                    Core.Overlay.Close();
                }
            }
        }

        private static IEnumerator<Wait> OnAreaChange()
        {
            while (true)
            {
                yield return new Wait(RemoteEvents.AreaChanged);
                Sw.Restart();
            }
        }
    }
}
