namespace GameOffsets.Natives
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    ///     Not sure the name of this structure. POE game uses it in a lot of different places
    ///     it might be a stdbucket, std unordered_set or std_unordered_multiset.
    ///     TODO: Create c++ HelloWorld program,
    ///     Create these structures (name it var),
    ///     Fill 1 value,
    ///     Use cheat engine on that HelloWorld Program.
    ///     HINT: use cout << &var << endl; to print memory address.
    ///     NOTE: A reader function that uses this datastructure exists
    ///     in SafeMemoryHandle class. If you modify this datastructure
    ///     modify that function too.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct StdBucket
    {
        public StdVector Data; // ComponentArrayStructure
        public IntPtr UnknownPtr; // todo: figure out what this pointer store
        public int Capacity; // actually, it's Capacity number - 1. // given that the Data is StdVector, we don't need this
        public int PAD_0x24; // byte + padd
        public int Unknown1;
        public int PAD_0x2C;
        public int Unknown2;
        public int Unknown3;
    }
}