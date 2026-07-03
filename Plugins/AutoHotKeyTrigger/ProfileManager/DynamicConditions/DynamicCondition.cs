// <copyright file="DynamicCondition.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.DynamicConditions
{
    using System;
    using System.Linq.Dynamic.Core;
    using System.Numerics;
    using AutoHotKeyTrigger;
    using AutoHotKeyTrigger.ProfileManager.Component;
    using GameHelper;
    using ImGuiNET;
    using Newtonsoft.Json;

    /// <summary>
    ///     A customizable condition allowing to specify when it is satisfied in user-supplied code
    /// </summary>
    public class DynamicCondition
    {
        private static readonly Vector4 ConditionSuccess = new(0, 255, 0, 255);
        private static readonly Vector4 ConditionFailure = new(255, 255, 0, 255);
        private static readonly Vector4 CodeCompileFailure = new(255, 0, 0, 255);
        private static readonly DynamicCondition ConfigurationInstance = new("");

        private static Lazy<DynamicConditionState> state = null!;

        [JsonProperty] private string conditionSource;
        [JsonProperty] private IComponent? component;

        private string? lastException;
        private Func<DynamicConditionState, bool>? func;
        private ulong exceptionCounter;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DynamicCondition" /> class.
        /// </summary>
        /// <param name="from">Source condition to create this condition from</param>
        public DynamicCondition(DynamicCondition from) : this(from.conditionSource, from.component?.Clone()) { }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DynamicCondition" /> class.
        /// </summary>
        /// <param name="conditionSource">The source code for the condition</param>
        public DynamicCondition(string conditionSource) : this(conditionSource, null) { }

        /// <summary>
        ///     Initializes a new instance of the <see cref="DynamicCondition" /> class.
        /// </summary>
        /// <param name="conditionSource">The source code for the condition</param>
        /// <param name="component">Component to add in the condition</param>
        [JsonConstructor]
        public DynamicCondition(string conditionSource, IComponent? component)
        {
            this.conditionSource = conditionSource;
            this.component = component;
            this.RebuildFunction();
        }

        static DynamicCondition()
        {
            UpdateState();
        }

        /// <summary>
        ///     Indicates that the shared dynamic condition state needs to be rebuilt
        /// </summary>
        public static void UpdateState()
        {
            state = new Lazy<DynamicConditionState>(() => new DynamicConditionState(Core.States.InGameStateObject));
        }

        /// <summary>
        ///     Draws the ImGui widget for adding the condition.
        /// </summary>
        /// <returns>
        ///     <see cref="DynamicConditions" /> if user wants to add the condition, otherwise null.
        /// </returns>
        public static DynamicCondition? Add()
        {
            ConfigurationInstance.ToImGui();
            ImGui.SameLine();
            if (ImGui.Button(AhkText.Label("button.add", "Add", "StatusEffect")) &&
                !string.IsNullOrEmpty(ConfigurationInstance.conditionSource))
            {
                return new DynamicCondition(ConfigurationInstance.conditionSource);
            }

            return null;
        }

        /// <summary>
        ///     Displays the condition as ImGui Widget.
        /// </summary>
        /// <param name="expand">display for modification or not</param>
        public void Display(bool expand)
        {
            this.ToImGui(expand);
            this.component?.Display(expand);
        }

        /// <inheritdoc/>
        public void Add(IComponent component)
        {
            this.component = component;
        }

        /// <summary>
        ///     Evaluate the condition
        /// </summary>
        /// <returns>result of the condition evaluation</returns>
        public bool Evaluate()
        {
            var isConditionValid = this.EvaluateInternal() ?? false;
            return this.component == null ? isConditionValid : this.component.execute(isConditionValid);
        }

        /// <summary>
        ///     Merge two conditions into one. Note that conditions with component can't be merged.
        /// </summary>
        /// <param name="otherCondition">other condition to merge with</param>
        /// <returns>true if successfully merged otherwise false.</returns>
        public bool Merge(DynamicCondition otherCondition)
        {
            if (this.component != null)
            {
                return false;
            }

            if (otherCondition.component != null)
            {
                return false;
            }

            if (string.IsNullOrEmpty(this.conditionSource) && string.IsNullOrEmpty(otherCondition.conditionSource))
            {
            }
            else if (string.IsNullOrEmpty(this.conditionSource))
            {
                this.conditionSource = otherCondition.conditionSource;
            }
            else if (string.IsNullOrEmpty (otherCondition.conditionSource))
            {
            }
            else
            {
                this.conditionSource = $"{this.conditionSource} && {otherCondition.conditionSource}";
            }

            this.RebuildFunction();
            return true;
        }

        private void RebuildFunction()
        {
            try
            {
                var parsingConfig = new ParsingConfig()
                {
                    AllowNewToEvaluateAnyType = true,
                    ResolveTypesBySimpleName = true,
                    AllowEqualsAndToStringMethodsOnObject = true
                };

                var types = new System.Collections.Generic.List<Type>
                {
                    typeof(GameHelper.RemoteEnums.Animation),
                    typeof(Interface.MonsterRarity),
                    typeof(Interface.MonsterNearbyZones),
                    typeof(ClickableTransparentOverlay.Win32.VK)
                };

                parsingConfig.CustomTypeProvider = new System.Linq.Dynamic.Core.CustomTypeProviders.DefaultDynamicLinqCustomTypeProvider(parsingConfig, types, true);

                var expression = DynamicExpressionParser.ParseLambda<DynamicConditionState, bool>(
                    parsingConfig,
                    false,
                    this.conditionSource);
                this.func = expression.Compile();
                this.lastException = null;
            }
            catch (Exception ex)
            {
                this.lastException = $"Expression compilation failed: {ex.Message}";
                this.func = null;
            }
        }

        private void ToImGui(bool expand = true)
        {
            if (!expand)
            {
                ImGui.Text(AhkText.T("condition.expression", "Expression:"));
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(255, 255, 0, 255), $"{this.conditionSource.Replace("\n", " ").Trim()}");
                ImGui.SameLine();
                ImGui.Text(AhkText.F("condition.errors", "(Errors {0})", this.exceptionCounter));
                return;
            }

            ImGui.TextWrapped(AhkText.T("condition.custom_help", "Type the expression in the following box to make custom condition."));
            if (ImGui.InputTextMultiline(
                "##dynamicConditionCode",
                ref this.conditionSource,
                10000,
                new Vector2(ImGui.GetContentRegionAvail().X / 1f, ImGui.GetFontSize() * 5)))
            {
                this.RebuildFunction();
            }

            var result = this.EvaluateInternal();
            ImGui.PushTextWrapPos(ImGui.GetContentRegionAvail().X);
            if (result == null)
            {
                ImGui.TextColored(CodeCompileFailure, this.lastException);
            }
            else if (this.Evaluate())
            {
                ImGui.TextColored(ConditionSuccess, AhkText.F("condition.yields_true", "Condition yeilds true. Exception Counter is {0}", this.exceptionCounter));
            }
            else
            {
                ImGui.TextColored(ConditionFailure, AhkText.F("condition.yields_false", "Condition yeilds false. Error Counter is {0}", this.exceptionCounter));
            }

            ImGui.PopTextWrapPos();
        }

        /// <summary>
        /// Evaluates the condition expression and returns true, false, null (for exception).
        /// Also, increments the total number of exceptions that occurs in this condition
        /// and stores the last exception message.
        ///
        /// Example Exceptions that can occur:
        ///   0/0
        ///   Flasks[-1].Charges > 0
        ///   MaxHp/CurrentHp where CurrentHp = 0
        ///
        /// NOTE: This function hides the last exception message if the exception
        /// is automatically resolved in the next call. This is because we want to
        /// be forgiving instead of requiring the user to add a bunch of preconditions
        /// to their code snippets.
        /// </summary>
        private bool? EvaluateInternal()
        {
            bool? result = null;
            if (this.func != null)
            {
                try
                {
                    result = this.func(state.Value);
                    this.lastException = string.Empty;
                }
                catch (Exception ex)
                {
                    this.lastException = $"Exception while evaluation ({this.exceptionCounter}): {ex.Message}";
                    this.exceptionCounter++;
                }
            }

            return result;
        }
    }
}
