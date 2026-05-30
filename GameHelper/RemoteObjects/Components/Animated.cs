// <copyright file="Animated.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>


namespace GameHelper.RemoteObjects.Components
{
    using System;
    using GameOffsets.Objects.Components;
    using GameOffsets.Objects.States.InGameState;
    using ImGuiNET;

    /// <summary>
    ///     The <see cref="Animated" /> component in the entity.
    /// </summary>
    public class Animated : ComponentBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Animated" /> class.
        /// </summary>
        /// <param name="address">address of the <see cref="Animated" /> component.</param>
        public Animated(IntPtr address)
            : base(address) { }

        /// <summary>
        ///     Gets the path of the animated entity.
        /// </summary>
        public string Path { get; private set; } = string.Empty;

        /// <summary>
        ///     Gets the Id of the animated entity.
        /// </summary>
        public uint Id { get; private set; }

        /// <summary>
        ///     Converts to <see cref="Animated"/> class data to Imgui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"Path: {this.Path}");
            ImGui.Text($"Id: {this.Id}");
        }

        /// <inheritdoc/>
        protected override void UpdateData(bool hasAddressChanged)
        {
            if (!hasAddressChanged)
            {
                return;
            }

            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<AnimatedOffsets>(this.Address);
            this.OwnerEntityAddress = data.Header.EntityPtr;
            if (data.AnimatedEntityPtr != IntPtr.Zero)
            {
                var entity = reader.ReadMemory<EntityOffsets>(data.AnimatedEntityPtr);
                var details = reader.ReadMemory<EntityDetails>(entity.ItemBase.EntityDetailsPtr);
                this.Path = reader.ReadStdWString(details.name);
                this.Id = entity.Id;
            }
        }
    }
}
