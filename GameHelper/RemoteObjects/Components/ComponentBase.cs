// <copyright file="ComponentBase.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>


namespace GameHelper.RemoteObjects.Components
{
    using System;
    using GameHelper.RemoteEnums;
    using System.Collections.Generic;
    using GameHelper.Utils;
    using GameOffsets.Objects.Components;
    using GameOffsets.Natives;

    /// <summary>
    ///     Component base object that contains component owner entity address.
    ///     All components in the game have this.
    /// </summary>
    public class ComponentBase : RemoteObjectBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ComponentBase" /> class.
        /// </summary>
        /// <param name="Address"></param>
        public ComponentBase(IntPtr Address) :
            base(Address, true)
        {
        }

        /// <summary>
        ///     Owner entity address of this component.
        /// </summary>
        protected IntPtr OwnerEntityAddress;

        /// <inheritdoc />
        protected override void CleanUpData()
        {
            // Zero the only mutable field in the base. Derived components
            // can override to clean their own state. Previously this threw
            // unconditionally (audit F-112) which propagated through the
            // Address setter and (pre-Phase-1) killed the entity reader.
            this.OwnerEntityAddress = IntPtr.Zero;
        }

        /// <inheritdoc />
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGuiHelper.IntPtrToImGui("Owner Address", this.OwnerEntityAddress);
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var data = Core.Process.Handle.ReadMemory<ComponentHeader>(this.Address);
            this.OwnerEntityAddress = data.EntityPtr;
        }

        /// <summary>
        ///     Validate if the component is pointing to parent entity address or not
        /// </summary>
        /// <param name="parentEntityAddress">true if component is pointing to parent entity address otherwise false</param>
        /// <returns></returns>
        public bool IsParentValid(IntPtr parentEntityAddress)
        {
            return this.OwnerEntityAddress == parentEntityAddress;
        }

        protected void StatUpdator(Dictionary<GameStats, int> stats, StdVector statsptr)
        {
            var mystats = Core.Process.Handle.ReadStdVector<StatArrayStruct>(statsptr);
            lock (stats)
            {
                stats.Clear();
                foreach (var newStat in mystats)
                {
                    stats[(GameStats)newStat.key] = newStat.value;
                }
            }
        }
    }
}
