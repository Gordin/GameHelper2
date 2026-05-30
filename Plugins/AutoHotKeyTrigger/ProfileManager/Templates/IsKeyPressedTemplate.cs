// <copyright file="IsKeyPressedTemplate.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.Templates
{
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions;
    using ClickableTransparentOverlay.Win32;
    using GameHelper.Utils;
    using ImGuiNET;

    /// <summary>
    ///     ImGui widget that helps user modify the condition code in <see cref="DynamicCondition"/>.
    /// </summary>
    public static class IsKeyPressedTemplate
    {
        private static VK pressedKey = VK.KEY_A;

        /// <summary>
        ///     Display the ImGui widget for adding the condition in <see cref="DynamicCondition"/>.
        /// </summary>
        /// <returns>
        ///     condition in string format if user press Add button otherwise empty string.
        /// </returns>
        public static string Add()
        {
            ImGui.Text("User has pressed");
            ImGui.SameLine();
            ImGuiHelper.NonContinuousEnumComboBox("Key##IsKeyPressedTemplate", ref pressedKey);
            if (ImGui.Button("Add##IsKeyPressed"))
            {
                return $"IsKeyPressedForAction(VK.{pressedKey})";
            }
            else
            {
                return string.Empty;
            }
        }
    }
}
