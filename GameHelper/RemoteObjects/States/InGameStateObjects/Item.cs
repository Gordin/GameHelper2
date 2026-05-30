// <copyright file="Item.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.States.InGameStateObjects
{
    using System;
    using GameHelper.RemoteEnums.Entity;
    using GameOffsets.Objects.States.InGameState;

    /// <summary>
    ///     Points to the item in the game.
    ///     Item is basically anything that can be put in the inventory/stash.
    /// </summary>
    public class Item : Entity
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="Item" /> class.
        /// </summary>
        /// <param name="address">address of the Entity.</param>
        internal Item(IntPtr address)
            : base(address) { }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;

            // NOTE: ItemStruct is defined in EntityOffsets.cs file.
            var itemData = reader.ReadMemory<ItemStruct>(this.Address);

            // this.Id will always be 0x00 because Items don't have
            // Id associated with them.
            this.IsValid = true;
            this.EntityType = EntityTypes.Item;
            this.EntitySubtype = EntitySubtypes.InventoryItem;
            if (!this.UpdateComponentData(itemData, hasAddressChanged))
            {
                this.UpdateComponentData(itemData, true);
            }
        }
    }
}