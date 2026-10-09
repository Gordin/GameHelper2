// <copyright file="PatchLedger.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>
namespace PortalAccess
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    internal interface IByteMemory
    {
        bool ReadByte(long address, out byte value);
        bool WriteByte(long address, byte value);
    }

    internal readonly record struct PortalIdentity(long Entity, uint Id, long Details, long Targetable, long Portal);
    internal readonly record struct FieldKey(PortalIdentity Portal, int Offset);

    /// <summary>Tracks only writes made in one live area. Never restores stale identities.</summary>
    internal sealed class PatchLedger
    {
        private readonly Dictionary<FieldKey, byte> originals = new();
        private readonly HashSet<PortalIdentity> repaired = new();
        public int Pending => this.originals.Count;
        public void Clear() { this.originals.Clear(); this.repaired.Clear(); }

        public (int Changed, int Failed) Maintain(IByteMemory memory, PortalIdentity portal, int targetOffset, int highlightOffset, Func<PortalIdentity, bool> validate)
        {
            // Match the reference's one successful repair per entity/area. Continually
            // re-opening a consumed old gate can replace newly created portals.
            if (this.repaired.Contains(portal)) return (0, 0);
            var targetOk = this.Apply(memory, portal, targetOffset, validate, out var targetChanged);
            var highlightOk = this.Apply(memory, portal, highlightOffset, validate, out var highlightChanged);
            if (targetOk && highlightOk && (targetChanged || highlightChanged)) this.repaired.Add(portal);
            return ((targetChanged ? 1 : 0) + (highlightChanged ? 1 : 0), (targetOk ? 0 : 1) + (highlightOk ? 0 : 1));
        }

        public void Retain(ISet<PortalIdentity> live)
        {
            foreach (var key in this.originals.Keys.Where(k => !live.Contains(k.Portal)).ToArray())
                this.originals.Remove(key);
        }

        public bool Apply(IByteMemory memory, PortalIdentity portal, int offset, Func<PortalIdentity, bool> validate, out bool changed)
        {
            changed = false;
            if (!validate(portal) || !memory.ReadByte(portal.Targetable + offset, out var value) || value > 1)
                return false;
            if (value == 1) return true;
            // Revalidate immediately before each individual one-byte write.
            if (!validate(portal)) return false;
            var key = new FieldKey(portal, offset);
            this.originals.TryAdd(key, value);
            // Retain the original even if the OS write or read-back reports failure.
            if (!memory.WriteByte(portal.Targetable + offset, 1) ||
                !memory.ReadByte(portal.Targetable + offset, out var after) || after != 1) return false;
            changed = true;
            return true;
        }

        public int Restore(IByteMemory memory, Func<PortalIdentity, bool> validate)
            => this.Restore(_ => memory, validate);

        public int Restore(Func<PortalIdentity, IByteMemory?> writerFor, Func<PortalIdentity, bool> validate)
        {
            var restored = 0;
            foreach (var (key, original) in this.originals.ToArray())
            {
                var address = key.Portal.Targetable + key.Offset;
                if (!validate(key.Portal)) continue;
                var memory = writerFor(key.Portal);
                if (memory == null) continue;
                // Do not overwrite a newer game value or restore a recycled entity address.
                if (validate(key.Portal) && memory.ReadByte(address, out var current) && current == 1 &&
                    validate(key.Portal) && memory.WriteByte(address, original) &&
                    memory.ReadByte(address, out var after) && after == original) restored++;
            }
            this.Clear();
            return restored;
        }
    }
}
