// <copyright file="MonsterInfo.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.DynamicConditions.Interface
{
    using System.Collections.Concurrent;
    using System.Numerics;
    using GameHelper;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using GameOffsets.Objects.Components;
    using ImGuiNET;

    /// <summary>
    ///     A lightweight, queryable snapshot of a nearby monster, exposing the
    ///     per-monster state needed by rules: its buffs/debuffs and its on-screen
    ///     distance to the mouse cursor.
    /// </summary>
    public class MonsterInfo
    {
        private readonly Entity entity;

        /// <summary>
        ///     Initializes a new instance of the <see cref="MonsterInfo" /> class.
        /// </summary>
        /// <param name="entity">The underlying game entity.</param>
        public MonsterInfo(Entity entity)
        {
            this.entity = entity;
            this.Buffs = new BuffDictionary(
                entity.TryGetComponent<Buffs>(out var buffs)
                    ? buffs.StatusEffects
                    : new ConcurrentDictionary<string, StatusEffectStruct>());
        }

        /// <summary>
        ///     Gets the buffs/debuffs currently applied to this monster.
        /// </summary>
        public IBuffDictionary Buffs { get; }

        /// <summary>
        ///     Gets the on-screen distance (in pixels) from this monster to the mouse cursor.
        ///     Returns <see cref="float.PositiveInfinity" /> if the monster has no render position.
        /// </summary>
        public float DistanceToCursor
        {
            get
            {
                if (!this.entity.TryGetComponent<Render>(out var render))
                {
                    return float.PositiveInfinity;
                }

                var world = Core.States.InGameStateObject.CurrentWorldInstance;
                var screenPos = world.WorldToScreen(render.WorldPosition);
                return Vector2.Distance(screenPos, ImGui.GetMousePos());
            }
        }
    }
}