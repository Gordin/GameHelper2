// <copyright file="NearbyMonsterInRangeTemplate.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.Templates
{
    using AutoHotKeyTrigger;
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions;
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions.Interface;
    using GameHelper.Utils;
    using ImGuiNET;
    using System;
    using System.Collections.Generic;

    /// <summary>
    ///     ImGui widget that helps user create a nearby-monster-count condition that uses an explicit
    ///     range (in the same units as the inner/outer circle, capped at the ~150 network bubble)
    ///     instead of the inner/outer circle, optionally filtered by whether the monsters are
    ///     currently damageable. Produces the <c>*MonsterCountInRange</c> functions in
    ///     <see cref="DynamicConditionState"/>.
    /// </summary>
    public static class NearbyMonsterInRangeTemplate
    {
        private const int MinRange = 1;
        private const int MaxRange = 150;

        private static readonly List<string> SupportedOperatorTypes = new()
        {
            ">",
            ">=",
            "<",
            "<="
        };

        private static readonly Dictionary<string, string> CountTypes = new()
        {
            { "Damageable", "DamageableMonsterCountInRange" },
            { "Undamageable", "UndamageableMonsterCountInRange" },
            { "Any (damageable or not)", "MonsterCountInRange" },
            { "Corpses (dead)", "CorpseCountInRange" }
        };

        private static string selectedOperator = ">";
        private static string selectedCountType = "Damageable";
        private static int counter = 0;
        private static int range = 70;
        private static MonsterRarity selectedRarity = MonsterRarity.Normal;

        /// <summary>
        ///     Display the ImGui widget for adding the condition in <see cref="DynamicCondition"/>.
        /// </summary>
        /// <returns>
        ///     condition in string format if user press Add button otherwise empty string.
        /// </returns>
        public static string Add()
        {
            ImGui.Text(AhkText.T("template.player_has", "Player has"));
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 3);
            ImGuiHelper.IEnumerableComboBox("##NearbyMonsterInRangeOperator", SupportedOperatorTypes, ref selectedOperator);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 5);
            ImGui.InputInt("##NearbyMonsterInRangeCounter", ref counter);

            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 14);
            ImGuiHelper.IEnumerableComboBox("##NearbyMonsterInRangeCountType", CountTypes.Keys, ref selectedCountType);
            ImGui.SameLine();
            ImGui.Text(AhkText.T("template.monsters_of", "monsters of"));

            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 10);
            if (ImGui.BeginCombo(AhkText.Label("template.rarity", "rarity", "nearby_monster_in_range_template"), $"{selectedRarity}"))
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

            ImGui.Text(AhkText.T("template.within_range", "within range"));
            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 12);
            ImGui.SliderInt("##NearbyMonsterInRangeDistance", ref range, MinRange, MaxRange);
            range = Math.Clamp(range, MinRange, MaxRange);
            ImGuiHelper.ToolTip(AhkText.T("template.range.tooltip", "Distance is in the same units as the inner/outer circle (default outer circle is 70). " +
                                "Capped at ~150 (the network bubble); monsters beyond that are not loaded by the game."));

            if (ImGui.Button(AhkText.Label("button.add", "Add", "NearbyMonsterInRangeAdd")))
            {
                if (selectedRarity != 0 && CountTypes.TryGetValue(selectedCountType, out var function))
                {
                    return $"{function}(MonsterRarity.{selectedRarity}: {range}) {selectedOperator} {counter}".
                        Replace(", ", "|MonsterRarity.").
                        Replace(":", ",");
                }
            }

            return string.Empty;
        }
    }
}
