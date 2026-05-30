// <copyright file="IsKeyPressedTemplate.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.Templates
{
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions;
    using ImGuiNET;

    /// <summary>
    ///     ImGui widget that helps user modify the condition code in <see cref="DynamicCondition"/>.
    /// </summary>
    public static class WeaponSetActiveTemplate
    {
        private static bool yesOrNo = true;

        /// <summary>
        ///     Display the ImGui widget for adding the condition in <see cref="DynamicCondition"/>.
        /// </summary>
        /// <returns>
        ///     condition in string format if user press Add button otherwise empty string.
        /// </returns>
        public static string Add()
        {
            ImGui.Checkbox("Player has first weapon set active", ref yesOrNo);
            if (ImGui.Button("Add##WeaponSetActive"))
            {
                return yesOrNo ? "PlayerFirstWeaponSetActive" : "PlayerSecondWeaponSetActive";
            }
            else
            {
                return string.Empty;
            }
        }
    }
}
