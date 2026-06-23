// <copyright file="WorldItem.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.Components
{
    using System;
    using GameOffsets.Objects.Components;
    using ImGuiNET;

    /// <summary>
    ///     The <see cref="WorldItem" /> component — present on a dropped/ground item entity. The ground
    ///     entity is a wrapper; the real item (carrying <see cref="Mods" />, <see cref="Base" />,
    ///     <see cref="RenderItem" />, <see cref="Stack" />) lives at <see cref="ItemEntityAddress" />.
    /// </summary>
    public class WorldItem : ComponentBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="WorldItem" /> class.
        /// </summary>
        /// <param name="address">address of the <see cref="WorldItem" /> component.</param>
        public WorldItem(IntPtr address)
            : base(address) { }

        /// <summary>
        ///     Gets the address of the inner item entity (the one carrying the item components).
        ///     <see cref="IntPtr.Zero" /> when unavailable.
        /// </summary>
        public IntPtr ItemEntityAddress { get; private set; } = IntPtr.Zero;

        /// <summary>
        ///     Converts the <see cref="WorldItem" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"Item Entity: {this.ItemEntityAddress.ToInt64():X}");
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<WorldItemOffsets>(this.Address);
            this.OwnerEntityAddress = data.Header.EntityPtr;
            this.ItemEntityAddress = data.ItemEntityPtr;
        }
    }
}
