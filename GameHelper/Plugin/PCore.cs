// <copyright file="PCore.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Plugin
{
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