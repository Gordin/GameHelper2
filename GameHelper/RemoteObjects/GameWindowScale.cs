// <copyright file="GameWindowScale.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects
{
    using System;
    using System.Collections.Generic;
    using Coroutine;
    using CoroutineEvents;
    using GameOffsets.Objects.UiElement;
    using ImGuiNET;

    /// <summary>
    ///     Initially this class reads the game window scale value from the game.
    ///     But that was PITA since it's memory location changed a lot. So, this
    ///     class was updated to just calculate the game window scale value in the
    ///     same way the game does it.
    /// </summary>
    public class GameWindowScale : RemoteObjectBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="GameWindowScale" /> class.
        /// </summary>
        internal GameWindowScale()
            : base(IntPtr.Zero)
        {
            CoroutineHandler.Start(this.OnGameMove(), priority: int.MaxValue - 1);
            CoroutineHandler.Start(this.OnGameForegroundChange(), priority: int.MaxValue - 1);
        }

        private float[] valuesSnap = new float[6] { 1f, 1f, 1f, 1f, 1f, 1f };

        /// <summary>
        ///     Gets the current game window scale values.
        ///     There are 3 rows (1 for each UI Element type)
        ///     and each row contains 2 element.
        ///     Returns a per-call snapshot — atomic with respect to UpdateData.
        /// </summary>
        public float[] Values => System.Threading.Volatile.Read(ref this.valuesSnap);

        /// <summary>
        ///     Gets the Scale Value depending on the index and multiplier.
        /// </summary>
        /// <param name="index">index read from the Ui-Element.</param>
        /// <param name="multiplier">multiplier read from the Ui-Element.</param>
        /// <returns>Returns the Width and Height scale value valid for the specific Ui-Element.</returns>
        public (float WidthScale, float HeightScale) GetScaleValue(int index, float multiplier)
        {
            var snap = System.Threading.Volatile.Read(ref this.valuesSnap);
            var widthScale = multiplier;
            var heightScale = multiplier;
            switch (index)
            {
                case 1:
                    widthScale *= snap[0];
                    heightScale *= snap[1];
                    break;
                case 2:
                    widthScale *= snap[2];
                    heightScale *= snap[3];
                    break;
                case 3:
                    widthScale *= snap[4];
                    heightScale *= snap[5];
                    break;
            }

            return (widthScale, heightScale);
        }

        /// <summary>
        ///     Converts the <see cref="GameWindowScale" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"Index 1: width, height {this.GetScaleValue(1, 1)} ratio");
            ImGui.Text($"Index 2: width, height {this.GetScaleValue(2, 1)} ratio");
            ImGui.Text($"Index 3: width, height {this.GetScaleValue(3, 1)} ratio");
        }

        /// <inheritdoc />
        protected override void CleanUpData()
        {
            System.Threading.Volatile.Write(ref this.valuesSnap, new float[6] { 1f, 1f, 1f, 1f, 1f, 1f });
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            // All of the code below is written after RE-ing the game function.
            var v1 = (float)((Core.Process.WindowArea.Width - Core.GameCull.Value - Core.GameCull.Value) / UiElementBaseFuncs.BaseResolution.X);
            var v2 = (float)((Core.Process.WindowArea.Height - 0) / UiElementBaseFuncs.BaseResolution.Y);
            var newValues = new float[6] { v1, v1, v2, v2, v1, v2 };
            System.Threading.Volatile.Write(ref this.valuesSnap, newValues);
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
                    Console.WriteLine($"[GameWindowScale.OnGameMove] {ex}");
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
                    Console.WriteLine($"[GameWindowScale.OnGameForegroundChange] {ex}");
                }
            }
        }
    }
}