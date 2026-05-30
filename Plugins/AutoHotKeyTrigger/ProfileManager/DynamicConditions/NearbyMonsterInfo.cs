// <copyright file="NearbyMonsterInfo.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace AutoHotKeyTrigger.ProfileManager.DynamicConditions
{
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteEnums.Entity;
    using GameHelper.RemoteObjects.Components;
    using GameHelper.RemoteObjects.States;
    using AutoHotKeyTrigger.ProfileManager.DynamicConditions.Interface;

    /// <summary>
    ///     Stores optimized information about nearby monster count
    /// </summary>
    public class NearbyMonsterInfo
    {
        private readonly int[] smallCircleMonsterCount = { 0, 0, 0, 0 };
        private readonly int[] largeCircleMonsterCount = { 0, 0, 0, 0 };

        /// <summary>
        ///     Creates a new instance of <see cref="NearbyMonsterInfo"/>
        /// </summary>
        /// <param name="state"></param>
        public NearbyMonsterInfo(InGameState state)
        {
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
                    if (entity.Zones.HasFlag(NearbyZones.InnerCircle))
                    {
                        this.incrementCounter(omp.Rarity, ref this.smallCircleMonsterCount);
                    }

                    if (entity.Zones.HasFlag(NearbyZones.OuterCircle))
                    {
                        this.incrementCounter(omp.Rarity, ref this.largeCircleMonsterCount);
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
