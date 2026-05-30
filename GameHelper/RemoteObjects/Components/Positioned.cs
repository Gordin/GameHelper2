// <copyright file="Positioned.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.Components
{
    using System;
    using GameOffsets.Objects.Components;
    using GameOffsets.Objects.States.InGameState;
    using ImGuiNET;

    /// <summary>
    ///     The <see cref="Positioned" /> component in the entity.
    /// </summary>
    public class Positioned : ComponentBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Positioned" /> class.
        /// </summary>
        /// <param name="address">address of the <see cref="Positioned" /> component.</param>
        public Positioned(IntPtr address)
            : base(address) { }

        /// <summary>
        ///     Gets the flags related to the entity from the positioned component.
        ///     NOTE: This flag contains the information if the entity is friendly or not.
        /// </summary>
        public byte Flags { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether the entity is friendly or not.
        /// </summary>
        public bool IsFriendly { get; private set; }

        /// <summary>
        ///     Converts the <see cref="Positioned" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"Flags: {this.Flags:X}");
            ImGui.Text($"IsFriendly: {this.IsFriendly}");
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<PositionedOffsets>(this.Address);
            this.OwnerEntityAddress = data.Header.EntityPtr;
            this.Flags = data.Reaction;
            this.IsFriendly = EntityHelper.IsFriendly(data.Reaction);
        }
    }
}