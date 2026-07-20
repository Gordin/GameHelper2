// <copyright file="UiElementParents.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>


namespace GameHelper.Cache
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using Coroutine;
    using GameHelper.RemoteObjects.UiElement;
    using GameHelper.CoroutineEvents;
    using GameOffsets.Objects.UiElement;
    using ImGuiNET;
    using GameHelper.RemoteEnums;
    using System.Threading.Tasks;

    internal class UiElementParents
    {
        private readonly string name;
        private readonly UiElementParents? grandparent;
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
        public UiElementParents(UiElementParents? grandparent, GameStateTypes ownerStateA, GameStateTypes ownerStateB, string name)
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
            if (address == IntPtr.Zero)
            {
                return;
            }

            if (this.grandparent != null)
            {
                bool inGrandparent;
                lock (this.grandparent.cache)
                {
                    inGrandparent = this.grandparent.cache.ContainsKey(address);
                }

                if (inGrandparent)
                {
                    return;
                }
            }

            lock (this.cache)
            {
                if (!this.cache.ContainsKey(address))
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

        public bool TryGetParent(IntPtr address, [NotNullWhen(true)] out UiElementBase? parent)
        {
            if (address == IntPtr.Zero)
            {
                parent = null;
                return false;
            }

            lock (this.cache)
            {
                if (this.cache.TryGetValue(address, out parent))
                {
                    return true;
                }
            }

            if (this.grandparent != null)
            {
                lock (this.grandparent.cache)
                {
                    if (this.grandparent.cache.TryGetValue(address, out parent))
                    {
                        return true;
                    }
                }
            }

            parent = null;
            return false;
        }

        public void UpdateAllParentsParallel()
        {
            KeyValuePair<IntPtr, UiElementBase>[] snapshot;
            lock (this.cache)
            {
                snapshot = new KeyValuePair<IntPtr, UiElementBase>[this.cache.Count];
                ((ICollection<KeyValuePair<IntPtr, UiElementBase>>)this.cache).CopyTo(snapshot, 0);
            }

            // A cached parent can be freed/reused by the game after we cached it (the atlas, for
            // example, churns through many node-container parents). Re-validate each parent's
            // self-pointer before updating: if it's no longer a Ui element, prune it instead of
            // re-assigning its Address — the forceUpdate setter would otherwise throw "not a Ui
            // Element" and spam the log every frame for every stale entry.
            var stale = new ConcurrentBag<IntPtr>();
            Parallel.ForEach(snapshot, (data) =>
            {
                try
                {
                    var offsets = Core.Process.Handle.ReadMemory<UiElementBaseOffset>(data.Key);
                    if (offsets.Self != IntPtr.Zero && offsets.Self != data.Key)
                    {
                        stale.Add(data.Key);
                        return;
                    }

                    data.Value.Address = data.Key;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Failed to update the UiElement Parent in the cache. 0x{data.Key.ToInt64():X} due to {e}");
                }
            });

            if (!stale.IsEmpty)
            {
                lock (this.cache)
                {
                    foreach (var key in stale)
                    {
                        this.cache.Remove(key);
                    }
                }
            }
        }

        public void Clear()
        {
            lock (this.cache)
            {
                this.cache.Clear();
            }
        }

        public void ToImGui()
        {
            KeyValuePair<IntPtr, UiElementBase>[] snapshot;
            lock (this.cache)
            {
                snapshot = new KeyValuePair<IntPtr, UiElementBase>[this.cache.Count];
                ((ICollection<KeyValuePair<IntPtr, UiElementBase>>)this.cache).CopyTo(snapshot, 0);
            }

            ImGui.Text($"Total Size: {snapshot.Length}");
            if (ImGui.TreeNode($"{this.name} Parent UiElements"))
            {
                foreach (var (key, value) in snapshot)
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
                try
                {
                    lock (this.cache)
                    {
                        this.cache.Clear();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UiElementParents.OnGameClose] {ex}");
                }
            }
        }

        private IEnumerable<Wait> OnStateChange()
        {
            while (true)
            {
                yield return new(RemoteEvents.StateChanged);
                try
                {
                    if (Core.States.GameCurrentState != this.ownerState1 &&
                        Core.States.GameCurrentState != this.ownerState2)
                    {
                        lock (this.cache)
                        {
                            this.cache.Clear();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UiElementParents.OnStateChange] {ex}");
                }
            }
        }
    }
}
