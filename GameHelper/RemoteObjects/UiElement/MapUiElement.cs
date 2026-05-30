// <copyright file="MiniMapUiElement.cs" company="None">
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
    ///     Points to the Map UiElement.
    /// </summary>
    public class MapUiElement : UiElementBase
    {
        protected Vector2 defaultShift = Vector2.Zero;
        protected Vector2 shift = Vector2.Zero;
        private IntPtr visibilityAddress;

        /// <summary>
        ///     Initializes a new instance of the <see cref="MapUiElement" /> class.
        /// </summary>
        /// <param name="address">address to the Map Ui Element of the game.</param>
        /// <param name="parents">parent cache to use for this Ui Element.</param>
        internal MapUiElement(IntPtr address, UiElementParents parents)
            : base(address, parents) { }

        /// <summary>
        ///     Gets the value indicating how much map has shifted.
        /// </summary>
        public Vector2 Shift => this.shift;

        /// <summary>
        ///     Gets the value indicating shifted amount at rest (default).
        /// </summary>
        public Vector2 DefaultShift => this.defaultShift;

        /// <summary>
        ///     Gets the value indicating amount of zoom in the Map.
        ///     Normally values are between 0.5f  - 1.5f.
        /// </summary>
        public float Zoom { get; protected set; } = 0.5f;

        /// <inheritdoc />
        public override bool IsVisible
        {
            get
            {
                if (this.visibilityAddress == IntPtr.Zero)
                {
                    return base.IsVisible;
                }

                var data = Core.Process.Handle.ReadMemory<UiElementBaseOffset>(this.visibilityAddress);
                return UiElementBaseFuncs.IsVisibleChecker(data.Flags);
            }
        }

        internal virtual void SetVisibilityAddress(IntPtr value)
        {
            this.visibilityAddress = value;
        }

        /// <summary>
        ///     Converts the <see cref="LargeMapUiElement" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"Visibility Address {this.visibilityAddress.ToInt64():X}");
            ImGui.Text($"Shift {this.shift}");
            ImGui.Text($"Default Shift {this.defaultShift}");
            ImGui.Text($"Zoom {this.Zoom}");
        }

        /// <inheritdoc />
        protected override void CleanUpData()
        {
            base.CleanUpData();
            this.shift = default;
            this.defaultShift = default;
            this.Zoom = 0.5f;
            this.visibilityAddress = IntPtr.Zero;
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var data = Core.Process.Handle.ReadMemory<MapUiElementOffset>(this.Address);
            this.UpdateData(data.UiElementBase, hasAddressChanged);
            this.UpdateMapData(data);

        }

        protected void UpdateMapData(MapUiElementOffset data)
        {
            this.shift.X = data.Shift.X;
            this.shift.Y = data.Shift.Y;

            this.defaultShift.X = data.DefaultShift.X;
            this.defaultShift.Y = data.DefaultShift.Y;

            this.Zoom = data.Zoom;
        }
    }
}
