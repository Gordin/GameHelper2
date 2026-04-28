// <copyright file="Render.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.Components
{
    using System;
    using GameOffsets.Natives;
    using GameOffsets.Objects.Components;
    using GameOffsets.Objects.States.InGameState;
    using ImGuiNET;

    /// <summary>
    ///     The <see cref="Render" /> component in the entity.
    /// </summary>
    public class Render : ComponentBase
    {
        private static readonly float WorldToGridRatio =
            TileStructure.TileToWorldConversion / TileStructure.TileToGridConversion;

        private GridPos2DSnap gridSnap = new(0f, 0f);

        private sealed record GridPos2DSnap(float X, float Y);

        /// <summary>
        ///     Initializes a new instance of the <see cref="Render" /> class.
        /// </summary>
        /// <param name="address">address of the <see cref="Render" /> component.</param>
        public Render(IntPtr address)
            : base(address) { }

        /// <summary>
        ///     Gets the position where entity is located on the grid (map).
        ///     Returns a per-call snapshot — atomic with respect to UpdateData.
        ///     Z is always 0 (the underlying field is 2D).
        /// </summary>
        public StdTuple3D<float> GridPosition
        {
            get
            {
                var s = System.Threading.Volatile.Read(ref this.gridSnap);
                return new StdTuple3D<float> { X = s.X, Y = s.Y, Z = 0f };
            }
        }

        /// <summary>
        ///     Gets the position where entity is located on the grid (map).
        /// </summary>
        public StdTuple3D<float> ModelBounds { get; private set; }

        /// <summary>
        ///     Gets the position where entity is rendered in the game world.
        ///     NOTE: Z-Axis is pointing to the (visible/invisible) healthbar.
        /// </summary>
        public StdTuple3D<float> WorldPosition { get; private set; }

        /// <summary>
        ///     Gets the terrain height on which the Entity is standing.
        /// </summary>
        public float TerrainHeight { get; private set; }

        /// <summary>
        ///     Converts the <see cref="Render" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            ImGui.Text($"Grid Position: {this.GridPosition}");
            ImGui.Text($"World Position: {this.WorldPosition}");
            ImGui.Text($"Terrain Height (Z-Axis): {this.TerrainHeight}");
            ImGui.Text($"Model Bounds: {this.ModelBounds}");
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<RenderOffsets>(this.Address);
            this.OwnerEntityAddress = data.Header.EntityPtr;
            this.WorldPosition = data.CurrentWorldPosition;
            this.ModelBounds = data.CharactorModelBounds;
            this.TerrainHeight = (float)Math.Round(data.TerrainHeight, 4);

            var newX = data.CurrentWorldPosition.X / WorldToGridRatio;
            var newY = data.CurrentWorldPosition.Y / WorldToGridRatio;
            System.Threading.Volatile.Write(ref this.gridSnap, new GridPos2DSnap(newX, newY));
        }
    }
}