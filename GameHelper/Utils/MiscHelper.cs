// <copyright file="MiscHelper.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Utils
{
    using ClickableTransparentOverlay.Win32;
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Threading.Tasks;

    /// <summary>
    ///     Util class to send keyboard/mouse keys to the game.
    /// </summary>
    public static class MiscHelper
    {
        private static readonly Random Rand = new();
        private static readonly Stopwatch DelayBetweenKeys = Stopwatch.StartNew();
        private static Task<IntPtr>? sendingMessage;

        internal static void ActiveSkillGemDataParser(
            uint unknownIdAndEquipmentInfo,
            out bool isUserEquipped,
            out byte Unknown0,
            out byte socketIndex,
            out byte linkId,
            out byte inventoryName,
            out uint activeSkillGemUnknownId)
        {
            activeSkillGemUnknownId = unknownIdAndEquipmentInfo >> 0x10;
            unknownIdAndEquipmentInfo &= 0x0000FFFF;

            inventoryName = (byte)((unknownIdAndEquipmentInfo & 0x007F) + 1);
            unknownIdAndEquipmentInfo >>= 0x07;

            linkId = (byte)(unknownIdAndEquipmentInfo & 0x07);
            unknownIdAndEquipmentInfo >>= 0x03;

            socketIndex = (byte)(unknownIdAndEquipmentInfo & 0x07);
            unknownIdAndEquipmentInfo >>= 0x03;

            Unknown0 = (byte)(unknownIdAndEquipmentInfo & 0x03);
            unknownIdAndEquipmentInfo >>= 0x02;

            isUserEquipped = unknownIdAndEquipmentInfo > 0;
        }

        internal static bool TryConvertStringToImGuiGlyphRanges(string data, out ushort[] ranges)
        {
            if (string.IsNullOrEmpty(data))
            {
                ranges = Array.Empty<ushort>();
                return false;
            }

            var intsInHex = data.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            ranges = new ushort[intsInHex.Length];
            for (var i = 0; i < intsInHex.Length; i++)
            {
                try
                {
                    ranges[i] = (ushort)Convert.ToInt32(intsInHex[i], 16);
                }
                catch (Exception)
                {
                    return false;
                }
            }

            return ranges[^1] == 0x00;
        }

        /// <summary>
        ///     Utility function that returns randomly generated string.
        /// </summary>
        /// <returns>randomly generated string.</returns>
        internal static string GenerateRandomString()
        {
            //more common letters!
            const string characters = "qwertyuiopasdfghjklzxcvbnm" + "eioadfc";
            var random = new Random();

            char GetRandomCharacter()
            {
                return characters[random.Next(0, characters.Length)];
            }

            string GetWord()
            {
                return char.ToUpperInvariant(GetRandomCharacter()) +
                       new string(Enumerable.Range(0, random.Next(5, 10))
                                            .Select(_ => GetRandomCharacter())
                                            .ToArray());
            }

            return string.Join(' ', Enumerable.Range(0, random.Next(1, 4)).Select(_ => GetWord()));
        }

        /// <summary>
        ///     Releases the key in the game. There is a hard delay of 30ms - 40ms
        ///     between Key releases to make sure game doesn't kick us for
        ///     too many key-presses.
        /// </summary>
        /// <param name="key">key to release.</param>
        /// <returns>Is the key actually pressed or not.</returns>
        public static bool KeyUp(VK key)
        {
            if (Core.GHSettings.EnableControllerMode)
            {
                return false;
            }

            if (sendingMessage != null && !sendingMessage.IsCompleted)
            {
                return false;
            }

            if (DelayBetweenKeys.ElapsedMilliseconds >= Core.GHSettings.KeyPressTimeout + Rand.Next() % 10)
            {
                DelayBetweenKeys.Restart();
            }
            else
            {
                return false;
            }

            if (Core.Process.Address != IntPtr.Zero)
            {
                sendingMessage = Task.Run(() => SendMessage(Core.Process.Information.MainWindowHandle, 0x101, (int)key, 0));
                return true;
            }

            return false;
        }

        /// <summary>
        ///     Kills the IPV4 TCP Connection for the process.
        /// </summary>
        /// <param name="processId">process Id whos tcp connection to kill.</param>
        public static void KillTCPConnectionForProcess(uint processId)
        {
            MibTcprowOwnerPid[] table;
            var afInet = 2;
            var buffSize = 0;
            var ret = GetExtendedTcpTable(IntPtr.Zero, ref buffSize, true, afInet, TcpTableClass.TcpTableOwnerPidAll);
            var buffTable = Marshal.AllocHGlobal(buffSize);
            try
            {
                ret = GetExtendedTcpTable(buffTable, ref buffSize, true, afInet, TcpTableClass.TcpTableOwnerPidAll);
                if (ret != 0)
                {
                    return;
                }

                var tab = Marshal.PtrToStructure<MibTcptableOwnerPid>(buffTable);
                var rowPtr = (IntPtr)((long)buffTable + Marshal.SizeOf(tab.DwNumEntries));
                table = new MibTcprowOwnerPid[tab.DwNumEntries];
                for (var i = 0; i < tab.DwNumEntries; i++)
                {
                    var tcpRow = Marshal.PtrToStructure<MibTcprowOwnerPid>(rowPtr);
                    table[i] = tcpRow;
                    rowPtr = (IntPtr)((long)rowPtr + Marshal.SizeOf(tcpRow));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffTable);
            }

            // Kill Path Connection
            var pathConnection = table.FirstOrDefault(t => t.OwningPid == processId);
            if (!EqualityComparer<MibTcprowOwnerPid>.Default.Equals(pathConnection, default))
            {
                pathConnection.State = 12;
                var ptr = Marshal.AllocCoTaskMem(Marshal.SizeOf(pathConnection));
                Marshal.StructureToPtr(pathConnection, ptr, false);
                _ = SetTcpEntry(ptr);
                Marshal.FreeCoTaskMem(ptr);
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = false)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr pTcpTable,
            ref int dwOutBufLen,
            bool sort,
            int ipVersion,
            TcpTableClass tblClass,
            uint reserved = 0
        );

        [DllImport("iphlpapi.dll")] private static extern int SetTcpEntry(IntPtr pTcprow);

        private enum TcpTableClass
        {
            TcpTableBasicListener,
            TcpTableBasicConnections,
            TcpTableBasicAll,
            TcpTableOwnerPidListener,
            TcpTableOwnerPidConnections,
            TcpTableOwnerPidAll,
            TcpTableOwnerModuleListener,
            TcpTableOwnerModuleConnections,
            TcpTableOwnerModuleAll
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MibTcprowOwnerPid
        {
            public uint State;
            public readonly uint LocalAddr;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public readonly byte[] LocalPort;

            public readonly uint RemoteAddr;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public readonly byte[] RemotePort;

            public readonly uint OwningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct MibTcptableOwnerPid
        {
            public readonly uint DwNumEntries;
            private readonly MibTcprowOwnerPid table;
        }
    }
}