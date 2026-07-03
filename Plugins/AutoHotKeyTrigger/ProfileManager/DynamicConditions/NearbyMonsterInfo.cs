// <copyright file="NearbyMonsterInfo.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.DynamicConditions
{
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteEnums.Entity;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.RemoteObjects.States;
    using GameHelper.RemoteObjects.States.InGameStateObjects;
    using System;
    using System.Collections.Generic;
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions.Interface;

    /// <summary>
    ///     Stores optimized information about nearby monster count
    /// </summary>
    public class NearbyMonsterInfo
    {
        private readonly int[] smallCircleMonsterCount = { 0, 0, 0, 0 };
        private readonly int[] largeCircleMonsterCount = { 0, 0, 0, 0 };
        private readonly int[] smallCircleUndamageableCount = { 0, 0, 0, 0 };
        private readonly int[] largeCircleUndamageableCount = { 0, 0, 0, 0 };
        private readonly int[] smallCircleCorpseCount = { 0, 0, 0, 0 };
        private readonly int[] largeCircleCorpseCount = { 0, 0, 0, 0 };
        private readonly InGameState state;

        /// <summary>
        ///     Creates a new instance of <see cref="NearbyMonsterInfo"/>
        /// </summary>
        /// <param name="state"></param>
        public NearbyMonsterInfo(InGameState state)
        {
            this.state = state;
            foreach (var entity in state.CurrentAreaInstance.AwakeEntities.Values)
            {
                if (entity.Zones == NearbyZones.None)
                {
                    continue;
                }

                if (entity.EntityType != EntityTypes.Monster ||
                    entity.EntityState == EntityStates.PinnacleBossHidden)
                {
                    continue;
                }

                if (entity.EntityState == EntityStates.MonsterFriendly)
                {
                    if (entity.Zones.HasFlag(NearbyZones.InnerCircle))
                    {
                        this.FriendlyCount[0]++;
                    }

                    if (entity.Zones.HasFlag(NearbyZones.OuterCircle))
                    {
                        this.FriendlyCount[1]++;
                    }
                }
                else if (entity.TryGetComponent<ObjectMagicProperties>(out var omp))
                {
                    var inInner = entity.Zones.HasFlag(NearbyZones.InnerCircle);
                    var inOuter = entity.Zones.HasFlag(NearbyZones.OuterCircle);

                    // Corpses keep their components, so route dead monsters into the corpse
                    // counters and out of the (alive) monster counters.
                    if (!IsAliveMonster(entity))
                    {
                        if (inInner)
                        {
                            this.incrementCounter(omp.Rarity, ref this.smallCircleCorpseCount);
                        }

                        if (inOuter)
                        {
                            this.incrementCounter(omp.Rarity, ref this.largeCircleCorpseCount);
                        }

                        continue;
                    }

                    var undamageable = IsCurrentlyUndamageable(entity);
                    if (inInner)
                    {
                        this.incrementCounter(omp.Rarity, ref this.smallCircleMonsterCount);
                        if (undamageable)
                        {
                            this.incrementCounter(omp.Rarity, ref this.smallCircleUndamageableCount);
                        }
                    }

                    if (inOuter)
                    {
                        this.incrementCounter(omp.Rarity, ref this.largeCircleMonsterCount);
                        if (undamageable)
                        {
                            this.incrementCounter(omp.Rarity, ref this.largeCircleUndamageableCount);
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     Number of friendly nearby monsters in inner (index 0) or outer (index 1) circle.
        /// </summary>
        public int[] FriendlyCount { get; } = { 0, 0 };

        /// <summary>
        /// Calculates the nearby monster count
        /// </summary>
        /// <param name="rarity">filter monster based on rarity</param>
        /// <param name="zone">nearby zone in which we want to count the monster in</param>
        public int GetMonsterCount(MonsterRarity rarity, MonsterNearbyZones zone)
        {
            switch (zone)
            {
                case MonsterNearbyZones.InnerCircle:
                    return this.getCounterValue(rarity, this.smallCircleMonsterCount);
                case MonsterNearbyZones.OuterCircle:
                    return this.getCounterValue(rarity, this.largeCircleMonsterCount);
                default:
                    return 0;
            }
        }

        /// <summary>
        ///     Calculates the nearby monster count that are currently undamageable (i.e. in an
        ///     invulnerability phase, flagged by the <see cref="GameStats.cannot_be_damaged"/> stat).
        /// </summary>
        /// <param name="rarity">filter monster based on rarity</param>
        /// <param name="zone">nearby zone in which we want to count the monster in</param>
        public int GetUndamageableMonsterCount(MonsterRarity rarity, MonsterNearbyZones zone)
        {
            switch (zone)
            {
                case MonsterNearbyZones.InnerCircle:
                    return this.getCounterValue(rarity, this.smallCircleUndamageableCount);
                case MonsterNearbyZones.OuterCircle:
                    return this.getCounterValue(rarity, this.largeCircleUndamageableCount);
                default:
                    return 0;
            }
        }

        /// <summary>
        ///     Calculates the nearby corpse (dead monster) count of the given rarity.
        /// </summary>
        /// <param name="rarity">filter corpse based on rarity</param>
        /// <param name="zone">nearby zone in which we want to count the corpse in</param>
        public int GetCorpseCount(MonsterRarity rarity, MonsterNearbyZones zone)
        {
            switch (zone)
            {
                case MonsterNearbyZones.InnerCircle:
                    return this.getCounterValue(rarity, this.smallCircleCorpseCount);
                case MonsterNearbyZones.OuterCircle:
                    return this.getCounterValue(rarity, this.largeCircleCorpseCount);
                default:
                    return 0;
            }
        }

        /// <summary>
        ///     Counts nearby monsters of the given rarity within an explicit distance (in the same
        ///     units as the inner/outer circle settings). Unlike the zone-based counts, this is not
        ///     bound by the configured outer circle, so it can reach out to the network bubble
        ///     (~<see cref="GameOffsets.Objects.States.InGameState.AreaInstanceConstants.NETWORK_BUBBLE_RADIUS"/>);
        ///     monsters beyond that are not loaded by the game and cannot be counted.
        /// </summary>
        /// <param name="rarity">filter monster based on rarity</param>
        /// <param name="maxDistance">maximum distance from the player to include a monster</param>
        public int GetMonsterCountInRange(MonsterRarity rarity, int maxDistance) =>
            this.CountInRange(rarity, maxDistance, IsAliveMonster);

        /// <summary>
        ///     Counts nearby monsters of the given rarity that are currently undamageable (in an
        ///     invulnerability phase) within an explicit distance. See
        ///     <see cref="GetMonsterCountInRange"/> for the distance semantics.
        /// </summary>
        /// <param name="rarity">filter monster based on rarity</param>
        /// <param name="maxDistance">maximum distance from the player to include a monster</param>
        public int GetUndamageableMonsterCountInRange(MonsterRarity rarity, int maxDistance) =>
            this.CountInRange(rarity, maxDistance, e => IsAliveMonster(e) && IsCurrentlyUndamageable(e));

        /// <summary>
        ///     Counts nearby corpses (dead monsters) of the given rarity within an explicit distance.
        ///     See <see cref="GetMonsterCountInRange"/> for the distance semantics.
        /// </summary>
        /// <param name="rarity">filter corpse based on rarity</param>
        /// <param name="maxDistance">maximum distance from the player to include a corpse</param>
        public int GetCorpseCountInRange(MonsterRarity rarity, int maxDistance) =>
            this.CountInRange(rarity, maxDistance, e => !IsAliveMonster(e));

        private int CountInRange(MonsterRarity rarity, int maxDistance, Func<Entity, bool> include)
        {
            var area = this.state.CurrentAreaInstance;
            var player = area.Player;
            var count = 0;
            foreach (var entity in area.AwakeEntities.Values)
            {
                if (entity.EntityType != EntityTypes.Monster ||
                    entity.EntityState == EntityStates.PinnacleBossHidden ||
                    entity.EntityState == EntityStates.MonsterFriendly)
                {
                    continue;
                }

                if (!entity.TryGetComponent<ObjectMagicProperties>(out var omp) ||
                    !RarityMatches(rarity, omp.Rarity))
                {
                    continue;
                }

                if (entity.DistanceFrom(player) > maxDistance)
                {
                    continue;
                }

                if (include(entity))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool RarityMatches(MonsterRarity filter, Rarity rarity)
        {
            return rarity switch
            {
                Rarity.Normal => filter.HasFlag(MonsterRarity.Normal),
                Rarity.Magic => filter.HasFlag(MonsterRarity.Magic),
                Rarity.Rare => filter.HasFlag(MonsterRarity.Rare),
                Rarity.Unique => filter.HasFlag(MonsterRarity.Unique),
                _ => false,
            };
        }

        /// <summary>
        ///     A monster only counts while it is alive. Corpses keep their components (so they would
        ///     otherwise still be counted, including as "damageable"), hence the explicit life check.
        ///     <see cref="Life.IsAlive"/> is simply <c>Health.Current &gt; 0</c>.
        /// </summary>
        private static bool IsAliveMonster(Entity entity) =>
            entity.TryGetComponent<Life>(out var life) && life.IsAlive;

        /// <summary>
        ///     A monster is treated as undamageable while the <see cref="GameStats.cannot_be_damaged"/>
        ///     (or its base variant) stat is set. Bosses toggle this on/off during invulnerability phases.
        /// </summary>
        private static bool IsCurrentlyUndamageable(Entity entity)
        {
            if (!entity.TryGetComponent<Stats>(out var stats))
            {
                return false;
            }

            return HasCannotBeDamaged(stats.StatsChangedByBuffAndActions) ||
                   HasCannotBeDamaged(stats.StatsChangedByItems);
        }

        private static bool HasCannotBeDamaged(Dictionary<GameStats, int> stats)
        {
            if (stats == null)
            {
                return false;
            }

            return (stats.TryGetValue(GameStats.cannot_be_damaged, out var v1) && v1 != 0) ||
                   (stats.TryGetValue(GameStats.base_cannot_be_damaged, out var v2) && v2 != 0);
        }

        private void incrementCounter(Rarity rarity, ref int[] counterArray)
        {
            switch (rarity)
            {
                case Rarity.Normal:
                    counterArray[0]++;
                    break;
                case Rarity.Magic:
                    counterArray[1]++;
                    break;
                case Rarity.Rare:
                    counterArray[2]++;
                    break;
                case Rarity.Unique:
                    counterArray[3]++;
                    break;
            }
        }

        private int getCounterValue(MonsterRarity rarity, int[] counterArray)
        {
            var sum = 0;

            if (rarity.HasFlag(MonsterRarity.Normal))
            {
                sum += counterArray[0];
            }

            if (rarity.HasFlag(MonsterRarity.Magic))
            {
                sum += counterArray[1];
            }

            if (rarity.HasFlag(MonsterRarity.Rare))
            {
                sum += counterArray[2];
            }

            if (rarity.HasFlag(MonsterRarity.Unique))
            {
                sum += counterArray[3];
            }

            return sum;
        }
    }
}
