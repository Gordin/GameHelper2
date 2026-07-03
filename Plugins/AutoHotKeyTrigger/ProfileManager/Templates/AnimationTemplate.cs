// <copyright file="AnimationTemplate.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.Templates
{
    using AutoHotKeyTrigger;
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions;
    using GameHelper.Utils;
    using ImGuiNET;
    using System.Collections.Generic;

    /// <summary>
    ///     ImGui widget that helps user modify the condition code in <see cref="DynamicCondition"/>.
    /// </summary>
    public static class AnimationTemplate
    {
        private static readonly List<string> SupportedOperatorTypes = new() { "is", "is not" };
        private static string selectedOperator = "is";
        private static int animation = 0x00;

        /// <summary>
        ///     Display the ImGui widget for adding the condition in <see cref="DynamicCondition"/>.
        /// </summary>
        /// <returns>
        ///     condition in string format if user press Add button otherwise empty string.
        /// </returns>
        public static string Add()
        {
            var ret = string.Empty;
            ImGui.Text(AhkText.T("template.player_animation", "Player animation"));
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 4);
            ImGuiHelper.IEnumerableComboBox("##AnimationOperator", SupportedOperatorTypes, ref selectedOperator);
            ImGui.SameLine();
            ImGui.InputInt("##AnimationRHS", ref animation);
            ImGuiHelper.ToolTip(AhkText.T("template.animation.tooltip", "Open Core -> DV -> States -> InGameStateObject -> " +
                "CurrentAreaInstance -> Player -> Components -> Actor -> AnimationId to figure " +
                "out what value to put here. Make sure you are doing that animation after opening."));
            ImGui.SameLine();
            if (ImGui.Button(AhkText.Label("button.add", "Add", "Animation")))
            {
                ret = $"PlayerAnimation.Equals({animation})";
                if (selectedOperator == "is")
                {
                    return ret;
                }
                else
                {
                    return $"!{ret}";
                }
            }

            return ret;
        }
    }
}
