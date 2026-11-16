// <copyright file="ScanBudget.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>
namespace PortalAccess
{
    using System;
    using System.Diagnostics;

    /// <summary>Bounds all reads in one render-frame sample, including candidate validation.</summary>
    internal sealed class ScanBudget : IRemoteMemory
    {
        private readonly IRemoteMemory memory;
        private readonly Func<bool> current;
        private readonly long deadline;
        private int remaining;
        public int ReadsUsed { get; private set; }

        public ScanBudget(IRemoteMemory memory, Func<bool> current, int maxReads = 4000, int milliseconds = 15)
        {
            this.memory = memory;
            this.current = current;
            this.remaining = Math.Max(0, maxReads);
            this.deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * Math.Max(0, milliseconds) / 1000;
        }

        public bool CanContinue => this.remaining > 0 && Stopwatch.GetTimestamp() < this.deadline && this.current();
        public string StopReason => !this.current() ? "area-changed" : this.remaining <= 0 ? "read-limit" :
            Stopwatch.GetTimestamp() >= this.deadline ? "time-limit" : "none";
        private bool Take() { if (!this.CanContinue) return false; this.remaining--; this.ReadsUsed++; return true; }
        public bool Read<T>(long address, out T value) where T : unmanaged
        {
            value = default;
            return this.Take() && this.memory.Read(address, out value);
        }
        public bool ReadBytes(long address, int length, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            return this.Take() && this.memory.ReadBytes(address, length, out bytes);
        }
        public bool ReadByte(long address, out byte value) => this.Read(address, out value);
        // Observation must never acquire a mutation path through the budget wrapper.
        public bool WriteByte(long address, byte value) => false;
    }
}
