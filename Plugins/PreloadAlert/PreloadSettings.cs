// <copyright file="PreloadSettings.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace PreloadAlert
{
    using System.Numerics;
    using GameHelper.Plugin;

    /// <summary>
    ///     Preload GUI settings.
    /// </summary>
    public sealed class PreloadSettings : IPSettings
    {
        /// <summary>
        ///     Background color of the preload alert window.
        /// </summary>
        public Vector4 BackgroundColor = new(Vector3.Zero, 0.8f);

        /// <summary>
        ///     Gets a value indicating whether to hide the Ui when not in the game or game in background.
        /// </summary>
        public bool EnableHideUi = true;

        /// <summary>
        ///     Gets a value indicating whether the preload alert window should hide when in town/hideout.
        /// </summary>
        public bool HideWhenInTownOrHideout = true;

        /// <summary>
        ///     Gets a value indicating whether the preload alert window should hide when empty.
        /// </summary>
        public bool HideWindowWhenEmpty = false;

        /// <summary>
        ///     Gets a value indicating whether the preload alert window is locked or not.
        /// </summary>
        public bool Locked = false;

        /// <summary>
        ///     Position of the preload alert window.
        /// </summary>
        public Vector2 Pos = Vector2.Zero;

        /// <summary>
        ///     Size of the preload alert window.
        /// </summary>
        public Vector2 Size = Vector2.Zero;

        /// <summary>
        ///     Gets a value indicating whether the user want to display time since last map spawn.
        /// </summary>
        public bool TimeSinceLastMapSpawn = true;
    }
}