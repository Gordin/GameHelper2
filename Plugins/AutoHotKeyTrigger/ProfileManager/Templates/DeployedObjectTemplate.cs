// <copyright file="DeployedObjectTemplate.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.Templates
{
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
        private static string[] object_types;

        private static string selectedOperator;
        private static readonly List<string> SupportedOperatorTypes;

        private static int count;

        static DeployedObjectTemplate()
        {
            count = 0;

            selectedOperator = ">";
            SupportedOperatorTypes = new()
            {
                ">",
                ">=",
                "<",
                "<="
            };

            objectType = 0;
            object_types = new string[256];
            for (var i = 0; i < object_types.Length; i++)
            {
                object_types[i] = $"{i}";
            }
        }

        /// <summary>
        ///     Display the ImGui widget for adding the condition in <see cref="DynamicCondition"/>.
        /// </summary>
        /// <returns>
        ///     condition in string format if user press Add button otherwise empty string.
        /// </returns>
        public static string Add()
        {
            ImGui.Text("Player has deployed the object of type");
            ImGui.SameLine();
            ImGui.PushItemWidth(ImGui.GetFontSize() * 4);
            ImGui.Combo("##DeployedObjectType", ref objectType, object_types, object_types.Length);
            ImGuiHelper.ToolTip("Open Core -> DV -> States -> InGameStateObject -> " +
                "CurrentAreaInstance -> Player -> Components -> Actor -> Deployed Objects to figure " +
                "out what value to put here.");
            ImGui.SameLine();
            ImGuiHelper.IEnumerableComboBox("##DeployedObjectOperator", SupportedOperatorTypes, ref selectedOperator);
            ImGui.SameLine();
            ImGui.Combo("times##DeployedObjectCount", ref count, object_types, object_types.Length);
            ImGui.PopItemWidth();
            ImGui.SameLine();
            if (ImGui.Button("Add##DeployedObject"))
            {
                return $"DeployedObjectsCount[{objectType}] {selectedOperator} {count}";
            }

            return string.Empty;
        }
    }
}
