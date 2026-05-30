// <copyright file="TriggerableBlockage.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.Components
{
    using System;
    using GameOffsets.Objects.Components;
    using ImGuiNET;

    /// <summary>
    ///     The <see cref="TriggerableBlockage" /> component in the entity.
    /// </summary>
    public class TriggerableBlockage : ComponentBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="TriggerableBlockage" /> class.
        /// </summary>
        /// <param name="address">address of the <see cref="TriggerableBlockage" /> component.</param>
        public TriggerableBlockage(IntPtr address)
            : base(address) { }

        /// <summary>
        ///     Gets a value indicating whether TriggerableBlockage is closed or not.
        /// </summary>
        public bool IsBlocked { get; private set; }

        /// <summary>
        ///     Converts the <see cref="Chest" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"Is Blocked: {this.IsBlocked}");
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<TriggerableBlockageOffsets>(this.Address);
            this.OwnerEntityAddress = data.Header.EntityPtr;
            this.IsBlocked = data.IsBlocked;
        }
    }
}