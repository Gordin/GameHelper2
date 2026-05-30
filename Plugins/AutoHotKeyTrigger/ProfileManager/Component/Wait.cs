// <copyright file="Wait.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>


namespace AutoHotKeyTrigger.ProfileManager.Component
{
    using ImGuiNET;
    using Newtonsoft.Json;
    using System.Diagnostics;
    using System.Numerics;

    /// <summary>
    ///     Adds wait to the condition.
    /// </summary>
    public class Wait : IComponent
    {
        [JsonProperty] private float duration;
        private Stopwatch sw;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Wait" /> class.
        /// </summary>
        /// <param name="duation">duration in seconds to wait for.</param>
        public Wait(float duation)
        {
            this.duration = duation;
            this.sw = new();
        }

        /// <inheritdoc/>
        public IComponent Clone()
        {
            return new Wait(this.duration);
        }

        /// <inheritdoc/>
        public void Display(bool expand)
        {
            if (expand)
            {
                ImGui.Text("Condition WAITS for");
                ImGui.SameLine();
                ImGui.DragFloat("(seconds)##WAITCOMPONENT", ref this.duration, 0.05f, 0.0f, 5f);
            }
            else
            {
                ImGui.SameLine();
                ImGui.Text("for");
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(255, 255, 0, 255), $"{this.duration}");
                ImGui.SameLine();
                ImGui.Text("seconds.");
            }
        }

        /// <inheritdoc/>
        public bool execute(bool isConditionValid)
        {
            if (isConditionValid)
            {
                if (!this.sw.IsRunning)
                {
                    this.sw.Start();
                }

                if (this.sw.ElapsedMilliseconds >= (this.duration * 1000f))
                {
                    return true;
                }
            }
            else
            {
                this.sw.Reset();
            }

            return false;
        }
    }
}
