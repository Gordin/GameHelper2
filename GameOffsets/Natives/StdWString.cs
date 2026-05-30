// <copyright file="StdWString.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameOffsets.Natives
{
    using System;
    using System.Runtime.InteropServices;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct StdWString
    {
        public IntPtr Buffer;

        //// There is an optimization in std::wstring, where
        //// if a Capacity is less than or equal to 8
        //// then the wstring is stored locally (without a pointer).
        //// Since the pointer takes 8 bytes and 8 Capacity wstring takes 16 bytes
        //// wstring reserves 8 bytes over here which is then used to store the string.
        public IntPtr ReservedBytes;
        public int Length; // according to debugger this is long but for now int is working fine.
        public int PAD_14;
        public int Capacity; // according to debugger this is long but for now int is working fine.
        public int PAD_1C;

        public override string ToString()
        {
            return $"Buffer: {this.Buffer.ToInt64():X}, ReservedBytes: {this.ReservedBytes.ToInt64():X}, " +
                   $"Length: {this.Length}, Capacity: {this.Capacity}";
        }
    }
}