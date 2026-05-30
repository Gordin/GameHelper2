// <copyright file="Chest.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.Components
{
    using System;
    using GameOffsets.Objects.Components;
    using ImGuiNET;

    /// <summary>
    ///     The <see cref="Chest" /> component in the entity.
    /// </summary>
    public class Chest : ComponentBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Chest" /> class.
        /// </summary>
        /// <param name="address">address of the <see cref="Chest" /> component.</param>
        public Chest(IntPtr address)
            : base(address) { }

        /// <summary>
        ///     Gets a value indicating whether chest is opened or not.
        /// </summary>
        public bool IsOpened { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether chest is a strongbox or not.
        /// </summary>
        public bool IsStrongbox { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether chest label is visible or not
        ///     NOTE: Breach chests, Legion chests, Normal Chests labels are visible.
        /// </summary>
        public bool IsLabelVisible { get; private set; }

        /// <summary>
        ///     Converts the <see cref="Chest" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"IsOpened: {this.IsOpened}");
            ImGui.Text($"IsStrongbox: {this.IsStrongbox}");
            ImGui.Text($"IsLabelVisible: {this.IsLabelVisible}");
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<ChestOffsets>(this.Address);
            this.OwnerEntityAddress = data.Header.EntityPtr;
            this.IsOpened = data.IsOpened;
            if (hasAddressChanged)
            {
                var dataInternal = reader.ReadMemory<ChestsStructInternal>(data.ChestsDataPtr);
                this.IsStrongbox = dataInternal.StrongboxDatPtr != IntPtr.Zero;
                this.IsLabelVisible = dataInternal.IsLabelVisible;
            }
        }
    }
}