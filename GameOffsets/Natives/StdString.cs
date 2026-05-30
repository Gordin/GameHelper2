namespace GameOffsets.Natives
{
    using System;
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct StdString
    {
        public IntPtr Buffer;

        //// There is an optimization in std::string, where
        //// if a Capacity is less than or equal to 15
        //// then the string is stored locally (without a pointer).
        //// Since the pointer takes 8 bytes and the reserved field takes 8 bytes (16 total),
        //// std::string can fit 15 ASCII characters + null terminator in-place.
        public IntPtr ReservedBytes;
        public int Length;
        public int PAD_14;
        public int Capacity;
        public int PAD_1C;

        public override string ToString()
        {
            return $"Buffer: {this.Buffer.ToInt64():X}, ReservedBytes: {this.ReservedBytes.ToInt64():X}, " +
                   $"Length: {this.Length}, Capacity: {this.Capacity}";
        }
    }
}
