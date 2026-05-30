// <copyright file="DisappearingEntity.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Cache
{
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using Coroutine;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using GameOffsets.Objects.States.InGameState;
    using ImGuiNET;

    /// <summary>
    ///     The purpose of this class is to solve the disappearing entity problem.
    ///     Basically, there are a group of entities that disappears automatically after the
    ///     environment is gone from the map. The problem happens when those disappearing entities
    ///     are outside the network bubble, GameHelper will never know to clean them up.
    ///
    ///     For example, when breach is opened, breach environment is activated, and all the breach
    ///     entities start spawning. Once breach is completed/closed, all of the spawned breach entities
    ///     disappears (even if they are not in the network bubble). However, GameHelper still show them
    ///     and process them as they are still there.
    ///
    ///     Disappearing entity problem does not impact all the environments e.g. (Legion) because in that
    ///     case 99.9% of the time the entities that disappear will be in the network bubble. So this solution
    ///     will be applied on case by case basis.
    ///     
    ///     NOTE: Currently activated environment information is available in <see cref="AreaInstance"/> structure
    ///     and environment key details can be read from Environment.dat GGPK file.
    /// </summary>
    public class DisappearingEntity
    {
        private readonly string name;
        private readonly int envKeyMin;
        private readonly int envKeyMax;
        private readonly ConcurrentDictionary<EntityNodeKey, bool> cache;
        private readonly ConcurrentDictionary<EntityNodeKey, Entity> entities;
        private bool isActivated;
        private bool isCleanUpTriggered;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DisappearingEntity" /> class.
        /// </summary>
        /// <param name="entityPathIdentifier">
        ///     A string in the entity path that helps identify the entity belonging to this cache.
        /// </param>
        /// <param name="environmentKeyMin">
        ///     Environment keys minimum value to activate this cache.
        /// </param>
        /// <param name="environmentKeyMax">
        ///     Environment keys maximum value to activate this cache.
        /// </param>
        /// <param name="entities_">
        ///     Reference to the structure storing all the entities known to GameHelper.
        /// </param>
        public DisappearingEntity(
            string entityPathIdentifier,
            int environmentKeyMin,
            int environmentKeyMax,
            ConcurrentDictionary<EntityNodeKey, Entity> entities_)
        {
            this.name = entityPathIdentifier;
            this.envKeyMin = environmentKeyMin;
            this.envKeyMax = environmentKeyMax;
            this.isActivated = false;
            this.isCleanUpTriggered = false;
            this.cache = new();
            this.entities = entities_;
        }

        /// <summary>
        ///     Update the cache states/processes based on currently active environments.
        /// </summary>
        /// <param name="environments">array of currently activated environments.</param>
        internal void UpdateState(IReadOnlyList<int> environments)
        {
            this.UpdateActivation(environments);
            this.UpdateCleanUpJob();
        }

        /// <summary>
        ///     Adds the entitiy to the cache if cache is activated and entity belong to it.
        /// </summary>
        /// <param name="entity">entity key to add.</param>
        /// <param name="path">entity path to validate entity belonging to the cache.</param>
        /// <returns>true in case the entity is added otherwise false.</returns>
        internal bool TryAddParallel(EntityNodeKey entity, string path)
        {
            if (this.isActivated && path.Contains(this.name, System.StringComparison.Ordinal))
            {
                this.cache[entity] = true;
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Clears all the data in this cache.
        /// </summary>
        internal void Clear()
        {
            this.isActivated = false;
            this.cache.Clear();
        }

        /// <summary>
        ///     Draws a widget for this class.
        /// </summary>
        internal void ToImGui()
        {
            if (ImGui.TreeNode(this.name))
            {
                ImGui.Text($"Is Activated: {this.isActivated}");
                ImGui.Text($"Total Entities: {this.cache.Count}");
                ImGui.TreePop();
            }
        }

        /// <summary>
        ///     Checks if the entity exists in this cache or not.
        /// </summary>
        /// <param name="entity">entity key to check.</param>
        /// <returns>true in case entity exists otherwise false.</returns>
        public bool Contains(EntityNodeKey entity) => this.cache.ContainsKey(entity);

        /// <summary>
        ///     checks if the cache is active or not.
        /// </summary>
        /// <returns>true in case the cache is active otherwise false.</returns>
        public bool IsActive()
        {
            return this.isActivated;
        }

        private void UpdateActivation(IReadOnlyList<int> environments)
        {
            // F-042: compute the new state into a local first, then publish once.
            // The previous false-then-maybe-true pattern opened a race window where
            // parallel TryAddParallel readers saw the transient false and skipped
            // caching active environments.
            var active = false;
            foreach (var envKey in environments)
            {
                if (envKey >= this.envKeyMin && envKey <= this.envKeyMax)
                {
                    active = true;
                    break;
                }
            }

            this.isActivated = active;
        }

        private void UpdateCleanUpJob()
        {
            if (this.isCleanUpTriggered)
            {
                return;
            }

            if (!this.isActivated && !this.cache.IsEmpty)
            {
                CoroutineHandler.InvokeLater(new Wait(0.5d), () =>
                {
                    foreach (var dE in this.cache)
                    {
                        this.entities.TryRemove(dE.Key, out var _);
                        this.cache.TryRemove(dE.Key, out var _);
                    }

                    this.isCleanUpTriggered = false;
                });

                this.isCleanUpTriggered = true;
            }
        }
    }
}
