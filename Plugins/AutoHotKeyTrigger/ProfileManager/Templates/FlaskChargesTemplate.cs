// <copyright file="FlaskChargesTemplate.cs" company="PlaceholderCompany">
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
    public static class FlaskChargesTemplate
    {
        private static readonly List<string> SupportedOperatorTypes = new()
        {
            ">",
            ">=",
            "<",
            "<="
        };

        private static int flaskSlot = 1;
        private static int flaskCharges = 50;
        private static string selectedOperator = ">";

        /// <summary>
        ///     Display the ImGui widget for adding the condition in <see cref="DynamicCondition"/>.
        /// </summary>
        /// <returns>
        ///     condition in string format if user press Add button otherwise empty string.
        /// </returns>
        public static string Add()
        {
            ImGui.Text("Flask");
            ImGui.SameLine();
            ImGui.PushItemWidth(ImGui.GetFontSize() * 3);
            ImGui.DragInt("has##FlaskChargesFlaskSlot", ref flaskSlot, 0.02f, 1, 2);
            ImGui.SameLine();
            ImGuiHelper.IEnumerableComboBox("##FlaskChargesOperator", SupportedOperatorTypes, ref selectedOperator);
            ImGui.SameLine();
            ImGui.DragInt("charges##FlaskChargesFlaskCharge", ref flaskCharges, 0.1f, 2, 80);
            ImGui.PopItemWidth();
            ImGui.SameLine();
            return ImGui.Button("Add##FlaskCharges") ?
                $"Flasks.Flask{flaskSlot}.Charges {selectedOperator} {flaskCharges}" :
                string.Empty;
        }
    }
}
