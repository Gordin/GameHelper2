// <copyright file="PCore.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Plugin
{
    using GameHelper.Localization;
    using System;
    using System.Collections.Generic;

    /// <summary>
    ///     Interface for creating plugins.
    /// </summary>
    /// <typeparam name="TSettings">plugin's setting class name.</typeparam>
    public abstract class PCore<TSettings> : IPCore
        where TSettings : IPSettings, new()
    {
        /// <summary>
        ///     Gets or sets the plugin root directory folder.
        /// </summary>
        public string DllDirectory = null!;

        /// <summary>
        ///     Gets or sets the plugin settings.
        /// </summary>
        public TSettings Settings = new();

        private PluginLocalization? pluginText;

        /// <summary>
        ///     Gets localized text from the plugin-owned Localization directory.
        /// </summary>
        protected PluginLocalization PluginText => this.pluginText ??= new PluginLocalization(this.DllDirectory);

        /// <inheritdoc />
        public virtual string GetDescription() => this.PluginText.T("plugin.description", string.Empty);

        /// <inheritdoc />
        public virtual IReadOnlyCollection<string> ConflictsWith => Array.Empty<string>();

        /// <inheritdoc />
        public virtual int ConflictPriority => 0;

        /// <inheritdoc />
        public abstract void OnDisable();

        /// <inheritdoc />
        public abstract void OnEnable(bool isGameOpened);

        /// <inheritdoc />
        public abstract void DrawSettings();

        /// <inheritdoc />
        public abstract void DrawUI();

        /// <inheritdoc />
        public abstract void SaveSettings();

        /// <inheritdoc />
        public void SetPluginDllLocation(string dllLocation)
        {
            this.DllDirectory = dllLocation;
        }
    }
}
