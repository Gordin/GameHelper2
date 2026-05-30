// <copyright file="Shrine.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.Components
{
    using System;
    using GameOffsets.Objects.Components;
    using ImGuiNET;

    /// <summary>
    ///     The <see cref="Shrine" /> component in the entity.
    /// </summary>
    public class Shrine : ComponentBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Shrine" /> class.
        /// </summary>
        /// <param name="address">address of the <see cref="Shrine" /> component.</param>
        public Shrine(IntPtr address)
            : base(address) { }

        /// <summary>
        ///     Gets a value indicating whether chest is opened or not.
        /// </summary>
        public bool IsUsed { get; private set; }

        /// <summary>
        ///     Converts the <see cref="Chest" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"Is Shrine Used: {this.IsUsed}");
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<ShrineOffsets>(this.Address);
            this.OwnerEntityAddress = data.Header.EntityPtr;
            this.IsUsed = data.IsUsed;
        }
    }
}