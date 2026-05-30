// <copyright file="Targetable.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.Components
{
    using System;
    using System.Runtime.InteropServices;
    using GameOffsets.Objects.Components;
    using ImGuiNET;

    /// <summary>
    ///     The <see cref="Targetable" /> component in the entity.
    /// </summary>
    public class Targetable : ComponentBase
    {
        private TargetableOffsets cache;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Targetable" /> class.
        /// </summary>
        /// <param name="address">address of the <see cref="Targetable" /> component.</param>
        public Targetable(IntPtr address)
            : base(address) { }

        /// <summary>
        ///     Gets the value indicating whether the entity is targetable or not.
        /// </summary>
        public bool IsTargetable { get; private set; } = false;

        /// <summary>
        ///     Converts the <see cref="Targetable" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"IsHighlightable: {this.cache.IsHighlightable}");
            ImGui.Text($"IsTargettedByPlayer: {this.cache.IsTargettedByPlayer}");

            ImGui.Text($"IsTargetable: {this.cache.IsTargetable}");
            ImGui.Text($"HiddenfromPlayer: {this.cache.HiddenfromPlayer}");
            ImGui.Text($"NeedsTrue: {this.cache.NeedsTrue}");
            ImGui.Text($"MeetsQuestState: {this.cache.MeetsQuestState}");
            ImGui.Text($"NeedsFalse: {this.cache.NeedsFalse}");
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<TargetableOffsets>(this.Address);
            this.OwnerEntityAddress = data.Header.EntityPtr;
            this.cache = data;
            this.IsTargetable = data.IsTargetable && !data.HiddenfromPlayer &&
                data.NeedsTrue && data.MeetsQuestState && !data.NeedsFalse;
        }
    }
}