// <copyright file="Entity.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.States.InGameStateObjects
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;
    using Components;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteEnums.Entity;
    using GameOffsets.Objects.States.InGameState;
    using ImGuiNET;
    using Utils;

    /// <summary>
    ///     Points to an Entity/Object in the game.
    ///     Entity is basically item/monster/effect/player/etc on the ground.
    /// </summary>
    public class Entity : RemoteObjectBase
    {
        private static readonly int MaxComponentsInAnEntity = 50;
        private static readonly string DeliriumHiddenMonsterStarting =
            "Metadata/Monsters/LeagueDelirium/DoodadDaemons/DoodadDaemon";
        private static readonly string DeliriumUselessMonsterStarting =
            "Metadata/Monsters/LeagueDelirium/Volatile/";

        private const int DormantCheckInterval = 30;
        private const int MaxUnresolvedRetries = 5;

        private readonly ConcurrentDictionary<string, IntPtr> componentAddresses;
        private readonly ConcurrentDictionary<string, ComponentBase> componentCache;

        private NearbyZones zone;
        private int customGroup;
        private EntitySubtypes oldSubtypeWithoutPOI;
        private int dormantCheckSkip;
        private int unresolvedRetryCount;
        private int consecutiveInvalidFrames;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Entity" /> class.
        /// </summary>
        /// <param name="address">address of the Entity.</param>
        internal Entity(IntPtr address)
            : this()
        {
            this.Address = address;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Entity" /> class.
        ///     NOTE: Without providing an address, only invalid and empty entity is created.
        /// </summary>
        internal Entity()
            : base(IntPtr.Zero, true)
        {
            this.componentAddresses = new();
            this.componentCache = new();
            this.zone = NearbyZones.None;
            this.Path = string.Empty;
            this.Id = 0;
            this.IsValid = false;
            this.EntityType = EntityTypes.Unidentified;
            this.EntitySubtype = EntitySubtypes.Unidentified;
            this.oldSubtypeWithoutPOI = EntitySubtypes.None;
            this.EntityState = EntityStates.None;
            this.customGroup = 0;
        }

        /// <summary>
        ///     Gets the Path (e.g. Metadata/Character/int/int) assocaited to the entity.
        /// </summary>
        public string Path { get; private set; }

        /// <summary>
        ///     Gets the Id associated to the entity. This is unique per map/Area.
        /// </summary>
        public uint Id { get; private set; }

        /// <summary>
        ///     Gets a value indicating whether the entity is near the player or not.
        ///     Useless and Invalid entities are not considered nearby.
        /// </summary>
        public NearbyZones Zones => this.IsValid ? this.zone : NearbyZones.None;

        /// <summary>
        ///     Gets or Sets a value indicating whether the entity
        ///     exists in the game or not.
        /// </summary>
        public bool IsValid { get; set; }

        /// <summary>
        ///     Gets a value indicating the type of entity this is.
        /// </summary>
        public EntityTypes EntityType { get; protected set; }

        /// <summary>
        ///     Get a value indicating the sub-type of the entity.
        /// </summary>
        public EntitySubtypes EntitySubtype { get; protected set; }

        /// <summary>
        ///     Gets the custom group given to a <see cref="EntityTypes.Monster"/> entity type by the user.
        /// </summary>
        public int EntityCustomGroup => this.EntitySubtype == EntitySubtypes.POIMonster ||
            this.EntityType == EntityTypes.OtherImportantObjects ? this.customGroup : 0;

        /// <summary>
        ///     Get a value indicating the state the entity is in. This can change on every frame.
        /// </summary>
        public EntityStates EntityState { get; protected set; }

        /// <summary>
        ///     Gets a value indicating whether this entity can be exploded (or
        ///     removed from the game memory) while it's inside the network bubble.
        /// </summary>
        public bool CanExplodeOrRemovedFromGame => this.EntityState == EntityStates.Useless ||
            this.EntityType == EntityTypes.Renderable ||
            this.EntityType == EntityTypes.DeliriumSpawner ||
            this.EntityType == EntityTypes.DeliriumBomb ||
            this.EntityType == EntityTypes.OtherImportantObjects ||
            this.EntityType == EntityTypes.Monster ||
            (Core.GHSettings.EnableNpcEntityCleanup &&
             this.EntityType == EntityTypes.NPC);

        /// <summary>
        ///     Gets how many consecutive frames this entity has been invalid.
        /// </summary>
        public int ConsecutiveInvalidFrames => this.consecutiveInvalidFrames;

        /// <summary>
        ///     Increments the consecutive invalid frames counter.
        /// </summary>
        internal void IncrementInvalidFrames() => this.consecutiveInvalidFrames++;

        /// <summary>
        ///     Resets the consecutive invalid frames counter.
        /// </summary>
        internal void ResetInvalidFrames() => this.consecutiveInvalidFrames = 0;

        /// <summary>
        ///     Calculate the distance from the other entity.
        /// </summary>
        /// <param name="other">Other entity object.</param>
        /// <returns>
        ///     the distance from the other entity
        ///     if it can calculate; otherwise, return 0.
        /// </returns>
        public int DistanceFrom(Entity other)
        {
            if (this.TryGetComponent<Render>(out var myPosComp) &&
                other.TryGetComponent<Render>(out var otherPosComp))
            {
                var dx = myPosComp.GridPosition.X - otherPosComp.GridPosition.X;
                var dy = myPosComp.GridPosition.Y - otherPosComp.GridPosition.Y;
                return (int)Math.Sqrt(dx * dx + dy * dy);
            }

            return 0;
        }

        /// <summary>
        ///     Gets the Component data associated with the entity.
        /// </summary>
        /// <typeparam name="T">Component type to get.</typeparam>
        /// <param name="component">component data.</param>
        /// <param name="shouldCache">should entity cache this component or not.</param>
        /// <returns>true if the entity contains the component; otherwise, false.</returns>
        public bool TryGetComponent<T>([NotNullWhen(true)] out T? component, bool shouldCache = true)
            where T : ComponentBase
        {
            component = null;
            var componenName = typeof(T).Name;
            if (this.componentCache.TryGetValue(componenName, out var comp))
            {
                component = (T)comp;
                return true;
            }

            if (this.componentAddresses.TryGetValue(componenName, out var compAddr))
            {
                if (compAddr != IntPtr.Zero)
                {
                    if (shouldCache)
                    {
                        // Atomic get-or-add: prevents two parallel callers each constructing
                        // their own ComponentBase + caching it (audit F-134). ConcurrentDictionary
                        // may invoke the factory more than once under contention, but only one
                        // result is committed; the orphan is GC'd without participating in
                        // subsequent state mutations.
                        var cached = this.componentCache.GetOrAdd(componenName, _ =>
                            (ComponentBase)Activator.CreateInstance(typeof(T), compAddr)!);
                        component = (T)cached;
                        return true;
                    }
                    else
                    {
                        // Caller doesn't want caching; construct fresh each call. No race
                        // because the result isn't shared.
                        component = Activator.CreateInstance(typeof(T), compAddr) as T;
                        if (component != null)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        ///     Validates if the monster is/was of a specific subtype.
        /// </summary>
        /// <param name="subType">subType the entity is/was in.</param>
        /// <returns>true if it is/was that subtype; otherwise false.</returns>
        public bool IsOrWasMonsterSubType(EntitySubtypes subType)
        {
            var toCheck = this.EntitySubtype == EntitySubtypes.POIMonster ?
                this.oldSubtypeWithoutPOI : this.EntitySubtype;
            return toCheck == subType;
        }

        /// <summary>
        ///     Converts the <see cref="Entity" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"Path: {this.Path}");
            ImGui.Text($"Id: {this.Id}");
            ImGui.Text($"Is Valid: {this.IsValid}");
            ImGui.Text($"Nearby Zone: {this.Zones}");
            ImGui.Text($"Entity Type: {this.EntityType}");
            ImGui.Text($"Entity SubType: {this.EntitySubtype}");
            if (this.EntitySubtype == EntitySubtypes.POIMonster)
            {
                ImGui.Text($"Entity Old SubType: {this.oldSubtypeWithoutPOI}");
            }

            ImGui.Text($"Entity Custom Group: {this.EntityCustomGroup}");
            ImGui.Text($"Entity State: {this.EntityState}");
            if (ImGui.TreeNode("Components"))
            {
                foreach (var kv in this.componentAddresses)
                {
                    if (this.componentCache.TryGetValue(kv.Key, out var value))
                    {
                        if (ImGui.TreeNode($"{kv.Key}"))
                        {
                            value.ToImGui();
                            ImGui.TreePop();
                        }
                    }
                    else
                    {
                        var componentType = Type.GetType($"{typeof(NPC).Namespace}.{kv.Key}");
                        if (componentType != null)
                        {
                            if (ImGui.SmallButton($"Load##{kv.Key}"))
                            {
                                this.LoadComponent(componentType);
                            }

                            ImGui.SameLine();
                        }

                        ImGuiHelper.IntPtrToImGui(kv.Key, kv.Value);
                    }
                }

                ImGui.TreePop();
            }
        }

        internal IEnumerable<string> GetComponentNames()
        {
            return this.componentAddresses.Keys;
        }

        internal void UpdateNearby(Entity player)
        {
            if (this.EntityState != EntityStates.Useless)
            {
                var distance = this.DistanceFrom(player);
                if (distance < Core.GHSettings.InnerCircle.Meaning)
                {
                    this.zone = NearbyZones.InnerCircle | NearbyZones.OuterCircle;
                    return;
                }
                else if (distance < Core.GHSettings.OuterCircle.Meaning)
                {
                    this.zone = NearbyZones.OuterCircle;
                    return;
                }
            }

            this.zone = NearbyZones.None;
        }

        /// <summary>
        ///     Updates the component data associated with the Entity base object (i.e. item).
        /// </summary>
        /// <param name="idata">Entity base (i.e. item) data.</param>
        /// <param name="refreshComponentMap">should component map be rebuilt from memory.</param>
        /// <returns>false if this method detects an issue, otherwise true.</returns>
        protected bool UpdateComponentData(ItemStruct idata, bool refreshComponentMap)
        {
            var reader = Core.Process.Handle;
            if (refreshComponentMap)
            {
                this.componentAddresses.Clear();
                this.componentCache.Clear();

                var entityDetails = reader.ReadMemory<EntityDetails>(idata.EntityDetailsPtr);
                this.Path = reader.ReadStdWString(entityDetails.name);
                if (string.IsNullOrEmpty(this.Path))
                {
                    return false;
                }

                var lookupPtr = reader.ReadMemory<ComponentLookUpStruct>(entityDetails.ComponentLookUpPtr);
                if (lookupPtr.ComponentsNameAndIndex.Capacity > MaxComponentsInAnEntity)
                {
                    return false;
                }

                var namesAndIndexes = reader.ReadStdBucket<ComponentNameAndIndexStruct>(
                    lookupPtr.ComponentsNameAndIndex);
                var entityComponent = reader.ReadStdVector<IntPtr>(idata.ComponentListPtr);

                for (var i = 0; i < namesAndIndexes.Length; i++)
                {
                    var nameAndIndex = namesAndIndexes[i];
                    if (nameAndIndex.Index >= 0 && nameAndIndex.Index < entityComponent.Length)
                    {
                        var name = reader.ReadString(nameAndIndex.NamePtr);
                        if (!string.IsNullOrEmpty(name))
                        {
                            this.componentAddresses[name] = entityComponent[nameAndIndex.Index];
                        }
                    }
                }
            }
            else
            {
                foreach (var kv in this.componentCache)
                {
                    kv.Value.Address = kv.Value.Address;
                    if (!kv.Value.IsParentValid(this.Address))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <inheritdoc />
        protected override void CleanUpData()
        {
            this.componentAddresses?.Clear();
            this.componentCache?.Clear();
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var entityData = reader.ReadMemory<EntityOffsets>(this.Address);
            this.IsValid = EntityHelper.IsValidEntity(entityData.IsValid);
            if (!this.IsValid)
            {
                return;
            }

            this.Id = entityData.Id;

            if (hasAddressChanged)
            {
                this.unresolvedRetryCount = 0;
                this.dormantCheckSkip = 0;
            }

            var isUnresolved =
                (this.EntityType == EntityTypes.Unidentified ||
                 this.EntitySubtype == EntitySubtypes.Unidentified) &&
                this.unresolvedRetryCount < MaxUnresolvedRetries;

            var isMonster = this.EntityType == EntityTypes.Monster ||
                            this.componentAddresses.ContainsKey("Monster") ||
                            this.Path.StartsWith("Metadata/Monsters/");

            // Skip useless non-monster, non-unresolved entities immediately.
            if (this.EntityState == EntityStates.Useless && !isUnresolved)
            {
                if (!isMonster)
                {
                    return;
                }

                // Throttle dormant monster checks — once every DormantCheckInterval frames.
                this.dormantCheckSkip++;
                if (this.dormantCheckSkip < DormantCheckInterval)
                {
                    return;
                }

                this.dormantCheckSkip = 0;

                if (!this.ShouldForceMonsterRefresh())
                {
                    return;
                }
            }

            // Full refresh when:
            // - address changed
            // - still unresolved (retry classification)
            // - useless monster passed wake-up check above
            var shouldRefreshComponentMap =
                hasAddressChanged ||
                isUnresolved ||
                (isMonster && this.EntityState == EntityStates.Useless);

            if (!this.UpdateComponentData(entityData.ItemBase, shouldRefreshComponentMap))
            {
                this.UpdateComponentData(entityData.ItemBase, true);
            }

            if (this.EntityType == EntityTypes.Unidentified)
            {
                if (!this.TryCalculateEntityType())
                {
                    this.unresolvedRetryCount++;
                    this.EntityState = EntityStates.Useless;
                    return;
                }
            }

            if (this.EntitySubtype == EntitySubtypes.Unidentified)
            {
                if (!this.TryCalculateEntitySubType())
                {
                    this.unresolvedRetryCount++;
                    this.EntityState = EntityStates.Useless;
                    return;
                }
            }

            this.CalculateEntityState();
        }

        /// <summary>
        ///     Loads the component class for the entity.
        /// </summary>
        /// <param name="componentType">component type to load.</param>
        private void LoadComponent(Type componentType)
        {
            if (this.componentAddresses.TryGetValue(componentType.Name, out var compAddr))
            {
                if (compAddr != IntPtr.Zero)
                {
                    if (Activator.CreateInstance(componentType, compAddr) is ComponentBase component)
                    {
                        this.componentCache[componentType.Name] = component;
                    }
                }
            }
        }

        private bool ShouldForceMonsterRefresh()
        {
            if (this.EntityType != EntityTypes.Monster && !this.componentAddresses.ContainsKey("Monster"))
            {
                return false;
            }

            if (this.EntityState != EntityStates.Useless)
            {
                return false;
            }

            // Cheap wake-up signals for dormant monsters using the same ptr/id.
            if (this.TryGetStatValue(GameStats.is_dead, out var isDead) && isDead == 0)
            {
                return true;
            }

            // For monsters marked dead via Life fallback (no is_dead stat),
            // check if Life becomes alive again.
            if (!this.TryGetStatValue(GameStats.is_dead, out _) &&
                this.TryGetComponent<Life>(out var lifeComp, false) && lifeComp.IsAlive)
            {
                return true;
            }

            if (this.TryGetComponent<StateMachine>(out var sm, false) &&
                sm.States != null &&
                sm.States.Count > 0 &&
                sm.States.Any(x => x.Value != 0))
            {
                return true;
            }

            if (this.TryGetComponent<Targetable>(out var targetable, false) && targetable.IsTargetable)
            {
                return true;
            }

            if (this.TryGetComponent<Buffs>(out var buffs, false) &&
                buffs.StatusEffects != null &&
                buffs.StatusEffects.Count > 0)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Try to figure out the entity type of an entity.
        /// </summary>
        /// <returns>return false if it fails to do so, otherwise true.</returns>
        private bool TryCalculateEntityType()
        {
            if (!this.TryGetComponent<Render>(out var _))
            {
                return false;
            }
            else if (this.TryGetComponent<Chest>(out var _))
            {
                this.EntityType = EntityTypes.Chest;
            }
            else if (this.TryGetComponent<Player>(out var _))
            {
                this.EntityType = EntityTypes.Player;
            }
            else if (this.TryGetComponent<Shrine>(out var _))
            {
                this.EntityType = EntityTypes.Shrine;
            }
            else if (this.IsInSpecialMiscObjPaths())
            {
                this.EntityType = EntityTypes.OtherImportantObjects;
            }
            else if (this.TryGetComponent<Life>(out var _))
            {
                if (this.TryGetComponent<TriggerableBlockage>(out var _))
                {
                    return false;
                }

                if (!this.TryGetComponent<Positioned>(out var pos))
                {
                    return false;
                }

                if (!pos.IsFriendly && this.TryGetComponent<DiesAfterTime>(out var _))
                {
                    if (this.TryGetComponent<Targetable>(out var tComp) && tComp.IsTargetable)
                    {
                        this.EntityType = EntityTypes.Monster;
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (this.Path.StartsWith(DeliriumHiddenMonsterStarting) ||
                         this.Path.StartsWith(DeliriumUselessMonsterStarting))
                {
                    if (this.Path.Contains("ShardPack"))
                    {
                        this.EntityType = EntityTypes.DeliriumSpawner;
                    }
                    else
                    {
                        return false;
                    }
                }
                else if (Core.GHSettings.MonstersPathsToIgnore.Any(this.Path.StartsWith))
                {
                    return false;
                }
                else if (this.componentAddresses.ContainsKey("Monster") ||
                         this.componentAddresses.ContainsKey(nameof(Buffs)) ||
                         this.componentAddresses.ContainsKey(nameof(ObjectMagicProperties)))
                {
                    this.EntityType = EntityTypes.Monster;
                }
                else
                {
                    return false;
                }
            }
            else if (this.TryGetComponent<NPC>(out var _))
            {
                this.EntityType = EntityTypes.NPC;
            }
            else if (Core.GHSettings.ProcessAllRenderableEntities &&
                     this.TryGetComponent<Positioned>(out var _))
            {
                this.EntityType = EntityTypes.Renderable;
            }
            else
            {
                return false;
            }

            return true;
        }

        /// <summary>
        ///     Try to figure out the sub-type of an entity.
        /// </summary>
        /// <returns>false if it fails to do so, otherwise true.</returns>
        private bool TryCalculateEntitySubType()
        {
            switch (this.EntityType)
            {
                case EntityTypes.Unidentified:
                    Console.WriteLine($"[Entity.TryCalculateEntitySubType] Unidentified entity reached subtype calc - path={this.Path}, Id={this.Id}; skipping (audit F-133).");
                    return false;

                case EntityTypes.Chest:
                    this.TryGetComponent<Chest>(out var chestComp);
                    if (this.Path.StartsWith("Metadata/Chests/LeaguesExpedition"))
                    {
                        this.EntitySubtype = EntitySubtypes.ExpeditionChest;
                    }
                    else if (chestComp != null && chestComp.IsStrongbox)
                    {
                        this.EntitySubtype = EntitySubtypes.Strongbox;
                    }
                    else if (this.TryGetComponent<MinimapIcon>(out var _))
                    {
                        return false;
                    }
                    else if (this.Path.StartsWith("Metadata/Chests/Breach"))
                    {
                        this.EntitySubtype = EntitySubtypes.BreachChest;
                    }
                    else if (this.TryGetComponent<ObjectMagicProperties>(out var chestOMP) &&
                             (chestOMP.Rarity == Rarity.Magic ||
                              chestOMP.Rarity == Rarity.Rare ||
                              chestOMP.Rarity == Rarity.Unique))
                    {
                        this.EntitySubtype = chestOMP.Rarity == Rarity.Rare || chestOMP.Rarity == Rarity.Unique
                            ? EntitySubtypes.ChestWithRareRarity
                            : EntitySubtypes.ChestWithMagicRarity;
                    }
                    else
                    {
                        this.EntitySubtype = EntitySubtypes.None;
                    }

                    break;

                case EntityTypes.Player:
                    {
                        // Snapshot the Player.Id chain once - 4 levels of RemoteObject property
                        // access, each acquiring updateLock. During state transitions any link
                        // can be a default-valued instance; treat NRE as "not self" (audit F-135).
                        uint localPlayerId;
                        try
                        {
                            localPlayerId = Core.States.InGameStateObject.CurrentAreaInstance.Player.Id;
                        }
                        catch (NullReferenceException)
                        {
                            this.EntitySubtype = EntitySubtypes.PlayerOther;
                            break;
                        }

                        this.EntitySubtype = this.Id == localPlayerId
                            ? EntitySubtypes.PlayerSelf
                            : EntitySubtypes.PlayerOther;
                    }
                    break;

                case EntityTypes.Shrine:
                    this.EntitySubtype = EntitySubtypes.None;
                    break;

                case EntityTypes.DeliriumSpawner:
                case EntityTypes.DeliriumBomb:
                case EntityTypes.Renderable:
                    break;

                case EntityTypes.Monster:
                    this.EntitySubtype = EntitySubtypes.None;
                    this.customGroup = 0;
                    this.oldSubtypeWithoutPOI = EntitySubtypes.None;

                    this.TryGetComponent<ObjectMagicProperties>(out var omp, false);
                    this.TryGetComponent<Stats>(out var statcomp, false);

                    if (omp != null && omp.ModNames.Contains("PinnacleAtlasBoss"))
                    {
                        this.EntitySubtype = EntitySubtypes.PinnacleBoss;
                    }

                    if (omp != null)
                    {
                        for (var i = 0; i < Core.GHSettings.PoiMonstersCategories2.Count; i++)
                        {
                            var (filtertype, filter, rarity, stat, group) = Core.GHSettings.PoiMonstersCategories2[i];
                            var matched = filtertype switch
                            {
                                EntityFilterType.PATH => this.Path.StartsWith(filter),
                                EntityFilterType.PATHANDRARITY => omp.Rarity == rarity && this.Path.StartsWith(filter),
                                EntityFilterType.MOD => omp.ModNames.Contains(filter),
                                EntityFilterType.MODANDRARITY => omp.Rarity == rarity && omp.ModNames.Contains(filter),
                                EntityFilterType.PATHANDSTAT =>
                                    this.Path.StartsWith(filter) &&
                                    ((omp.ModStats != null && omp.ModStats.ContainsKey(stat)) ||
                                     (statcomp != null &&
                                      statcomp.StatsChangedByItems != null &&
                                      statcomp.StatsChangedByItems.ContainsKey(stat))),
                                _ => LogUnhandledFilterType(filtertype),
                            };

                            if (matched)
                            {
                                this.oldSubtypeWithoutPOI = this.EntitySubtype;
                                this.EntitySubtype = EntitySubtypes.POIMonster;
                                this.customGroup = group;
                            }
                        }
                    }

                    break;

                case EntityTypes.NPC:
                    this.EntitySubtype = Core.GHSettings.SpecialNPCPaths.Any(this.Path.StartsWith)
                        ? EntitySubtypes.SpecialNPC
                        : EntitySubtypes.None;
                    break;

                case EntityTypes.OtherImportantObjects:
                    this.EntitySubtype = EntitySubtypes.None;
                    break;

                case EntityTypes.Item:
                    this.EntitySubtype = EntitySubtypes.WorldItem;
                    break;

                default:
                    Console.WriteLine($"[Entity.TryCalculateEntitySubType] Unhandled EntityType={this.EntityType} - skipping (audit F-133).");
                    return false;
            }

            return true;
        }

        private void CalculateEntityState()
        {
            if (this.EntityType == EntityTypes.Chest)
            {
                if (this.TryGetComponent<Chest>(out var chestComp) && chestComp.IsOpened)
                {
                    this.EntityState = EntityStates.Useless;
                }
                else
                {
                    this.EntityState = EntityStates.None;
                }
            }
            else if (this.EntityType == EntityTypes.DeliriumBomb || this.EntityType == EntityTypes.DeliriumSpawner)
            {
                if (this.TryGetComponent<Life>(out var lifeComp) && !lifeComp.IsAlive)
                {
                    this.EntityState = EntityStates.Useless;
                }
                else
                {
                    this.EntityState = EntityStates.None;
                }
            }
            else if (this.EntityType == EntityTypes.Monster)
            {
                var hasDead = this.TryGetStatValue(GameStats.is_dead, out var isDead);
                if (hasDead && isDead > 0)
                {
                    this.EntityState = EntityStates.Useless;
                    return;
                }

                // Fallback: if is_dead stat is unavailable, use Life component.
                if (!hasDead && this.TryGetComponent<Life>(out var lifeComp, false) && !lifeComp.IsAlive)
                {
                    this.EntityState = EntityStates.Useless;
                    return;
                }

                if (this.TryGetComponent<Positioned>(out var posComp) && posComp.IsFriendly)
                {
                    this.EntityState = EntityStates.MonsterFriendly;
                }
                else if (this.IsOrWasMonsterSubType(EntitySubtypes.PinnacleBoss) &&
                         this.TryGetComponent<Buffs>(out var buffsComp) &&
                         buffsComp.StatusEffects.ContainsKey("hidden_monster"))
                {
                    this.EntityState = EntityStates.PinnacleBossHidden;
                }
                else
                {
                    this.EntityState = EntityStates.None;
                }
            }
            else if (this.EntitySubtype == EntitySubtypes.PlayerOther)
            {
                if (this.TryGetComponent<Player>(out var playerComp) &&
                    playerComp.Name.Equals(Core.GHSettings.LeaderName))
                {
                    this.EntityState = EntityStates.PlayerLeader;
                }
                else
                {
                    this.EntityState = EntityStates.None;
                }
            }
            else
            {
                this.EntityState = EntityStates.None;
            }
        }

        private bool TryGetStatValue(GameStats stat, out int value)
        {
            value = 0;
            if (!this.TryGetComponent<Stats>(out var statsComp, false))
            {
                return false;
            }

            if (statsComp.StatsChangedByBuffAndActions != null &&
                statsComp.StatsChangedByBuffAndActions.TryGetValue(stat, out value))
            {
                return true;
            }

            if (statsComp.StatsChangedByItems != null &&
                statsComp.StatsChangedByItems.TryGetValue(stat, out value))
            {
                return true;
            }

            return false;
        }

        private bool IsInSpecialMiscObjPaths()
        {
            for (var i = 0; i < Core.GHSettings.SpecialMiscObjPaths.Count; i++)
            {
                var (moPath, moGroup) = Core.GHSettings.SpecialMiscObjPaths[i];
                if (this.Path.StartsWith(moPath))
                {
                    this.customGroup = moGroup;
                    return true;
                }
            }

            return false;
        }

        private static bool LogUnhandledFilterType(EntityFilterType filtertype)
        {
            Console.WriteLine($"[Entity.TryCalculateEntitySubType] EntityFilterType {filtertype} added but not handled - skipping match (audit F-133).");
            return false;
        }
    }
}