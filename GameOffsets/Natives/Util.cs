namespace GameOffsets.Natives
{
    using System;

    public static class Util
    {
        /// <summary>
        ///     Checks if the bit is set or not. Pos can be any number from 0 to 7.
        /// </summary>
        public static Func<byte, int, bool> isBitSetByte = (x, pos) => (x & (1 << pos)) != 0;

        /// <summary>
        ///     Checks if the bit is set or not. Pos can be any number from 0 to 31.
        /// </summary>
        public static Func<uint, int, bool> isBitSetUint = (x, pos) => (x & (1 << pos)) != 0;
    }
}