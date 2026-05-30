// <copyright file="IPCore.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Plugin
{
    /// <summary>
    ///     Interface for creating plugins.
    /// </summary>
    internal interface IPCore
    {
        /// <summary>
        ///     Called at the init of the plugin to set
        ///     it's directory information.
        /// </summary>
        /// <param name="dllLocation">plugin dll directory.</param>
        public void SetPluginDllLocation(string dllLocation);

        /// <summary>
        ///     Called when the plugin is enabled by the user or when GameOverlay
        ///     starts and plugin is already enabled.
        /// </summary>
        /// <param name="isGameOpened">value indicating whether game is open or not.</param>
        public void OnEnable(bool isGameOpened);

        /// <summary>
        ///     Called when the plugin is disabled by the user.
        /// </summary>
        public void OnDisable();

        /// <summary>
        ///     Called to draw the plugin settings on the GameHelper Settings window.
        ///     Should use ImGui objects to draw the settings.
        /// </summary>
        public void DrawSettings();

        /// <summary>
        ///     Draws the plugin UI. This function isn't called when the plugin is disabled.
        /// </summary>
        public void DrawUI();

        /// <summary>
        ///     Called when it's time to save all the settings
        ///     related to the plugin on the file.
        ///     NOTE: Load settings function isn't provided as
        ///     it's expected the plugin will load the settings
        ///     in OnEnable function.
        /// </summary>
        public void SaveSettings();
    }
}