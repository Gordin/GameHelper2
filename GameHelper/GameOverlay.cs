// <copyright file="GameOverlay.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using ClickableTransparentOverlay;
    using Coroutine;
    using CoroutineEvents;
    using GameHelper.Utils;
    using ImGuiNET;
    using Plugin;
    using Settings;
    using Ui;

    /// <inheritdoc />
    public sealed class GameOverlay : Overlay
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="GameOverlay" /> class.
        /// </summary>
        internal GameOverlay(string windowTitle)
            : base(windowTitle, true, 3840, 2160)
        {
            CoroutineHandler.Start(this.UpdateOverlayBounds(), priority: int.MaxValue);
            SettingsWindow.InitializeCoroutines();
            PerformanceStats.InitializeCoroutines();
            DataVisualization.InitializeCoroutines();
            GameUiExplorer.InitializeCoroutines();
            ElementFinder.InitializeCoroutines();
            PerformanceProfiler.InitializeCoroutines();
            MemoryReadDiagnostics.InitializeCoroutines();
            OverlayKiller.InitializeCoroutines();
            NearbyVisualization.InitializeCoroutines();
            KrangledPassiveDetector.InitializeCoroutines();
        }

        /// <summary>
        ///     Gets the fonts loaded in the overlay.
        /// </summary>
        public ImFontPtr[]? Fonts { get; private set; }

        /// <inheritdoc />
        public override async Task Run()
        {
            Core.Initialize();
            Core.InitializeCororutines();
            this.VSync = Core.GHSettings.Vsync;
            await base.Run();
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Core.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <inheritdoc />
        protected override Task PostInitialized()
        {
            Ui.ImGuiTheme.Apply();

            UniversalFont.ApplyFromSettings();

            PManager.InitializePlugins();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override void Render()
        {
            PerformanceProfiler.StartFrame();

            try { CoroutineHandler.Tick(ImGui.GetIO().DeltaTime); }
            catch (Exception ex) { Console.WriteLine($"[GameOverlay.Render.Tick] {ex}"); }

            try { CoroutineHandler.RaiseEvent(GameHelperEvents.PerFrameDataUpdate); }
            catch (Exception ex) { Console.WriteLine($"[GameOverlay.Render.PerFrameDataUpdate] {ex}"); }

            try { CoroutineHandler.RaiseEvent(GameHelperEvents.PostPerFrameDataUpdate); }
            catch (Exception ex) { Console.WriteLine($"[GameOverlay.Render.PostPerFrameDataUpdate] {ex}"); }

            try { CoroutineHandler.RaiseEvent(GameHelperEvents.OnRender); }
            catch (Exception ex) { Console.WriteLine($"[GameOverlay.Render.OnRender] {ex}"); }

            try { CoroutineHandler.RaiseEvent(GameHelperEvents.OnPostRender); }
            catch (Exception ex) { Console.WriteLine($"[GameOverlay.Render.OnPostRender] {ex}"); }

            if (!Core.GHSettings.IsOverlayRunning)
            {
                this.Close();
            }
        }

        private IEnumerator<Wait> UpdateOverlayBounds()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnMoved);
                this.Position = Core.Process.WindowArea.Location;
                this.Size = Core.Process.WindowArea.Size -
                    (Core.GHSettings.FixTaskbarNotShowing ?
                        new System.Drawing.Size(0, 1) :
                        System.Drawing.Size.Empty);
            }
        }
    }
}