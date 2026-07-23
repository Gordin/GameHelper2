// <copyright file="Rule.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Numerics;
    using AutoHotKeyTrigger;
    using GameHelper.Utils;
    using ImGuiNET;
    using Newtonsoft.Json;
    using AutoHotKeyTrigger.ProfileManager.Enums;
    using AutoHotKeyTrigger.ProfileManager.Component;
    using ClickableTransparentOverlay.Win32;
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions;
    using AutoHotKeyTrigger.ProfileManager.Templates;
    using Coroutine;

    /// <summary>
    ///     Abstraction for the rule condition list
    /// </summary>
    public class Rule
    {
        private int conditionToModify = -1;
        private int conditionIndexToSwap = -1;
        private static bool expand = false;
        private ConditionType newConditionType = ConditionType.AILMENT;
        private readonly Stopwatch cooldownStopwatch = Stopwatch.StartNew();
        private bool isExecuting = false;

        [JsonProperty("Conditions", NullValueHandling = NullValueHandling.Ignore)]
        private readonly List<DynamicCondition> conditions = new();

        [JsonProperty] private float delayBetweenRuns = 0;

        /// <summary>
        ///     Enable/Disable the rule.
        /// </summary>
        public bool Enabled;

        /// <summary>
        ///     User friendly name given to a rule.
        /// </summary>
        public string Name;

        /// <summary>
        ///     Rule key to press on success.
        /// </summary>
        public VK Key;

        /// <summary>
        ///     Key action mode (Tap or Hold).
        /// </summary>
        [JsonProperty]
        public KeyActionMode ActionMode = KeyActionMode.Tap;

        /// <summary>
        ///     Duration in seconds to hold key when ActionMode is Hold.
        /// </summary>
        [JsonProperty]
        public float HoldDuration = 0.35f;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Rule" /> class.
        /// </summary>
        /// <param name="name"></param>
        [JsonConstructor]
        public Rule(string name)
        {
            this.Name = name;
        }

        /// <summary>
        ///     Initializes a new instace of the <see cref="Rule"/> class by cloning existing one
        /// </summary>
        /// <param name="other"></param>
        public Rule(Rule other)
        {
            this.delayBetweenRuns = other.delayBetweenRuns;
            this.Enabled = false;
            this.Name = $"{other.Name}1";
            this.Key = other.Key;
            this.ActionMode = other.ActionMode;
            this.HoldDuration = other.HoldDuration;
            this.conditions = new();
            foreach (var condition in other.conditions)
            {
                this.conditions.Add(new(condition));
            }
        }

        /// <summary>
        ///     Creates default rules that are only valid for flasks on the newly created character.
        /// </summary>
        /// <returns>List of rules that are valid for newly created player.</returns>
        public static Rule[] CreateDefaultRules()
        {
            var rules = new Rule[2];
            rules[0] = new("LifeFlask");
            rules[0].Enabled = true;
            rules[0].Key = VK.KEY_1;
            rules[0].conditions.Add(new DynamicCondition($"PlayerVitals.HP.Percent <= 80 && Flasks.Flask1.IsUsable && !Flasks.Flask1.Active"));

            rules[1] = new($"ManaFlask");
            rules[1].Enabled = true;
            rules[1].Key = VK.KEY_2;
            rules[1].conditions.Add(new DynamicCondition($"PlayerVitals.MANA.Percent <= 30 && Flasks.Flask2.IsUsable && !Flasks.Flask2.Active"));

            return rules;
        }

        /// <summary>
        ///     Clears the list of conditions
        /// </summary>
        public void Clear()
        {
            this.conditions.Clear();
        }

        /// <summary>
        ///     Displays the rule settings
        /// </summary>
        public void DrawSettings()
        {
            ImGui.Checkbox(AhkText.Label("rule.enable", "Enable", "AhkRuleEnable"), ref this.Enabled);
            ImGui.InputText(AhkText.Label("rule.name", "Name", "AhkRuleName"), ref this.Name, 100);
            var tmpKey = this.Key;
            if (ImGuiHelper.NonContinuousEnumComboBox(AhkText.Label("rule.key", "Key", "AhkRuleKey"), ref tmpKey))
            {
                this.Key = tmpKey;
            }

            var tmpActionMode = this.ActionMode;
            if (ImGuiHelper.EnumComboBox(AhkText.Label("rule.action_mode", "Action Mode", "AhkRuleActionMode"), ref tmpActionMode))
            {
                this.ActionMode = tmpActionMode;
            }

            if (this.ActionMode == KeyActionMode.Hold)
            {
                ImGui.DragFloat(AhkText.Label("rule.hold_duration", "Hold Duration (seconds)", "AhkRuleHoldDuration"), ref this.HoldDuration, 0.01f, 0.01f, 10.0f);
            }

            this.DrawCooldownWidget();
            this.DrawAddNewCondition();
            this.DrawExistingConditions();
        }

        /// <summary>
        ///     Checks the rule conditions and presses or holds its key if conditions are satisfied
        /// </summary>
        /// <param name="logger"></param>
        public void Execute(Action<string> logger)
        {
            if (this.Enabled && !this.isExecuting && this.Evaluate())
            {
                if (this.ActionMode == KeyActionMode.Tap)
                {
                    if (MiscHelper.KeyUp(this.Key))
                    {
                        logger($"{this.Key} is pressed.");
                        this.cooldownStopwatch.Restart();
                    }
                }
                else if (this.ActionMode == KeyActionMode.Hold)
                {
                    CoroutineHandler.Start(this.RunHoldCoroutine(logger));
                }
            }
        }

        private IEnumerator<Coroutine.Wait> RunHoldCoroutine(Action<string> logger)
        {
            this.isExecuting = true;
            try
            {
                while (!MiscHelper.KeyHoldDown(this.Key, isRepeat: false))
                {
                    if (!this.Enabled)
                    {
                        yield break;
                    }

                    yield return new Coroutine.Wait(0.01d);
                }

                logger($"{this.Key} is held down for {this.HoldDuration:F2}s...");
                var holdEndTime = DateTime.UtcNow.AddSeconds(this.HoldDuration);

                while (DateTime.UtcNow < holdEndTime)
                {
                    if (!this.Enabled)
                    {
                        break;
                    }

                    MiscHelper.KeyHoldDown(this.Key, isRepeat: true);
                    yield return new Coroutine.Wait(0.02d);
                }

                while (!MiscHelper.KeyHoldUp(this.Key))
                {
                    yield return new Coroutine.Wait(0.01d);
                }

                logger($"{this.Key} released after hold.");
                this.cooldownStopwatch.Restart();
            }
            finally
            {
                this.isExecuting = false;
            }
        }

        /// <summary>
        ///     Adds a new condition
        /// </summary>
        /// <param name="conditionType"></param>
        private void Add(ConditionType conditionType)
        {
            if (conditionType == ConditionType.DYNAMIC)
            {
                var condition = DynamicCondition.Add();
                if (condition != null)
                {
                    this.conditions.Add(condition);
                }
            }
            else
            {
                var sourceString = TemplateHelper.EnumToTemplate(conditionType);
                if (!string.IsNullOrEmpty(sourceString))
                {
                    this.conditions.Add(new(sourceString));
                }
            }
        }

        private void ModifyExistingCondition(ConditionType conditionType, int index)
        {
            if (conditionType == ConditionType.DYNAMIC)
            {
                var condition = DynamicCondition.Add();
                if (condition != null)
                {
                    this.conditions[index] = condition;
                }
            }
            else
            {
                var sourceString = TemplateHelper.EnumToTemplate(conditionType);
                if (!string.IsNullOrEmpty(sourceString))
                {
                    this.conditions[index] = new(sourceString);
                }
            }
        }

        /// <summary>
        ///     Removes a condition at a specific index.
        /// </summary>
        /// <param name="index">index of the condition to remove.</param>
        private void RemoveAt(int index)
        {
            this.conditions.RemoveAt(index);
        }

        /// <summary>
        ///     Swap two conditions.
        /// </summary>
        /// <param name="i">index of the condition to swap.</param>
        /// <param name="j">index of the condition to swap.</param>
        private void Swap(int i, int j)
        {
            (this.conditions[i], this.conditions[j]) = (this.conditions[j], this.conditions[i]);
        }

        /// <summary>
        ///     Checks the specified conditions, shortcircuiting on the first unsatisfied one
        /// </summary>
        /// <returns>true if all the rules conditions are true otherwise false.</returns>
        private bool Evaluate()
        {
            if (this.cooldownStopwatch.Elapsed.TotalSeconds > this.delayBetweenRuns)
            {
                if (this.conditions.TrueForAll(x => x.Evaluate()))
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawCooldownWidget()
        {
            ImGui.DragFloat(AhkText.Label("rule.cooldown", "Cooldown time (seconds)", "DelayTimerConditionDelay"), ref this.delayBetweenRuns, 0.1f, 0.0f, 30.0f);
            if (this.delayBetweenRuns > 0)
            {
                var cooldownTimeFraction = this.delayBetweenRuns <= 0f ? 1f :
                    MathF.Min((float)this.cooldownStopwatch.Elapsed.TotalSeconds, this.delayBetweenRuns) / this.delayBetweenRuns;
                ImGui.PushStyleColor(ImGuiCol.Text, ImGuiHelper.Color(200, 0, 200, 255));
                ImGui.ProgressBar(
                    (float)cooldownTimeFraction,
                    Vector2.Zero,
                    cooldownTimeFraction < 1f ? AhkText.F("rule.cooling", "Cooling {0:0}%", cooldownTimeFraction * 100f) : AhkText.T("rule.ready", "Ready"));
                ImGui.PopStyleColor();
            }
        }

        private void DrawExistingConditions()
        {
            var isOpened = ImGui.TreeNodeEx(AhkText.Title("rule.existing_conditions", "Existing Conditions (?)", "AhkExistingConditions"), ImGuiTreeNodeFlags.DefaultOpen);
            ImGuiHelper.ToolTip(AhkText.T("rule.existing_conditions.tooltip", "All of the conditions needs to be true. Conditions can be moved up and " +
                "down via drag and drop when not expanded."));
            if (isOpened)
            {
                ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X / 6);
                for (var i = 0; i < this.conditions.Count; i++)
                {
                    ImGui.PushID($"ConditionNo{i}");
                    if (i != 0)
                    {
                        ImGui.Separator();
                    }

                    ImGui.PushStyleColor(ImGuiCol.Button, 0);
                    if (ImGui.ArrowButton("###ExpandHideButton", (expand) ? ImGuiDir.Down : ImGuiDir.Right))
                    {
                        expand = !expand;
                    }

                    ImGui.PopStyleColor();
                    ImGui.SameLine();
                    if (expand && ImGui.SmallButton(AhkText.Label("button.delete", "Delete", "AhkDeleteCondition")))
                    {
                        this.RemoveAt(i);
                        ImGui.PopID();
                        break;
                    }

                    ImGui.SameLine();
                    if (expand && ImGui.SmallButton(AhkText.Label("button.add_component", "Add Component", "AhkAddComponent")))
                    {
                        this.conditions[i].Add(new AutoHotKeyTrigger.ProfileManager.Component.Wait(0));
                    }

                    ImGui.SameLine();
                    if (expand && ImGui.SmallButton(AhkText.Label("button.edit_template", "Edit Via Template", "AhkEditTemplate")))
                    {
                        this.conditionToModify = i;
                        ImGui.OpenPopup("ModifyExistingConditionPopUp");
                    }

                    if (ImGui.BeginPopup("ModifyExistingConditionPopUp"))
                    {
                        ImGui.Text(AhkText.T("popup.click_outside", "NOTE: Click outside this popup to close it."));
                        ImGui.Text(AhkText.T("popup.overwrite_condition", "NOTE: This Overwrites the whole condition."));
                        ImGuiHelper.EnumComboBox(AhkText.Label("condition.type", "Condition Type", "AhkModifyConditionType"), ref this.newConditionType);
                        ImGui.Separator();
                        this.ModifyExistingCondition(this.newConditionType, this.conditionToModify);
                        ImGui.EndPopup();
                    }

                    ImGui.BeginGroup();
                    this.conditions[i].Display(expand);
                    ImGui.EndGroup();
                    if (!expand)
                    {
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGuiHelper.Color(255, 255, 0, 255));
                        }

                        if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceAllowNullID))
                        {
                            this.conditionIndexToSwap = i;
                            ImGui.SetDragDropPayload("ConditionIndex", IntPtr.Zero, 0);
                            ImGui.EndDragDropSource();
                        }

                        if (ImGui.BeginDragDropTarget())
                        {
                            ImGui.AcceptDragDropPayload("ConditionIndex");
                            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                            {
                                this.Swap(this.conditionIndexToSwap, i);
                            }

                            ImGui.EndDragDropTarget();
                        }

                        ImGui.SameLine();
                        var evaluationResult = this.conditions[i].Evaluate();
                        ImGui.TextColored(
                            evaluationResult ? new Vector4(0, 1, 0, 1) : new Vector4(1, 0, 0, 1),
                            evaluationResult ? AhkText.T("condition.true", "(true)") : AhkText.T("condition.false", "(false)"));
                    }

                    ImGui.PopID();
                }

                ImGui.PopItemWidth();
                ImGui.TreePop();
            }
        }

        private void DrawAddNewCondition()
        {
            if (ImGui.Button(AhkText.Label("button.add_condition", "Add New Condition", "AhkAddNewCondition")))
            {
                ImGui.OpenPopup("AddNewConditionPopUp");
            }

            ImGui.SameLine();
            if (ImGui.Button(AhkText.Label("button.clear_conditions", "Clear All Conditions", "AhkClearConditions")))
            {
                this.Clear();
            }

            ImGui.SameLine();
            var isClicked = ImGui.Button(AhkText.Label("button.merge_conditions", "Merge All conditions", "AhkMergeConditions"));
            ImGuiHelper.ToolTip(AhkText.T("button.merge_conditions.tooltip", "This merges all the conditions into one so you " +
                "can easily copy paste it into multiple rules. Conditions with " +
                "component can not be merged so this button will create a new " +
                "condition when it encounter a component attached to the condition."));
            if (isClicked)
            {
                var newConditions = new List<DynamicCondition>();
                foreach (var condition in this.conditions)
                {
                    if (newConditions.Count == 0)
                    {
                        newConditions.Add(condition);
                        continue;
                    }

                    if (!newConditions.Last().Merge(condition))
                    {
                        newConditions.Add(condition);
                    }
                }

                this.conditions.Clear();
                this.conditions.AddRange(newConditions);
            }

            if (ImGui.BeginPopup("AddNewConditionPopUp"))
            {
                ImGui.Text(AhkText.T("popup.click_outside", "NOTE: Click outside this popup to close it."));
                ImGuiHelper.EnumComboBox(AhkText.Label("condition.type", "Condition Type", "AhkAddConditionType"), ref this.newConditionType);
                ImGui.Separator();
                this.Add(this.newConditionType);
                ImGui.EndPopup();
            }
        }
    }
}
