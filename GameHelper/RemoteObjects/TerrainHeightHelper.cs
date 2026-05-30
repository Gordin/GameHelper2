// <copyright file="TerrainHeightHelper.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects
{
    using System;
    using ImGuiNET;

    /// <summary>
    ///     Contains the static data for calculating the terrain height.
    /// </summary>
    public class TerrainHeightHelper : RemoteObjectBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="TerrainHeightHelper" /> class.
        /// </summary>
        /// <param name="address">address of the remote memory object.</param>
        /// <param name="size">expected size of this remote memory object.</param>
        internal TerrainHeightHelper(IntPtr address, int size)
            : base(address)
        {
            this.Values = new byte[size];
        }

        /// <summary>
        ///     Gets the values associated with this class.
        /// </summary>
        public byte[] Values { get; private set; }

        /// <inheritdoc />
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text(string.Join(' ', this.Values));
        }

        /// <inheritdoc />
        protected override void CleanUpData()
        {
            for (var i = 0; i < this.Values.Length; i++)
            {
                this.Values[i] = 0;
            }
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            this.Values = reader.ReadMemoryArray<byte>(this.Address, this.Values.Length);
        }
    }
}