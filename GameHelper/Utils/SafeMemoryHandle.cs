// <copyright file="SafeMemoryHandle.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using GameOffsets.Natives;
    using Microsoft.Win32.SafeHandles;
    using ProcessMemoryUtilities.Managed;
    using ProcessMemoryUtilities.Native;

    /// <summary>
    ///     Handle to a process.
    /// </summary>
    internal class SafeMemoryHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>
        ///     Max valid user-mode address on 64-bit Windows (48-bit addressing).
        /// </summary>
        private const long MaxUserModeAddress = 0x7FFFFFFFFFFF;

        /// <summary>
        ///     Required by SafeHandle infrastructure for finalizer / marshaling support.
        ///     Private to prevent callers accidentally constructing a zombie handle
        ///     without a PID — see audit F-034. Real construction must go through
        ///     the <see cref="SafeMemoryHandle(int)"/> ctor.
        /// </summary>
        private SafeMemoryHandle()
            : base(true)
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="SafeMemoryHandle" /> class.
        /// </summary>
        /// <param name="processId">processId you want to access.</param>
        internal SafeMemoryHandle(int processId)
            : base(true)
        {
            var handle = NativeWrapper.OpenProcess(ProcessAccessFlags.VirtualMemoryRead, processId);
            if (NativeWrapper.HasError)
            {
                Console.WriteLine($"Failed to open a new handle 0x{handle:X}" +
                                  $" due to ErrorNo: {NativeWrapper.LastError}");
            }
            else
            {
                Console.WriteLine($"Opened a new handle using IntPtr 0x{handle:X}");
            }

            this.SetHandle(handle);
        }

        /// <summary>
        ///     Reads the process memory as type T.
        /// </summary>
        /// <typeparam name="T">type of data structure to read.</typeparam>
        /// <param name="address">address to read the data from.</param>
        /// <returns>data from the process in T format.</returns>
        internal T ReadMemory<T>(IntPtr address)
            where T : unmanaged
        {
            T result = default;
            var addr = address.ToInt64();
            if (this.IsInvalid || addr <= 0 || addr > MaxUserModeAddress)
            {
                return result;
            }

            try
            {
                if (!NativeWrapper.ReadProcessMemory(this.handle, address, ref result))
                {
                    throw new Exception("Failed To Read the Memory (T)" +
                                        $" due to Error Number: 0x{NativeWrapper.LastError:X} on " +
                                        $"adress 0x{address.ToInt64():X}");
                }

                return result;
            }
            catch (Exception e)
            {
                Console.WriteLine($"ERROR: {e.Message}");
                return default;
            }
        }

        /// <summary>
        ///     Reads the std::vector into an array.
        /// </summary>
        /// <typeparam name="T">Object type to read.</typeparam>
        /// <param name="nativeContainer">StdVector address to read from.</param>
        /// <returns>An array of elements of type T.</returns>
        internal T[] ReadStdVector<T>(StdVector nativeContainer)
            where T : unmanaged
        {
            var typeSize = Marshal.SizeOf<T>();
            var length = nativeContainer.Last.ToInt64() - nativeContainer.First.ToInt64();
            if (length <= 0 || length % typeSize != 0 || length > 16_000_000)
            {
                return Array.Empty<T>();
            }

            return this.ReadMemoryArray<T>(nativeContainer.First, (int)length / typeSize);
        }

        /// <summary>
        ///     Reads the process memory as an array.
        /// </summary>
        /// <typeparam name="T">Array type to read.</typeparam>
        /// <param name="address">memory address to read from.</param>
        /// <param name="nsize">total array elements to read.</param>
        /// <returns>
        ///     An array of type T and of size nsize. In case or any error it returns empty array.
        /// </returns>
        internal T[] ReadMemoryArray<T>(IntPtr address, int nsize)
            where T : unmanaged
        {
            var addr = address.ToInt64();
            if (this.IsInvalid || addr <= 0 || addr > MaxUserModeAddress || nsize <= 0)
            {
                return Array.Empty<T>();
            }

            var buffer = new T[nsize];
            try
            {
                if (!NativeWrapper.ReadProcessMemoryArray(
                    this.handle, address, buffer, out var numBytesRead))
                {
                    throw new Exception("Failed To Read the Memory (array)" +
                                        $" due to Error Number: 0x{NativeWrapper.LastError:X}" +
                                        $" on address 0x{address.ToInt64():X} with size {nsize}");
                }

                var expectedBytes = (long)nsize * Marshal.SizeOf<T>();
                if (numBytesRead.ToInt64() < expectedBytes)
                {
                    throw new Exception($"Number of bytes read {numBytesRead.ToInt64()} is less " +
                        $"than the expected {expectedBytes} bytes ({nsize} elements of size {Marshal.SizeOf<T>()}) " +
                        $"on address 0x{address.ToInt64():X}.");
                }

                return buffer;
            }
            catch (Exception e)
            {
                Console.WriteLine($"ERROR: {e.Message}");
                return Array.Empty<T>();
            }
        }

        /// <summary>
        ///     Reads the std::string. String read is in ASCII format.
        /// </summary>
        /// <param name="nativecontainer">native object of std::string.</param>
        /// <returns>string.</returns>
        internal string ReadStdString(StdString nativecontainer)
        {
            const int MaxAllowed = 1000;
            if (nativecontainer.Length <= 0 ||
                nativecontainer.Length > MaxAllowed ||
                nativecontainer.Capacity <= 0 ||
                nativecontainer.Capacity > MaxAllowed)
            {
                return string.Empty;
            }

            if (nativecontainer.Capacity <= 15)
            {
                var buffer = BitConverter.GetBytes(nativecontainer.Buffer.ToInt64());
                var ret = Encoding.ASCII.GetString(buffer);
                buffer = BitConverter.GetBytes(nativecontainer.ReservedBytes.ToInt64());
                ret += Encoding.ASCII.GetString(buffer);
                if (nativecontainer.Length < ret.Length)
                {
                    return ret[..nativecontainer.Length];
                }
                else
                {
                    return string.Empty;
                }
            }
            else
            {
                var buffer = this.ReadMemoryArray<byte>(nativecontainer.Buffer, nativecontainer.Length);
                return Encoding.ASCII.GetString(buffer);
            }
        }

        /// <summary>
        ///     Reads the std::wstring. String read is in unicode format.
        /// </summary>
        /// <param name="nativecontainer">native object of std::wstring.</param>
        /// <returns>string.</returns>
        internal string ReadStdWString(StdWString nativecontainer)
        {
            const int MaxAllowed = 1000;
            if (nativecontainer.Length <= 0 ||
                nativecontainer.Length > MaxAllowed ||
                nativecontainer.Capacity <= 0 ||
                nativecontainer.Capacity > MaxAllowed)
            {
                return string.Empty;
            }

            if (nativecontainer.Capacity <= 8)
            {
                var buffer = BitConverter.GetBytes(nativecontainer.Buffer.ToInt64());
                var ret = Encoding.Unicode.GetString(buffer);
                buffer = BitConverter.GetBytes(nativecontainer.ReservedBytes.ToInt64());
                ret += Encoding.Unicode.GetString(buffer);
                if (nativecontainer.Length < ret.Length)
                {
                    return ret[..nativecontainer.Length];
                }
                else
                {
                    return string.Empty;
                }
            }
            else
            {
                var buffer = this.ReadMemoryArray<byte>(nativecontainer.Buffer, nativecontainer.Length * 2);
                return Encoding.Unicode.GetString(buffer);
            }
        }

        /// <summary>
        ///     Reads the string.
        /// </summary>
        /// <param name="address">pointer to the string.</param>
        /// <returns>string read.</returns>
        internal string ReadString(IntPtr address)
        {
            var buffer = this.ReadMemoryArray<byte>(address, 128);
            var count = Array.IndexOf<byte>(buffer, 0x00, 0);
            if (count > 0)
            {
                return Encoding.ASCII.GetString(buffer, 0, count);
            }

            return string.Empty;
        }

        /// <summary>
        ///     Reads Unicode string when string length isn't know.
        ///     Use  <see cref="ReadStdWString" /> if string length is known.
        /// </summary>
        /// <param name="address">points to the Unicode string pointer.</param>
        /// <returns>string read from the memory.</returns>
        internal string ReadUnicodeString(IntPtr address)
        {
            var buffer = this.ReadMemoryArray<byte>(address, 256);
            var count = 0x00;
            for (var i = 0; i < buffer.Length - 2; i++)
            {
                if (buffer[i] == 0x00 && buffer[i + 1] == 0x00 && buffer[i + 2] == 0x00)
                {
                    count = i % 2 == 0 ? i : i + 1;
                    break;
                }
            }

            // let's not return a string if null isn't found.
            if (count == 0)
            {
                return string.Empty;
            }

            var ret = Encoding.Unicode.GetString(buffer, 0, count);
            return ret;
        }

        /// <summary>
        ///     Reads the StdMap in parallel and execute onValue function on each node that isn't null
        /// </summary>
        /// <typeparam name="TKey">stdmap key type</typeparam>
        /// <typeparam name="TValue">stdmap value type</typeparam>
        /// <param name="nativeContainer">native object pointing to the std::map</param>
        /// <param name="maxSizeAllowed">to remove infinite loops, function will return upon reaching this number</param>
        /// <param name="enableCounting">extract more juice from the cpu</param>
        /// <param name="onEachNotNullNode">function to execute on each std map node that isn't null.</param>
        /// <returns>total nodes/childrens in the stdmap</returns>
        internal int ReadStdMap<TKey, TValue>(StdMap nativeContainer, int maxSizeAllowed, bool enableCounting, Func<TKey, TValue, bool> onEachNotNullNode)
            where TKey : unmanaged
            where TValue : unmanaged
        {
            if (nativeContainer.Size <= 0 || nativeContainer.Size > maxSizeAllowed)
            {
                return 0;
            }

            var head = this.ReadMemory<StdMapNode<TKey, TValue>>(nativeContainer.Head);
            var parent = this.ReadMemory<StdMapNode<TKey, TValue>>(head.Parent);
            var first64Childrens = new Queue<StdMapNode<TKey, TValue>>(64);

            // processing first 63 nodes will gives us 64 childrens
            // in the first64Childrens list (assuming there is no node with just 1 child).
            // TODO: Benchmark 64 childrens
            var totalChildrenProcessed = processSubTree(first64Childrens, parent, 64);

            // then Parallel.ForEach loop will process those 32 childrens in parallel
            Parallel.ForEach(first64Childrens,
                new ParallelOptions() { MaxDegreeOfParallelism = Core.GHSettings.EntityReaderMaxDegreeOfParallelism },
                // executed once per task/thread
                () => { return (new Queue<StdMapNode<TKey, TValue>>(2000), new int()); },
                // executed once per iteration
                (first32Child, _, _, localState) =>
                {
                    localState.Item2 += processSubTree(localState.Item1, first32Child, maxSizeAllowed / first64Childrens.Count);
                    return localState;
                },
                // executed once per task/thread
                localFinal =>
                {
                    if(enableCounting)
                    {
                        Interlocked.Add(ref totalChildrenProcessed, localFinal.Item2);
                    }
                });

            return totalChildrenProcessed;

            void processNode(Queue<StdMapNode<TKey, TValue>> childrens, StdMapNode<TKey, TValue> current)
            {
                if (!current.IsNil)
                {
                    onEachNotNullNode(current.Data.Key, current.Data.Value);
                }

                var leftChild = this.ReadMemory<StdMapNode<TKey, TValue>>(current.Left);
                if (!leftChild.IsNil)
                {
                    childrens.Enqueue(leftChild);
                }

                var rightChild = this.ReadMemory<StdMapNode<TKey, TValue>>(current.Right);
                if (!rightChild.IsNil)
                {
                    childrens.Enqueue(rightChild);
                }
            }

            int processSubTree(Queue<StdMapNode<TKey, TValue>> childrens, StdMapNode<TKey, TValue> subTreeRoot, int forceBreakOnIteration)
            {
                childrens.Enqueue(subTreeRoot);
                var counter = 0;
                while (++counter < forceBreakOnIteration && childrens.TryDequeue(out var current))
                {
                    processNode(childrens, current);
                }

                return counter;
            }
        }

        /// <summary>
        ///     Reads the StdList into a List.
        /// </summary>
        /// <typeparam name="TValue">StdList element structure.</typeparam>
        /// <param name="nativeContainer">native object of the std::list.</param>
        /// <returns>List containing TValue elements.</returns>
        internal List<TValue> ReadStdList<TValue>(StdList nativeContainer)
            where TValue : unmanaged
        {
            const int MaxIterations = 100_000;
            var retList = new List<TValue>();
            var currNodeAddress = this.ReadMemory<StdListNode>(nativeContainer.Head).Next;
            var iterations = 0;
            while (currNodeAddress != nativeContainer.Head)
            {
                if (++iterations > MaxIterations)
                {
                    Console.WriteLine($"[SafeMemoryHandle.ReadStdList] iteration cap {MaxIterations} hit; possible cycle in torn list. Returning partial result.");
                    break;
                }

                var currNode = this.ReadMemory<StdListNode<TValue>>(currNodeAddress);
                if (currNodeAddress == IntPtr.Zero)
                {
                    Console.WriteLine("Terminating reading of list next nodes because of" +
                                      "unexpected 0x00 found. This is normal if it happens " +
                                      "after closing the game, otherwise report it.");
                    break;
                }

                retList.Add(currNode.Data);
                currNodeAddress = currNode.Next;
            }

            return retList;
        }

        /// <summary>
        ///     Reads the std::bucket into a array.
        /// </summary>
        /// <typeparam name="TValue">value type that the std bucket contains.</typeparam>
        /// <param name="nativeContainer">native object of the std::bucket.</param>
        /// <returns>a array containing all the valid values found in std::bucket.</returns>
        internal TValue[] ReadStdBucket<TValue>(StdBucket nativeContainer)
            where TValue : unmanaged
        {
            if (nativeContainer.Data.First == IntPtr.Zero ||
                nativeContainer.Capacity <= 0x00)
            {
                return Array.Empty<TValue>();
            }

            return this.ReadStdVector<TValue>(nativeContainer.Data);
        }

        /// <summary>
        ///     When overridden in a derived class, executes the code required to free the handle.
        /// </summary>
        /// <returns>
        ///     true if the handle is released successfully; otherwise, in the event of a catastrophic failure, false.
        ///     In this case, it generates a releaseHandleFailed MDA Managed Debugging Assistant.
        /// </returns>
        protected override bool ReleaseHandle()
        {
            Console.WriteLine($"Releasing handle on 0x{this.handle:X}\n");
            return NativeWrapper.CloseHandle(this.handle);
        }
    }
}
