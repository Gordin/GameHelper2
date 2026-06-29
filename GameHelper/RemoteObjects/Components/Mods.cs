// <copyright file="Mods.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.Components
{
    using System;
    using System.Collections.Generic;
    using GameHelper.RemoteEnums;
    using GameOffsets.Objects.Components;
    using ImGuiNET;

    /// <summary>
    ///     The <see cref="Mods" /> component in the entity.
    /// </summary>
    public class Mods : ComponentBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Mods" /> class.
        /// </summary>
        /// <param name="address">address of the <see cref="Mods" /> component.</param>
        public Mods(IntPtr address)
            : base(address) { }

        /// <summary>
        ///     Gets a value indicating item rarity information.
        /// </summary>
        public Rarity Rarity { get; private set; } = Rarity.Normal;

        /// <summary>
        ///     Gets the mods and their values of the entity.
        ///     If a mod doesn't have a value, it will be represented by
        ///     <see cref="float.NaN"/>.
        /// </summary>
        public List<(string name, (float value0, float value1) values)>
            ImplicitMods = new(),
            ExplicitMods = new(),
            EnchantMods = new(),
            HellscapeMods = new();

        /// <summary>
        ///     Converts the <see cref="Mods" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"Rarity: {this.Rarity}");
            ObjectMagicProperties.ModsToImGui("ImplicitMods", this.ImplicitMods);
            ObjectMagicProperties.ModsToImGui("ExplicitMods", this.ExplicitMods);
            ObjectMagicProperties.ModsToImGui("EnchantMods", this.EnchantMods);
            ObjectMagicProperties.ModsToImGui("HellscapeMods", this.HellscapeMods);
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<ModsOffsets>(this.Address);
            this.OwnerEntityAddress = data.Header.EntityPtr;
            this.Rarity = (Rarity)data.Details0.Rarity;

            if (hasAddressChanged)
            {
                this.ImplicitMods.Clear();
                this.ExplicitMods.Clear();
                this.EnchantMods.Clear();
                this.HellscapeMods.Clear();
                ObjectMagicProperties.AddToMods(this.ImplicitMods, reader.ReadStdVector<ModArrayStruct>(data.Details0.Mods.ImplicitMods));
                ObjectMagicProperties.AddToMods(this.ExplicitMods, reader.ReadStdVector<ModArrayStruct>(data.Details0.Mods.ExplicitMods));
                ObjectMagicProperties.AddToMods(this.EnchantMods, reader.ReadStdVector<ModArrayStruct>(data.Details0.Mods.EnchantMods));
                ObjectMagicProperties.AddToMods(this.HellscapeMods, reader.ReadStdVector<ModArrayStruct>(data.Details0.Mods.HellscapeMods));
                ObjectMagicProperties.AddToMods(this.HellscapeMods, reader.ReadStdVector<ModArrayStruct>(data.Details0.Mods.CrucibleMods));
            }
        }
    }
}