// <copyright file="GameStates.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects
{
    using System;
    using System.Collections.Generic;
    using Coroutine;
    using CoroutineEvents;
    using GameOffsets.Objects;
    using ImGuiNET;
    using RemoteEnums;
    using States;
    using Utils;

    /// <summary>
    ///     Reads and stores the global states of the game.
    /// </summary>
    public class GameStates : RemoteObjectBase
    {
        private readonly object gameStatesLock = new();
        private IntPtr currentStateAddress = IntPtr.Zero;
        private GameStateTypes currentStateName = GameStateTypes.GameNotLoaded;
        private GameStateStaticOffset myStaticObj;

        /// <summary>
        ///     Initializes a new instance of the <see cref="GameStates" /> class.
        /// </summary>
        /// <param name="address">address of the remote memory object.</param>
        internal GameStates(IntPtr address)
            : base(address)
        {
            CoroutineHandler.Start(this.OnPerFrame(), priority: int.MaxValue);
        }

        /// <summary>
        ///     Gets a dictionary containing all the Game States addresses.
        /// </summary>
        public Dictionary<IntPtr, GameStateTypes> AllStates { get; } = new();

        /// <summary>
        ///     Gets the AreaLoadingState object.
        /// </summary>
        public AreaLoadingState AreaLoading { get; } = new(IntPtr.Zero);

        /// <summary>
        ///     Gets the InGameState Object.
        /// </summary>
        public InGameState InGameStateObject { get; } = new(IntPtr.Zero);

        /// <summary>
        ///     Gets the current state the game is in.
        /// </summary>
        public GameStateTypes GameCurrentState
        {
            get => this.currentStateName;
            private set
            {
                if (this.currentStateName != value)
                {
                    this.currentStateName = value;
                    if (value != GameStateTypes.GameNotLoaded)
                    {
                        CoroutineHandler.RaiseEvent(RemoteEvents.StateChanged);
                    }
                }
            }
        }

        /// <summary>
        ///     Converts the <see cref="GameStates" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            if (ImGui.TreeNode("All States Info"))
            {
                ImGui.Text($"Configured State Slots: {GameStateHelper.TOTAL_STATES}");
                foreach (var state in this.AllStates)
                {
                    ImGuiHelper.IntPtrToImGui($"{state.Value}", state.Key);
                }

                ImGui.TreePop();
            }

            ImGui.Text($"Current State: {this.GameCurrentState}");
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            if (hasAddressChanged)
            {
                lock (this.gameStatesLock)
                {
                    this.myStaticObj = reader.ReadMemory<GameStateStaticOffset>(this.Address);
                    var data = reader.ReadMemory<GameStateOffset>(this.myStaticObj.GameState);
                    for (var i = 0; i < GameStateHelper.TOTAL_STATES; i++)
                    {
                        this.AllStates[data.States[i].X] = (GameStateTypes)i;
                    }

                    this.AreaLoading.Address = data.States[0].X;
                    this.InGameStateObject.Address = data.States[4].X;
                }
            }
            else
            {
                // Caller (OnPerFrame) takes gameStatesLock around this call.
                var data = reader.ReadMemory<GameStateOffset>(this.myStaticObj.GameState);
                var cStateAddr = reader.ReadMemory<IntPtr>(data.CurrentStatePtr.Last - 0x10); // Get 2nd-last ptr.
                if (cStateAddr != IntPtr.Zero && cStateAddr != this.currentStateAddress)
                {
                    this.currentStateAddress = cStateAddr;
                    this.GameCurrentState = this.AllStates[this.currentStateAddress];
                }
            }
        }

        /// <inheritdoc />
        protected override void CleanUpData()
        {
            lock (this.gameStatesLock)
            {
                this.myStaticObj = default;
                this.currentStateAddress = IntPtr.Zero;
                this.GameCurrentState = GameStateTypes.GameNotLoaded;
                this.AllStates.Clear();
                this.AreaLoading.Address = IntPtr.Zero;
                this.InGameStateObject.Address = IntPtr.Zero;
            }
        }

        private IEnumerator<Wait> OnPerFrame()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.PerFrameDataUpdate);
                try
                {
                    if (this.Address != IntPtr.Zero)
                    {
                        lock (this.gameStatesLock)
                        {
                            this.UpdateData(false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GameStates.OnPerFrame] {ex}");
                }
            }
        }
    }
}