// <copyright file="FlaskIsUseableTemplate.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.Templates
{
    using AutoHotKeyTrigger;
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions;
    using ImGuiNET;

    /// <summary>
    ///     ImGui widget that helps user modify the condition code in <see cref="DynamicCondition"/>.
    /// </summary>
    public static class FlaskIsUseableTemplate
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
            ImGui.Text(AhkText.T("template.flask", "Flask"));
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 3);
            ImGui.DragInt(AhkText.Label("template.has_enough_charges", "has enough charges.", "FlaskIsUseableFlaskSlot"), ref flaskSlot, 0.02f, 1, 2);
            ImGui.SameLine();
            return ImGui.Button(AhkText.Label("button.add", "Add", "FlaskIsUseable")) ? $"Flasks.Flask{flaskSlot}.IsUsable" : string.Empty;
        }
    }
}
