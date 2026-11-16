// <copyright file="GameOverlay.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using System.Threading;
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
        private bool disposed;
        private uint windowThreadId;
        private int closeRequested;
        private bool closingWindow;
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
            OffsetHelper.InitializeCoroutines();
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

        /// <summary>Destroy the HWND on its owner thread before .NET tears that thread down.</summary>
        public override void Close()
        {
            Interlocked.Exchange(ref this.closeRequested, 1);
            if (this.windowThreadId != 0 && GetCurrentThreadId() == this.windowThreadId)
                this.CloseWindowOnOwnerThread();
            else if (this.windowThreadId == 0)
                base.Close();
        }

        private void CloseWindowOnOwnerThread()
        {
            // DestroyWindow synchronously sends WM_DESTROY, which calls Close again.
            if (this.closingWindow) return;
            this.closingWindow = true;
            try { this.window?.Dispose(); }
            finally { base.Close(); }
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (this.disposed) return;
            this.disposed = true;
            try
            {
                // Overlay.Dispose joins the render thread. Plugin caches and process
                // state must not be cleared while that thread is still using them.
                if (disposing) this.Close();
                base.Dispose(disposing);
            }
            finally
            {
                if (disposing) Core.Dispose();
                GC.KeepAlive(this);
            }
        }

        /// <inheritdoc />
        protected override Task PostInitialized()
        {
            this.windowThreadId = GetWindowThreadProcessId(this.window.Handle, out _);
            Ui.ImGuiTheme.Apply();

            UniversalFont.ApplyFromSettings();

            PManager.InitializePlugins();
            if (Volatile.Read(ref this.closeRequested) != 0) this.CloseWindowOnOwnerThread();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override void Render()
        {
            if (Volatile.Read(ref this.closeRequested) != 0)
            {
                this.CloseWindowOnOwnerThread();
                return;
            }
            PerformanceProfiler.StartFrame();

            try { CoroutineHandler.Tick(ImGui.GetIO().DeltaTime); }
            catch (Exception ex) { Console.WriteLine($"[GameOverlay.Render.Tick] {ex}"); }

            if (Volatile.Read(ref this.closeRequested) != 0) return;

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

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    }
}
