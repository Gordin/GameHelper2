// <copyright file="HealthBars.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>


namespace HealthBars
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using GameHelper;

    /// <summary>
    ///     Loads and cleans up the textures.
    /// </summary>
    public class TextureLoader
    {
        private readonly Dictionary<string, (IntPtr, int w, int h)> loadedTextures = new();

        /// <summary>
        ///     Gets all the keys of the loaded textures.
        /// </summary>
        public List<string> TextureKeys => new(this.loadedTextures.Keys);

        /// <summary>
        ///     Gets the total number of loaded textures.
        /// </summary>
        public int TotalTexturesLoaded => this.loadedTextures.Count;

        /// <summary>
        ///     Unloads all the textures.
        /// </summary>
        /// <param name="texturesPath">path to texture folder.</param>
        public void cleanup(string texturesPath)
        {
            foreach (var filename in this.loadedTextures.Keys)
            {
                var pathname = Path.Join(texturesPath, filename);
                if (Core.Overlay.RemoveImage(pathname))
                {
                    this.loadedTextures.Remove(filename);
                }
            }
        }

        /// <summary>
        ///     Loads all the textures.
        /// </summary>
        /// <param name="texturesPath">Path to texture folder.</param>
        public void Load(string texturesPath)
        {
            if (Directory.Exists(texturesPath))
            {
                foreach (var pathname in Directory.EnumerateFiles(texturesPath))
                {
                    var filename = Path.GetFileName(pathname);
                    Core.Overlay.AddOrGetImagePointer(pathname, false, out var handle, out var w, out var h);
                    this.loadedTextures.Add(filename, (handle, (int)w, (int)h));
                }
            }
        }

        /// <summary>
        ///     Gets the texture along with width and height.
        /// </summary>
        /// <param name="key">texture identifier</param>
        /// <returns></returns>
        public (IntPtr, int w, int h) GetTexture(string key) => this.loadedTextures[key];
    }
}