// <copyright file="AilmentTemplate.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.Templates
{
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions;
    using GameHelper.Utils;
    using ImGuiNET;

    /// <summary>
    ///     ImGui widget that helps user modify the condition code in <see cref="DynamicCondition"/>.
    /// </summary>
    public static class AilmentTemplate
    {
        private static string statusEffectGroupKey = "Bleeding Or Corruption";

        /// <summary>
        ///     Display the ImGui widget for adding the condition in <see cref="DynamicCondition"/>.
        /// </summary>
        /// <returns>
        ///     condition in string format if user press Add button otherwise empty string.
        /// </returns>
        public static string Add()
        {
            ImGui.Text("Player has");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 11f);
            ImGuiHelper.IEnumerableComboBox(
                    "ailment.##AilmentCondition",
                    JsonDataHelper.StatusEffectGroups.Keys,
                    ref statusEffectGroupKey);
            ImGui.SameLine();
            if (ImGui.Button("Add##AilmentAdd"))
            {
                return $"PlayerAilments.Contains(\"{statusEffectGroupKey}\")";
            }

            return string.Empty;
        }
    }
}
