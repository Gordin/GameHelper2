namespace GameOffsets.Objects
{
    using System;
    using System.Runtime.InteropServices;
    using Natives;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct LoadedFilesRootObject
    {
        public StdBucket LoadedFiles;
        public static int TotalCount = 0x10;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FilesPointerStructure
    {
        public IntPtr Useless0;
        public IntPtr FilesPointer;
        public IntPtr Useless1;
    }

    [StructLayout(LayoutKind.Explicit, Pack = 1)]
    public struct FileInfoValueStruct
    {
        [FieldOffset(0x08)] public StdWString Name;

        //[FieldOffset(0x28)] public int FileType;
        //[FieldOffset(0x30)] public IntPtr UnknownPtr;
        //[FieldOffset(0x38)] public IntPtr UnknownConstant;
        [FieldOffset(0x40)] public int AreaChangeCount;

        // This saves a hell lot of memory but for debugging purposes
        // Feel free to set it to 0.
        public static readonly int IGNORE_FIRST_X_AREAS = 2;
    }
}