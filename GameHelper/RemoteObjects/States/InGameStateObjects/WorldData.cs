// <copyright file="WorldData.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.States.InGameStateObjects
{
    using System;
    using System.Collections.Generic;
    using System.Numerics;
    using Coroutine;
    using GameHelper.CoroutineEvents;
    using GameHelper.RemoteObjects.FilesStructures;
    using GameOffsets.Natives;
    using GameOffsets.Objects.States.InGameState;
    using ImGuiNET;

    /// <summary>
    ///     Points to InGameState -> WorldData object.
    /// </summary>
    public class WorldData : RemoteObjectBase
    {
        private IntPtr areaDetailsPtrCache = IntPtr.Zero;
        /// <summary>
        ///     Wrapper for the World-to-Screen matrix. Wrapped in a sealed record
        ///     so we can swap it atomically via Volatile.Write — a 64-byte struct
        ///     write is not atomic on x64, which produces torn matrices for
        ///     concurrent WorldToScreen readers (audit F-131).
        /// </summary>
        private sealed record MatrixSnap(Matrix4x4 M);
        private MatrixSnap matrixSnap = new(Matrix4x4.Identity);

        /// <summary>
        ///     Gets the Area Details.
        /// </summary>
        public WorldAreaDat AreaDetails { get; } = new(IntPtr.Zero);

        /// <summary>
        ///     Converts the World position to Screen location.
        /// </summary>
        /// <param name="worldPosition">3D world position of the entity.</param>
        /// <returns>screen location of the entity.</returns>
        public Vector2 WorldToScreen(StdTuple3D<float> worldPosition) =>
            this.WorldToScreen(worldPosition, worldPosition.Z);

        /// <summary>
        ///     Converts the World position to Screen location.
        /// </summary>
        /// <param name="worldPosition">3D world position of the entity.</param>
        /// <param name="height">height value (e.g. terrain heigh or entity Z axis)</param>
        /// <returns>screen location of the entity.</returns>
        public Vector2 WorldToScreen(StdTuple3D<float> worldPosition, float height)
        {
            if (this.Address == IntPtr.Zero)
            {
                return Vector2.Zero;
            }

            var snap = System.Threading.Volatile.Read(ref this.matrixSnap);
            var matrix = snap.M;

            Vector2 result = Vector2.Zero;
            double[] tmpResult = [0, 0, 0, 0];
            double[] temp0 = [worldPosition.X, worldPosition.Y, height, 1.0f];

            for (var i = 0; i < 4; i++)
            {
                tmpResult[i] = 0;
                for (var j = 0; j < 4; j++)
                {
                    tmpResult[i] += matrix[j, i] * temp0[j];
                }
            }

            for (var i = 0; i < 4; i++)
            {
                tmpResult[i] /= tmpResult[3];
            }

            tmpResult[0] = (tmpResult[0] + 1.0f) * (Core.Process.WindowArea.Width / 2.0f);
            tmpResult[1] = (1.0f - tmpResult[1]) * (Core.Process.WindowArea.Height / 2.0f);
            result.X = (float)Math.Round(tmpResult[0], 6, MidpointRounding.ToNegativeInfinity);
            result.Y = (float)Math.Round(tmpResult[1], 6, MidpointRounding.ToNegativeInfinity);
            return result;
        }

        /// <summary>
        ///     Converts the World position to Screen location.
        /// </summary>
        /// <param name="worldPosition">3D world position of the entity.</param>
        /// <param name="height">height value (e.g. terrain heigh or entity Z axis)</param>
        /// <returns>screen location of the entity.</returns>
        public Vector2 WorldToScreen(Vector2 worldPosition, float height)
        {
            if (this.Address == IntPtr.Zero)
            {
                return Vector2.Zero;
            }

            var snap = System.Threading.Volatile.Read(ref this.matrixSnap);
            var matrix = snap.M;

            Vector2 result = Vector2.Zero;
            double[] tmpResult = [0, 0, 0, 0];
            double[] temp0 = [worldPosition.X, worldPosition.Y, height, 1.0f];

            for (var i = 0; i < 4; i++)
            {
                tmpResult[i] = 0;
                for (var j = 0; j < 4; j++)
                {
                    tmpResult[i] += matrix[j, i] * temp0[j];
                }
            }

            for (var i = 0; i < 4; i++)
            {
                tmpResult[i] /= tmpResult[3];
            }

            tmpResult[0] = (tmpResult[0] + 1.0f) * (Core.Process.WindowArea.Width / 2.0f);
            tmpResult[1] = (1.0f - tmpResult[1]) * (Core.Process.WindowArea.Height / 2.0f);
            result.X = (float)Math.Round(tmpResult[0], 6, MidpointRounding.ToNegativeInfinity);
            result.Y = (float)Math.Round(tmpResult[1], 6, MidpointRounding.ToNegativeInfinity);
            return result;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="WorldData" /> class.
        /// </summary>
        /// <param name="address">address of the remote memory object.</param>
        internal WorldData(IntPtr address)
            : base(address)
        {
            Core.CoroutinesRegistrar.Add(CoroutineHandler.Start(
                this.OnPerFrame(), "[AreaInstance] Update World Data"));
        }

        /// <summary>
        ///     Converts the <see cref="WorldData" /> class data to ImGui.
        /// </summary>
        internal override void ToImGui()
        {
            base.ToImGui();
            if (ImGui.TreeNode("WindowToScreenMatrix"))
            {
                var d = System.Threading.Volatile.Read(ref this.matrixSnap).M;
                ImGui.Text($"{d.M11:0.00}\t{d.M12:0.00}\t{d.M13:0.00}\t{d.M14:0.00}");
                ImGui.Text($"{d.M21:0.00}\t{d.M22:0.00}\t{d.M23:0.00}\t{d.M24:0.00}");
                ImGui.Text($"{d.M31:0.00}\t{d.M32:0.00}\t{d.M33:0.00}\t{d.M34:0.00}");
                ImGui.Text($"{d.M41:0.00}\t{d.M42:0.00}\t{d.M43:0.00}\t{d.M44:0.00}");
                ImGui.TreePop();
            }
        }

        /// <inheritdoc />
        protected override void CleanUpData()
        {
            this.areaDetailsPtrCache = IntPtr.Zero;
            this.AreaDetails.Address = IntPtr.Zero;
            System.Threading.Volatile.Write(ref this.matrixSnap, new MatrixSnap(Matrix4x4.Identity));
        }

        /// <inheritdoc />
        protected override void UpdateData(bool hasAddressChanged)
        {
            var reader = Core.Process.Handle;
            var data = reader.ReadMemory<WorldDataOffset>(this.Address);
            if (this.areaDetailsPtrCache != data.WorldAreaDetailsPtr)
            {
                var areaInfo = reader.ReadMemory<WorldAreaDetailsStruct>(data.WorldAreaDetailsPtr);
                this.AreaDetails.Address = areaInfo.WorldAreaDetailsRowPtr;
                this.areaDetailsPtrCache = data.WorldAreaDetailsPtr;
            }

            System.Threading.Volatile.Write(ref this.matrixSnap, new MatrixSnap(data.CameraStructurePtr.WorldToScreenMatrix));
        }

        private IEnumerator<Wait> OnPerFrame()
        {
            while (true)
            {
                yield return new Wait(GameHelperEvents.PostPerFrameDataUpdate);
                if (this.Address != IntPtr.Zero)
                {
                    this.UpdateData(false);
                }
            }
        }
    }
}
