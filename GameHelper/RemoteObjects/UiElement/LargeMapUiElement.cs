// <copyright file="LargeMapUiElement.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.UiElement
{
    using System;
    using System.Numerics;
    using GameHelper.Cache;
    using GameOffsets.Objects.UiElement;
    using ImGuiNET;

    /// <summary>
    ///     Points to the LargeMap UiElement.
    ///     It is exactly like any other element, except its in-memory position is its center
    /// </summary>
    public class LargeMapUiElement : MapUiElement
    {
        private readonly UiElementBase centerElement;
        private readonly UiElementBase verticalCenterElement;
        private readonly UiElementBase visibilityElement;
        private readonly UiElementBase inverseVisibilityElement;

        /// <summary>
        ///     Initializes a new instance of the <see cref="LargeMapUiElement" /> class.
        /// </summary>
        /// <param name="address">address to the Map Ui Element of the game.</param>
        /// <param name="parents">parent cache to use for this Ui Element.</param>
        internal LargeMapUiElement(IntPtr address, UiElementParents parents)
            : base(address, parents)
        {
            this.centerElement = new UiElementBase(IntPtr.Zero, parents);
            this.verticalCenterElement = new UiElementBase(IntPtr.Zero, parents);
            this.visibilityElement = new UiElementBase(IntPtr.Zero, parents);
            this.inverseVisibilityElement = new UiElementBase(IntPtr.Zero, parents);
        }

        /// <inheritdoc />
        public override Vector2 Position => new(Core.GameCull.Value, 0f);

        /// <inheritdoc />
        public override Vector2 Size => new(Core.Process.WindowArea.Width - (Core.GameCull.Value * 2), Core.Process.WindowArea.Height);

        /// <summary>
        ///     Gets the center of the map.
        /// </summary>
        public Vector2 Center
        {
            get
            {
                var center = this.centerElement.Position;
                if (this.verticalCenterElement.Address == IntPtr.Zero)
                {
                    return center;
                }

                return new Vector2(center.X, this.verticalCenterElement.Position.Y);
            }
        }

        public override bool IsVisible
        {
            get
            {
                if (this.visibilityElement.Address == IntPtr.Zero)
                {
                    return false;
                }

                var data = Core.Process.Handle.ReadMemory<UiElementBaseOffset>(this.visibilityElement.Address);
                return UiElementBaseFuncs.IsVisibleChecker(data.Flags);
            }
        }

        internal override void SetVisibilityAddress(IntPtr value)
        {
            this.visibilityElement.Address = value;
        }

        internal void SetInverseVisibilityAddress(IntPtr value)
        {
            this.inverseVisibilityElement.Address = value;
        }

        internal void SetCenterAddress(IntPtr value)
        {
            this.centerElement.Address = value;
        }

        internal void SetVerticalCenterAddress(IntPtr value)
        {
            this.verticalCenterElement.Address = value;
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<MapUiElementOffset>(this.Address);
            this.UpdateData(data.UiElementBase, hasAddressChanged);

            var mapDataAddress = data.UiElementBase.ChildrensPtr.First;
            if (mapDataAddress != IntPtr.Zero)
            {
                mapDataAddress = reader.ReadMemory<IntPtr>(mapDataAddress);
            }

            if (mapDataAddress == IntPtr.Zero)
            {
                this.UpdateMapData(data);
                return;
            }

            this.UpdateMapData(reader.ReadMemory<MapUiElementOffset>(mapDataAddress));
        }

        protected override void CleanUpData()
        {
            base.CleanUpData();

            if (this.centerElement != null)
            {
                this.centerElement.Address = IntPtr.Zero;
            }

            if (this.verticalCenterElement != null)
            {
                this.verticalCenterElement.Address = IntPtr.Zero;
            }

            if (this.visibilityElement != null)
            {
                this.visibilityElement.Address = IntPtr.Zero;
            }

            if (this.inverseVisibilityElement != null)
            {
                this.inverseVisibilityElement.Address = IntPtr.Zero;
            }
        }

        /// <summary>
        ///     Converts the <see cref="LargeMapUiElement" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"Visibility Address {this.visibilityElement.Address.ToInt64():X}");
            ImGui.Text($"Inverse Visibility Address {this.inverseVisibilityElement.Address.ToInt64():X}");
            ImGui.Text($"Center Address {this.centerElement.Address.ToInt64():X}");
            ImGui.Text($"Vertical Center Address {this.verticalCenterElement.Address.ToInt64():X}");
            ImGui.Text($"Center (without shift/default-shift) {this.Center}");
        }
    }
}
