// <copyright file="AreaInstance.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.States.InGameStateObjects
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Numerics;
    using System.Reflection;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Components;
    using Coroutine;
    using CoroutineEvents;
    using GameHelper.Cache;
    using GameHelper.RemoteEnums;
    using GameHelper.RemoteEnums.Entity;
    using GameOffsets.Natives;
    using GameOffsets.Objects.States.InGameState;
    using ImGuiNET;
    using Utils;

    /// <summary>
    ///     Points to the InGameState -> AreaInstanceData Object.
    /// </summary>
    public class AreaInstance : RemoteObjectBase
    {
        private int uselesssEntities;
        private int totalEntityRemoved;
        private string entityIdFilter;
        private string entityPathFilter;
        private Rarity entityRarityFilter;
        private byte filterBy;

        private StdVector environmentPtr;
        private readonly List<int> environments;

        /// <summary>
        ///     Initializes a new instance of the <see cref="AreaInstance" /> class.
        /// </summary>
        /// <param name="address">address of the remote memory object.</param>
        internal AreaInstance(IntPtr address)
            : base(address)
        {
            this.entityIdFilter = string.Empty;
            this.entityPathFilter = string.Empty;
            this.entityRarityFilter = Rarity.Normal;
            this.filterBy = 0;

            this.environmentPtr = default;
            this.environments = new();

            this.CurrentAreaLevel = 0;
            this.AreaHash = string.Empty;

            this.ServerDataObject = new(IntPtr.Zero);
            this.Player = new();
            this.AwakeEntities = new();
            this.EntityCaches = new()
            {
                new("/LeagueDelirium/", 1105, 1105, this.AwakeEntities), // always keep this at index 0.
                new("Breach", 1100, 1100, this.AwakeEntities),
            };

            this.NetworkBubbleEntityCount = 0;
            this.TerrainMetadata = default;
            this.GridHeightData = Array.Empty<float[]>();
            this.GridWalkableData = Array.Empty<byte>();
            this.TgtTilesLocations = new();

            Core.CoroutinesRegistrar.Add(CoroutineHandler.Start(
                this.OnPerFrame(), "[AreaInstance] Update Area Data", int.MaxValue - 4));
        }

        /// <summary>
        ///     Gets the level of the current Area.
        /// </summary>
        public int CurrentAreaLevel { get; private set; }

        /// <summary>
        ///     Gets the Hash of the current Area/Zone.
        ///     This value is sent to the client from the server.
        /// </summary>
        public string AreaHash { get; private set; }

        /// <summary>
        ///     Gets the data related to the player the user is playing.
        /// </summary>
        public ServerData ServerDataObject { get; }

        /// <summary>
        ///     Gets the player Entity.
        /// </summary>
        public Entity Player { get; }

        /// <summary>
        ///     Gets the Awake Entities of the current Area/Zone.
        ///     Awake Entities are the ones which player can interact with
        ///     e.g. Monsters, Players, NPC, Chests and etc. Sleeping entities
        ///     are opposite of awake entities e.g. Decorations, Effects, particles and etc.
        /// </summary>
        public ConcurrentDictionary<EntityNodeKey, Entity> AwakeEntities { get; }

        /// <summary>
        ///     Gets important environments entity caches. This only contain awake entities.
        /// </summary>
        public List<DisappearingEntity> EntityCaches { get; }

        /// <summary>
        ///     Gets the total number of entities (awake as well as sleeping) in the network bubble.
        /// </summary>
        public int NetworkBubbleEntityCount { get; private set; }

        /// <summary>
        ///     Gets the total number of useless entities in the gamehelper cache that are in the network bubble.
        /// </summary>
        public int UselessAwakeEntities => this.uselesssEntities;

        /// <summary>
        ///     Gets the terrain metadata data of the current Area/Zone instance.
        /// </summary>
        public TerrainStruct TerrainMetadata { get; private set; }

        /// <summary>
        ///     Gets the terrain height data.
        /// </summary>
        public float[][] GridHeightData { get; private set; }

        /// <summary>
        ///     Gets the terrain data of the current Area/Zone instance.
        /// </summary>
        public byte[] GridWalkableData { get; private set; }

        /// <summary>
        ///     Gets the Disctionary of Lists containing only the named tgt tiles locations.
        /// </summary>
        public Dictionary<string, List<Vector2>> TgtTilesLocations { get; private set; }

        /// <summary>
        ///     Gets a value that can convert World coordinate to Grid coordinate.
        /// </summary>
        public float WorldToGridConvertor => TileStructure.TileToWorldConversion / TileStructure.TileToGridConversion;

        /// <summary>
        ///     Converts the <see cref="AreaInstance" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            if (ImGui.TreeNode("Environment Info"))
            {
                ImGuiHelper.IntPtrToImGui("Address", this.environmentPtr.First);
                if (ImGui.TreeNode($"All Environments ({this.environments.Count})###AllEnvironments"))
                {
                    for (var i = 0; i < this.environments.Count; i++)
                    {
                        if (ImGui.Selectable($"{this.environments[i]}"))
                        {
                            ImGui.SetClipboardText($"{this.environments[i]}");
                        }
                    }

                    ImGui.TreePop();
                }

                foreach (var eCache in this.EntityCaches)
                {
                    eCache.ToImGui();
                }

                ImGui.TreePop();
            }

            ImGui.Text($"Area Hash: {this.AreaHash}");
            ImGui.Text($"Monster Level: {this.CurrentAreaLevel}");
            if (ImGui.TreeNode("Terrain Metadata"))
            {
                ImGui.Text($"Total Tiles: {this.TerrainMetadata.TotalTiles}");
                ImGui.Text($"Tiles Data Pointer: {this.TerrainMetadata.TileDetailsPtr}");
                ImGui.Text($"Tiles Height Multiplier: {this.TerrainMetadata.TileHeightMultiplier}");
                ImGui.Text($"Grid Walkable Data: {this.TerrainMetadata.GridWalkableData}");
                ImGui.Text($"Grid Landscape Data: {this.TerrainMetadata.GridLandscapeData}");
                ImGui.Text($"Data Bytes Per Row (for Walkable/Landscape Data): {this.TerrainMetadata.BytesPerRow}");
                ImGui.TreePop();
            }

            if (this.Player.TryGetComponent<Render>(out var pPos))
            {
                var y = (int)pPos.GridPosition.Y;
                var x = (int)pPos.GridPosition.X;
                if (y < this.GridHeightData.Length && y >= 0)
                {
                    if (x < this.GridHeightData[0].Length && x >= 0)
                    {
                        ImGui.Text($"Player Pos (y:{y / TileStructure.TileToGridConversion}, x:{x / TileStructure.TileToGridConversion}) to Terrain Height: " +
                                   $"{this.GridHeightData[y][x]}");
                    }
                }
            }

            ImGui.Text($"Total Entity Removed Per Area: {this.totalEntityRemoved}");
            ImGui.Text($"Entities in network bubble: {this.NetworkBubbleEntityCount}");
            this.EntitiesWidget("Awake", this.AwakeEntities);
        }

        /// <inheritdoc />
        protected override void CleanUpData()
        {
            this.Cleanup(false);
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<AreaInstanceOffsets>(this.Address);

            if (hasAddressChanged)
            {
                this.Cleanup(true);
                this.TerrainMetadata = data.TerrainMetadata;
                this.CurrentAreaLevel = data.CurrentAreaLevel;
                this.AreaHash = $"{data.CurrentAreaHash:X}";
                this.GridWalkableData = reader.ReadStdVector<byte>(
                    this.TerrainMetadata.GridWalkableData);
                this.GridHeightData = this.GetTerrainHeight();
                this.TgtTilesLocations = this.GetTgtFileData();
            }

            this.UpdateEnvironmentAndCaches(data.Environments);
            this.ServerDataObject.Address = data.PlayerInfo.ServerDataPtr;
            this.Player.Address = data.PlayerInfo.LocalPlayerPtr;
            this.UpdateEntities(data.Entities.AwakeEntities, this.AwakeEntities, true);
        }

        private void UpdateEnvironmentAndCaches(StdVector environments)
        {
            this.environments.Clear();
            var reader = Core.Process.Handle;
            this.environmentPtr = environments;
            var envData = reader.ReadStdVector<EnvironmentStruct>(environments);
            for (var i = 0; i < envData.Length; i++)
            {
                this.environments.Add(envData[i].Key);
            }

            this.EntityCaches.ForEach((eCache) => eCache.UpdateState(this.environments));
        }

        private void AddToCacheParallel(EntityNodeKey key, string path)
        {
            for (var i = 0; i < this.EntityCaches.Count; i++)
            {
                if (this.EntityCaches[i].TryAddParallel(key, path))
                {
                    break;
                }
            }
        }

        private void UpdateEntities(
            StdMap ePtr,
            ConcurrentDictionary<EntityNodeKey, Entity> data,
            bool addToCache)
        {
            var reader = Core.Process.Handle;
            var dc = Core.GHSettings.DisableAllCounters;
            var areaDetails = Core.States.InGameStateObject.CurrentWorldInstance.AreaDetails;
            if (Core.GHSettings.DisableEntityProcessingInTownOrHideout &&
                (areaDetails.IsHideout || areaDetails.IsTown))
            {
                this.NetworkBubbleEntityCount = 0;
                return;
            }

            this.uselesssEntities = 0;
            Parallel.ForEach(data, (kv) =>
            {
                if (kv.Value.IsValid)
                {
                    if (dc == false && kv.Value.EntityState == EntityStates.Useless)
                    {
                        Interlocked.Increment(ref this.uselesssEntities);
                    }
                }
                else
                {
                    if (kv.Value.EntityState == EntityStates.MonsterFriendly ||
                        (kv.Value.CanExplodeOrRemovedFromGame &&
                        this.Player.DistanceFrom(kv.Value) < AreaInstanceConstants.NETWORK_BUBBLE_RADIUS))
                    {
                        // This logic isn't perfect in case something happens to the entity before
                        // we can cache the location of that entity. In that case we will just
                        // delete that entity anyway. This activity is fine as long as it doesn't
                        // crash the GameHelper. This logic is to detect if entity exploded due to
                        // explodi-chest or just left the network bubble since entity leaving network
                        // bubble is same as entity exploded.
                        data.TryRemove(kv.Key, out _);
                        if (dc == false)
                        {
                            Interlocked.Increment(ref this.totalEntityRemoved);
                        }
                    }
                }

                kv.Value.IsValid = false;
            });

            this.NetworkBubbleEntityCount = reader.ReadStdMap<EntityNodeKey, EntityNodeValue>(ePtr, 100000, dc == false, (key, value) =>
            {
                if (!Core.GHSettings.ProcessAllRenderableEntities && !EntityFilter.IgnoreVisualsAndDecorations(key))
                {
                    return false;
                }

                if (data.TryGetValue(key, out var entity))
                {
                    entity.Address = value.EntityPtr;
                }
                else
                {
                    entity = new Entity(value.EntityPtr);
                    if (!string.IsNullOrEmpty(entity.Path))
                    {
                        data[key] = entity;
                        if (addToCache)
                        {
                            this.AddToCacheParallel(key, entity.Path);
                        }
                    }
                    else
                    {
                        entity = null;
                    }
                }

                entity?.UpdateNearby(this.Player);
                return true;
            });
        }

        private Dictionary<string, List<Vector2>> GetTgtFileData()
        {
            var reader = Core.Process.Handle;
            var tileData = reader.ReadStdVector<TileStructure>(this.TerrainMetadata.TileDetailsPtr);
            var ret = new Dictionary<string, List<Vector2>>();
            object mylock = new();
            Parallel.For(
                0,
                tileData.Length,
                // happens on every thread, rather than every iteration.
                () => new Dictionary<string, List<Vector2>>(),
                // happens on every iteration.
                (tileNumber, _, localstate) =>
                {
                    var tile = tileData[tileNumber];
                    var tgtFile = reader.ReadMemory<TgtFileStruct>(tile.TgtFilePtr);
                    var tgtName = reader.ReadStdWString(tgtFile.TgtPath);
                    if (string.IsNullOrEmpty(tgtName))
                    {
                        return localstate;
                    }

                    if (tile.RotationSelector % 2 == 0)
                    {
                        tgtName += $"x:{tile.tileIdX}-y:{tile.tileIdY}";
                    }
                    else
                    {
                        tgtName += $"x:{tile.tileIdY}-y:{tile.tileIdX}";
                    }

                    var loc = new Vector2
                    {
                        Y = (tileNumber / this.TerrainMetadata.TotalTiles.X) * TileStructure.TileToGridConversion,
                        X = (tileNumber % this.TerrainMetadata.TotalTiles.X) * TileStructure.TileToGridConversion
                    };

                    if (localstate.ContainsKey(tgtName))
                    {
                        localstate[tgtName].Add(loc);
                    }
                    else
                    {
                        localstate[tgtName] = new() { loc };
                    }

                    return localstate;
                },
                finalresult => // happens on every thread, rather than every iteration.
                {
                    lock (mylock)
                    {
                        foreach (var kv in finalresult)
                        {
                            if (!ret.TryGetValue(kv.Key, out var value))
                            {
                                value = new();
                                ret[kv.Key] = value;
                            }

                            value.AddRange(kv.Value);
                        }
                    }
                });

            return ret;
        }

        private float[][] GetTerrainHeight()
        {
            var rotationHelper = Core.RotationSelector.Values;
            var rotatorMetrixHelper = Core.RotatorHelper.Values;
            var reader = Core.Process.Handle;
            var tileData = reader.ReadStdVector<TileStructure>(this.TerrainMetadata.TileDetailsPtr);
            var subTileHeightCache = new ConcurrentDictionary<IntPtr, sbyte[]>();
            Parallel.For(0, tileData.Length, index =>
            {
                var val = tileData[index];
                subTileHeightCache.AddOrUpdate(
                    val.SubTileDetailsPtr,
                    addr =>
                    {
                        var subTileData = reader.ReadMemory<SubTileStruct>(addr);
                        var subTileHeightData = reader.ReadStdVector<sbyte>(subTileData.SubTileHeight);
                        return subTileHeightData;
                    },
                    (addr, data) => data);
            });

            var gridSizeX = (int)this.TerrainMetadata.TotalTiles.X * TileStructure.TileToGridConversion;
            var gridSizeY = (int)this.TerrainMetadata.TotalTiles.Y * TileStructure.TileToGridConversion;
            var result = new float[gridSizeY][];
            Parallel.For(0, gridSizeY, y =>
            {
                result[y] = new float[gridSizeX];
                for (var x = 0; x < gridSizeX; x++)
                {
                    var tileDataIndex = (y / TileStructure.TileToGridConversion) * ((int)this.TerrainMetadata.TotalTiles.X);
                    tileDataIndex += x / TileStructure.TileToGridConversion;
                    var subTileHeight = 0;
                    if (tileDataIndex < tileData.Length)
                    {
                        var mytiledata = tileData[tileDataIndex];
                        if (subTileHeightCache.TryGetValue(mytiledata.SubTileDetailsPtr, out var subTileHeightsArray))
                        {
                            var gridXremaining = x % TileStructure.TileToGridConversion;
                            var gridYremaining = y % TileStructure.TileToGridConversion;

                            // 8 is the max number in rotationHelper array. 8 * 3 = 24.
                            // According to the game, this number should never go above 24.
                            var rotationSelected = mytiledata.RotationSelector < rotationHelper.Length ?
                                rotationHelper[mytiledata.RotationSelector] * 3 : 24;
                            rotationSelected = rotationSelected > 24 ? 24 : rotationSelected;

                            var rotatorMetrix = new int[4]
                            {
                                TileStructure.TileToGridConversion - gridXremaining - 1,
                                gridXremaining,
                                TileStructure.TileToGridConversion - gridYremaining - 1,
                                gridYremaining
                            };

                            int rotatedX0 = rotatorMetrixHelper[rotationSelected];
                            int rotatedX1 = rotatorMetrixHelper[rotationSelected + 1];
                            int rotatedY0 = rotatorMetrixHelper[rotationSelected + 2];
                            var rotatedY1 = 0;
                            if (rotatedX0 == 0)
                            {
                                rotatedY1 = 2;
                            }

                            var finalRotatedX = rotatorMetrix[rotatedX0 * 2 + rotatedX1];
                            var finalRotatedY = rotatorMetrix[rotatedY0 + rotatedY1];
                            subTileHeight = this.GetSubTerrainHeight(subTileHeightsArray, finalRotatedY, finalRotatedX);
                            result[y][x] = mytiledata.TileHeight * (float)this.TerrainMetadata.TileHeightMultiplier + subTileHeight;
                            result[y][x] = result[y][x] * TerrainStruct.TileHeightFinalMultiplier * -1;
                        }
                    }
                }
            });

            return result;
        }

        private int GetSubTerrainHeight(sbyte[] subterrainheightarray, int y, int x)
        {
            if (x < 0 || y < 0 || x >= TileStructure.TileToGridConversion || y >= TileStructure.TileToGridConversion)
            {
                return 0;
            }

            var index = y * TileStructure.TileToGridConversion + x;
            if (subterrainheightarray.Length == 0)
            {
                return 0;
            }

#if true
            var arrayLength = subterrainheightarray.Length;
            switch (arrayLength)
            {
                case 0x01:
                    return subterrainheightarray[0];
                case 0x45:
                    return subterrainheightarray[(byte)subterrainheightarray[(index >> 3) + 2] >> ((index & 7) << 0) & 0x01];
                case 0x89:
                    return subterrainheightarray[(byte)subterrainheightarray[(index >> 2) + 4] >> ((index & 3) << 1) & 0x03];
                case 0x119:
                    return subterrainheightarray[(byte)subterrainheightarray[(index >> 1) + 16] >> ((index & 1) << 2) & 0xF];
                default:
                    if (arrayLength > index)
                    {
                        return subterrainheightarray[index];
                    }
                    else
                    {
                        throw new Exception($"SubterrainHeightArray Length {arrayLength} less-than index {index}");
                    }
            }
#else
            return 0;
#endif
        }

        /// <summary>
        ///     knows how to clean up the <see cref="AreaInstance"/> class.
        /// </summary>
        /// <param name="isAreaChange">
        ///     true in case it's a cleanup due to area change otherwise false.
        /// </param>
        private void Cleanup(bool isAreaChange)
        {
            this.totalEntityRemoved = 0;
            this.uselesssEntities = 0;
            this.AwakeEntities.Clear();
            this.EntityCaches.ForEach((e) => e.Clear());

            if (!isAreaChange)
            {
                this.environmentPtr = default;
                this.environments.Clear();
                this.CurrentAreaLevel = 0;
                this.AreaHash = string.Empty;
                this.ServerDataObject.Address = IntPtr.Zero;
                this.Player.Address = IntPtr.Zero;
                this.NetworkBubbleEntityCount = 0;
                this.TerrainMetadata = default;
                this.GridHeightData = Array.Empty<float[]>();
                this.GridWalkableData = Array.Empty<byte>();
                this.TgtTilesLocations.Clear();
            }
        }

        private void EntitiesWidget(string label, ConcurrentDictionary<EntityNodeKey, Entity> data)
        {
            if (ImGui.TreeNode($"{label} Entities ({data.Count})###${label} Entities"))
            {
                if (ImGui.RadioButton("Filter by Id           ", this.filterBy == 0))
                {
                    this.filterBy = 0;
                }

                ImGui.SameLine();
                if (ImGui.RadioButton("Filter by Path           ", this.filterBy == 1))
                {
                    this.filterBy = 1;
                }

                ImGui.SameLine();
                if (ImGui.RadioButton("Filter by Rarity", this.filterBy == 2))
                {
                    this.filterBy = 2;
                }

                switch (this.filterBy)
                {
                    case 0:
                        ImGui.InputText("Entity Id Filter", ref this.entityIdFilter, 10, ImGuiInputTextFlags.CharsDecimal);
                        break;
                    case 1:
                        ImGui.InputText("Entity Path Filter", ref this.entityPathFilter, 100);
                        break;
                    case 2:
                        ImGuiHelper.EnumComboBox("Entity Rarity Filter", ref this.entityRarityFilter);
                        break;
                    default:
                        break;
                }

                foreach (var entity in data)
                {
                    switch (this.filterBy)
                    {
                        case 0:
                            if (!(string.IsNullOrEmpty(this.entityIdFilter) ||
                                $"{entity.Key.id}".Contains(this.entityIdFilter)))
                            {
                                continue;
                            }

                            break;
                        case 1:
                            if (!(string.IsNullOrEmpty(this.entityPathFilter) ||
                                entity.Value.Path.ToLower().Contains(this.entityPathFilter.ToLower())))
                            {
                                continue;
                            }

                            break;
                        case 2:
                            if (!(entity.Value.TryGetComponent(out ObjectMagicProperties omp) &&
                                omp.Rarity == this.entityRarityFilter))
                            {
                                continue;
                            }

                            break;
                        default:
                            break;
                    }

                    var isClicked = ImGui.TreeNode($"{entity.Value.Id} {entity.Value.Path}");
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"dump##{entity.Key}"))
                    {
                        string path = entity.Value.Path;
                        string safeName = string.IsNullOrWhiteSpace(path) ? $"{entity.Value.Id}" : path.Replace("/", "_");

                        // Helpers to robustly read "name" from unknown tuple/dictionary shapes.
                        static string TryGetKeyName(object entry)
                        {
                            if (entry == null) return null;

                            // Try Key (e.g., KeyValuePair<,>)
                            var keyProp = entry.GetType().GetProperty("Key", BindingFlags.Public | BindingFlags.Instance);
                            if (keyProp != null)
                            {
                                var keyVal = keyProp.GetValue(entry);
                                return keyVal?.ToString();
                            }

                            // Try Item1 (e.g., ValueTuple<string, ...> or Tuple<string,...>)
                            var item1Prop = entry.GetType().GetProperty("Item1", BindingFlags.Public | BindingFlags.Instance);
                            if (item1Prop != null)
                            {
                                var item1Val = item1Prop.GetValue(entry);
                                return item1Val?.ToString();
                            }

                            // Fallback
                            return entry.ToString();
                        }

                        // Build a dictionary<string,int> from an unknown KVP/tuple-like collection
                        static Dictionary<string, int> ToStringIntMap(IEnumerable<object> entries)
                        {
                            var result = new Dictionary<string, int>();
                            if (entries == null) return result;

                            foreach (var e in entries)
                            {
                                if (e == null) continue;

                                // Try Key/Value properties first (KeyValuePair-like)
                                var t = e.GetType();
                                var keyProp = t.GetProperty("Key", BindingFlags.Public | BindingFlags.Instance);
                                var valProp = t.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
                                if (keyProp != null && valProp != null)
                                {
                                    var k = keyProp.GetValue(e)?.ToString();
                                    if (k == null) continue;
                                    var vObj = valProp.GetValue(e);
                                    if (vObj == null) continue;
                                    int v;
                                    if (vObj is int vi) v = vi;
                                    else if (!int.TryParse(vObj.ToString(), out v)) continue;
                                    result[k] = v;
                                    continue;
                                }

                                // Otherwise, try tuple pattern: Item1 (key), Item2 (value)
                                var item1 = t.GetProperty("Item1", BindingFlags.Public | BindingFlags.Instance)?.GetValue(e);
                                var item2 = t.GetProperty("Item2", BindingFlags.Public | BindingFlags.Instance)?.GetValue(e);
                                if (item1 != null && item2 != null)
                                {
                                    var k = item1.ToString();
                                    int v;
                                    if (item2 is int vi2) v = vi2;
                                    else if (!int.TryParse(item2.ToString(), out v)) continue;
                                    result[k] = v;
                                    continue;
                                }

                                // Last resort: just ToString the whole entry as a key with value 1
                                var s = e.ToString();
                                if (!string.IsNullOrEmpty(s) && !result.ContainsKey(s))
                                    result[s] = 1;
                            }
                            return result;
                        }

                        // -------- ObjectMagicProperties --------
                        List<string> ompMods = null;
                        Dictionary<string, int> ompModStats = null;
                        string rarityStr = null;

                        if (entity.Value.TryGetComponent<ObjectMagicProperties>(out var omp))
                        {
                            // Mods: enumerate without relying on Count
                            if (omp.Mods != null)
                            {
                                var list = new List<string>();
                                foreach (var m in omp.Mods)
                                {
                                    var name = TryGetKeyName(m);
                                    if (!string.IsNullOrEmpty(name))
                                        list.Add(name);
                                }
                                if (list.Count > 0) ompMods = list;
                            }

                            // ModStats: enumerate unknown shapes into string->int
                            if (omp.ModStats != null)
                            {
                                // Cast to object-enumerable safely
                                var objs = omp.ModStats.Cast<object>();
                                var map = ToStringIntMap(objs);
                                if (map.Count > 0) ompModStats = map;
                            }

                            rarityStr = omp.Rarity.ToString();
                        }

                        // -------- Buffs --------
                        List<string> buffs = null;
                        if (entity.Value.TryGetComponent<Buffs>(out var buf) && buf.StatusEffects != null)
                        {
                            var list = new List<string>();
                            foreach (var se in buf.StatusEffects) // don’t check Count; enumerate directly
                            {
                                // se might be KeyValuePair<string, T>
                                var name = TryGetKeyName(se);
                                if (!string.IsNullOrEmpty(name))
                                    list.Add(name);
                            }
                            if (list.Count > 0) buffs = list;
                        }

                        // -------- Components list --------
                        var components = entity.Value.GetComponentNames()?.ToList();

                        // -------- Stats component (enum-ish keys -> string) --------
                        Dictionary<string, int> statsByItems = null;
                        Dictionary<string, int> statsByBuffs = null;
                        if (entity.Value.TryGetComponent<Stats>(out var stats))
                        {
                            if (stats.StatsChangedByItems != null)
                            {
                                var map = ToStringIntMap(stats.StatsChangedByItems.Cast<object>());
                                if (map.Count > 0) statsByItems = map;
                            }

                            if (stats.StatsChangedByBuffAndActions != null)
                            {
                                var map = ToStringIntMap(stats.StatsChangedByBuffAndActions.Cast<object>());
                                if (map.Count > 0) statsByBuffs = map;
                            }
                        }

                        // -------- Actor component --------
                        List<String> activeSkills = null;
                        if (entity.Value.TryGetComponent<Actor>(out var actor) && actor.ActiveSkills != null)
                        {
                            var list = new List<string>();
                            foreach (var kv in actor.ActiveSkills) // enumerate directly
                            {
                                var name = TryGetKeyName(kv);
                                if (!string.IsNullOrEmpty(name))
                                    list.Add(name);
                            }
                            if (list.Count > 0) activeSkills = list;
                        }

                        // -------- Positions (Render) --------
                        Vector3? worldPos = null;
                        Vector2? gridPos = null;
                        if (entity.Value.TryGetComponent<Render>(out var render))
                        {
                            worldPos = new Vector3(render.WorldPosition.X, render.WorldPosition.Y, render.WorldPosition.Z);
                            gridPos = new Vector2(render.GridPosition.X, render.GridPosition.Y);
                        }

                        // -------- Build payload --------
                        var payload = new
                        {
                            EntityId = entity.Value.Id,
                            Key = new { entity.Key.id },
                            Path = path,
                            Rarity = rarityStr,
                            Position = new
                            {
                                World = worldPos.HasValue ? new { X = worldPos.Value.X, Y = worldPos.Value.Y, Z = worldPos.Value.Z } : null,
                                Grid = gridPos.HasValue ? new { X = gridPos.Value.X, Y = gridPos.Value.Y } : null
                            },
                            ObjectMagicProperties = new
                            {
                                Mods = ompMods,
                                ModStats = ompModStats
                            },
                            Buffs = buffs,
                            Components = components,
                            Stats = new
                            {
                                StatsChangedByItems = statsByItems,
                                StatsChangedByBuffAndActions = statsByBuffs
                            },
                            Actor = new
                            {
                                ActiveSkillsOnEntity = activeSkills
                            }
                        };

                        // -------- Serialize (pretty) --------
                        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
                        string json = JsonSerializer.Serialize(payload, jsonOptions);

                        // -------- Write to file + clipboard --------
                        Directory.CreateDirectory("entity_dumps");
                        var outPath = Path.Join("entity_dumps", safeName + ".json");
                        File.WriteAllText(outPath, json);
                        ImGui.SetClipboardText(json);
                    }
                    ImGuiHelper.ToolTip("Dump entity data as pretty-printed JSON (also copied to clipboard).");
                    if (isClicked)
                    {
                        entity.Value.ToImGui();
                        ImGui.TreePop();
                    }

                    if (entity.Value.IsValid &&
                        entity.Value.TryGetComponent<Render>(out var eRender))
                    {
                        switch (this.filterBy)
                        {
                            case 0:
                                ImGuiHelper.DrawText(eRender.WorldPosition, $"ID: {entity.Key.id}");
                                break;
                            case 1:
                                ImGuiHelper.DrawText(eRender.WorldPosition, $"Path: {entity.Value.Path}");
                                break;
                            default:
                                break;
                        }
                    }
                }

                ImGui.TreePop();
            }
        }

        private IEnumerator<Wait> OnPerFrame()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.PerFrameDataUpdate);
                if (this.Address != IntPtr.Zero)
                {
                    this.UpdateData(false);
                }
            }
        }
    }
}
