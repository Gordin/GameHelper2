// <copyright file="Profile.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager
{
    using System;
    using System.Collections.Generic;
    using System.Numerics;
    using GameHelper;
    using GameHelper.Utils;
    using ImGuiNET;
    using Newtonsoft.Json;

    /// <summary>
    ///     A class containing the rules for triggering the actions.
    /// </summary>
    public class Profile
    {
#nullable enable annotations
        /// <summary>
        ///  Contains List of all the available profiles. Can be null, so do check for it before using it.
        /// </summary>
        [JsonIgnore]
#pragma warning disable CS0414 // Field is assigned but never used (kept for compatibility with serialization model).
        private readonly Dictionary<string, Profile>? root = null;
#pragma warning restore CS0414
#nullable restore annotations

        private int contextWindowOn = -1;
        private int ruleIndexToDelete = -1;
        private int ruleIndexToSwap = -1;

        /// <summary>
        ///     Gets the rules to trigger the actions on.
        /// </summary>
        public List<Rule> Rules { get; } = new();

        /// <summary>
        ///     Initializes a new instance of the <see cref="Profile" /> class.
        /// </summary>
        [JsonConstructor]
        public Profile() { }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Profile" /> class from an existing one.
        /// </summary>
        /// <param name="other"></param>
        public Profile(Profile other)
        {
            this.Rules = new();
            foreach (var rule in other.Rules)
            {
                this.Rules.Add(new(rule));
            }
        }

        /// <summary>
        ///     A helper function to draw profile settings on ImGui window so that user can modify it.
        /// </summary>
        public void DrawSettings(string profileName, Dictionary<string, Profile> root)
        {
            if (ImGui.BeginTabBar("Profile Rules", ImGuiTabBarFlags.AutoSelectNewTabs | ImGuiTabBarFlags.Reorderable))
            {
                if (ImGui.TabItemButton("+", ImGuiTabItemFlags.Leading))
                {
                    this.Rules.Add(new Rule(this.Rules.Count.ToString()));
                }

                for (var i = 0; i < this.Rules.Count; i++)
                {
                    var currRule = this.Rules[i];
                    var shouldNotDelete = true;
                    if (!currRule.Enabled)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiHelper.Color(255, 75, 0, 255));
                    }

                    var isSelected = ImGui.BeginTabItem(
                        $"{currRule.Name}###Rule{i}",
                        ref shouldNotDelete,
                        ImGuiTabItemFlags.NoAssumedClosure);

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.GetWindowDrawList().AddRect(
                            ImGui.GetItemRectMin(),
                            ImGui.GetItemRectMax(),
                            ImGuiHelper.Color(255, 255, 0, 255));

                        if (ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                        {
                            this.contextWindowOn = i;
                        }
                    }

                    if (!currRule.Enabled)
                    {
                        ImGui.PopStyleColor();
                    }

                    if (ImGui.BeginDragDropSource())
                    {
                        this.ruleIndexToSwap = i;
                        ImGui.SetDragDropPayload("RuleIndex", IntPtr.Zero, 0);

                        ImGui.Text(currRule.Name);
                        ImGui.EndDragDropSource();
                    }

                    if (ImGui.BeginDragDropTarget())
                    {
                        ImGui.AcceptDragDropPayload("RuleIndex");
                        if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                        {
                            if (this.ruleIndexToSwap != -1)
                            {
                                this.SwapRules(this.ruleIndexToSwap, i);
                                this.ruleIndexToSwap = -1;
                            }
                            else
                            {
                                foreach (var profile in root)
                                {
                                    if (profile.Value.ruleIndexToSwap != -1)
                                    {
                                        this.Rules.Add(profile.Value.Rules[profile.Value.ruleIndexToSwap]);
                                        profile.Value.Rules.RemoveAt(profile.Value.ruleIndexToSwap);
                                        profile.Value.ruleIndexToSwap = -1;
                                    }
                                }
                            }
                        }

                        ImGui.EndDragDropTarget();
                    }

                    if (isSelected)
                    {
                        currRule.DrawSettings();
                        ImGui.EndTabItem();
                    }

                    if (this.contextWindowOn == i)
                    {
                        if (ImGui.BeginPopupContextWindow())
                        {
                            if (ImGui.Button("Clone"))
                            {
                                this.Rules.Add(new(this.Rules[this.contextWindowOn]));
                                this.contextWindowOn = -1;
                            }

                            ImGui.SameLine();
                            if (ImGui.Button("Cancel"))
                            {
                                this.contextWindowOn = -1;
                            }

                            ImGui.EndPopup();
                        }
                    }

                    if (!shouldNotDelete)
                    {
                        this.ruleIndexToDelete = i;
                        ImGui.OpenPopup("RuleDeleteConfirmation");
                    }
                }

                this.DrawConfirmationPopup();
                ImGui.EndTabBar();
            }
        }

        private void DrawConfirmationPopup()
        {
            ImGui.SetNextWindowPos(new Vector2(Core.Overlay.Size.Width / 3f, Core.Overlay.Size.Height / 3f));
            if (ImGui.BeginPopup("RuleDeleteConfirmation"))
            {
                ImGui.Text($"Do you want to delete rule with name: {this.Rules[this.ruleIndexToDelete].Name}?");
                ImGui.Separator();
                if (ImGui.Button("Yes",
                    new Vector2(ImGui.GetContentRegionAvail().X / 2f, ImGui.GetTextLineHeight() * 2)))
                {
                    var ruleToDelete = this.Rules[this.ruleIndexToDelete];
                    ImGui.SetTabItemClosed($"{ruleToDelete.Name}###Rule{this.ruleIndexToDelete}");
                    this.Rules.RemoveAt(this.ruleIndexToDelete);
                    this.ruleIndexToDelete = -1;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                if (ImGui.Button("No", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetTextLineHeight() * 2)))
                {
                    this.ruleIndexToDelete = -1;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
        }

        private void SwapRules(int index1, int index2)
        {
            if (index1 < 0 || index1 >= this.Rules.Count)
            {
                return;
            }

            if (index2 < 0 || index2 >= this.Rules.Count)
            {
                return;
            }

            (this.Rules[index1], this.Rules[index2]) = (this.Rules[index2], this.Rules[index1]);
        }
    }
}