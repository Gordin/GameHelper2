// <copyright file="Charges.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.Components
{
    using System;
    using GameOffsets.Objects.Components;
    using ImGuiNET;

    /// <summary>
    ///     The <see cref="Charges" /> component in the entity.
    /// </summary>
    public class Charges : ComponentBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Charges" /> class.
        /// </summary>
        /// <param name="address">address of the <see cref="Charges" /> component.</param>
        public Charges(IntPtr address)
            : base(address) { }

        /// <summary>
        ///     Gets a value indicating number of charges the flask has.
        /// </summary>
        public int Current { get; private set; }

        /// <summary>
        ///     Gets the value indicating the number of cahrges required per flask use.
        /// </summary>
        public int PerUseCharge { get; private set; }

        /// <summary>
        ///     Converts the <see cref="Charges" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"Current Charges: {this.Current}");
            ImGui.Text($"PerUse Charges: {this.PerUseCharge}");
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<ChargesOffsets>(this.Address);
            this.OwnerEntityAddress = data.Header.EntityPtr;
            this.Current = data.current;
            if (hasAddressChanged )
            {
                this.PerUseCharge = reader.ReadMemory<ChargesInternalStruct>(data.ChargesInternalPtr).PerUseCharges;
            }
        }
    }
}