namespace GameHelper.RemoteObjects.Components
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using GameHelper.RemoteEnums;
    using GameHelper.Utils;
    using GameOffsets.Objects.Components;
    using ImGuiNET;

    /// <summary>
    ///     The <see cref="Stats" /> component in the entity.
    ///     Concurrency-safe publication of stats via atomic snapshot swap (no in-place mutation).
    /// </summary>
    public class Stats : ComponentBase
    {
        /// <summary>
        ///     All stats changed by items (published as immutable snapshots by reference swapping).
        ///     NOTE: The reference is replaced atomically; treat as read-only outside this class.
        /// </summary>
        public Dictionary<GameStats, int> StatsChangedByItems = new();

        /// <summary>
        ///     All stats changed by buffs and actions (published as immutable snapshots by reference swapping).
        ///     NOTE: The reference is replaced atomically; treat as read-only outside this class.
        /// </summary>
        public Dictionary<GameStats, int> StatsChangedByBuffAndActions = new();

        /// <summary>
        ///     Gets the WeaponIndex (I or II) that is currently active.
        /// </summary>
        public int CurrentWeaponIndex = 0;

        /// <summary>
        ///     Gets the value indicating if entity is in shape shifted form or not
        /// </summary>
        public bool IsInShapeshiftedForm = false;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Stats" /> class.
        /// </summary>
        /// <param name="address">address of the <see cref="Stats" /> component.</param>
        public Stats(IntPtr address)
            : base(address) { }

        /// <inheritdoc/>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"CurrentWeaponIndex: {this.CurrentWeaponIndex}");
            ImGui.Text($"IsInShapeshiftedForm: {this.IsInShapeshiftedForm}");

            // Read stable snapshots before rendering.
            var itemsSnapshot = Volatile.Read(ref this.StatsChangedByItems);
            var buffsSnapshot = Volatile.Read(ref this.StatsChangedByBuffAndActions);

            ImGuiHelper.StatsWidget(itemsSnapshot, "Entity Stats Changed By Items");
            ImGuiHelper.StatsWidget(buffsSnapshot, "Entity Stats Changed By BuffAndActions");
        }

        /// <summary>
        ///     Build fresh dictionaries from memory and atomically publish them.
        ///     Avoids mutating a shared Dictionary instance while other threads enumerate it.
        /// </summary>
        /// <param name="hasAddressChanged">Indicates whether the backing address changed.</param>
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<StatsOffsets>(this.Address);

            this.OwnerEntityAddress = data.Header.EntityPtr;
            this.CurrentWeaponIndex = data.CurrentWeaponIndex;
            this.IsInShapeshiftedForm = data.ShapeshiftFormsRowPtr != 0x00;

            // Build new snapshots locally (no shared mutations).
            var nextItems = new Dictionary<GameStats, int>();
            var nextBuffs = new Dictionary<GameStats, int>();

            if (data.StatsChangedByItemsPtr != IntPtr.Zero)
            {
                var data2 = reader.ReadMemory<StatsStructInternal>(data.StatsChangedByItemsPtr);
                // Fill the local dictionary; ComponentBase.StatUpdator writes into the provided instance.
                base.StatUpdator(nextItems, data2.Stats);
            }

            if (data.StatsChangedByBuffAndActions != IntPtr.Zero)
            {
                var data3 = reader.ReadMemory<StatsStructInternal>(data.StatsChangedByBuffAndActions);
                base.StatUpdator(nextBuffs, data3.Stats);
            }

            // Atomically publish fully-built snapshots so readers never see in-flight mutations.
            Volatile.Write(ref this.StatsChangedByItems, nextItems);
            Volatile.Write(ref this.StatsChangedByBuffAndActions, nextBuffs);
        }
    }
}