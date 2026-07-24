// <copyright file="ImportantUiElements.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.States.InGameStateObjects
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Numerics;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;
    using Coroutine;
    using CoroutineEvents;
    using GameHelper.Cache;
    using GameHelper.Utils;
    using GameOffsets.Natives;
    using GameOffsets.Objects.States.InGameState;
    using GameOffsets.Objects.UiElement;
    using ImGuiNET;
    using RemoteEnums;
    using UiElement;

    /// <summary>
    ///     This is actually UiRoot main child which contains
    ///     all the UiElements (100+). Normally it's at index 1 of UiRoot.
    ///     This class is created because traversing childrens of
    ///     UiRoot is a slow process that requires lots of memory reads.
    ///     Drawback:
    ///     1: Every league/patch the offsets needs to be updated.
    ///     Offsets used over here are very unstable.
    ///     2: More UiElements we are tracking = More memory read
    ///     every X seconds just to update IsVisible :(.
    /// </summary>
    public class ImportantUiElements : RemoteObjectBase
    {
        private static readonly int[] WorldMapPanelChildPath = { 22, 0 };
        private static readonly int[] Act1PanelChildPath = { 22, 0, 0 };
        private static readonly int[] Act2PanelChildPath = { 22, 0, 1 };
        private static readonly int[] Act3PanelChildPath = { 22, 0, 2 };
        private static readonly int[] Act4PanelChildPath = { 22, 0, 3 };
        private static readonly int[] InterludePanelChildPath = { 22, 0, 5 };
        private static readonly int[] AtlasPanelChildPath = { 22, 0, 6 };
        private static readonly int[] AtlasSkillsPanelChildPath = { 25, 0 };
        private static readonly int[] CurrencyExchangePanelChildPath = { 114, 20, 6, 1 };
        private static readonly int[] LeftPanelCoopPath = { 22 };
        private static readonly int[] RightPanelCoopPath = { 23 };
        private static readonly int[] TempleConsoleChildPath = { 64, 0 };
        private const int AtlasMapCacheRefreshFrames = 20;
        private const int AtlasNodeBiomeIdOffset = 0x2CE;
        private const int AtlasNodeStatusByteOffset = 0x2CF;
        private const int AtlasNodeMapDataOffset = 0x2A0;

        // Atlas layout notes and offsets in this block are adapted from yokkenUA's Atlas plugin
        // reverse engineering (dfb52db through afecda4), based on live-memory inspection and
        // Ghidra analysis. Keep the behavioral notes with the offsets when updating them.
        // The panel owns a flat vector of {unknown, source grid, target grid} connection edges.
        private const int AtlasNodeConnectionsVectorOffset = 0x5A8;
        private const int AtlasNodeContentNameOffset = 0x290;

        // +0x350 is the separate vector<u32> content-token store. Token low 16 bits identify the
        // effect/stat row and its data can change between game patches.
        private const int AtlasNodeContentVecOffset = 0x350;

        // Unlike culled UI badge children, +0x368/+0x370 is a persistent sorted vector<u8> of
        // EndgameMapContent row indices. AtlasMapNodeWidget's constructor fills it from server
        // chunk cell-state, so it survives fog, off-screen culling, and sea nodes. The public id
        // is row + 100. This vector does not contain the +0x350 token content.
        private const int AtlasNodeBadgeVecBeginOffset = 0x368;
        private const int AtlasNodeBadgeVecEndOffset = 0x370;
        private const int AtlasNodeBadgeRowToContentId = 100;
        private const int AtlasNodeBadgeContentIdOffset = 0x188;
        private const byte AtlasNodeAccessibleBit = 0x01;
        private const byte AtlasNodeCompletedBit = 0x02;
        private const int UiElementBaseFlagsOffset = 0x180;
        private const uint IsVisibleMask = 0x800;
        private const uint AtlasCurrentNodeMarkerFp = 0x502EF3;
        private const uint AtlasMapNodeFp = 0x542EF3;

        // A mist-shrouded King in the Mists node uses the normal 0x542EF3 fingerprint with bit 20
        // cleared. Its data layout is otherwise identical, so both fingerprints must be accepted.
        private const uint AtlasMistNodeFp = 0x442EF3;

        // A sea ship is an EndgameRegionActionButton in the atlas child list, not a map node.
        // Rows are 0=Breach, 1=Forest, 2=Ocean/ship, 3=Tower. Its grid coordinate identifies the
        // 16x16 chunk a logbook will reveal; those fogged nodes are already materialized and have
        // maps assigned, which is why Atlas2 can preview their nodes and leylines.
        private const int AtlasRegionButtonRowPtrOffset = 0x320;
        private const int AtlasRegionButtonGridOffset = 0x330;
        private const int AtlasRegionButtonRowIndexOffset = 0x338;
        private const int AtlasOceanRegionButtonRow = 2;
        private const int AtlasNodeMaxContentChildren = 64;
        private const int AtlasNodeMaxContentTokens = 64;

        private readonly UiElementParents rootCache;
        private readonly UiElementParents passiveSkillTreeCache;
        private readonly List<AtlasMapNode> atlasMaps = new();
        private readonly List<AtlasRegionButton> atlasOceanButtons = new();
        private readonly List<PlayerMarker> atlasMarkers = new();
        private int atlasMapCacheFrameCounter = int.MaxValue;
        private int cachedAtlasMapCount = -1;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct AtlasNodeConnectionEdgeOffsets
        {
            public int Unknown;
            public StdTuple2D<int> Source;
            public StdTuple2D<int> Target;
        }

        /// <summary>
        ///     Passive skill tree node Parent UI element.
        ///     UiRoot -> MainChild -> index 28 -> 1
        /// </summary>
        private UiElementBase passiveskilltreenodes;

        /// <summary>
        ///     Sekhemas Trial Map panel.
        ///     UiRoot MainChild -> child 1 -> child 84 -> child 0
        /// </summary>
        private UiElementBase sekhemasTrialMapPanel;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ImportantUiElements" /> class.
        /// </summary>
        /// <param name="address">
        ///     UiRoot 1st child address (starting from 0)
        ///     or <see cref="IntPtr.Zero" /> in case UiRoot has no child.
        /// </param>
        internal ImportantUiElements(IntPtr address)
            : base(address)
        {
            this.rootCache = new(null, GameStateTypes.InGameState, GameStateTypes.EscapeState, "Root");
            this.passiveSkillTreeCache = new(this.rootCache, GameStateTypes.InGameState, GameStateTypes.EscapeState, "PassiveSkillTree");

            this.passiveskilltreenodes = new(IntPtr.Zero, this.rootCache);
            this.sekhemasTrialMapPanel = new(IntPtr.Zero, this.rootCache);
            this.LargeMap = new(IntPtr.Zero, this.rootCache);
            this.MiniMap = new(IntPtr.Zero, this.rootCache);
            this.WorldMapPanel = new(IntPtr.Zero, this.rootCache);
            this.Act1 = new(IntPtr.Zero, this.rootCache);
            this.Act2 = new(IntPtr.Zero, this.rootCache);
            this.Act3 = new(IntPtr.Zero, this.rootCache);
            this.Act4 = new(IntPtr.Zero, this.rootCache);
            this.Interlude = new(IntPtr.Zero, this.rootCache);
            this.Atlas = new(IntPtr.Zero, this.rootCache);
            this.AtlasSkillsPanel = new(IntPtr.Zero, this.rootCache);
            this.TempleConsole = new(IntPtr.Zero, this.rootCache);
            this.CurrencyExchangePanel = new(IntPtr.Zero, this.rootCache);
            this.LeftPanel = new(IntPtr.Zero, this.rootCache);
            this.RightPanel = new(IntPtr.Zero, this.rootCache);
            this.ChatParent = new(IntPtr.Zero, this.rootCache);

            this.SkillTreeNodesUiElements = new();
            Core.CoroutinesRegistrar.Add(CoroutineHandler.Start(
                this.OnPerFrame(), "[InGameState] Update ImportantUiElements", priority: int.MaxValue - 3));
        }

        /// <summary>
        ///     Gets the LargeMap UiElement.
        ///     UiRoot -> MainChild -> 3rd index -> 1nd index.
        /// </summary>
        public LargeMapUiElement LargeMap { get; }

        /// <summary>
        ///     Gets the MiniMap UiElement.
        ///     UiRoot -> MainChild -> 3rd index -> 2nd index.
        /// </summary>
        public MapUiElement MiniMap { get; }

        /// <summary>
        ///     Gets the checkpoint / world-travel map panel UiElement.
        ///     It is only <see cref="UiElementBase.IsVisible" /> while that screen is open
        ///     (opened by interacting with a checkpoint). The in-area LargeMap stays visible
        ///     underneath it, so consumers gate on this to tell the two apart.
        ///     GameUi -> child 22 -> child 0.
        /// </summary>
        public UiElementBase WorldMapPanel { get; }

        /// <summary>
        ///     Gets the Act 1 tab under the checkpoint / world-travel map panel.
        ///     GameUi -> child 22 -> child 0 -> child 0.
        /// </summary>
        public UiElementBase Act1 { get; }

        /// <summary>
        ///     Gets the Act 2 tab under the checkpoint / world-travel map panel.
        ///     GameUi -> child 22 -> child 0 -> child 1.
        /// </summary>
        public UiElementBase Act2 { get; }

        /// <summary>
        ///     Gets the Act 3 tab under the checkpoint / world-travel map panel.
        ///     GameUi -> child 22 -> child 0 -> child 2.
        /// </summary>
        public UiElementBase Act3 { get; }

        /// <summary>
        ///     Gets the Act 4 tab under the checkpoint / world-travel map panel.
        ///     GameUi -> child 22 -> child 0 -> child 3.
        /// </summary>
        public UiElementBase Act4 { get; }

        /// <summary>
        ///     Gets the Interlude tab under the checkpoint / world-travel map panel.
        ///     GameUi -> child 22 -> child 0 -> child 5.
        /// </summary>
        public UiElementBase Interlude { get; }

        /// <summary>
        ///     Gets the Atlas tab under the checkpoint / world-travel map panel.
        ///     GameUi -> child 22 -> child 0 -> child 6.
        /// </summary>
        public UiElementBase Atlas { get; }

        /// <summary>
        ///     Gets the Atlas Skills panel UiElement.
        ///     It is only <see cref="UiElementBase.IsVisible" /> while that panel is open.
        ///     GameUi -> child 25 -> child 0 (child 25 itself is always visible).
        /// </summary>
        public UiElementBase AtlasSkillsPanel { get; }

        /// <summary>
        ///     Gets the Temple Console panel UiElement.
        ///     It is only <see cref="UiElementBase.IsVisible" /> while that panel is open.
        ///     GameUi -> child 64 -> child 0.
        /// </summary>
        public UiElementBase TempleConsole { get; }

        /// <summary>
        ///     Gets the current Atlas map nodes exposed for plugins.
        /// </summary>
        public IReadOnlyList<AtlasMapNode> AtlasMaps => this.atlasMaps;

        /// <summary>Gets the Uncharted Waters region buttons currently materialized by the atlas panel.</summary>
        public IReadOnlyList<AtlasRegionButton> AtlasOceanButtons => this.atlasOceanButtons;

        /// <summary>
        ///     Gets the "you are here" marker children on the Atlas (fp 0x502EF3).
        ///     These are not real map nodes — they render on the player's current
        ///     node. Exposed so plugins can draw a position indicator.
        /// </summary>
        public IReadOnlyList<PlayerMarker> AtlasMarkers => this.atlasMarkers;

        /// <summary>
        ///     Gets the currently-open left-side panel UiElement (character, skills, etc.).
        ///     It is only <see cref="UiElementBase.IsVisible" /> while such a panel is open;
        ///     the backing pointer is null when no left panel is open.
        ///     UiRoot manager -> 0x6D8.
        /// </summary>
        public UiElementBase LeftPanel { get; }

        /// <summary>
        ///     Gets the currently-open right-side panel UiElement (inventory, vendor/shop, stash, etc.).
        ///     It is only <see cref="UiElementBase.IsVisible" /> while such a panel is open;
        ///     the backing pointer is null when no right panel is open.
        ///     UiRoot manager -> 0x6E0.
        /// </summary>
        public UiElementBase RightPanel { get; }

        /// <summary>
        ///     Gets the Currency Exchange item-list panel.
        ///     Its visibility reliably indicates that the Currency Exchange screen is open.
        ///     GameUi -> child 114 -> 20 -> 6 -> 1.
        /// </summary>
        public UiElementBase CurrencyExchangePanel { get; }

        /// <summary>
        ///     Gets a value indicating whether any large blocking panel is currently open
        ///     (a left/right side panel, the passive skill tree, or the world-travel map).
        ///     Useful for overlays that should hide world-space drawing while the player is in a menu.
        /// </summary>
        public bool IsAnyLargePanelOpen =>
            this.LeftPanel.IsVisible ||
            this.RightPanel.IsVisible ||
            this.WorldMapPanel.IsVisible ||
            this.AtlasSkillsPanel.IsVisible ||
            this.TempleConsole.IsVisible ||
            this.CurrencyExchangePanel.IsVisible ||
            this.SekhemasTrialMapPanel.IsVisible ||
            this.IsPassiveSkillTreeOpen;

        /// <summary>
        ///     Gets a value indicating whether the passive skill tree is currently open.
        ///     Gated on the visibility of the tree's node container (UiRoot manager -> 0x730 -> child 2),
        ///     not on <see cref="SkillTreeNodesUiElements" />: in v0.5.x the per-node SkillInfo pointer
        ///     reads null so that list never populates, but the container's visibility bit is reliable.
        /// </summary>
        public bool IsPassiveSkillTreeOpen => this.passiveskilltreenodes.IsVisible;

        /// <summary>
        ///     Gets the Sekhemas Trial Map panel UiElement.
        ///     Visible only during Trial of the Sekhemas.
        ///     UiRoot MainChild -> child 1 -> child 84 -> child 0.
        /// </summary>
        public UiElementBase SekhemasTrialMapPanel => this.sekhemasTrialMapPanel;

        /// <summary>
        ///     Gets the Chat UiElement parent.
        ///     UiRoot -> MainChild -> this index keeps moving around
        /// </summary>
        public ChatParentUiElement ChatParent { get; }

        /// <summary>
        ///     Gets the skill tree nodes UI Elements.
        ///     UiRoot -> MainChild -> index 28 -> 2 -> all childrens;
        /// </summary>
        public List<SkillTreeNodeUiElement> SkillTreeNodesUiElements { get; }

        internal override void ToImGui()
        {
            this.displayParentsCache();
            base.ToImGui();
            ImGui.Text($"Passive Skill Tree Panel Visible: {this.passiveskilltreenodes.IsVisible}");
            ImGui.Text($"Total Atlas Maps: {this.AtlasMaps.Count}");
            if (ImGui.TreeNode("Atlas Maps"))
            {
                foreach (var map in this.AtlasMaps)
                {
                    if (ImGui.TreeNode($"{map.Index}: {map.DisplayName}##AtlasMap{map.Index}"))
                    {
                        ImGui.Text($"Internal Id: {map.MapId}");
                        ImGui.Text($"Address: 0x{map.Address.ToInt64():X}");
                        ImGui.Text($"Grid Position: {map.GridPosition.X}, {map.GridPosition.Y}");
                        ImGui.Text($"Biome: {map.BiomeId}");
                        ImGui.Text($"State: {map.State}");
                        ImGui.Text($"Type: {map.Type}");
                        ImGui.Text($"Tags: {string.Join(", ", map.Tags)}");
                        ImGui.Text($"Connected Nodes: {map.ConnectedGridPositions.Count}");
                        foreach (var connected in map.ConnectedGridPositions)
                        {
                            ImGui.Text($"- {connected.X}, {connected.Y}");
                        }

                        ImGui.Text($"Badge UI Children: {map.BadgeCount}");
                        foreach (var badgeAddress in map.BadgeAddresses)
                        {
                            ImGui.Text($"- 0x{badgeAddress.ToInt64():X}");
                        }

                        ImGui.Text($"Badge Names: {map.ContentNames.Count}");
                        foreach (var badge in map.ContentNames)
                        {
                            ImGui.Text($"- {badge}");
                        }

                        ImGui.Text($"Content Tokens: {map.ContentTokens.Count}");
                        string? deliriousContent = null;
                        foreach (var content in map.GetContentDisplayNames(includeUnmapped: true))
                        {
                            if (content.Contains("Delirious", StringComparison.Ordinal))
                            {
                                deliriousContent = content;
                                break;
                            }
                        }

                        var displayedDeliriousContent = false;
                        foreach (var token in map.ContentTokens)
                        {
                            if ((token & 0xFFFFu) == 0x685Au)
                            {
                                if (!displayedDeliriousContent && deliriousContent != null)
                                {
                                    ImGui.Text($"- {deliriousContent}");
                                    displayedDeliriousContent = true;
                                }

                                continue;
                            }

                            var tokenName = AtlasMapNode.GetContentTokenName(token);
                            ImGui.Text(tokenName != null ? $"- {tokenName} (0x{token:X8})" : $"- 0x{token:X8}");
                        }

                        ImGui.Text($"Badge Content Ids: {map.BadgeContentIds.Count}");
                        foreach (var id in map.BadgeContentIds)
                        {
                            var badgeName = AtlasMapNode.GetBadgeContentName(id);
                            ImGui.Text(badgeName != null ? $"- {badgeName} (0x{id:X8})" : $"- 0x{id:X8}");
                        }

                        ImGui.TreePop();
                    }
                }

                ImGui.TreePop();
            }

            ImGui.Text($"Player Marker: {(this.AtlasMarkers.Count > 0 ? $"on node {this.AtlasMarkers[0].MapNodeIndex}" : "none")}");
            if (ImGui.TreeNode("Player Marker"))
            {
                foreach (var marker in this.AtlasMarkers)
                {
                    var mapNode = this.atlasMaps.Find(m => m.Index == marker.MapNodeIndex);
                    var label = mapNode != null
                        ? $"{marker.Index} -> {mapNode.DisplayName} (idx {marker.MapNodeIndex})"
                        : $"{marker.Index}: Marker##AtlasMarker{marker.Index}";
                    if (ImGui.TreeNode($"{label}##AtlasMarker{marker.Index}"))
                    {
                        ImGui.Text($"Address: 0x{marker.Address.ToInt64():X}");
                        ImGui.Text($"Map Node Index: {marker.MapNodeIndex}");
                        if (mapNode != null)
                        {
                            ImGui.Text($"Map Node: {mapNode.DisplayName}");
                            ImGui.Text($"Map Node Grid: {mapNode.GridPosition.X}, {mapNode.GridPosition.Y}");
                        }
                        ImGui.TreePop();
                    }
                }

                ImGui.TreePop();
            }

            ImGui.Text($"Total Skill Tree Nodes: {this.SkillTreeNodesUiElements.Count}");
            if (ImGui.TreeNode("Skill Tree Nodes"))
            {
                for (var i = 0; i < this.SkillTreeNodesUiElements.Count; i++)
                {
                    var skillId = this.SkillTreeNodesUiElements[i].SkillGraphId;
                    ImGuiHelper.DisplayTextAndCopyOnClick($"index: {i}, skillId: {skillId}", $"{skillId}");
                    ImGui.GetForegroundDrawList().AddText(this.SkillTreeNodesUiElements[i].Position, 0xFF0000FF, $"{i}");
                }

                ImGui.TreePop();
            }
        }

        /// <inheritdoc />
        protected override void CleanUpData()
        {
            this.passiveskilltreenodes.Address = IntPtr.Zero;
            this.sekhemasTrialMapPanel.Address = IntPtr.Zero;
            this.MiniMap.Address = IntPtr.Zero;
            this.LargeMap.Address = IntPtr.Zero;
            this.WorldMapPanel.Address = IntPtr.Zero;
            this.Act1.Address = IntPtr.Zero;
            this.Act2.Address = IntPtr.Zero;
            this.Act3.Address = IntPtr.Zero;
            this.Act4.Address = IntPtr.Zero;
            this.Interlude.Address = IntPtr.Zero;
            this.Atlas.Address = IntPtr.Zero;
            this.AtlasSkillsPanel.Address = IntPtr.Zero;
            this.TempleConsole.Address = IntPtr.Zero;
            this.CurrencyExchangePanel.Address = IntPtr.Zero;
            this.LeftPanel.Address = IntPtr.Zero;
            this.RightPanel.Address = IntPtr.Zero;
            this.ChatParent.Address = IntPtr.Zero;
            this.atlasMaps.Clear();
            this.atlasMapCacheFrameCounter = int.MaxValue;
            this.cachedAtlasMapCount = -1;
            this.SkillTreeNodesUiElements.Clear();
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            this.UpdateParentsCache();
            var reader = Core.Process.Handle;
            var data1 = reader.ReadMemory<ImportantUiElementsOffsets>(Core.GHSettings.IsTaiwanClient ? this.Address - 0x08 : this.Address);
            if (Core.GHSettings.EnableControllerMode)
            {
                var data2 = reader.ReadMemory<MapParentStruct>(data1.ControllerModeMapParentPtr);
                this.LargeMap.Address = data2.LargeMapPtr;
                this.MiniMap.Address = data2.MiniMapPtr;
                this.UpdateWorldMapPanelAddresses();
                if (this.IsCoopMode())
                {
                    this.UpdateCoopPanelAddresses();
                }
                else
                {
                    this.LeftPanel.Address = IntPtr.Zero;
                    this.RightPanel.Address = IntPtr.Zero;
                }
                this.ChatParent.Address = IntPtr.Zero;
                this.passiveskilltreenodes.Address = IntPtr.Zero;
                this.sekhemasTrialMapPanel.Address = IntPtr.Zero;
            }
            else
            {
                var data2 = reader.ReadMemory<MapParentStruct>(data1.MapParentPtr);
                var data3 = reader.ReadMemory<UiElementBaseOffset>(data1.PassiveSkillTreePanel);
                var data4 = reader.ReadMemory<IntPtr>(data3.ChildrensPtr.First + (PassiveSkillTreeStruct.ChildNumber));
                // This won't throw an exception (i.e. this address is not a UIElement) because (lucky us)
                // game UiElement garbage collection is not instant. if this ever changes, put try catch on it.
                this.LargeMap.Address = data2.LargeMapPtr;
                this.MiniMap.Address = data2.MiniMapPtr;
                this.UpdateWorldMapPanelAddresses();
                this.LeftPanel.Address = ValidUiElementOrZero(data1.LeftPanelPtr);
                this.RightPanel.Address = ValidUiElementOrZero(data1.RightPanelPtr);
                this.ChatParent.Address = data1.ChatParentPtr;
                this.passiveskilltreenodes.Address = ValidUiElementOrZero(data4);
                this.updatePassiveSkillTreeData();
                {
                    var mgrOff = reader.ReadMemory<UiElementBaseOffset>(this.Address);
                    var parentAddr = reader.ReadMemory<IntPtr>(mgrOff.ChildrensPtr.First + (84 * IntPtr.Size));
                    var parentOff = reader.ReadMemory<UiElementBaseOffset>(parentAddr);
                    this.sekhemasTrialMapPanel.Address =
                      ValidUiElementOrZero(reader.ReadMemory<IntPtr>(parentOff.ChildrensPtr.First));
                }
            }

            this.CurrencyExchangePanel.Address = ResolveChildAddress(this.Address, CurrencyExchangePanelChildPath);
            this.UpdateAtlasMapData();
        }

        private void UpdateWorldMapPanelAddresses()
        {
            this.WorldMapPanel.Address = ResolveChildAddress(this.Address, WorldMapPanelChildPath);
            this.Act1.Address = ResolveChildAddress(this.Address, Act1PanelChildPath);
            this.Act2.Address = ResolveChildAddress(this.Address, Act2PanelChildPath);
            this.Act3.Address = ResolveChildAddress(this.Address, Act3PanelChildPath);
            this.Act4.Address = ResolveChildAddress(this.Address, Act4PanelChildPath);
            this.Interlude.Address = ResolveChildAddress(this.Address, InterludePanelChildPath);
            this.Atlas.Address = ResolveChildAddress(this.Address, AtlasPanelChildPath);
            this.AtlasSkillsPanel.Address = ResolveChildAddress(this.Address, AtlasSkillsPanelChildPath);
            this.TempleConsole.Address = ResolveChildAddress(this.Address, TempleConsoleChildPath);
        }

        private void UpdateAtlasMapData()
        {
            if (this.Atlas.Address == IntPtr.Zero || !this.Atlas.IsVisible)
            {
                this.atlasMaps.Clear();
                this.atlasOceanButtons.Clear();
                this.atlasMarkers.Clear();
                this.cachedAtlasMapCount = -1;
                this.atlasMapCacheFrameCounter = int.MaxValue;
                return;
            }

            var atlasCount = this.Atlas.TotalChildrens;
            if (atlasCount <= 0 || atlasCount > 10000)
            {
                this.atlasMaps.Clear();
                this.atlasOceanButtons.Clear();
                this.atlasMarkers.Clear();
                this.cachedAtlasMapCount = -1;
                this.atlasMapCacheFrameCounter = int.MaxValue;
                return;
            }

            if (++this.atlasMapCacheFrameCounter < AtlasMapCacheRefreshFrames &&
                this.cachedAtlasMapCount == atlasCount &&
                this.atlasMaps.Count > 0)
            {
                return;
            }

            var reader = Core.Process.Handle;
            var connections = ReadAtlasConnections(this.Atlas.Address);
            var maps = new List<AtlasMapNode>(atlasCount);
            var oceanButtons = new List<AtlasRegionButton>();
            var markers = new List<PlayerMarker>(2);
            var markerFpMasked = AtlasCurrentNodeMarkerFp & ~IsVisibleMask;
            for (var i = 0; i < atlasCount; i++)
            {
                var nodeUi = this.Atlas[i];
                if (nodeUi == null || nodeUi.Address == IntPtr.Zero)
                {
                    continue;
                }

                // Identify each child by its UiElement fingerprint. The "you are here" marker shares
                // the node-list container fp (0x502EF3); real atlas map nodes are 0x542EF3. Only parse
                // actual map nodes — other children (markers, sub-containers, decorative elements) have
                // unrelated layouts, and chasing the map-node pointer chain on them dereferences garbage
                // (huge bogus child counts → multi-MB reads), which is what froze the overlay.
                var flags = reader.ReadMemory<uint>(nodeUi.Address + UiElementBaseFlagsOffset);
                var fpMasked = flags & ~IsVisibleMask;
                if (fpMasked == markerFpMasked && (flags & IsVisibleMask) != 0)
                {
                    markers.Add(new PlayerMarker(i, nodeUi.Address, -1));
                    continue;
                }

                if (fpMasked != (AtlasMapNodeFp & ~IsVisibleMask) &&
                    fpMasked != (AtlasMistNodeFp & ~IsVisibleMask))
                {
                    if (reader.TryReadMemory<int>(nodeUi.Address + AtlasRegionButtonRowIndexOffset, out var rowIndex) &&
                        rowIndex == AtlasOceanRegionButtonRow &&
                        reader.TryReadMemory<IntPtr>(nodeUi.Address + AtlasRegionButtonRowPtrOffset, out var rowPtr) &&
                        rowPtr != IntPtr.Zero &&
                        reader.TryReadMemory<StdTuple2D<int>>(nodeUi.Address + AtlasRegionButtonGridOffset, out var buttonGrid) &&
                        buttonGrid.X is >= -0x80000 and <= 0x80000 &&
                        buttonGrid.Y is >= -0x80000 and <= 0x80000)
                    {
                        oceanButtons.Add(new AtlasRegionButton(i, nodeUi.Address, buttonGrid, (flags & IsVisibleMask) != 0));
                    }

                    continue;
                }

                var map = ReadAtlasMapNode(i, nodeUi, connections);
                if (map != null)
                {
                    maps.Add(map);
                }
            }

            this.atlasMaps.Clear();
            this.atlasMaps.AddRange(maps);
            this.atlasOceanButtons.Clear();
            this.atlasOceanButtons.AddRange(oceanButtons);

            // Resolve each marker to the map node it sits on. The marker
            // renders above the node, so offset its Y center downward ~100px
            // before the nearest-center search to avoid picking the node above.
            const float markerYOffset = 100f;
            var mapCenters = new List<(Vector2 Center, int Index)>(maps.Count);
            for (int mi = 0; mi < maps.Count; mi++)
            {
                var mu = this.Atlas[maps[mi].Index];
                if (mu == null) continue;
                mapCenters.Add((mu.Position + mu.Size * 0.5f, maps[mi].Index));
            }

            var resolved = new List<PlayerMarker>(markers.Count);
            foreach (var m in markers)
            {
                var mu = this.Atlas[m.Index];
                if (mu == null) continue;
                var markerPos = mu.Position + mu.Size * 0.5f;
                markerPos.Y += markerYOffset;

                int bestIdx = -1;
                float bestD = float.MaxValue;
                foreach (var (center, index) in mapCenters)
                {
                    var d = Vector2.DistanceSquared(markerPos, center);
                    if (d < bestD) { bestD = d; bestIdx = index; }
                }

                resolved.Add(new PlayerMarker(m.Index, m.Address, bestIdx));
            }

            this.atlasMarkers.Clear();
            this.atlasMarkers.AddRange(resolved);
            this.cachedAtlasMapCount = atlasCount;
            this.atlasMapCacheFrameCounter = 0;
        }

        private static AtlasMapNode? ReadAtlasMapNode(
            int index,
            UiElementBase nodeUi,
            Dictionary<StdTuple2D<int>, List<StdTuple2D<int>>> connections)
        {
            var nodeAddr = nodeUi.Address;
            if (nodeAddr == IntPtr.Zero)
            {
                return null;
            }

            // The atlas child list can momentarily contain non-map-node children (or be read mid-
            // mutation), in which case this pointer chain dereferences garbage. Use TryReadMemory so
            // those expected, recoverable failures don't flood the log (and stall the app) — bail and
            // skip the node instead. A real map node reads cleanly through the whole chain.
            var reader = Core.Process.Handle;
            if (!reader.TryReadMemory<IntPtr>(nodeAddr + 0x10, out var nodeDataStorage) || nodeDataStorage == IntPtr.Zero)
            {
                return null;
            }

            if (!reader.TryReadMemory<IntPtr>(nodeDataStorage + 0x20, out var nodeData) || nodeData == IntPtr.Zero)
            {
                return null;
            }

            if (!reader.TryReadMemory<StdTuple2D<int>>(nodeAddr + 0x320, out var gridPosition) ||
                !reader.TryReadMemory<byte>(nodeData + AtlasNodeBiomeIdOffset, out var biomeId) ||
                !reader.TryReadMemory<byte>(nodeData + AtlasNodeStatusByteOffset, out var status))
            {
                return null;
            }

            var state = (status & AtlasNodeCompletedBit) != 0
                ? AtlasMapNodeState.CompletedBase
                : (status & AtlasNodeAccessibleBit) != 0
                    ? AtlasMapNodeState.AccessibleNow
                    : AtlasMapNodeState.None;

            var mapId = string.Empty;
            if (reader.TryReadMemory<IntPtr>(nodeData + AtlasNodeMapDataOffset, out var mapDataWrapper)
                && mapDataWrapper != IntPtr.Zero
                && reader.TryReadMemory<IntPtr>(mapDataWrapper, out var stringHeader)
                && stringHeader != IntPtr.Zero
                && reader.TryReadMemory<IntPtr>(stringHeader, out var stringBuffer)
                && stringBuffer != IntPtr.Zero)
            {
                mapId = reader.ReadUnicodeString(stringBuffer);
            }

            ReadAtlasContentContainer(nodeUi, out var badgeAddresses, out var contentNames, out var badgeContentIds);
            var contentTokens = ReadAtlasContentTokens(nodeAddr);
            MergePersistentAtlasBadgeContent(nodeAddr, mapId, contentNames, badgeContentIds);

            return new AtlasMapNode(
                index,
                nodeAddr,
                mapId,
                gridPosition,
                biomeId,
                state,
                contentNames,
                badgeAddresses,
                contentTokens,
                badgeContentIds,
                connections.TryGetValue(gridPosition, out var connected) ? connected : []);
        }

        private static List<uint> ReadAtlasContentTokens(IntPtr nodeAddr)
        {
            var reader = Core.Process.Handle;
            var tokenVector = reader.ReadMemory<StdVector>(nodeAddr + AtlasNodeContentVecOffset);

            // Sanity-cap the element count so a torn/garbage vector can't trigger a multi-MB read
            // (ReadStdVector only bounds at 50 MB). Real content lists are tiny.
            var count = (tokenVector.Last.ToInt64() - tokenVector.First.ToInt64()) / sizeof(uint);
            if (count <= 0 || count > AtlasNodeMaxContentTokens)
            {
                return new List<uint>();
            }

            var tokens = reader.ReadStdVector<uint>(tokenVector);
            return tokens.Length > 0 ? new List<uint>(tokens) : new List<uint>();
        }

        // The badge UI children are culled for fogged/off-screen nodes, but the persistent raw
        // badge vector described above remains. Merge it into the public badge model so consumers
        // see the same server cell-state content both on- and off-screen. yokkenUA traced its
        // population through AtlasMapNodeWidget_ctor and AtlasNode_badgeVec_insert in Ghidra.
        private static void MergePersistentAtlasBadgeContent(
            IntPtr nodeAddr,
            string mapId,
            List<string> contentNames,
            List<uint> badgeContentIds)
        {
            var tags = WorldAreaTags.GetMeta(mapId)?.Tags;
            if (tags?.Contains("hideout", StringComparer.OrdinalIgnoreCase) == true)
                return;

            var reader = Core.Process.Handle;
            if (reader.TryReadMemory<IntPtr>(nodeAddr + AtlasNodeBadgeVecBeginOffset, out var begin) &&
                reader.TryReadMemory<IntPtr>(nodeAddr + AtlasNodeBadgeVecEndOffset, out var end) &&
                begin != IntPtr.Zero)
            {
                var count = end.ToInt64() - begin.ToInt64();
                if (count is > 0 and <= AtlasNodeMaxContentChildren)
                {
                    for (var i = 0; i < count; i++)
                    {
                        var id = (uint)(reader.ReadMemory<byte>(begin + i) + AtlasNodeBadgeRowToContentId);
                        AddAtlasBadgeContent(id, contentNames, badgeContentIds);
                    }
                }
            }

            // Some distant sea maps have neither UI badges nor a transmitted content vector. Their
            // content is intrinsic to the persistent map id and can still be recovered reliably.
            if (mapId.StartsWith("ExpeditionSubArea", StringComparison.OrdinalIgnoreCase))
                AddAtlasBadgeContent(100, contentNames, badgeContentIds); // Powerful Map Boss
            else if (mapId.StartsWith("ExpeditionLogBook", StringComparison.OrdinalIgnoreCase))
                AddAtlasBadgeContent(166, contentNames, badgeContentIds); // Grand Expedition
        }

        private static void AddAtlasBadgeContent(
            uint id,
            List<string> contentNames,
            List<uint> badgeContentIds)
        {
            if (!badgeContentIds.Any(existing => (existing & 0xFFFFu) == id))
                badgeContentIds.Add(id);

            var name = AtlasMapNode.GetBadgeContentName(id);
            if (!string.IsNullOrWhiteSpace(name) && !contentNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                contentNames.Add(name);
        }

        private static Dictionary<StdTuple2D<int>, List<StdTuple2D<int>>> ReadAtlasConnections(IntPtr atlasAddress)
        {
            var result = new Dictionary<StdTuple2D<int>, List<StdTuple2D<int>>>();
            if (atlasAddress == IntPtr.Zero)
            {
                return result;
            }

            var reader = Core.Process.Handle;
            var connectionVector = reader.ReadMemory<StdVector>(atlasAddress + AtlasNodeConnectionsVectorOffset);
            var connections = reader.ReadStdVector<AtlasNodeConnectionEdgeOffsets>(connectionVector);
            foreach (var connection in connections)
            {
                AddAtlasConnection(result, connection.Source, connection.Target);
            }

            return result;
        }

        private static void AddAtlasConnection(
            Dictionary<StdTuple2D<int>, List<StdTuple2D<int>>> connections,
            StdTuple2D<int> source,
            StdTuple2D<int> target)
        {
            if (target.Equals(default(StdTuple2D<int>)) || target.Equals(source))
            {
                return;
            }

            if (!connections.TryGetValue(source, out var sourceConnections))
            {
                sourceConnections = new List<StdTuple2D<int>>(4);
                connections[source] = sourceConnections;
            }

            if (!sourceConnections.Contains(target))
            {
                sourceConnections.Add(target);
            }

            if (!connections.TryGetValue(target, out var targetConnections))
            {
                targetConnections = new List<StdTuple2D<int>>(4);
                connections[target] = targetConnections;
            }

            if (!targetConnections.Contains(source))
            {
                targetConnections.Add(source);
            }
        }

        // Single pass over the node's content container (node[0][0]) collecting, for each badge child:
        // its address, its content-name string (wide string off the child+0x290 pointer, class-2
        // labelled content), and its badge content id (u32 at child+0x188). Empty/blank names are
        // skipped; the address and id lists stay child-aligned for callers that need the raw badges.
        private static void ReadAtlasContentContainer(
            UiElementBase nodeUi,
            out List<IntPtr> badgeAddresses,
            out List<string> contentNames,
            out List<uint> badgeContentIds)
        {
            badgeAddresses = new List<IntPtr>();
            contentNames = new List<string>();
            badgeContentIds = new List<uint>();

            var contentContainer = nodeUi[0]?[0];
            if (contentContainer == null)
            {
                return;
            }

            // Real content containers hold a handful of badges; a huge count means a torn/garbage read,
            // so skip it rather than iterating (and materializing a UiElementBase for) every entry.
            var childCount = contentContainer.TotalChildrens;
            if (childCount > AtlasNodeMaxContentChildren)
            {
                return;
            }

            var reader = Core.Process.Handle;
            for (var i = 0; i < childCount; i++)
            {
                var childAddr = contentContainer[i]?.Address ?? IntPtr.Zero;
                if (childAddr == IntPtr.Zero)
                {
                    continue;
                }

                badgeAddresses.Add(childAddr);
                badgeContentIds.Add(reader.ReadMemory<uint>(childAddr + AtlasNodeBadgeContentIdOffset));

                var contentPtr = reader.ReadMemory<IntPtr>(childAddr + AtlasNodeContentNameOffset);
                if (contentPtr == IntPtr.Zero)
                {
                    continue;
                }

                var contentName = reader.ReadUnicodeString(contentPtr);
                if (!string.IsNullOrWhiteSpace(contentName))
                {
                    contentNames.Add(contentName);
                }
            }
        }

        private bool IsCoopMode()
        {
            if (!Core.GHSettings.EnableControllerMode)
            {
                return false;
            }

            var inGameState = Core.States.InGameStateObject;
            if (inGameState == null)
            {
                return false;
            }

            var currentArea = inGameState.CurrentAreaInstance;
            if (currentArea == null || currentArea.Address == IntPtr.Zero)
            {
                return false;
            }

            foreach (var entity in currentArea.AwakeEntities.Values)
            {
                if (entity.EntitySubtype == GameHelper.RemoteEnums.Entity.EntitySubtypes.PlayerOther)
                {
                    return true;
                }
            }

            return false;
        }

        private void UpdateCoopPanelAddresses()
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<UiElementBaseOffset>(this.Address);
            var childCount = data.ChildrensPtr.TotalElements(IntPtr.Size);
            if (childCount == 0 || data.ChildrensPtr.First == IntPtr.Zero)
            {
                this.LeftPanel.Address = IntPtr.Zero;
                this.RightPanel.Address = IntPtr.Zero;
                return;
            }

            IntPtr foundLeft = IntPtr.Zero;
            IntPtr foundRight = IntPtr.Zero;

            // Scan candidate index window (indices 20 to 25) for adjacent co-op panels
            int startIdx = Math.Max(0, 20);
            int endIdx = Math.Min((int)childCount - 2, 25);

            for (int i = startIdx; i <= endIdx; i++)
            {
                var leftCandidate = ValidUiElementOrZero(reader.ReadMemory<IntPtr>(data.ChildrensPtr.First + (i * IntPtr.Size)));
                var rightCandidate = ValidUiElementOrZero(reader.ReadMemory<IntPtr>(data.ChildrensPtr.First + ((i + 1) * IntPtr.Size)));

                if (leftCandidate == IntPtr.Zero || rightCandidate == IntPtr.Zero)
                {
                    continue;
                }

                var leftData = reader.ReadMemory<UiElementBaseOffset>(leftCandidate);
                var rightData = reader.ReadMemory<UiElementBaseOffset>(rightCandidate);

                // In co-op mode, LeftPanel relative X < RightPanel relative X
                if (leftData.RelativePosition.X < rightData.RelativePosition.X)
                {
                    foundLeft = leftCandidate;
                    foundRight = rightCandidate;
                    break;
                }
            }

            // Fallback to static paths if candidate scanning doesn't find a matching pair
            this.LeftPanel.Address = foundLeft != IntPtr.Zero ? foundLeft : ResolveChildAddress(this.Address, LeftPanelCoopPath);
            this.RightPanel.Address = foundRight != IntPtr.Zero ? foundRight : ResolveChildAddress(this.Address, RightPanelCoopPath);
        }

        private static IntPtr ResolveChildAddress(IntPtr rootAddress, int[] childPath)
        {
            if (rootAddress == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var reader = Core.Process.Handle;
            var currentAddress = rootAddress;
            foreach (var childIndex in childPath)
            {
                if (childIndex < 0)
                {
                    return IntPtr.Zero;
                }

                var data = reader.ReadMemory<UiElementBaseOffset>(currentAddress);
                var childCount = data.ChildrensPtr.TotalElements(IntPtr.Size);
                if (data.ChildrensPtr.First == IntPtr.Zero || childIndex >= childCount)
                {
                    return IntPtr.Zero;
                }

                currentAddress = reader.ReadMemory<IntPtr>(data.ChildrensPtr.First + (childIndex * IntPtr.Size));
                if (currentAddress == IntPtr.Zero)
                {
                    return IntPtr.Zero;
                }
            }

            // Only hand back addresses that are actually Ui elements (see ValidUiElementOrZero).
            return ValidUiElementOrZero(currentAddress);
        }

        // Returns the address only if it points to a real Ui element (its self-pointer matches),
        // otherwise IntPtr.Zero. The panel UiElementBase instances use forceUpdate=true, so assigning
        // a non-Ui address (a stale/garbage pointer, or a fixed child-index path landing on different
        // memory for some screens) makes UiElementBase.UpdateData throw — caught and re-logged by
        // Address.set every frame. Validating with the same self-pointer check keeps that off the log.
        private static IntPtr ValidUiElementOrZero(IntPtr address)
        {
            if (address == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var data = Core.Process.Handle.ReadMemory<UiElementBaseOffset>(address);
            return data.Self != IntPtr.Zero && data.Self != address ? IntPtr.Zero : address;
        }

        private void updatePassiveSkillTreeData()
        {
            if (this.passiveskilltreenodes.IsVisible)
            {
                this.AddOrUpdateSkillNodes();
            }
            else
            {
                this.ClearSkillNodes();
            }
        }

        private void ClearSkillNodes()
        {
            this.SkillTreeNodesUiElements.Clear();
            this.passiveSkillTreeCache.Clear();
        }

        private void AddOrUpdateSkillNodes()
        {
            if (this.SkillTreeNodesUiElements.Count == 0)
            {
                var currentChild = new UiElementBase(IntPtr.Zero, this.passiveSkillTreeCache);
                for (var i = 3; i < this.passiveskilltreenodes.TotalChildrens; i++)
                {
                    currentChild.Address = this.passiveskilltreenodes[i]!.Address;
                    if (!currentChild.IsVisible)
                    {
                        break;
                    }

                    this.AddSkillTreeNodeUiElementRecursive(currentChild);
                }
            }
            else
            {
                Parallel.For(0, this.SkillTreeNodesUiElements.Count, (i) =>
                {
                    this.SkillTreeNodesUiElements[i].Address = this.SkillTreeNodesUiElements[i].Address;
                });
            }
        }

        private void AddSkillTreeNodeUiElementRecursive(UiElementBase uie)
        {
            if (uie.TotalChildrens > 0)
            {
                for (var i = 0; i < uie.TotalChildrens; i++)
                {
                    this.AddSkillTreeNodeUiElementRecursive(uie[i]!);
                }
            }
            else
            {
                var skillNode = new SkillTreeNodeUiElement(uie.Address, this.passiveSkillTreeCache);
                if (skillNode.SkillGraphId != 0)
                {
                    this.SkillTreeNodesUiElements.Add(skillNode);
                }
            }
        }

        private void displayParentsCache()
        {
            this.rootCache.ToImGui();
            this.passiveSkillTreeCache.ToImGui();
        }

        private void UpdateParentsCache()
        {
            this.rootCache.UpdateAllParentsParallel();
            this.passiveSkillTreeCache.UpdateAllParentsParallel();
        }

        private IEnumerator<Wait> OnPerFrame()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.PerFrameDataUpdate);
                try
                {
                    if (this.Address != IntPtr.Zero &&
                        Core.States.GameCurrentState is GameStateTypes.InGameState or GameStateTypes.EscapeState)
                    {
                        // sending false because "true" use-case is handled
                        // by UpdateData function when address actually gets changed.
                        this.UpdateData(false);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ImportantUiElements.OnPerFrame] {ex}");
                }
            }
        }
    }
}
