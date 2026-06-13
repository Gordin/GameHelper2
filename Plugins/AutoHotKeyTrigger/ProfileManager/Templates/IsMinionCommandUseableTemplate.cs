// <copyright file="IsMinionCommandUseableTemplate.cs" company="PlaceholderCompany">
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
    ///     ImGui widget that helps the user add a "minion command useable" condition to a
    ///     <see cref="DynamicCondition"/>. Minion command skills carry their cooldown on the
    ///     summoned minion, so they never appear in the normal player skill list.
    /// </summary>
    public static class IsMinionCommandUseableTemplate
    {
        private static string commandName = string.Empty;

        /// <summary>
        ///     Display the ImGui widget for adding the condition in <see cref="DynamicCondition"/>.
        /// </summary>
        /// <returns>
        ///     condition in string format if user press Add button otherwise empty string.
        /// </returns>
        public static string Add()
        {
            ImGui.Text("Minion command");
            ImGui.SameLine();
            if (Core.States.InGameStateObject.CurrentAreaInstance.Player.TryGetComponent<Actor>(out var actor))
            {
                ImGuiHelper.IEnumerableComboBox("###MinionCommandName", actor.MinionCommandSkills.Keys, ref commandName);
            }
            else
            {
                ImGui.Text("NO_MINION_FOUND, summon a minion that has a command skill first.");
                if (!string.IsNullOrEmpty(commandName))
                {
                    commandName = string.Empty;
                }
            }

            ImGui.SameLine();
            return ImGui.Button("Add##MinionCommandUsable")
                ? $"MinionCommandSkillIsUsable.Contains(\"{commandName}\")"
                : string.Empty;
        }
    }
}
