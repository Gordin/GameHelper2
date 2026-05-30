namespace GameOffsets.Natives
{
    using System;
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct StdVector
    {
        public IntPtr First;
        public IntPtr Last;
        public IntPtr End;

        /// <summary>
        ///     Counts the number of elements in the StdVector.
        /// </summary>
        /// <param name="elementSize">Number of bytes in 1 element.</param>
        /// <returns></returns>
        public long TotalElements(int elementSize)
        {
            return (this.Last.ToInt64() - this.First.ToInt64()) / elementSize;
        }

        public override string ToString()
        {
            return $"First: {this.First.ToInt64():X} - " +
                   $"Last: {this.Last.ToInt64():X} - " +
                   $"Size (bytes): {this.TotalElements(1)}";
        }
    }
}