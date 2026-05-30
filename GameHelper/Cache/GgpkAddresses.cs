// <copyright file="GgpkAddresses.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Cache
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using ImGuiNET;
    using Coroutine;
    using GameHelper.Utils;

    /// <summary>
    ///    The purpose of this class is to cache GGPK data on their addresses.
    ///    This will help us save multiple memory reads since normally
    ///    GGPK data requires (minimum) 2+ reads per GGPK row address.
    /// </summary>
    internal class GgpkAddresses<T>
    {
        private readonly ConcurrentDictionary<IntPtr, T> cache;

        /// <summary>
        ///     Initializes a new instance of the <see cref="GgpkAddresses{T}"/> class.
        /// </summary>
        public GgpkAddresses()
        {
            this.cache = new();
            CoroutineHandler.Start(this.OnGameClose());
            CoroutineHandler.Start(this.OnAreaChange());
        }

        /// <summary>
        ///     Adds a key/value pair to the cache if the key doesn't already exist.
        /// </summary>
        /// <param name="key">The key of the element to add.</param>
        /// <param name="valueFactory">The function used to generate a value for the key</param>
        /// <returns>
        /// The value for the key.  This will be either the existing value for the key if the
        /// key is already in the dictionary, or the new value for the key as returned by valueFactory
        /// if the key was not in the dictionary.
        /// </returns>
        public T AddOrGetExisting(IntPtr key, Func<IntPtr,T> valueFactory) => key == IntPtr.Zero ?
            throw new Exception($"Object tried to load 0x{key.ToInt64():X} in the cache.") :
            this.cache.GetOrAdd(key, valueFactory);

        /// <summary>
        ///     Draws a widget for this class.
        /// </summary>
        public void ToImGui()
        {
            ImGui.Text($"Total Size: {this.cache.Count}");
            if (ImGui.TreeNode("GGPK Addresses"))
            {
                foreach(var (key, value) in this.cache)
                {
                    var addr = $"0x{key.ToInt64():X}";
                    ImGuiHelper.DisplayTextAndCopyOnClick($"{addr} - {value}", addr);
                }

                ImGui.TreePop();
            }
        }

        private IEnumerable<Wait> OnGameClose()
        {
            while (true)
            {
                yield return new(CoroutineEvents.GameHelperEvents.OnClose);
                this.cache.Clear();
            }
        }

        private IEnumerable<Wait> OnAreaChange()
        {
            while (true)
            {
                yield return new(CoroutineEvents.RemoteEvents.AreaChanged);
                this.cache.Clear();
            }
        }
    }
}