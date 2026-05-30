// <copyright file="IsSkillUseableTemplate.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>


namespace AutoHotKeyTrigger.ProfileManager.Templates
{

    using AutoHotKeyTrigger.ProfileManager.DynamicConditions;
    using GameHelper;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.Utils;
    using ImGuiNET;

    /// <summary>
    ///     ImGui widget that helps user modify the condition code in <see cref="DynamicCondition"/>.
    /// </summary>
    public static class IsSkillUseableTemplate
    {
        private static string skillId = string.Empty;

        /// <summary>
        ///     Display the ImGui widget for adding the condition in <see cref="DynamicCondition"/>.
        /// </summary>
        /// <returns>
        ///     condition in string format if user press Add button otherwise empty string.
        /// </returns>
        public static string Add()
        {
            ImGui.Text("Skill");
            ImGui.SameLine();
            if (Core.States.InGameStateObject.CurrentAreaInstance.Player.TryGetComponent<Actor>(out var actor))
            {
                ImGuiHelper.IEnumerableComboBox("###SkillIdOfPlayer", actor.ActiveSkills.Keys, ref skillId);
            }
            else
            {
                ImGui.Text("NO_SKILL_FOUND, Please put skill in the Gem Socket first.");
                if (!string.IsNullOrEmpty(skillId))
                {
                    skillId = string.Empty;
                }
            }

            ImGui.SameLine();
            return ImGui.Button("Add##SkillUsable") ? $"PlayerSkillIsUseable.Contains(\"{skillId}\")" : string.Empty;
        }
    }
}
