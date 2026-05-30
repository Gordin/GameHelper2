// <copyright file="NearbyMonsterTemplate.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.Templates
{
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions;
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions.Interface;
    using GameHelper.Utils;
    using ImGuiNET;
    using System;
    using System.Collections.Generic;

    /// <summary>
    ///     ImGui widget that helps user modify the condition code in <see cref="DynamicCondition"/>.
    /// </summary>
    public static class NearbyMonsterTemplate
    {
        private static readonly List<string> SupportedOperatorTypes = new()
        {
            ">",
            ">=",
            "<",
            "<="
        };

        private static string selectedOperator = ">";
        private static bool friendly = false;
        private static int counter = 0;
        private static MonsterRarity selectedRarity = MonsterRarity.Normal;
        private static MonsterNearbyZones zones = MonsterNearbyZones.OuterCircle;

        /// <summary>
        ///     Display the ImGui widget for adding the condition in <see cref="DynamicCondition"/>.
        /// </summary>
        /// <returns>
        ///     condition in string format if user press Add button otherwise empty string.
        /// </returns>
        public static string Add()
        {
            ImGui.Checkbox("Enable friendly monster condition##friendly_nearby_monster_template", ref friendly);
            ImGui.Text("Player has");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 3);
            ImGuiHelper.IEnumerableComboBox("##NearbyMonsterOperator", SupportedOperatorTypes, ref selectedOperator);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
            ImGui.InputInt(friendly ? "friendly monsters" : "monsters", ref counter);
            if (!friendly)
            {
                ImGui.SameLine();
                ImGui.Text("near them of");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 10);
                if (ImGui.BeginCombo($"rarity##nearby_monster_template", $"{selectedRarity}"))
                {
                    foreach (var rarity in Enum.GetValues<MonsterRarity>())
                    {
                        var IsSelected = selectedRarity.HasFlag(rarity);
                        if (ImGui.Checkbox($"{rarity}", ref IsSelected))
                        {
                            if (IsSelected)
                            {
                                selectedRarity |= rarity;
                            }
                            else
                            {
                                selectedRarity &= ~rarity;
                            }
                        }
                    }

                    ImGui.EndCombo();
                }
            }

            ImGui.SameLine();
            ImGui.Text("in");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 8);
            ImGuiHelper.EnumComboBox(".##NearbyZoneSelector", ref zones);

            if (ImGui.Button("Add##NearbyMonsterAdd"))
            {
                if (friendly)
                {
                    return $"{zones}FriendlyMonsterCount {selectedOperator} {counter}";
                }
                else if (selectedRarity != 0)
                {
                    return $"MonsterCount(MonsterRarity.{selectedRarity}: MonsterNearbyZones.{zones}) {selectedOperator} {counter}".
                        Replace(", ", "|MonsterRarity.").
                        Replace(":", ",");
                }
            }

            return string.Empty;
        }
    }
}
