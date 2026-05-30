// <copyright file="SkillTreeNodeUiElement.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.UiElement
{
    using System;
    using GameHelper.Cache;
    using GameOffsets.Objects.UiElement;
    using ImGuiNET;

    /// <summary>
    ///     Points to the Map UiElement.
    /// </summary>
    public class SkillTreeNodeUiElement : UiElementBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="SkillTreeNodeUiElement" /> class.
        /// </summary>
        /// <param name="address">address to the Skill Tree Node Ui Element of the game.</param>
        /// <param name="parents">parent cache to use for this Ui Element.</param>
        internal SkillTreeNodeUiElement(IntPtr address, UiElementParents parents)
            : base(address, parents) { }

        /// <summary>
        ///     Gets the graph ID associated with the skill.
        /// </summary>
        public int SkillGraphId { get; private set; } = 0x00;

        /// <summary>
        ///     Converts the <see cref="LargeMapUiElement" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"SkillGraphId = {this.SkillGraphId}");
        }

        /// <inheritdoc />
        protected override void CleanUpData()
        {
            base.CleanUpData();
            this.SkillGraphId = 0x00;
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var handle = Core.Process.Handle;
            var data = handle.ReadMemory<SkillTreeNodeUiElementOffset>(this.Address);
            this.UpdateData(data.UiElementBase, hasAddressChanged);
            if (data.SkillInfo != IntPtr.Zero)
            {
                this.SkillGraphId = handle.ReadMemory<PassiveSkillsDatStruct>(
                    handle.ReadMemory<SkillInfoStruct>(
                        data.SkillInfo).PassiveSkillsDatRow).PassiveSkillGraphId;
            }
        }
    }
}