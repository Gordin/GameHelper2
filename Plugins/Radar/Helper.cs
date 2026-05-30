// <copyright file="Helper.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace Radar
{
    using System;
    using System.Numerics;

    /// <summary>
    /// Contains the helper functions.
    /// </summary>
    public static class Helper
    {
        /// <summary>
        /// Map rotation in Radian format.
        /// </summary>
        public static readonly double CameraAngle = 38.7 * Math.PI / 180;
        private static double diagonalLength = 0x00;
        private static float scale = 0.5f;
        private static float cos = 0x00;
        private static float sin = 0x00;

        /// <summary>
        /// Sets the diagonal length of the Mini/Large Map UiElement,
        /// depending on what's visible.
        /// </summary>
        public static double DiagonalLength
        {
            private get
            {
                return diagonalLength;
            }

            set
            {
                if (value > 0 && value != diagonalLength)
                {
                    diagonalLength = value;
                    UpdateCosSin();
                }
            }
        }

        /// <summary>
        /// Sets the scale of the Mini/Large Map, depending on what's visible.
        /// </summary>
        public static float Scale
        {
            private get
            {
                return scale;
            }

            set
            {
                if (value > 0 && value != scale)
                {
                    scale = value;
                    UpdateCosSin();
                }
            }
        }

        /// <summary>
        /// Converts Entity to Player delta w.r.t Grid Postion
        /// to the Minimap pixel location Delta.
        /// </summary>
        /// <param name="delta">
        /// Grid postion delta between player and the entity to draw.
        /// This is due to the fact that player always remains at center of the mini/large,
        /// if we ignore the map shifting feature.
        /// </param>
        /// <param name="deltaZ">
        /// Terrain level difference between player and entity.
        /// </param>
        /// <returns>nothing.</returns>
        public static Vector2 DeltaInWorldToMapDelta(Vector2 delta, float deltaZ)
        {
            // WorldPosition distance between 2 points
            // divide it by GridPosition distance between 2 points.
            // Rounded to 5 decimal points.
            // reading game data shows that it's actually // 10.86957f
            deltaZ /= 10.86957f;
            return new Vector2((delta.X - delta.Y) * cos, (deltaZ - (delta.X + delta.Y)) * sin);
        }

        private static void UpdateCosSin()
        {
            // Magic number that works with diagnonal length.
            float mapScale = 240f / Scale;
            cos = (float)(DiagonalLength * Math.Cos(CameraAngle) / mapScale);
            sin = (float)(DiagonalLength * Math.Sin(CameraAngle) / mapScale);
        }
    }
}
