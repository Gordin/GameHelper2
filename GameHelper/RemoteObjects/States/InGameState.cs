// <copyright file="InGameState.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.States
{
    using System;
    using System.Collections.Generic;
    using Coroutine;
    using CoroutineEvents;
    using GameOffsets.Objects.States;
    using InGameStateObjects;
    using GameHelper.Utils;

    /// <summary>
    ///     Reads InGameState Game Object.
    /// </summary>
    public class InGameState : RemoteObjectBase
    {
        private IntPtr uiRootAddress;

        /// <summary>
        ///     Initializes a new instance of the <see cref="InGameState" /> class.
        /// </summary>
        /// <param name="address">address of the remote memory object.</param>
        internal InGameState(IntPtr address)
            : base(address)
        {
            Core.CoroutinesRegistrar.Add(CoroutineHandler.Start(
                this.OnPerFrame(), "[InGameState] Update Game State", int.MaxValue - 2));
        }

        /// <summary>
        ///     Gets the data related to the currently loaded world area.
        /// </summary>
        public WorldData CurrentWorldInstance { get; } = new(IntPtr.Zero);

        /// <summary>
        ///     Gets the data related to the current area instance.
        /// </summary>
        public AreaInstance CurrentAreaInstance { get; } = new(IntPtr.Zero);

        /// <summary>
        ///     Gets the UiRoot main child which contains all the UiElements of the game.
        /// </summary>
        public ImportantUiElements GameUi { get; } = new(IntPtr.Zero);

        /// <inheritdoc />
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGuiHelper.IntPtrToImGui("Active UI Manager", this.uiRootAddress);
            ImGuiHelper.IntPtrToImGui("Current GameUi Address", this.GameUi.Address);

            if (this.Address != IntPtr.Zero)
            {
                var data = Core.Process.Handle.ReadMemory<InGameStateOffset>(this.Address);
                ImGuiHelper.IntPtrToImGui("KB/M UI Manager (+0x2F0)", data.UiRootStructPtr);
                ImGuiHelper.IntPtrToImGui("Gamepad UI Manager (+0x318)", data.GamepadUiRootStructPtr);
            }
        }

        /// <inheritdoc />
        protected override void CleanUpData()
        {
            this.CurrentAreaInstance.Address = IntPtr.Zero;
            this.uiRootAddress = IntPtr.Zero;
            this.GameUi.Address = IntPtr.Zero;
            this.CurrentWorldInstance.Address = IntPtr.Zero;
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var data = Core.Process.Handle.ReadMemory<InGameStateOffset>(this.Address);
            this.CurrentAreaInstance.Address = data.AreaInstanceData;
            this.CurrentWorldInstance.Address = data.WorldData;

            Core.GHSettings.EnableControllerMode = data.UiRootStructPtr == IntPtr.Zero &&
                data.GamepadUiRootStructPtr != IntPtr.Zero;
            this.uiRootAddress = Core.GHSettings.EnableControllerMode
                ? data.GamepadUiRootStructPtr
                : data.UiRootStructPtr;
            this.GameUi.Address = this.uiRootAddress;
        }

        private IEnumerator<Wait> OnPerFrame()
        {
            // TODO optimization: convert this into OnAreaChange.
            while (true)
            {
                yield return new Wait(GameHelperEvents.PerFrameDataUpdate);
                try
                {
                    if (this.Address != IntPtr.Zero)
                    {
                        this.UpdateData(false);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[InGameState.OnPerFrame] {ex}");
                }
            }
        }
    }
}
