// <copyright file="PortalMemory.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>
namespace PortalAccess
{
    using System;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    using System.Runtime.CompilerServices;
    using GameOffsets.Objects.Components;
    using GameOffsets.Objects.States.InGameState;
    using Microsoft.Win32.SafeHandles;

    /// <summary>A plugin-owned handle; the framework and other plugins remain read-only.</summary>
    internal sealed class PortalMemory : IRemoteMemory, IDisposable
    {
        private SafeProcessHandle? handle;
        private SafeProcessHandle? recoveryHandle;
        private bool recoveryLayoutChecked;
        private PortalRecoveryLayout.VerifiedLayout? recoveryLayout;
        private uint attachedPid;
        private bool writable;
        public string Session { get; private set; } = string.Empty;
        public int LastError { get; private set; }
        public long ImageStart { get; private set; }
        public int ImageSize { get; private set; }
        // Legacy offsets are used only in offline own-process fixtures. Live flags
        // must be interpreted by PortalFlagReader; these locations are pointers in
        // the inspected 2026-10-05 client and must never be used for game writes.
        public static readonly int FixtureTargetOffset = Marshal.OffsetOf<TargetableOffsets>(nameof(TargetableOffsets.IsTargetable)).ToInt32();
        public static readonly int FixtureHighlightOffset = Marshal.OffsetOf<TargetableOffsets>(nameof(TargetableOffsets.IsHighlightable)).ToInt32();

        public bool Attach(uint pid, bool write)
        {
            // Arbitrary writes remain restricted to own-process fixtures. Live recovery
            // uses CreateRecoveryWriter, which can touch only two validated flag bytes.
            if (write && pid != (uint)Environment.ProcessId)
            {
                this.Dispose();
                this.LastError = 5;
                return false;
            }
            try
            {
                using var process = Process.GetProcessById(checked((int)pid));
                var session = $"{pid}:{process.StartTime.ToUniversalTime().Ticks}";
                if (this.handle is { IsInvalid: false, IsClosed: false } && this.Session == session && this.writable == write && this.IsAlive())
                    return true;
                this.Dispose();
                // QUERY_LIMITED_INFORMATION | VM_READ; add only the two necessary write rights.
                this.handle = OpenProcess(0x1000u | 0x10u | (write ? 0x28u : 0), false, pid);
                if (this.handle.IsInvalid)
                {
                    this.LastError = Marshal.GetLastWin32Error();
                    return false;
                }
                this.Session = session;
                this.attachedPid = pid;
                this.ImageStart = process.MainModule?.BaseAddress.ToInt64() ?? 0;
                this.ImageSize = process.MainModule?.ModuleMemorySize ?? 0;
                this.writable = write;
                this.LastError = 0;
                return this.IsAlive();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or OverflowException)
            {
                this.Dispose();
                this.LastError = ex is System.ComponentModel.Win32Exception w ? w.NativeErrorCode : -1;
                return false;
            }
        }

        public bool IsAlive() => this.handle is { IsInvalid: false, IsClosed: false } &&
            GetExitCodeProcess(this.handle, out var exit) && exit == 259;

        public static bool AddressValid(long address) => address >= 0x10000 && address < 0x7FFFFFFF0000;

        public bool Read<T>(long address, out T value) where T : unmanaged
        {
            value = default;
            if (!this.ReadBytes(address, Unsafe.SizeOf<T>(), out var buffer)) return false;
            value = MemoryMarshal.Read<T>(buffer);
            return true;
        }

        public bool ReadBytes(long address, int length, out byte[] buffer)
        {
            buffer = Array.Empty<byte>();
            if (!AddressValid(address) || length <= 0 || length > 8192 || this.handle is null || !this.IsAlive()) return false;
            var bytes = new byte[length];
            if (!ReadProcessMemory(this.handle, (IntPtr)address, bytes, (nuint)length, out var count) || count != (nuint)length) return false;
            buffer = bytes;
            return true;
        }

        public bool ReadByte(long address, out byte value) => this.Read(address, out value);

        public bool WriteByte(long address, byte value)
        {
            if (!this.writable || !AddressValid(address) || this.handle is null || !this.IsAlive()) return false;
            var success = WriteProcessMemory(this.handle, (IntPtr)address, new[] { value }, 1, out var count) && count == 1;
            if (!success) this.LastError = Marshal.GetLastWin32Error();
            return success;
        }

        internal PortalRecoveryLayout.VerifiedLayout? RecoveryLayout
        {
            get
            {
                if (!this.recoveryLayoutChecked)
                {
                    this.recoveryLayout = PortalRecoveryLayout.IdentifyImage(this, this.ImageStart, this.ImageSize);
                    this.recoveryLayoutChecked = true;
                }
                return this.recoveryLayout;
            }
        }

        internal bool RecoveryLayoutVerified => this.RecoveryLayout != null;

        internal IByteMemory? CreateRecoveryWriter(PortalIdentity portal, Func<bool> areaCurrent)
        {
            var layout = this.RecoveryLayout;
            if (layout == null) return null;
            var session = this.Session;
            bool Validate() => this.Session == session && this.IsAlive() && areaCurrent() && this.RecoveryLayout == layout &&
                PortalRecoveryLayout.ValidatePortal(this, portal, this.ImageStart, this.ImageSize, layout) &&
                this.Session == session && this.IsAlive() && areaCurrent();
            if (!Validate()) return null;
            if (this.recoveryHandle is not { IsInvalid: false, IsClosed: false })
            {
                this.recoveryHandle?.Dispose();
                this.recoveryHandle = OpenProcess(0x1000u | 0x10u | 0x28u, false, this.attachedPid);
                if (this.recoveryHandle.IsInvalid) { this.LastError = Marshal.GetLastWin32Error(); return null; }
            }
            return new PortalWriteGate(portal, new RecoveryBytes(this), Validate);
        }

        // Only exposed through the field/identity/area guard above, never returned directly.
        private sealed class RecoveryBytes(PortalMemory owner) : IByteMemory
        {
            public bool ReadByte(long address, out byte value) => owner.ReadByte(address, out value);
            public bool WriteByte(long address, byte value)
            {
                if (owner.recoveryHandle is not { IsInvalid: false, IsClosed: false } || !owner.IsAlive()) return false;
                var ok = WriteProcessMemory(owner.recoveryHandle, (IntPtr)address, new[] { value }, 1, out var count) && count == 1;
                if (!ok) owner.LastError = Marshal.GetLastWin32Error();
                return ok;
            }
        }

        public bool Validate(PortalIdentity portal)
        {
            // This validator is for the offline patch-ledger fixture, not live flags.
            if (!this.Session.StartsWith($"{Environment.ProcessId}:", StringComparison.Ordinal)) return false;
            if (!AddressValid(portal.Entity) || (portal.Portal != 0 && !AddressValid(portal.Portal)) || !AddressValid(portal.Targetable) ||
                !this.Read<EntityOffsets>(portal.Entity, out var entity) || entity.Id != portal.Id ||
                entity.ItemBase.EntityDetailsPtr.ToInt64() != portal.Details || !AddressValid(portal.Details) ||
                !this.Read<ComponentHeader>(portal.Targetable, out var target) || target.EntityPtr.ToInt64() != portal.Entity ||
                !AddressValid(target.StaticPtr.ToInt64())) return false;
            if (portal.Portal != 0)
            {
                if (!this.Read<ComponentHeader>(portal.Portal, out var gate) || gate.EntityPtr.ToInt64() != portal.Entity ||
                    !AddressValid(gate.StaticPtr.ToInt64())) return false;
            }
            else if (!new PortalScanner(this).TryReadCandidate(portal.Entity, portal.Id, "validation", out var fallback) ||
                     fallback!.Identity != portal || !PortalScanner.IsSupportedPortalPath(fallback.Path)) return false;

            // Verify that the live entity's component vector still contains both components.
            var first = entity.ItemBase.ComponentListPtr.First.ToInt64();
            var last = entity.ItemBase.ComponentListPtr.Last.ToInt64();
            var length = last - first;
            if (!AddressValid(first) || length < 16 || length > 50 * 8 || length % 8 != 0) return false;
            var hasTarget = false;
            var hasPortal = portal.Portal == 0;
            for (var p = first; p < last; p += 8)
            {
                if (!this.Read<long>(p, out var component)) return false;
                hasTarget |= component == portal.Targetable;
                hasPortal |= component == portal.Portal;
            }
            return hasTarget && hasPortal && FixtureTargetOffset is >= 0 and < 1024 && FixtureHighlightOffset is >= 0 and < 1024 &&
                this.ReadByte(portal.Targetable + FixtureTargetOffset, out var t) && t <= 1 &&
                this.ReadByte(portal.Targetable + FixtureHighlightOffset, out var h) && h <= 1;
        }

        public void Dispose()
        {
            this.recoveryHandle?.Dispose();
            this.recoveryHandle = null;
            this.recoveryLayout = null;
            this.recoveryLayoutChecked = false;
            this.attachedPid = 0;
            this.handle?.Dispose();
            this.handle = null;
            this.Session = string.Empty;
            this.ImageStart = 0;
            this.ImageSize = 0;
            this.writable = false;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(SafeProcessHandle process, IntPtr address, [Out] byte[] buffer, nuint length, out nuint read);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(SafeProcessHandle process, IntPtr address, byte[] buffer, nuint length, out nuint written);
    }
}
