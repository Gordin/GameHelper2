// <copyright file="ServerData.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.States.InGameStateObjects
{
    using System;
    using System.Collections.Generic;
    using Coroutine;
    using GameOffsets.Objects.States.InGameState;
    using ImGuiNET;
    using RemoteEnums;
    using Utils;

    /// <summary>
    ///     Points to the InGameState -> ServerData object.
    /// </summary>
    public class ServerData : RemoteObjectBase
    {
        private InventoryName selectedInvName = InventoryName.NoInvSelected;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ServerData" /> class.
        /// </summary>
        /// <param name="address">address of the remote memory object.</param>
        internal ServerData(IntPtr address)
            : base(address)
        {
            // Feel free to uncomment this if we ever add stuff like latency.
            //Core.CoroutinesRegistrar.Add(CoroutineHandler.Start(
            //    this.OnTimeTick(), "[ServerData] Update ServerData", int.MaxValue - 3));
        }

        /// <summary>
        ///     Gets an object that points to the flask inventory.
        /// </summary>
        public Inventory FlaskInventory { get; }

            = new(IntPtr.Zero, "Flask");

        /// <summary>
        ///     Gets the inventory to debug.
        /// </summary>
        internal Inventory SelectedInv { get; }

            = new(IntPtr.Zero, "CurrentlySelected");

        /// <summary>
        ///     Gets the inventories associated with the player.
        /// </summary>
        internal Dictionary<InventoryName, IntPtr> PlayerInventories { get; }

            = new();

        /// <inheritdoc />
        internal override void ToImGui()
        {
            if ((int)this.selectedInvName > this.PlayerInventories.Count)
            {
                this.ClearCurrentlySelectedInventory();
            }

            ImGuiHelper.IntPtrToImGui("Address", this.Address);
            if (ImGui.TreeNode("FlaskInventory"))
            {
                this.FlaskInventory.ToImGui();
                ImGui.TreePop();
            }

            ImGui.Text("please click Clear Selected before leaving this window.");
            if (ImGuiHelper.IEnumerableComboBox(
                "###Inventory Selector",
                this.PlayerInventories.Keys,
                ref this.selectedInvName))
            {
                this.SelectedInv.Address = this.PlayerInventories[this.selectedInvName];
            }

            ImGui.SameLine();
            if (ImGui.Button("Clear Selected"))
            {
                this.ClearCurrentlySelectedInventory();
            }

            if (this.selectedInvName != InventoryName.NoInvSelected)
            {
                if (ImGui.TreeNode("Currently Selected Inventory"))
                {
                    this.SelectedInv.ToImGui();
                    ImGui.TreePop();
                }
            }
        }

        /// <inheritdoc />
        protected override void CleanUpData()
        {
            this.ClearCurrentlySelectedInventory();
            this.PlayerInventories.Clear();
            this.FlaskInventory.Address = IntPtr.Zero;
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            // only happens when area is changed.
            if (hasAddressChanged)
            {
                this.ClearCurrentlySelectedInventory();
            }

            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<ServerDataOffsets>(this.Address);
            var playerDataArray = reader.ReadStdVector<IntPtr>(data.PlayerServerDataPtr);
            if (playerDataArray.Length == 0)
                return;
            var playerData = reader.ReadMemory<ServerDataStructure>(playerDataArray[0]);
            var inventoryData = reader.ReadStdVector<InventoryArrayStruct>(playerData.PlayerInventories);
            this.PlayerInventories.Clear();
            for (var i = 0; i < inventoryData.Length; i++)
            {
                var invName = (InventoryName)inventoryData[i].InventoryId;
                var invAddr = inventoryData[i].InventoryPtr0;
                this.PlayerInventories[invName] = invAddr;
                switch (invName)
                {
                    case InventoryName.Flask1:
                        this.FlaskInventory.Address = invAddr;
                        break;
                }
            }
        }

        private void ClearCurrentlySelectedInventory()
        {
            this.selectedInvName = InventoryName.NoInvSelected;
            this.SelectedInv.Address = IntPtr.Zero;
        }

        private IEnumerable<Wait> OnTimeTick()
        {
            while (true)
            {
                yield return new Wait(0.2d);
                if (this.Address != IntPtr.Zero)
                {
                    this.UpdateData(false);
                }
            }
        }
    }
}