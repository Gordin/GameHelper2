// <copyright file="UiElementParents.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>


namespace GameHelper.Cache
{
    using System;
    using System.Collections.Generic;
    using Coroutine;
    using GameHelper.RemoteObjects.UiElement;
    using GameHelper.CoroutineEvents;
    using ImGuiNET;
    using GameHelper.RemoteEnums;
    using System.Threading.Tasks;

    internal class UiElementParents
    {
        private readonly string name;
        private readonly UiElementParents grandparent;
        private readonly GameStateTypes ownerState1;
        private readonly GameStateTypes ownerState2;
        private readonly Dictionary<IntPtr, UiElementBase> cache;

        /// <summary>
        ///     Initializes a new instance of the <see cref="UiElementParents" /> class.
        /// </summary>
        /// <param name="grandparent">other Ui Element cache to check</param>
        /// <param name="ownerStateA"><see cref="GameStateTypes"/> on which cache shouldn't be cleaned</param>
        /// <param name="ownerStateB"><see cref="GameStateTypes"/> on which cache shouldn't be cleaned</param>
        /// <param name="name">human friendly name to give to this cache</param>
        public UiElementParents(UiElementParents grandparent, GameStateTypes ownerStateA, GameStateTypes ownerStateB, string name)
        {
            this.name = name;
            this.ownerState1 = ownerStateA;
            this.ownerState2 = ownerStateB;
            this.cache = new();
            this.grandparent = grandparent;
            CoroutineHandler.Start(this.OnGameClose());
            CoroutineHandler.Start(this.OnStateChange());
        }

        /// <summary>
        ///     Adds a Parent UiElement to the cache if the key doesn't already exist.
        /// </summary>
        /// <param name="address">address pointing to the parent UiElement.</param>
        public void AddIfNotExists(IntPtr address)
        {
            if (address != IntPtr.Zero)
            {
                if (this.grandparent != null && this.grandparent.cache.ContainsKey(address))
                {
                }
                else if (!this.cache.ContainsKey(address))
                {
                    try
                    {
                        this.cache.Add(address, new(address, this));
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Failed to add the UiElement Parent in the cache. 0x{address.ToInt64():X} due to {e}");
                    }
                }
            }
        }

        public UiElementBase GetParent(IntPtr address)
        {
            if (address == IntPtr.Zero)
            {
                throw new ArgumentException("address must not be zero", nameof(address));
            }

            if (this.cache != null && this.cache.TryGetValue(address, out var parent))
            {
                return parent;
            }
            else if (this.grandparent != null
                && this.grandparent.cache != null
                && this.grandparent.cache.TryGetValue(address, out var gParent))
            {
                return gParent;
            }
            else
            {
                throw new KeyNotFoundException(
                    $"UiElementBase with address 0x{address.ToInt64():X} not found in caches.");
            }
        }

        public void UpdateAllParentsParallel()
        {
            Parallel.ForEach(this.cache, (data) =>
            {
                try
                {
                    data.Value.Address = data.Key;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to update the UiElement Parent in the cache. 0x{data.Key.ToInt64():X} due to {e}");
                }
            });
        }

        public void Clear() => this.cache.Clear();

        public void ToImGui()
        {
            ImGui.Text($"Total Size: {this.cache.Count}");
            if (ImGui.TreeNode($"{this.name} Parent UiElements"))
            {
                foreach (var (key, value) in this.cache)
                {
                    if (ImGui.TreeNode($"0x{key.ToInt64():X}"))
                    {
                        value.ToImGui();
                        ImGui.TreePop();
                    }
                }

                ImGui.TreePop();
            }
        }

        private IEnumerable<Wait> OnGameClose()
        {
            while (true)
            {
                yield return new(GameHelperEvents.OnClose);
                this.cache.Clear();
            }
        }

        private IEnumerable<Wait> OnStateChange()
        {
            while (true)
            {
                yield return new(RemoteEvents.StateChanged);
                if (Core.States.GameCurrentState != this.ownerState1 &&
                    Core.States.GameCurrentState != this.ownerState2)
                {
                    this.cache.Clear();
                }
            }
        }
    }
}
