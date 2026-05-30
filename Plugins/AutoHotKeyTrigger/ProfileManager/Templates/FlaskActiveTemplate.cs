// <copyright file="FlaskActiveTemplate.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.Templates
{
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions;
    using ImGuiNET;

    /// <summary>
    ///     ImGui widget that helps user modify the condition code in <see cref="DynamicCondition"/>.
    /// </summary>
    public static class FlaskActiveTemplate
    {
        private static int flaskSlot = 1;

        /// <summary>
        ///     Display the ImGui widget for adding the condition in <see cref="DynamicCondition"/>.
        /// </summary>
        /// <returns>
        ///     condition in string format if user press Add button otherwise empty string.
        /// </returns>
        public static string Add()
        {
            ImGui.Text("Player does not have effect of flask");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 3);
            ImGui.DragInt("##FlaskEffectFlaskSlot", ref flaskSlot, 0.02f, 1, 2);
            ImGui.SameLine();
            return ImGui.Button("Add##FlaskEffect") ? $"!Flasks.Flask{flaskSlot}.Active" : string.Empty;
        }
    }
}
