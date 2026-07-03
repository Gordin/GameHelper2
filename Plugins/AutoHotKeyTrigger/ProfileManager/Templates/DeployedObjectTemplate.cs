// <copyright file="DeployedObjectTemplate.cs" company="PlaceholderCompany">
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
    public static class DeployedObjectTemplate
    {
        private static int objectType;

        private static string selectedOperator;
        private static readonly List<string> SupportedOperatorTypes;

        private static int count;

        static DeployedObjectTemplate()
        {
            count = 0;
            objectType = 0;

            selectedOperator = ">";
            SupportedOperatorTypes = new()
            {
                ">",
                ">=",
                "<",
                "<="
            };
        }

        /// <summary>
        ///     Display the ImGui widget for adding the condition in <see cref="DynamicCondition"/>.
        /// </summary>
        /// <returns>
        ///     condition in string format if user press Add button otherwise empty string.
        /// </returns>
        public static string Add()
        {
            ImGui.Text(AhkText.T("template.deployed_object", "Player has deployed the object of type"));
            ImGui.SameLine();
            ImGui.PushItemWidth(ImGui.GetFontSize() * 6);
            ImGui.InputInt("##DeployedObjectType", ref objectType);
            if (objectType < 0)
            {
                objectType = 0;
            }

            ImGuiHelper.ToolTip(AhkText.T("template.deployed_object.tooltip", "Open Core -> DV -> States -> InGameStateObject -> " +
                "CurrentAreaInstance -> Player -> Components -> Actor -> Deployed Objects to figure " +
                "out what value to put here. PoE2 uses large type ids (e.g. 22938), not 0-255."));
            ImGui.SameLine();
            ImGuiHelper.IEnumerableComboBox("##DeployedObjectOperator", SupportedOperatorTypes, ref selectedOperator);
            ImGui.SameLine();
            ImGui.InputInt(AhkText.Label("template.times", "times", "DeployedObjectCount"), ref count);
            if (count < 0)
            {
                count = 0;
            }

            ImGui.PopItemWidth();
            ImGui.SameLine();
            if (ImGui.Button(AhkText.Label("button.add", "Add", "DeployedObject")))
            {
                return $"DeployedObjectsCount[{objectType}] {selectedOperator} {count}";
            }

            return string.Empty;
        }
    }
}
