// <copyright file="WorldDrawingSettings.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace WorldDrawing
{
    using GameHelper.Plugin;
    using System.Numerics;

    /// <summary>
    /// <see cref="WorldDrawing"/> plugin settings class.
    /// </summary>
    public sealed class WorldDrawingSettings : IPSettings
    {
        /// <summary>
        ///     Gets a value indicating when user want to show abyss path
        /// </summary>
        public bool OnlyShowAbyssPathWhenLargeMapHidden = false;

        /// <summary>
        ///     Gets the config related to Abyss Path drawing feature.
        /// </summary>
        public (bool enable, float width, Vector4 color)[] AbyssPath = new[]
        {
            (true, 1f, Vector4.One),
            (true, 1f, new(1f, 0f, 0f, 1f)),
            (true, 1f, new(0f, 1f, 0f, 1f)),
            (true, 1f, new(0f, 0f, 1f, 1f)),
            (true, 1f, new(0f, 1f, 1f, 1f)),
            (true, 1f, new(1f, 0f, 1f, 1f)),
            (true, 1f, new(1f, 1f, 0f, 1f)),
            (true, 1f, new(0f, 0.5f, 0.5f, 1f)),
            (true, 1f, new(0.5f, 0.5f, 0f, 1f)),
            (true, 1f, new(0.25f, 0f, 0.5f, 1f)),
        };
    }
}
