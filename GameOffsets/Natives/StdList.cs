namespace GameOffsets.Natives
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    ///     A structure to read the c++ stdlist stored in the memory.
    ///     NOTE: A reader function that uses this datastructure exists
    ///     in SafeMemoryHandle class. If you modify this datastructure
    ///     modify that function too.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct StdList
    {
        public IntPtr Head;
        public int Size; // according to debugger this is long but for now int is working fine.
        public int PAD_C;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct StdListNode<TValue>
        where TValue : struct
    {
        public IntPtr Next;
        public IntPtr Previous;
        public TValue Data;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct StdListNode
    {
        public IntPtr Next;
        public IntPtr Previous;
    }
}