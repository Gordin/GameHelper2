// <copyright file="TemplateHelper.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using AutoHotKeyTrigger.ProfileManager.Enums;
using System;

namespace AutoHotKeyTrigger.ProfileManager.Templates
{
    /// <summary>
    ///     helps with Template related functions.
    /// </summary>
    public static class TemplateHelper
    {
        /// <summary>
        ///     Converts <see cref="ConditionType"/> to their template.
        /// </summary>
        /// <param name="conditionType">Condition type to convert</param>
        /// <returns>string for dynamic condition if user press the Add button otherwise null.</returns>
        /// <exception cref="Exception">if condition type is invalid.</exception>
        public static string EnumToTemplate(ConditionType conditionType)
        {
            return conditionType switch
            {
                ConditionType.VITALS => VitalTemplate.Add(),
                ConditionType.ANIMATION => AnimationTemplate.Add(),
                ConditionType.STATUS_EFFECT => StatusEffectTemplate.Add(),
                ConditionType.FLASK_IS_ACTIVE => FlaskActiveTemplate.Add(),
                ConditionType.FLASK_CHARGES => FlaskChargesTemplate.Add(),
                ConditionType.AILMENT => AilmentTemplate.Add(),
                ConditionType.FLASK_IS_USEABLE => FlaskIsUseableTemplate.Add(),
                ConditionType.IS_SKILL_USEABLE => IsSkillUseableTemplate.Add(),
                ConditionType.DEPLOYED_OBJECT_COUNT => DeployedObjectTemplate.Add(),
                ConditionType.NEARBY_MONSTER_COUNT => NearbyMonsterTemplate.Add(),
                ConditionType.ON_KEY_PRESSED_FOR_ACTION => IsKeyPressedTemplate.Add(),
                ConditionType.WEAPON_SET_ACTIVE => WeaponSetActiveTemplate.Add(),
                ConditionType.SHAPESHIFTED => ShapeShiftedTemplate.Add(),
                _ => throw new Exception($"{conditionType} not implemented in ConditionHelper class")
            };
        }
    }
}
