namespace GameOffsets.Natives
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    ///     A structure to read the c++ stdmap stored in the memory.
    ///     NOTE: A reader function that uses this datastructure exists
    ///     in SafeMemoryHandle class. If you modify this datastructure
    ///     modify that too.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct StdMap
    {
        public IntPtr Head;
        public int Size; // according to debugger this is long but for now int is working fine.
        public int PAD_C;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct StdMapNode<TKey, TValue>
        where TKey : struct
        where TValue : struct
    {
        public IntPtr Left; // 0x00
        public IntPtr Parent; // 0x08
        public IntPtr Right; // 0x10
        public byte Color; // 0x18
        public bool IsNil; // 0x19
        public byte pad_1A;
        public byte pad_1B;
        public uint pad_1C;
        public StdMapNodeData<TKey, TValue> Data; // 0x20
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct StdMapNodeData<TKey, TValue>
        where TKey : struct
        where TValue : struct
    {
        public TKey Key;
        public TValue Value;
    }
}