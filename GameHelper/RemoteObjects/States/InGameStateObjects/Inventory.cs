// <copyright file="Inventory.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.States.InGameStateObjects
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using GameHelper.RemoteObjects.Components;
    using GameOffsets.Objects.Components;
    using Coroutine;
    using GameOffsets.Natives;
    using GameOffsets.Objects.States.InGameState;
    using ImGuiNET;

    /// <summary>
    ///     Knows how to parse player, NPC, Crafting, stash inventories available in ServerData
    ///     and get the items available in them.
    /// </summary>
    public class Inventory : RemoteObjectBase
    {
        /// <summary>
        ///     This array stores items addresses in a given inventory.
        ///     Items addresses are in order w.r.t inventory slots. There might be duplicates or IntPtr.Zero
        ///     in case an item holds 2 slots or there is no item in the slot respectively.
        /// </summary>
        private IntPtr[] itemsToInventorySlotMapping = Array.Empty<IntPtr>();
        private readonly double updateInterval = 0.02d;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Inventory" /> class.
        /// </summary>
        /// <param name="address">address of the remote memory object.</param>
        /// <param name="name">name of the inventory for displaying purposes.</param>
        /// <param name="updateInterval">Background refresh interval in seconds.</param>
        internal Inventory(IntPtr address, string name, double updateInterval = 0.02d)
            : base(address)
        {
            this.updateInterval = updateInterval;
            Core.CoroutinesRegistrar.Add(CoroutineHandler.Start(
                this.OnTimeTick(), $"[Inventory] Update {name}", int.MaxValue - 4));
        }

        /// <summary>
        ///     Gets a value indicating total number of boxes in the inventory.
        /// </summary>
        public StdTuple2D<int> TotalBoxes { get; private set; }

        /// <summary>
        ///     Gets a value indicating total number of requests send to the server for this inventory.
        /// </summary>
        public int ServerRequestCounter { get; private set; }

        /// <summary>
        ///     Gets all the items in the inventory.
        /// </summary>
        public ConcurrentDictionary<IntPtr, Item> Items { get; } =
            new();

        /// <summary>Allows optional consumers to suspend background inventory reads.</summary>
        public bool AutomaticUpdatesEnabled { get; set; } = true;

        /// <summary>A copied currency stack; metadata remains the stable identity.</summary>
        public sealed record CurrencyStackSnapshot(string Metadata, string Name, int Count);

        /// <summary>
        /// Copies currency counts only when the live slots, item pointers and stack counts remain
        /// consistent throughout the read. A failed/incomplete read is never an empty backpack.
        /// </summary>
        public bool TryGetCurrencySnapshot(out IReadOnlyList<CurrencyStackSnapshot> snapshot)
        {
            snapshot = Array.Empty<CurrencyStackSnapshot>();
            var inventoryAddress = this.Address;
            if (!this.AutomaticUpdatesEnabled || inventoryAddress == IntPtr.Zero) return false;
            var reader = Core.Process.Handle;
            try
            {
                if (!reader.TryReadMemory<InventoryStruct>(inventoryAddress, out var before) ||
                    before.TotalBoxes.X <= 0 || before.TotalBoxes.Y <= 0 ||
                    before.TotalBoxes.X > 100 || before.TotalBoxes.Y > 100) return false;
                var slots = reader.ReadStdVector<IntPtr>(before.ItemList);
                if (slots.Length != before.TotalBoxes.X * before.TotalBoxes.Y ||
                    !slots.SequenceEqual(this.itemsToInventorySlotMapping)) return false;

                var result = new List<CurrencyStackSnapshot>();
                var observedStacks = new List<(IntPtr Address, IntPtr Owner, int Count)>();
                foreach (var slot in slots.Where(p => p != IntPtr.Zero).Distinct())
                {
                    if (!reader.TryReadMemory<InventoryItemStruct>(slot, out var invItem) ||
                        !this.Items.TryGetValue(slot, out var item) || !item.IsValid ||
                        invItem.Item == IntPtr.Zero || item.Address != invItem.Item ||
                        string.IsNullOrEmpty(item.Path)) return false;
                    if (!item.Path.StartsWith("Metadata/Items/Currency/", StringComparison.Ordinal)) continue;
                    if (!item.TryGetComponent<Stack>(out var stack) ||
                        !reader.TryReadMemory<StackOffsets>(stack.Address, out var stackData) ||
                        stackData.Header.EntityPtr != item.Address || stackData.Count <= 0 ||
                        stackData.Count > 1000000) return false;
                    var name = item.TryGetComponent<Base>(out var itemBase) &&
                               !string.IsNullOrWhiteSpace(itemBase.BaseItemName)
                        ? itemBase.BaseItemName : item.Path;
                    result.Add(new CurrencyStackSnapshot(item.Path, name, stackData.Count));
                    observedStacks.Add((stack.Address, item.Address, stackData.Count));
                }

                if (this.Address != inventoryAddress ||
                    !reader.TryReadMemory<InventoryStruct>(inventoryAddress, out var after) ||
                    before.ServerRequestCounter != after.ServerRequestCounter ||
                    before.TotalBoxes.X != after.TotalBoxes.X || before.TotalBoxes.Y != after.TotalBoxes.Y ||
                    !slots.SequenceEqual(reader.ReadStdVector<IntPtr>(after.ItemList))) return false;
                foreach (var observed in observedStacks)
                {
                    if (!reader.TryReadMemory<StackOffsets>(observed.Address, out var check) ||
                        check.Header.EntityPtr != observed.Owner || check.Count != observed.Count) return false;
                }

                snapshot = result;
                return true;
            }
            catch
            {
                // Transitions and partial remote reads invalidate this sample rather than its baseline.
                return false;
            }
        }

        /// <summary>
        ///     Gets the item at the specific slot in the inventory.
        ///     Always check if the returned item IsValid or not by comparing
        ///     Item Address with IntPtr.Zero.
        /// </summary>
        /// <param name="y">Inventory slot row, starting from 0.</param>
        /// <param name="x">Inventory slot column, starting from 0.</param>
        /// <returns>Item on the given slot.</returns>
        [SkipImGuiReflection]
        public Item this[int y, int x]
        {
            get
            {
                if (y >= this.TotalBoxes.Y || x >= this.TotalBoxes.X)
                {
                    return new Item(IntPtr.Zero);
                }

                var index = y * this.TotalBoxes.X + x;
                if (index >= this.itemsToInventorySlotMapping.Length)
                {
                    return new Item(IntPtr.Zero);
                }

                var itemAddr = this.itemsToInventorySlotMapping[index];
                if (itemAddr == IntPtr.Zero)
                {
                    return new Item(IntPtr.Zero);
                }

                if (this.Items.TryGetValue(itemAddr, out var item))
                {
                    return item;
                }

                return new Item(IntPtr.Zero);
            }
        }

        /// <inheritdoc />
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"Total Boxes: {this.TotalBoxes}");
            ImGui.Text($"Server Request Counter: {this.ServerRequestCounter}");
            if (ImGui.TreeNode("Inventory Slots"))
            {
                for (var y = 0; y < this.TotalBoxes.Y; y++)
                {
                    var data = string.Empty;
                    for (var x = 0; x < this.TotalBoxes.X; x++)
                    {
                        if (this.itemsToInventorySlotMapping[y * this.TotalBoxes.X + x] != IntPtr.Zero)
                        {
                            data += " 1";
                        }
                        else
                        {
                            data += " 0";
                        }
                    }

                    ImGui.Text(data);
                }

                ImGui.TreePop();
            }

            if (ImGui.TreeNode("Items"))
            {
                foreach (var item in this.Items)
                {
                    if (ImGui.TreeNode($"{item.Value.Path}##{item.Value.Address.ToInt64()}"))
                    {
                        item.Value.ToImGui();
                        ImGui.TreePop();
                    }
                }

                ImGui.TreePop();
            }
        }

        /// <inheritdoc />
        protected override void CleanUpData()
        {
            this.TotalBoxes = default;
            this.ServerRequestCounter = default;
            this.itemsToInventorySlotMapping = Array.Empty<IntPtr>();
            this.Items.Clear();
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var invInfo = reader.ReadMemory<InventoryStruct>(this.Address);
            this.TotalBoxes = invInfo.TotalBoxes;
            this.ServerRequestCounter = invInfo.ServerRequestCounter;
            this.itemsToInventorySlotMapping = reader.ReadStdVector<IntPtr>(invInfo.ItemList);
            if (hasAddressChanged)
            {
                this.Items.Clear();
            }

            foreach (var item in this.Items)
            {
                item.Value.IsValid = false;
            }

            Parallel.ForEach(this.itemsToInventorySlotMapping.Distinct(), invItemPtr =>
            {
                if (invItemPtr != IntPtr.Zero)
                {
                    var invItem = reader.ReadMemory<InventoryItemStruct>(invItemPtr);
                    if (this.Items.ContainsKey(invItemPtr))
                    {
                        this.Items[invItemPtr].Address = invItem.Item;
                    }
                    else
                    {
                        var item = new Item(invItem.Item);
                        if (!string.IsNullOrEmpty(item.Path))
                        {
                            // TryAdd returns false when another parallel worker already inserted
                            // this invItemPtr — legitimate race on duplicate pointers, not an
                            // error. Drop the previously-thrown bare Exception (audit F-130)
                            // which would otherwise propagate as AggregateException and kill
                            // the OnTimeTick coroutine.
                            this.Items.TryAdd(invItemPtr, item);
                        }
                    }
                }
            });

            foreach (var item in this.Items)
            {
                if (!item.Value.IsValid)
                {
                    this.Items.TryRemove(item.Key, out _);
                }
            }
        }

        private IEnumerable<Wait> OnTimeTick()
        {
            while (true)
            {
                yield return new Wait(this.updateInterval);
                try
                {
                    if (this.AutomaticUpdatesEnabled && this.Address != IntPtr.Zero)
                    {
                        this.UpdateData(false);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Inventory.OnTimeTick] {ex}");
                }
            }
        }
    }
}
