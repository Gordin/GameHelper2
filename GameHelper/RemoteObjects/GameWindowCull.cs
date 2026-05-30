// <copyright file="GameWindowCull.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects
{
    using System;
    using System.Collections.Generic;
    using Coroutine;
    using CoroutineEvents;
    using ImGuiNET;

    /// <summary>
    ///     Reads the Game Window Cull (black bar on each side) values from the game.
    ///     Only reads when game window moves/resize.
    /// </summary>
    public class GameWindowCull : RemoteObjectBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="GameWindowCull" /> class.
        /// </summary>
        /// <param name="address">address of the remote memory object.</param>
        internal GameWindowCull(IntPtr address)
            : base(address)
        {
            CoroutineHandler.Start(this.OnGameMove(), priority: int.MaxValue);
            CoroutineHandler.Start(this.OnGameForegroundChange(), priority: int.MaxValue);
        }

        /// <summary>
        ///     Gets the current game window scale values.
        /// </summary>
        public int Value { get; private set; }

        /// <summary>
        ///     Converts the <see cref="GameWindowCull" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"Game Window Cull Size: {this.Value}");
        }

        /// <inheritdoc />
        protected override void CleanUpData()
        {
            this.Value = 0;
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            this.Value = reader.ReadMemory<int>(this.Address);
        }

        private IEnumerator<Wait> OnGameMove()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnMoved);
                try
                {
                    // No need to check for IntPtr.zero
                    // because game will only move when it exists. :D
                    this.UpdateData(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GameWindowCull.OnGameMove] {ex}");
                }
            }
        }

        private IEnumerator<Wait> OnGameForegroundChange()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.OnForegroundChanged);
                try
                {
                    // No need to check for IntPtr.zero
                    // because game will only move when it exists. :D
                    this.UpdateData(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[GameWindowCull.OnGameForegroundChange] {ex}");
                }
            }
        }
    }
}