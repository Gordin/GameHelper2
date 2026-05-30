// <copyright file="AutoHotKeyTriggerSettings.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger
{
    using System.Collections.Generic;
    using GameHelper.Plugin;
    using AutoHotKeyTrigger.ProfileManager;
    using ClickableTransparentOverlay.Win32;
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions;
    using Newtonsoft.Json;

    /// <summary>
    ///     <see cref="AutoHotKeyTrigger" /> plugin settings class.
    /// </summary>
    public sealed class AutoHotKeyTriggerSettings : IPSettings
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="AutoHotKeyTriggerSettings" /> class.
        /// </summary>
        /// <param name="AutoQuitCondition"><see cref="DynamicCondition"/> AutoQuitCondition to use.</param>
        [JsonConstructor]
        public AutoHotKeyTriggerSettings(DynamicCondition AutoQuitCondition)
            : this()
        {
            this.AutoQuitCondition = new(AutoQuitCondition);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="AutoHotKeyTriggerSettings" /> class.
        /// </summary>
        public AutoHotKeyTriggerSettings()
        {
            this.EnableAutoQuit = false;
            this.AutoQuitCondition = new("PlayerVitals.HP.Percent <= 30");
            this.AutoQuitKey = VK.F11;
            this.EnableAutoQuitKey = false;
            this.Profiles = new();
            this.CurrentProfile = string.Empty;
            this.DebugMode = false;
            this.ShouldRunInHideout = false;
            this.DumpStatusEffectOnMe = VK.F10;
        }

        /// <summary>
        ///    Gets a value indicating whether to enable or disable the auto-quit feature.
        /// </summary>
        public bool EnableAutoQuit;

        /// <summary>
        ///     Condition on which user want to auto-quit.
        /// </summary>
        public DynamicCondition AutoQuitCondition;

        /// <summary>
        ///     Gets a value indicating whether to enable or disable the auto-quit key or not.
        /// </summary>
        public bool EnableAutoQuitKey;

        /// <summary>
        ///     Gets a key which allows the user to manually quit the game Connection.
        /// </summary>
        public VK AutoQuitKey;

        /// <summary>
        ///     Gets all the profiles containing rules on when to perform the action.
        /// </summary>
        public readonly Dictionary<string, Profile> Profiles;

        /// <summary>
        ///     Gets the currently selected profile.
        /// </summary>
        public string CurrentProfile;

        /// <summary>
        ///     Gets a value indicating weather the debug mode is enabled or not.
        /// </summary>
        public bool DebugMode;

        /// <summary>
        ///     Gets a value indicating weather this plugin should work in hideout or not.
        /// </summary>
        public bool ShouldRunInHideout;

        /// <summary>
        ///     Gets a value indicating weather user wants to dump the player
        ///     status effect or not.
        /// </summary>
        public VK DumpStatusEffectOnMe;
    }
}