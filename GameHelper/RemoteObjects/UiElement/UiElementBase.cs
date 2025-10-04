// <copyright file="UiElementBase.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.UiElement
{
    using System;
    using System.Numerics;
    using GameHelper.Cache;
    using GameOffsets.Objects.UiElement;
    using ImGuiNET;
    using Ui;
    using Utils;

    /// <summary>
    ///     Points to the Ui Element of the game and reads its data.
    /// </summary>
    public class UiElementBase : RemoteObjectBase
    {
        private Vector2 positionModifier;
        private bool show;
        private IntPtr[] childrenAddresses;
        private uint flags; // IsVisible and ShouldModifyPostion information
        private float localScaleMultiplier;
        private Vector2 relativePosition;
        private Vector2 unScaledSize; // Size before applying the scale multiplier.
        private IntPtr parentAddress;
        private readonly UiElementParents parents;
        protected Vector4 backgroundColor;

        /// <summary>
        ///     Index of <see cref="GameWindowScale"/>
        /// </summary>
        private byte scaleIndex;

        /// <summary>
        ///     Initializes a new instance of the <see cref="UiElementBase" /> class.
        /// </summary>
        /// <param name="address">address to the Ui Element of the game.</param>
        /// <param name="parents">parents cache to use for this Ui Element.</param>
        internal UiElementBase(IntPtr address, UiElementParents parents)
            : base(address, true, true)
        {
            this.CleanUpData();
            this.parents = parents;
            if (address != IntPtr.Zero )
            {
                this.UpdateData(true);
            }
        }

        /// <summary>
        ///     Gets the scale factor of the UiElement (ratio of the current UiElement's height to the base UiElement's height).
        /// </summary>
        public float Scale => this.positionModifier.X;

        /// <summary>
        ///     Gets the position of the Ui Element w.r.t the game UI.
        /// </summary>
        public virtual Vector2 Postion
        {
            get
            {
                var (widthScale, heightScale) = Core.GameScale.GetScaleValue(
                    this.scaleIndex, this.localScaleMultiplier);
                var pos = this.GetUnScaledPosition();
                pos.X *= widthScale;
                pos.Y *= heightScale;
                pos.X += Core.GameCull.Value;
                return pos;
            }
        }

        /// <summary>
        ///     Gets the size of the Ui Element w.r.t the game UI.
        /// </summary>
        public virtual Vector2 Size
        {
            get
            {
                var (widthScale, heightScale) = Core.GameScale.GetScaleValue(
                    this.scaleIndex, this.localScaleMultiplier);
                var size = this.unScaledSize;
                size.X *= widthScale;
                size.Y *= heightScale;
                return size;
            }
        }

        /// <summary>
        ///     Gets a value indicating whether the Ui Element is visible or not.
        /// </summary>
        public bool IsVisible
        {
            get
            {
                if (!UiElementBaseFuncs.IsVisibleChecker(this.flags))
                {
                    return false;
                }
                if (this.parentAddress == IntPtr.Zero)
                {
                    return true;
                }

                // During transitions, parent might not be cached yet; treat as not visible rather than throw
                return this.TryGetParent(out var parent) && parent != null && parent.IsVisible;
            }
        }

        /// <summary>
        ///     Gets the total number of childrens this Ui Element has.
        /// </summary>
        public int TotalChildrens => this.childrenAddresses.Length;

        public bool TryGetParent(out UiElementBase parent)
        {
            if (this.parentAddress == IntPtr.Zero)
            {
                parent = null;
                return false;
            }

            try
            {
                parent = this.parents.GetParent(this.parentAddress);
                return parent != null;
            }
            catch
            {
                // Parent not yet cached or cache chain in transition.
                parent = null;
                return false;
            }
        }

        /// <summary>
        ///     Gets the child Ui Element at specified index.
        ///     returns null in case of invalid index.
        /// </summary>
        /// <param name="i">index of the child Ui Element.</param>
        /// <returns>the child Ui Element.</returns>
        [SkipImGuiReflection]
        public UiElementBase this[int i]
        {
            get
            {
                if (this.childrenAddresses.Length <= i)
                {
                    return null;
                }

                return new UiElementBase(this.childrenAddresses[i], this.parents);
            }
        }

        /// <summary>
        ///     Converts the <see cref="UiElementBase" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            ImGui.Checkbox("Show", ref this.show);
            ImGui.SameLine();
            if (ImGui.Button("Explore"))
            {
                GameUiExplorer.AddUiElement(this);
            }

            base.ToImGui();
            if (this.show)
            {
                ImGuiHelper.DrawRect(this.Postion, this.Size, 255, 255, 0);
            }

            ImGui.Text($"Position  {this.Postion}");
            ImGui.Text($"Size  {this.Size}");
            ImGui.Text($"Unscaled Size {this.unScaledSize}");
            ImGui.Text($"IsVisible  {this.IsVisible}");
            ImGui.Text($"Total Childrens  {this.TotalChildrens}");
            ImGui.Text($"Parent  {this.parentAddress.ToInt64():X}");
            ImGui.Text($"Position Modifier {this.positionModifier}");
            ImGui.Text($"Scale Index {this.scaleIndex}");
            ImGui.Text($"Local Scale Multiplier {this.localScaleMultiplier}");
            ImGui.Text($"Flags: {this.flags:X}");
            ImGui.Text("Background Color");
            ImGui.SameLine();
            ImGui.ColorButton("##UiElementBackgroundColor", this.backgroundColor);
        }

        /// <inheritdoc />
        protected override void CleanUpData()
        {
            this.positionModifier = Vector2.Zero;
            this.show = false;
            this.childrenAddresses = Array.Empty<IntPtr>();
            this.flags = 0x00;
            this.localScaleMultiplier = 0x01;
            this.relativePosition = Vector2.Zero;
            this.unScaledSize = Vector2.Zero;
            this.scaleIndex = 0x00;
            this.parentAddress = IntPtr.Zero;
            this.backgroundColor = Vector4.Zero;
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            this.UpdateData(Core.Process.Handle.ReadMemory<UiElementBaseOffset>(this.Address), hasAddressChanged);
        }

        /// <summary>
        ///     Updates the UiElement data.
        /// </summary>
        /// <param name="data">UiElementBaseOffset structure read from the game memory.</param>
        /// <param name="hasAddressChanged">has the address of this object changed or not.</param>
        /// <exception cref="Exception">Throws an exception if it detects invalid UiElement.</exception>
        protected void UpdateData(UiElementBaseOffset data, bool hasAddressChanged)
        {
            if (data.Self != IntPtr.Zero && data.Self != this.Address)
            {
                throw new Exception($"This (address: {this.Address.ToInt64():X})" +
                                    $"is not a Ui Element. Self Address = {data.Self.ToInt64():X}");
            }

            this.parentAddress = data.ParentPtr;
            this.parents.AddIfNotExists(data.ParentPtr);
            this.childrenAddresses = Core.Process.Handle.ReadStdVector<IntPtr>(data.ChildrensPtr);

            this.positionModifier.X = data.PositionModifier.X;
            this.positionModifier.Y = data.PositionModifier.Y;

            this.scaleIndex = data.ScaleIndex;
            this.localScaleMultiplier = data.LocalScaleMultiplier;
            this.flags = data.Flags;

            this.relativePosition.X = data.RelativePosition.X;
            this.relativePosition.Y = data.RelativePosition.Y;

            this.unScaledSize.X = data.UnscaledSize.X;
            this.unScaledSize.Y = data.UnscaledSize.Y;

            this.backgroundColor = ImGuiHelper.Color(data.BackgroundColor);
        }

        /// <summary>
        ///     This function was basically parsed/read/decompiled from the game.
        ///     To find this function in the game, follow the data used in this function.
        ///     Although, this function haven't changed since last 3-4 years.
        /// </summary>
        /// <returns>Returns position without applying current element scaling values.</returns>
        private Vector2 GetUnScaledPosition()
        {
            // During zone/state transitions, the parent cache can be temporarily empty.
            if (!this.TryGetParent(out var myParent) || myParent == null)
            {
                // Treat as root during the brief window; avoids hard crash.
                return this.relativePosition;
            }
            var parentPos = myParent.GetUnScaledPosition();
            if (UiElementBaseFuncs.ShouldModifyPos(this.flags))
            {
                parentPos += myParent.positionModifier;
            }

            if (myParent.scaleIndex == this.scaleIndex &&
                myParent.localScaleMultiplier == this.localScaleMultiplier)
            {
                return parentPos + this.relativePosition;
            }

            var (parentScaleW, parentScaleH) = Core.GameScale.GetScaleValue(
                myParent.scaleIndex, myParent.localScaleMultiplier);
            var (myScaleW, myScaleH) = Core.GameScale.GetScaleValue(
                this.scaleIndex, this.localScaleMultiplier);
            Vector2 myPos;
            myPos.X = parentPos.X * parentScaleW / myScaleW
                      + this.relativePosition.X;
            myPos.Y = parentPos.Y * parentScaleH / myScaleH
                      + this.relativePosition.Y;
            return myPos;
        }
    }
}