// <copyright file="PortalScanner.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>
namespace PortalAccess
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;
    using System.Text;
    using GameOffsets.Natives;
    using GameOffsets.Objects.States.InGameState;

    internal interface IRemoteMemory : IByteMemory
    {
        bool Read<T>(long address, out T value) where T : unmanaged;
        bool ReadBytes(long address, int length, out byte[] bytes);
    }

    internal sealed record PortalCandidate(PortalIdentity Identity, string Path, string Source, byte EntityState, string[] Components);
    internal sealed record ScanResult(List<PortalCandidate> Portals, int Visited, bool Complete, string StopReason = "none");

    /// <summary>Only the ordered entity key survives a sample, never a traversal stack of old pointers.</summary>
    internal sealed class PortalScanCursor
    {
        internal long Head;
        internal uint? AfterId;
        internal int Examined;
        internal bool Complete;
        internal void Reset() { this.Head = 0; this.AfterId = null; this.Examined = 0; this.Complete = false; }
    }

    /// <summary>Reads current native entity maps, including invalid entities and decorations.</summary>
    internal sealed class PortalScanner
    {
        private readonly IRemoteMemory memory;
        private readonly Func<bool> canContinue;
        public PortalScanner(IRemoteMemory memory, Func<bool>? canContinue = null)
        {
            this.memory = memory;
            this.canContinue = canContinue ?? (() => true);
        }

        public ScanResult Scan(StdMap map, string source)
        {
            var portals = new List<PortalCandidate>();
            if (map.Size == 0) return new(portals, 0, true);
            if (!this.canContinue() || map.Size < 0 || map.Size > 100000 ||
                !this.memory.Read<StdMapNode<EntityNodeKey, EntityNodeValue>>(map.Head.ToInt64(), out var head))
                return new(portals, 0, false);
            var pending = new Stack<long>();
            var visited = new HashSet<long>();
            pending.Push(head.Parent.ToInt64());
            var complete = true;
            while (pending.Count > 0 && visited.Count < Math.Min(map.Size + 1, 2000) && this.canContinue())
            {
                var address = pending.Pop();
                if (address == map.Head.ToInt64()) continue;
                if (!visited.Add(address)) { complete = false; continue; }
                if (!this.memory.Read<StdMapNode<EntityNodeKey, EntityNodeValue>>(address, out var node))
                { complete = false; continue; }
                if (node.IsNil) continue;
                if (node.Color > 1) { complete = false; continue; }
                if (this.TryReadCandidate(node.Data.Value.EntityPtr.ToInt64(), node.Data.Key.id, source, out var candidate))
                    portals.Add(candidate!);
                if (node.Left != IntPtr.Zero) pending.Push(node.Left.ToInt64());
                if (node.Right != IntPtr.Zero) pending.Push(node.Right.ToInt64());
            }
            return new(portals, visited.Count, complete && pending.Count == 0 && this.canContinue());
        }

        /// <summary>Resume an ordered scan from its last fully inspected key, seeking again from the live root.</summary>
        internal ScanResult ScanPage(StdMap map, string source, PortalScanCursor cursor, int maxEntities = 128)
        {
            var portals = new List<PortalCandidate>();
            if (cursor.Head != map.Head.ToInt64()) { cursor.Reset(); cursor.Head = map.Head.ToInt64(); }
            if (map.Size < 0 || map.Size > 100000 || maxEntities <= 0) return new(portals, 0, false, "invalid-map");
            if (map.Size == 0) { cursor.Complete = true; return new(portals, 0, true); }
            if (cursor.Complete) return new(portals, 0, true);
            if (!this.canContinue() || !this.memory.Read<StdMapNode<EntityNodeKey, EntityNodeValue>>(cursor.Head, out var head))
                return new(portals, 0, false, "head-read-or-budget");

            var pending = new Stack<StdMapNode<EntityNodeKey, EntityNodeValue>>();
            var visited = new HashSet<long>();
            var address = head.Parent.ToInt64();
            var examined = 0;
            while (this.canContinue())
            {
                // Lower-bound search excludes already inspected keys. A changed tree is
                // reread each call; rotations/removals do not reuse saved node addresses.
                while (address != 0 && address != cursor.Head)
                {
                    if (!this.canContinue()) return new(portals, examined, false, "budget-or-area");
                    if (visited.Count >= 2000) return new(portals, examined, false, "node-limit");
                    if (!visited.Add(address)) return new(portals, examined, false, "tree-cycle");
                    if (!this.memory.Read<StdMapNode<EntityNodeKey, EntityNodeValue>>(address, out var node))
                        return new(portals, examined, false, "node-read-failed");
                    if (node.IsNil || node.Color > 1) return new(portals, examined, false, "invalid-node");
                    if (cursor.AfterId.HasValue && node.Data.Key.id <= cursor.AfterId.Value)
                        address = node.Right.ToInt64();
                    else { pending.Push(node); address = node.Left.ToInt64(); }
                }
                if (pending.Count == 0)
                {
                    cursor.Complete = true;
                    return new(portals, examined, true);
                }
                if (examined >= maxEntities) return new(portals, examined, false, "page-limit");
                var next = pending.Pop();
                if (cursor.AfterId.HasValue && next.Data.Key.id <= cursor.AfterId.Value) return new(portals, examined, false, "tree-changed");
                var found = this.TryReadCandidate(next.Data.Value.EntityPtr.ToInt64(), next.Data.Key.id, source, out var candidate);
                if (found) portals.Add(candidate!);
                // A partial candidate must be retried. Otherwise a budget boundary can
                // permanently skip the very portal we were trying to discover.
                if (!this.canContinue()) return new(portals, examined, false, "budget-or-area");
                cursor.AfterId = next.Data.Key.id;
                cursor.Examined++;
                examined++;
                address = next.Right.ToInt64();
            }
            return new(portals, examined, false, "budget-or-area");
        }

        public bool TryReadCandidate(long address, uint id, string source, out PortalCandidate? candidate)
        {
            candidate = null;
            if (!this.canContinue() || !this.memory.Read<EntityOffsets>(address, out var data) || data.Id != id ||
                !this.memory.Read<EntityDetails>(data.ItemBase.EntityDetailsPtr.ToInt64(), out var details) ||
                !this.TryReadPath(details.name, out var path) ||
                !path.Contains("Portal", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("Metadata/Monsters/", StringComparison.OrdinalIgnoreCase) ||
                !this.memory.Read<ComponentLookUpStruct>(details.ComponentLookUpPtr.ToInt64(), out var lookup) ||
                !this.TryReadVector<ComponentNameAndIndexStruct>(lookup.ComponentsNameAndIndex.Data, 50, out var names) ||
                !this.TryReadVector<long>(data.ItemBase.ComponentListPtr, 50, out var components)) return false;
            var componentNames = new List<string>();
            long portal = 0, target = 0;
            foreach (var item in names)
            {
                if (item.Index < 0 || item.Index >= components.Length || !this.TryReadName(item.NamePtr.ToInt64(), out var name)) continue;
                componentNames.Add(name);
                if (name == "Portal") portal = components[item.Index];
                if (name == "Targetable") target = components[item.Index];
            }
            if (target == 0 || (portal == 0 && !IsSupportedPortalPath(path))) return false;
            candidate = new(new(address, id, data.ItemBase.EntityDetailsPtr.ToInt64(), target, portal), path, source, data.IsValid, componentNames.ToArray());
            return true;
        }

        // The reference also accepts metadata-only portals. Limit that fallback to actual
        // town/map objects rather than monsters or projectile effects containing "Portal".
        internal static bool IsSupportedPortalPath(string path) =>
            path.Contains("Portal", StringComparison.OrdinalIgnoreCase) &&
            (path.StartsWith("Metadata/MiscellaneousObjects/", StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith("Metadata/Effects/Microtransactions/Town_Portals/", StringComparison.OrdinalIgnoreCase));

        private bool TryReadPath(StdWString value, out string path)
        {
            path = string.Empty;
            if (value.Length <= 0 || value.Length > 512 || value.Capacity < value.Length || value.Capacity > 4096) return false;
            byte[] bytes;
            if (value.Capacity <= 8)
            {
                bytes = new byte[16];
                BitConverter.GetBytes(value.Buffer.ToInt64()).CopyTo(bytes, 0);
                BitConverter.GetBytes(value.ReservedBytes.ToInt64()).CopyTo(bytes, 8);
            }
            else if (!this.memory.ReadBytes(value.Buffer.ToInt64(), value.Length * 2, out bytes)) return false;
            path = Encoding.Unicode.GetString(bytes, 0, value.Length * 2);
            return path.StartsWith("Metadata/", StringComparison.Ordinal);
        }

        private bool TryReadName(long address, out string name)
        {
            name = string.Empty;
            // Component names are immutable short strings. A single bounded read avoids
            // spending a process-memory call per character; keep the narrow fallback for
            // a string at the end of a readable page.
            if (this.memory.ReadBytes(address, 64, out var block))
            {
                var end = Array.IndexOf(block, (byte)0);
                if (end <= 0) return false;
                for (var i = 0; i < end; i++) if (block[i] < 32 || block[i] > 126) return false;
                name = Encoding.ASCII.GetString(block, 0, end);
                return true;
            }
            var bytes = new List<byte>();
            for (var i = 0; i < 64; i++)
            {
                if (!this.memory.ReadByte(address + i, out var b)) return false;
                if (b == 0) { name = Encoding.ASCII.GetString(bytes.ToArray()); return bytes.Count > 0; }
                if (b < 32 || b > 126) return false;
                bytes.Add(b);
            }
            return false;
        }

        private bool TryReadVector<T>(StdVector vector, int limit, out T[] elements) where T : unmanaged
        {
            elements = Array.Empty<T>();
            var length = vector.Last.ToInt64() - vector.First.ToInt64();
            var size = Unsafe.SizeOf<T>();
            if (length <= 0 || length % size != 0 || length / size > limit ||
                !this.memory.ReadBytes(vector.First.ToInt64(), (int)length, out var bytes)) return false;
            elements = MemoryMarshal.Cast<byte, T>(bytes).ToArray();
            return true;
        }
    }
}
