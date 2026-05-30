// <copyright file="Stats.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.Components
{
    using System;
    using System.Collections.Generic;
    using GameHelper.RemoteEnums;
    using GameHelper.Utils;
    using GameOffsets.Objects.Components;
    using ImGuiNET;

    /// <summary>
    ///     The <see cref="Stats" /> component in the entity.
    /// </summary>
    public class Stats : ComponentBase
    {
        /// <summary>
        ///     Gets all the stats of the entity.
        /// </summary>
        public Dictionary<GameStats, int> StatsChangedByItems = new();

        /// <summary>
        ///     Gets all the stats of the entity.
        /// </summary>
        public Dictionary<GameStats, int> StatsChangedByBuffAndActions = new();

        /// <summary>
        ///     Gets the WeaponIndex (I or II) that is currently active.
        /// </summary>
        public int CurrentWeaponIndex = 0;

        /// <summary>
        ///     Gets the value indicating if entity is in shape shfited form or not
        /// </summary>
        public bool IsInShapeshiftedForm = false;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Stats" /> class.
        /// </summary>
        /// <param name="address">address of the <see cref="Stats" /> component.</param>
        public Stats(IntPtr address)
            : base(address) { }

        /// <inheritdoc/>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"CurrentWeaponIndex: {this.CurrentWeaponIndex}");
            ImGui.Text($"IsInShapeshiftedForm: {this.IsInShapeshiftedForm}");
            ImGuiHelper.StatsWidget(this.StatsChangedByItems, "Entity Stats Changed By Items");
            ImGuiHelper.StatsWidget(this.StatsChangedByBuffAndActions, "Entity Stats Changed By BuffAndActions");
        }

        /// <inheritdoc/>
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<StatsOffsets>(this.Address);
            this.OwnerEntityAddress = data.Header.EntityPtr;
            this.CurrentWeaponIndex = data.CurrentWeaponIndex;
            this.IsInShapeshiftedForm = data.ShapeshiftFormsRowPtr != 0x00;
            if (data.StatsChangedByItemsPtr != IntPtr.Zero)
            {
                var data2 = reader.ReadMemory<StatsStructInternal>(data.StatsChangedByItemsPtr);
                base.StatUpdator(this.StatsChangedByItems, data2.Stats);
            }

            if (data.StatsChangedByBuffAndActions != IntPtr.Zero)
            {
                var data3 = reader.ReadMemory<StatsStructInternal>(data.StatsChangedByBuffAndActions);
                base.StatUpdator(this.StatsChangedByBuffAndActions, data3.Stats);
            }
        }
    }
}
