// <copyright file="StatusEffectTemplate.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.Templates
{
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions;
    using AutoHotKeyTrigger.ProfileManager.Enums;
    using GameHelper.Utils;
    using ImGuiNET;
    using System.Collections.Generic;

    /// <summary>
    ///     ImGui widget that helps user modify the condition code in <see cref="DynamicCondition"/>.
    /// </summary>
    public static class StatusEffectTemplate
    {
        private static readonly List<string> SupportedOperatorTypes = new()
        {
            "has",
            "not has",
            ">",
            ">=",
            "<",
            "<="
        };

        private static string buffId = "grace_period";
        private static string selectedOperator = "has";
        private static StatusEffectCheckType checkType = StatusEffectCheckType.PercentTimeLeft;
        private static float threshold = 0;

        /// <summary>
        ///     Display the ImGui widget for adding the condition in <see cref="DynamicCondition"/>.
        /// </summary>
        /// <returns>
        ///     condition in string format if user press Add button otherwise empty string.
        /// </returns>
        public static string Add()
        {
            ImGui.PushID("StatusEffectDuration");
            if (selectedOperator == "has" || selectedOperator == "not has")
            {
                ImGui.Text("Player");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 4);
                ImGuiHelper.IEnumerableComboBox("##StatusEffectOperator", SupportedOperatorTypes, ref selectedOperator);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 8);
                ImGui.InputText("(de)buff", ref buffId, 200);
                HelpBox();
            }
            else
            {
                ImGui.Text("Player has (de)buff");
                ImGui.SameLine();
                ImGui.InputText("with", ref buffId, 200);
                HelpBox();
                ImGui.SameLine();
                ImGuiHelper.IEnumerableComboBox("##StatusEffectOperator", SupportedOperatorTypes, ref selectedOperator);
                ImGui.SameLine();
                ImGui.InputFloat("##threshold", ref threshold);
                ImGui.SameLine();
                ImGuiHelper.EnumComboBox("##checkType", ref checkType);
                ImGuiHelper.ToolTip($"What to compare. {StatusEffectCheckType.PercentTimeLeft} ranges from " +
                    $"0 to 100, 0 being buff will expire imminently and 100 meaning " +
                    $"it was just applied");
            }

            ImGui.PopID();
            ImGui.SameLine();
            if (ImGui.Button("Add##StatusEffect"))
            {
                return selectedOperator switch
                {
                    "has" => $"PlayerBuffs.Has(\"{buffId}\")",
                    "not has" => $"!PlayerBuffs.Has(\"{buffId}\")",
                    _ => $"PlayerBuffs[\"{buffId}\"].{checkType} {selectedOperator} {threshold}",
                };
            }
            else
            {
                return string.Empty;
            }
        }

        private static void HelpBox()
        {
            ImGuiHelper.ToolTip("Open Core -> DV -> States -> InGameStateObject -> " +
                "CurrentAreaInstance -> Player -> Components -> Buffs -> Status Effect to figure " +
                "out what value to put here. Make sure that (de)buff is active.");
        }
    }
}
