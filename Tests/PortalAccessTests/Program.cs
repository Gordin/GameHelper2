using System.Runtime.InteropServices;
using GameOffsets.Natives;
using GameOffsets.Objects.Components;
using GameOffsets.Objects.States.InGameState;
using PortalAccess;

var passed = 0;
void Test(string name, Action action) { action(); Console.WriteLine("PASS " + name); passed++; }
void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
var portal = new PortalIdentity(0x10000, 42, 0x20000, 0x30000, 0x40000);
const int offset = 0x51;
var address = portal.Targetable + offset;

Test("Live write gate allows only the verified two bytes and preserves pointer fields", () =>
{
    var memory = new FakeMemory();
    for (var i = 0x48; i < 0x80; i++) memory.Bytes[portal.Targetable + i] = 0;
    var gate = new PortalWriteGate(portal, memory, () => true);
    for (var i = 0x48; i < 0x80; i++)
        Check(gate.WriteByte(portal.Targetable + i, 1) == (i is 0x69 or 0x6A));
    Check(memory.Writes == 2 && memory.Bytes[portal.Targetable + 0x51] == 0 && memory.Bytes[portal.Targetable + 0x52] == 0);
});
Test("Live write gate rejects non-booleans and invalidation immediately before mutation", () =>
{
    var memory = new FakeMemory(); var at = portal.Targetable + 0x69; memory.Bytes[at] = 0;
    var calls = 0; var gate = new PortalWriteGate(portal, memory, () => ++calls == 1);
    Check(!gate.WriteByte(at, 1) && memory.Writes == 0);
    gate = new PortalWriteGate(portal, memory, () => true);
    Check(!gate.WriteByte(at, 2)); memory.Bytes[at] = 7;
    Check(!gate.WriteByte(at, 1) && memory.Writes == 0);
});
Test("Live write gate repairs and undoes only through current-identity writers", () =>
{
    var memory = new FakeMemory(); var at = portal.Targetable + 0x69;
    memory.Bytes[at] = 0; memory.Bytes[at + 1] = 0;
    var current = true; var gate = new PortalWriteGate(portal, memory, () => current);
    var ledger = new PatchLedger();
    Check(ledger.Maintain(gate, portal, 0x69, 0x6A, _ => true).Changed == 2);
    Check(ledger.Restore(id => id == portal ? gate : null, _ => current) == 2);
    Check(memory.Bytes[at] == 0 && memory.Bytes[at + 1] == 0);
    Check(ledger.Maintain(gate, portal, 0x69, 0x6A, _ => true).Changed == 2);
    current = false;
    Check(ledger.Restore(_ => gate, _ => true) == 0 && memory.Bytes[at] == 1);
});
Test("Undo skips portals for which no verified writer can be created", () =>
{
    var memory = new FakeMemory(); memory.Bytes[address] = 0; var ledger = new PatchLedger();
    Check(ledger.Apply(memory, portal, offset, _ => true, out _));
    Check(ledger.Restore(_ => null, _ => true) == 0 && memory.Writes == 1 && ledger.Pending == 0);
});

Test("Zero changes to one and restores to zero", () =>
{
    var memory = new FakeMemory(); var ledger = new PatchLedger(); memory.Bytes[address] = 0;
    Check(ledger.Apply(memory, portal, offset, _ => true, out var changed) && changed);
    Check(memory.Bytes[address] == 1 && ledger.Pending == 1);
    Check(ledger.Restore(memory, _ => true) == 1 && memory.Bytes[address] == 0 && ledger.Pending == 0);
});
Test("Already open portal is never recorded or written", () =>
{
    var memory = new FakeMemory(); var ledger = new PatchLedger(); memory.Bytes[address] = 1;
    Check(ledger.Apply(memory, portal, offset, _ => true, out var changed) && !changed);
    Check(memory.Writes == 0 && ledger.Pending == 0);
});
Test("Unknown boolean value is rejected", () =>
{
    var memory = new FakeMemory(); var ledger = new PatchLedger(); memory.Bytes[address] = 3;
    Check(!ledger.Apply(memory, portal, offset, _ => true, out _) && memory.Writes == 0);
});
Test("Identity changing between read and write cancels mutation", () =>
{
    var memory = new FakeMemory(); var ledger = new PatchLedger(); memory.Bytes[address] = 0; var calls = 0;
    Check(!ledger.Apply(memory, portal, offset, _ => ++calls == 1, out _) && memory.Writes == 0);
});
Test("Read failure never becomes a zero value to write", () =>
{
    var memory = new FakeMemory(); var ledger = new PatchLedger();
    Check(!ledger.Apply(memory, portal, offset, _ => true, out _) && memory.Writes == 0);
});
Test("Write failure is not reported as a successful change", () =>
{
    var memory = new FakeMemory { FailWrites = true }; var ledger = new PatchLedger(); memory.Bytes[address] = 0;
    Check(!ledger.Apply(memory, portal, offset, _ => true, out var changed) && !changed);
    Check(ledger.Pending == 1 && memory.Bytes[address] == 0);
});
Test("Read-back mismatch is a failure and retains the undo record", () =>
{
    var memory = new FakeMemory { IgnoreWrites = true }; var ledger = new PatchLedger(); memory.Bytes[address] = 0;
    Check(!ledger.Apply(memory, portal, offset, _ => true, out var changed) && !changed && ledger.Pending == 1);
});
Test("Undo does not overwrite a newer game value", () =>
{
    var memory = new FakeMemory(); var ledger = new PatchLedger(); memory.Bytes[address] = 0;
    ledger.Apply(memory, portal, offset, _ => true, out _); memory.Bytes[address] = 2;
    Check(ledger.Restore(memory, _ => true) == 0 && memory.Bytes[address] == 2 && memory.Writes == 1);
});
Test("Undo rejects recycled entities", () =>
{
    var memory = new FakeMemory(); var ledger = new PatchLedger(); memory.Bytes[address] = 0;
    ledger.Apply(memory, portal, offset, _ => true, out _);
    Check(ledger.Restore(memory, _ => false) == 0 && memory.Writes == 1);
});
Test("Area change forgets all old addresses without writing", () =>
{
    var memory = new FakeMemory(); var ledger = new PatchLedger(); memory.Bytes[address] = 0;
    ledger.Apply(memory, portal, offset, _ => true, out _); ledger.Clear();
    Check(ledger.Restore(memory, _ => true) == 0 && memory.Writes == 1);
});
Test("Missing entity pruning includes ID in its identity", () =>
{
    var memory = new FakeMemory(); var ledger = new PatchLedger(); memory.Bytes[address] = 0;
    ledger.Apply(memory, portal, offset, _ => true, out _);
    ledger.Retain(new HashSet<PortalIdentity> { portal with { Id = 43 } });
    Check(ledger.Pending == 0);
});
Test("Repeated recovery retains the original and does not grow records", () =>
{
    var memory = new FakeMemory(); var ledger = new PatchLedger(); memory.Bytes[address] = 0;
    ledger.Apply(memory, portal, offset, _ => true, out _); memory.Bytes[address] = 0;
    ledger.Apply(memory, portal, offset, _ => true, out _);
    Check(ledger.Pending == 1 && ledger.Restore(memory, _ => true) == 1 && memory.Bytes[address] == 0);
});

Test("Consumed portal is repaired only once until the area is reset", () =>
{
    var memory = new FakeMemory(); var ledger = new PatchLedger();
    memory.Bytes[address] = 0; memory.Bytes[address + 1] = 0;
    Check(ledger.Maintain(memory, portal, offset, offset + 1, _ => true).Changed == 2);
    memory.Bytes[address] = 0; memory.Bytes[address + 1] = 0;
    Check(ledger.Maintain(memory, portal, offset, offset + 1, _ => true).Changed == 0 && memory.Writes == 2);
    ledger.Clear();
    Check(ledger.Maintain(memory, portal, offset, offset + 1, _ => true).Changed == 2);
});
Test("Initially open portal can still be recovered when it closes later", () =>
{
    var memory = new FakeMemory(); var ledger = new PatchLedger();
    memory.Bytes[address] = 1; memory.Bytes[address + 1] = 1;
    Check(ledger.Maintain(memory, portal, offset, offset + 1, _ => true).Changed == 0);
    memory.Bytes[address] = 0; memory.Bytes[address + 1] = 0;
    Check(ledger.Maintain(memory, portal, offset, offset + 1, _ => true).Changed == 2);
});
Test("A partially failed portal remains eligible for a later repair", () =>
{
    var memory = new FakeMemory { FailedAddress = address + 1 }; var ledger = new PatchLedger();
    memory.Bytes[address] = 0; memory.Bytes[address + 1] = 0;
    var first = ledger.Maintain(memory, portal, offset, offset + 1, _ => true);
    Check(first.Changed == 1 && first.Failed == 1);
    memory.FailedAddress = 0;
    Check(ledger.Maintain(memory, portal, offset, offset + 1, _ => true).Changed == 1);
});

// Use only this test process's allocated memory. Never attach to the game for these tests.
var allocations = new List<IntPtr>();
var allocationSizes = new Dictionary<long, int>();
IntPtr Allocate(int count)
{
    var p = Marshal.AllocHGlobal(count); allocations.Add(p); allocationSizes.Add(p.ToInt64(), count);
    Marshal.Copy(new byte[count], 0, p, count); return p;
}
try
{
    var entity = Allocate(256); var details = Allocate(128); var target = Allocate(128);
    var gate = Allocate(128); var vector = Allocate(16); var dummyVtable = Allocate(8);
    var identity = new PortalIdentity(entity.ToInt64(), 123, details.ToInt64(), target.ToInt64(), gate.ToInt64());
    var entityData = new EntityOffsets { Id = 123, ItemBase = new ItemStruct {
        EntityDetailsPtr = details, ComponentListPtr = new StdVector { First = vector, Last = vector + 16, End = vector + 16 } } };
    void Reset()
    {
        Marshal.StructureToPtr(entityData, entity, false);
        Marshal.StructureToPtr(new ComponentHeader { EntityPtr = entity, StaticPtr = dummyVtable }, target, false);
        Marshal.StructureToPtr(new ComponentHeader { EntityPtr = entity, StaticPtr = dummyVtable }, gate, false);
        Marshal.WriteIntPtr(vector, target); Marshal.WriteIntPtr(vector, 8, gate);
        Marshal.WriteByte(target, PortalMemory.FixtureTargetOffset, 0); Marshal.WriteByte(target, PortalMemory.FixtureHighlightOffset, 0);
    }
    using var native = new PortalMemory();
    Reset();
    Test("Writable attachments outside the test process are blocked", () =>
    {
        Check(!native.Attach(0, true) && native.LastError == 5 && native.Session == string.Empty);
    });
    Test("Native read-only handle rejects writes", () =>
    {
        Check(native.Attach((uint)Environment.ProcessId, false));
        Check(native.Validate(identity));
        Check(!native.WriteByte(target.ToInt64() + PortalMemory.FixtureTargetOffset, 1));
    });
    Test("Native one-byte update and rollback preserve neighboring bytes", () =>
    {
        Check(native.Attach((uint)Environment.ProcessId, true));
        Marshal.WriteByte(target, PortalMemory.FixtureTargetOffset - 1, 0x7B);
        var ledger = new PatchLedger();
        Check(ledger.Apply(native, identity, PortalMemory.FixtureTargetOffset, native.Validate, out var changed) && changed);
        Check(Marshal.ReadByte(target, PortalMemory.FixtureTargetOffset) == 1);
        Check(Marshal.ReadByte(target, PortalMemory.FixtureTargetOffset - 1) == 0x7B);
        Check(Marshal.ReadByte(target, PortalMemory.FixtureHighlightOffset) == 0);
        Check(ledger.Restore(native, native.Validate) == 1);
    });
    Test("Native identity rejects a changed entity ID", () =>
    {
        Reset(); Check(!native.Validate(identity with { Id = 456 }));
    });
    Test("Native identity rejects a component owned by another entity", () =>
    {
        Reset(); Marshal.WriteIntPtr(target, 8, details); Check(!native.Validate(identity));
    });
    Test("Native identity rejects a removed component", () =>
    {
        Reset(); Marshal.WriteIntPtr(vector, 8, IntPtr.Zero); Check(!native.Validate(identity));
    });
    Test("Native identity rejects a corrupt component vector", () =>
    {
        Reset(); var invalid = entityData; invalid.ItemBase.ComponentListPtr.Last = vector + 9;
        Marshal.StructureToPtr(invalid, entity, false); Check(!native.Validate(identity));
    });
    Test("Native identity rejects out-of-range flag bytes", () =>
    {
        Reset(); Marshal.WriteByte(target, PortalMemory.FixtureHighlightOffset, 7); Check(!native.Validate(identity));
    });

    var scanner = new PortalScanner(native);
    Test("Read budget stops without returning fabricated data", () =>
    {
        var budget = new ScanBudget(native, () => true, maxReads: 1, milliseconds: 1000);
        Check(budget.ReadByte(target.ToInt64(), out _));
        Check(!budget.ReadByte(target.ToInt64(), out _) && !budget.CanContinue);
        Check(!budget.WriteByte(target.ToInt64() + PortalMemory.FixtureTargetOffset, 1));
    });
    Test("Area-change cancellation rejects further reads", () =>
    {
        var current = true;
        var budget = new ScanBudget(native, () => current, milliseconds: 1000);
        Check(budget.ReadByte(target.ToInt64(), out _));
        current = false;
        Check(!budget.ReadByte(target.ToInt64(), out _) && !budget.CanContinue);
    });
    Test("An expired time budget rejects candidate parsing", () =>
    {
        var budget = new ScanBudget(native, () => true, milliseconds: 0);
        Check(!new PortalScanner(budget, () => budget.CanContinue).TryReadCandidate(entity.ToInt64(), 123, "test", out _));
    });
    var lookup = Allocate(256); var names = Allocate(32);
    var targetName = Allocate(32); var portalName = Allocate(32);
    var head = Allocate(64); var node = Allocate(64);
    void Write<T>(IntPtr destination, T data) where T : unmanaged
    {
        var bytes = MemoryMarshal.AsBytes(new[] { data }.AsSpan()).ToArray();
        var address = destination.ToInt64();
        if (!allocationSizes.Any(block => address >= block.Key && address - block.Key <= block.Value - bytes.Length))
            throw new InvalidOperationException("Fixture write exceeds its owned allocation");
        Marshal.Copy(bytes, 0, destination, bytes.Length);
    }
    Test("Native fixture refuses a struct write larger than its allocation", () =>
    {
        var small = Allocate(16); var rejected = false;
        try { Write(small, new AreaInstanceOffsets()); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected);
    });
    void Metadata(string path, string secondName = "Portal", uint entityId = 123)
    {
        Reset();
        var raw = System.Text.Encoding.Unicode.GetBytes(path); var pathPtr = Allocate(raw.Length);
        Marshal.Copy(raw, 0, pathPtr, raw.Length);
        Write(details, new EntityDetails { name = new StdWString { Buffer = pathPtr, Length = path.Length, Capacity = path.Length }, ComponentLookUpPtr = lookup });
        Write(lookup, new ComponentLookUpStruct { ComponentsNameAndIndex = new StdBucket {
            Data = new StdVector { First = names, Last = names + 32, End = names + 32 }, Capacity = 2 } });
        Marshal.Copy(System.Text.Encoding.ASCII.GetBytes("Targetable\0"), 0, targetName, 11);
        var second = System.Text.Encoding.ASCII.GetBytes(secondName + "\0"); Marshal.Copy(second, 0, portalName, second.Length);
        Write(names, new ComponentNameAndIndexStruct { NamePtr = targetName, Index = 0 });
        Write(names + 16, new ComponentNameAndIndexStruct { NamePtr = portalName, Index = 1 });
        var disabled = entityData; disabled.Id = entityId; disabled.IsValid = 3; Write(entity, disabled);
        Write(head, new StdMapNode<EntityNodeKey, EntityNodeValue> { IsNil = true, Parent = node });
        Write(node, new StdMapNode<EntityNodeKey, EntityNodeValue> { Left = head, Right = head, Parent = head,
            Data = new StdMapNodeData<EntityNodeKey, EntityNodeValue> { Key = new EntityNodeKey { id = entityId }, Value = new EntityNodeValue { EntityPtr = entity } } });
    }
    var map = new StdMap { Head = head, Size = 1 };
    StdMap OrderedMap(int count, uint portalId, out IntPtr treeNodes)
    {
        var treeHead = Allocate(64); var tree = Allocate(count * 64); treeNodes = tree;
        IntPtr Build(int lo, int hi, IntPtr parent)
        {
            if (lo > hi) return treeHead;
            var mid = (lo + hi) / 2; var at = tree + mid * 64;
            var left = Build(lo, mid - 1, at); var right = Build(mid + 1, hi, at);
            var id = (uint)mid + 1;
            Write(at, new StdMapNode<EntityNodeKey, EntityNodeValue> { Left = left, Right = right, Parent = parent,
                Data = new StdMapNodeData<EntityNodeKey, EntityNodeValue> {
                    Key = new EntityNodeKey { id = id }, Value = new EntityNodeValue { EntityPtr = id == portalId ? entity : IntPtr.Zero } } });
            return at;
        }
        var root = Build(0, count - 1, treeHead);
        Write(treeHead, new StdMapNode<EntityNodeKey, EntityNodeValue> { IsNil = true, Parent = root });
        return new StdMap { Head = treeHead, Size = count };
    }
    Test("Paged discovery eventually reaches a portal beyond 2000 entities with tiny read budgets", () =>
    {
        Metadata("Metadata/MiscellaneousObjects/MapPortal", entityId: 2503);
        var many = OrderedMap(2503, 2503, out _); var cursor = new PortalScanCursor();
        var found = new List<PortalCandidate>(); var pages = 0; var previous = 0;
        while (!cursor.Complete && pages++ < 200)
        {
            var budget = new ScanBudget(native, () => true, maxReads: 130, milliseconds: 1000);
            var page = new PortalScanner(budget, () => budget.CanContinue).ScanPage(many, "paged", cursor, 37);
            found.AddRange(page.Portals);
            Check(cursor.Examined >= previous && page.Visited <= 37 && budget.ReadsUsed <= 130);
            previous = cursor.Examined;
        }
        Check(cursor.Complete && cursor.Examined == 2503 && pages > 1 && found.Any(x => x.Identity.Id == 2503));
    });
    Test("Budget exhaustion midway through a candidate retries the same key", () =>
    {
        Metadata("Metadata/MiscellaneousObjects/MapPortal"); var cursor = new PortalScanCursor();
        var budget = new ScanBudget(native, () => true, maxReads: 5, milliseconds: 1000);
        var partial = new PortalScanner(budget, () => budget.CanContinue).ScanPage(map, "paged", cursor);
        Check(!partial.Complete && cursor.AfterId == null && budget.StopReason == "read-limit");
        var retry = scanner.ScanPage(map, "paged", cursor);
        Check(retry.Complete && retry.Portals.Count == 1 && cursor.AfterId == 123);
    });
    Test("A discovered portal is returned even when the final candidate read consumes the budget", () =>
    {
        Metadata("Metadata/MiscellaneousObjects/MapPortal");
        var measure = new ScanBudget(native, () => true, milliseconds: 1000);
        Check(new PortalScanner(measure, () => measure.CanContinue).ScanPage(map, "measure", new()).Portals.Count == 1);
        var exact = new ScanBudget(native, () => true, maxReads: measure.ReadsUsed, milliseconds: 1000);
        var page = new PortalScanner(exact, () => exact.CanContinue).ScanPage(map, "boundary", new());
        Check(page.Portals.Count == 1 && !exact.CanContinue);
    });
    Test("Resuming seeks the current tree root instead of reading a saved traversal pointer", () =>
    {
        Metadata("Metadata/MiscellaneousObjects/MapPortal", entityId: 3);
        var tree = OrderedMap(3, 3, out var treeNodes); var cursor = new PortalScanCursor();
        Check(scanner.ScanPage(tree, "first", cursor, 1).Visited == 1 && cursor.AfterId == 1);
        // The first two nodes disappear between pages. Only node 3 remains under the same head.
        Write(tree.Head, new StdMapNode<EntityNodeKey, EntityNodeValue> { IsNil = true, Parent = treeNodes + 128 });
        Write(treeNodes + 128, new StdMapNode<EntityNodeKey, EntityNodeValue> { Left = tree.Head, Right = tree.Head, Parent = tree.Head,
            Data = new StdMapNodeData<EntityNodeKey, EntityNodeValue> { Key = new EntityNodeKey { id = 3 }, Value = new EntityNodeValue { EntityPtr = entity } } });
        Marshal.Copy(new byte[128], 0, treeNodes, 128); tree.Size = 1;
        var page = scanner.ScanPage(tree, "resume", cursor);
        Check(page.Complete && page.Portals.Count == 1 && page.Portals[0].Identity.Id == 3);
    });
    Test("A replaced map head resets key progress even if its previous sweep completed", () =>
    {
        Metadata("Metadata/MiscellaneousObjects/MapPortal"); var cursor = new PortalScanCursor();
        Check(scanner.ScanPage(map, "first", cursor).Complete);
        Metadata("Metadata/MiscellaneousObjects/MapPortal", entityId: 1);
        var replacement = OrderedMap(1, 1, out _);
        Check(scanner.ScanPage(replacement, "new-head", cursor).Portals.Count == 1 && cursor.AfterId == 1);
    });
    Test("Area cancellation makes no cursor progress and malformed trees cannot hang", () =>
    {
        Metadata("Metadata/MiscellaneousObjects/MapPortal"); var cursor = new PortalScanCursor();
        var cancelled = new ScanBudget(native, () => false, milliseconds: 1000);
        Check(!new PortalScanner(cancelled, () => cancelled.CanContinue).ScanPage(map, "cancelled", cursor).Complete);
        Check(cursor.AfterId == null && cancelled.ReadsUsed == 0 && cancelled.StopReason == "area-changed");
        Marshal.WriteIntPtr(node, node);
        Check(!scanner.ScanPage(map, "cycle", cursor).Complete && cursor.AfterId == null);
    });
    Test("Read, time and parent-child budget limits remain independently visible", () =>
    {
        var expired = new ScanBudget(native, () => true, milliseconds: 0);
        Check(expired.StopReason == "time-limit");
        var parent = new ScanBudget(native, () => true, maxReads: 2, milliseconds: 1000);
        var child = new ScanBudget(parent, () => parent.CanContinue, maxReads: 1, milliseconds: 1000);
        Check(child.ReadByte(target.ToInt64(), out _) && child.ReadsUsed == 1 && parent.ReadsUsed == 1);
        Check(!child.ReadByte(target.ToInt64(), out _) && parent.ReadByte(target.ToInt64(), out _));
        Check(parent.StopReason == "read-limit" && parent.ReadsUsed == 2);
    });
    Test("Native map discovery reads a closed portal first encountered as invalid", () =>
    {
        Metadata("Metadata/MiscellaneousObjects/MapPortal");
        var sample = scanner.Scan(map, "awake-native");
        Check(sample.Complete && sample.Portals.Count == 1 && sample.Portals[0].EntityState == 3);
        Check(native.Validate(sample.Portals[0].Identity));
    });
    Test("Native discovery includes sleeping and high-ID cosmetic portals", () =>
    {
        Metadata("Metadata/Effects/Microtransactions/Town_Portals/ExamplePortal", entityId: 0x40000007);
        var sample = scanner.Scan(map, "sleeping-native");
        Check(sample.Portals.Count == 1 && sample.Portals[0].Identity.Id == 0x40000007 && sample.Portals[0].Source == "sleeping-native");
    });
    Test("Map-object path fallback works without a Portal component", () =>
    {
        Metadata("Metadata/MiscellaneousObjects/MapPortal", "Render");
        var sample = scanner.Scan(map, "awake-native");
        Check(sample.Portals.Count == 1 && sample.Portals[0].Identity.Portal == 0);
        Check(native.Validate(sample.Portals[0].Identity));
    });
    Test("Portal-named monster and spell effects are excluded", () =>
    {
        Metadata("Metadata/Monsters/PortalMonster"); Check(scanner.Scan(map, "awake").Portals.Count == 0);
        Metadata("Metadata/Effects/Spells/IcePortal", "Render"); Check(scanner.Scan(map, "awake").Portals.Count == 0);
    });
    Test("Retained portal must still match its live entity ID", () =>
    {
        Metadata("Metadata/MiscellaneousObjects/MapPortal");
        Check(scanner.TryReadCandidate(entity.ToInt64(), 123, "retained", out var saved));
        var replacement = entityData; replacement.Id = 456; Write(entity, replacement);
        Check(!scanner.TryReadCandidate(entity.ToInt64(), saved!.Identity.Id, "retained", out _));
    });
    Test("Malformed native trees terminate and report an incomplete scan", () =>
    {
        Metadata("Metadata/MiscellaneousObjects/MapPortal");
        Marshal.WriteIntPtr(node, node);
        Check(!scanner.Scan(map, "awake").Complete);
    });
    Test("A scan interrupted by area loading reports incomplete", () =>
    {
        Metadata("Metadata/MiscellaneousObjects/MapPortal");
        var budget = new ScanBudget(native, () => false, milliseconds: 1000);
        var sample = new PortalScanner(budget, () => budget.CanContinue).Scan(map, "awake");
        Check(sample.Visited == 0 && !sample.Complete && sample.Portals.Count == 0);
    });
    Test("Native tree traversal has a hard entity limit", () =>
    {
        const int count = 2003;
        var largeHead = Allocate(64); var nodes = Allocate(count * 64);
        Write(largeHead, new StdMapNode<EntityNodeKey, EntityNodeValue> { IsNil = true, Parent = nodes });
        for (var i = 0; i < count; i++)
            Write(nodes + i * 64, new StdMapNode<EntityNodeKey, EntityNodeValue> { Left = i + 1 < count ? nodes + (i + 1) * 64 : largeHead, Right = largeHead, Parent = largeHead });
        var sample = scanner.Scan(new StdMap { Head = largeHead, Size = count }, "awake");
        Check(sample.Visited == 2000 && !sample.Complete);
    });
    var debugImage = Allocate(4096);
    void DebugFlags(int t = 0x69, int h = 0x73, int q = 0x6E, int it = 0x6F)
    {
        Reset();
        Marshal.Copy(new byte[4096], 0, debugImage, 4096);
        Marshal.StructureToPtr(new ComponentHeader { EntityPtr = entity, StaticPtr = debugImage }, target, false);
        Marshal.WriteIntPtr(debugImage, 13 * 8, debugImage + 0x100);
        var labels = new[] { "Targetable: ", "Hidden from Player: ", "Meets Quest State: ", "Meets Item Requirements: " };
        var offsets = new[] { t, h, q, it };
        for (var i = 0; i < labels.Length; i++)
        {
            var pos = 0x100 + i * 42;
            var text = 0x400 + i * 64;
            var code = new byte[] { 0x48, 0x8D, 0x15, 0, 0, 0, 0, 0x48, 0x8B, 0xCB, 0xE8, 0, 0, 0, 0,
                0x0F, 0xB6, 0x57, (byte)offsets[i], 0x48, 0x8B, 0xC8, 0xE8, 0, 0, 0, 0 };
            BitConverter.GetBytes(text - pos - 7).CopyTo(code, 3);
            Marshal.Copy(code, 0, debugImage + pos, code.Length);
            var label = System.Text.Encoding.Unicode.GetBytes(labels[i] + "\0");
            Marshal.Copy(label, 0, debugImage + text, label.Length);
            Marshal.WriteByte(target, offsets[i], (byte)(i < 2 ? 0 : 1));
        }
    }
    bool ReadFlags(out PortalFlagReader.Flags? flags) =>
        PortalFlagReader.TryRead(native, identity, debugImage.ToInt64(), 4096, out flags);
    Test("Client debug labels identify flags without reading legacy pointer bytes", () =>
    {
        DebugFlags();
        Marshal.WriteByte(target, 0x51, 1); Marshal.WriteByte(target, 0x52, 1);
        Check(ReadFlags(out var flags) && flags!.TargetOffset == 0x69 && flags.HiddenOffset == 0x73 &&
            flags.Targetable == 0 && flags.HiddenFromPlayer == 0 && flags.MeetsQuestState == 1 && flags.MeetsItemRequirements == 1);
    });
    Test("Changed field offsets are recovered from labels instead of guessed", () =>
    {
        DebugFlags(0x70, 0x77, 0x75, 0x76);
        Check(ReadFlags(out var flags) && flags!.TargetOffset == 0x70 && flags.HiddenOffset == 0x77 && flags.QuestOffset == 0x75);
    });
    Test("Unrecognized debug instructions do not produce flag values", () =>
    {
        DebugFlags(); Marshal.WriteByte(debugImage, 0x100 + 15, 0x90);
        Check(!ReadFlags(out var flags) && flags == null);
    });
    Test("Missing semantic label cannot validate a layout", () =>
    {
        DebugFlags(); Marshal.WriteInt16(debugImage, 0x400, (short)'X'); Check(!ReadFlags(out _));
    });
    Test("Overlapping flag offsets are rejected", () =>
    {
        DebugFlags(0x69, 0x69); Check(!ReadFlags(out _));
    });
    Test("Debug methods outside the executable image are rejected", () =>
    {
        DebugFlags(); Marshal.WriteIntPtr(debugImage, 13 * 8, debugImage + 4096); Check(!ReadFlags(out _));
    });
    Test("Identified flags still reject non-boolean values", () =>
    {
        DebugFlags(); Marshal.WriteByte(target, 0x69, 7); Check(!ReadFlags(out _));
    });
    Test("Flag diagnostics reject recycled entity identities", () =>
    {
        DebugFlags(); var changed = entityData; changed.Id++; Write(entity, changed); Check(!ReadFlags(out _));
    });
    Test("Flag inspection respects the shared read budget", () =>
    {
        DebugFlags(); var budget = new ScanBudget(native, () => true, maxReads: 2, milliseconds: 1000);
        Check(!PortalFlagReader.TryRead(budget, identity, debugImage.ToInt64(), 4096, out var flags) && flags == null);
    });
    Test("Unknown executable layout never grants a live recovery writer", () =>
    {
        Check(!native.RecoveryLayoutVerified);
        Check(native.CreateRecoveryWriter(identity, () => true) == null);
    });
    var loadingPtr = Allocate(System.Runtime.CompilerServices.Unsafe.SizeOf<GameOffsets.Objects.States.AreaLoadingStateOffset>());
    var inGamePtr = Allocate(System.Runtime.CompilerServices.Unsafe.SizeOf<GameOffsets.Objects.States.InGameStateOffset>());
    var areaPtr = Allocate(System.Runtime.CompilerServices.Unsafe.SizeOf<AreaInstanceOffsets>());
    void ReadyArea()
    {
        var player = entityData; player.IsValid = 12; Write(entity, player);
        Write(loadingPtr, new GameOffsets.Objects.States.AreaLoadingStateOffset { TotalLoadingScreenTimeMs = 77 });
        Write(inGamePtr, new GameOffsets.Objects.States.InGameStateOffset { AreaInstanceData = areaPtr });
        var area = new AreaInstanceOffsets { CurrentAreaHash = 123 };
        area.PlayerInfo.LocalPlayerPtr = entity; Write(areaPtr, area);
    }
    Test("Native area fence rejects loading, area replacement, and recycled player IDs", () =>
    {
        ReadyArea();
        Check(PortalAreaFence.TryCapture(native, loadingPtr.ToInt64(), inGamePtr.ToInt64(), out var fence));
        Check(fence!.IsCurrent(native));
        Write(loadingPtr, new GameOffsets.Objects.States.AreaLoadingStateOffset { IsLoading = 1, TotalLoadingScreenTimeMs = 77 });
        Check(!fence.IsCurrent(native));
        ReadyArea(); Write(loadingPtr, new GameOffsets.Objects.States.AreaLoadingStateOffset { TotalLoadingScreenTimeMs = 78 });
        Check(!fence.IsCurrent(native));
        ReadyArea(); Write(inGamePtr, new GameOffsets.Objects.States.InGameStateOffset { AreaInstanceData = areaPtr + 16 });
        Check(!fence.IsCurrent(native));
        ReadyArea(); var changed = entityData; changed.IsValid = 12; changed.Id++; Write(entity, changed);
        Check(!fence.IsCurrent(native));
    });
    Test("Loading or invalid player cannot establish a recovery fence", () =>
    {
        ReadyArea(); Write(loadingPtr, new GameOffsets.Objects.States.AreaLoadingStateOffset { IsLoading = 1 });
        Check(!PortalAreaFence.TryCapture(native, loadingPtr.ToInt64(), inGamePtr.ToInt64(), out _));
        ReadyArea(); var invalid = entityData; invalid.IsValid = 3; Write(entity, invalid);
        Check(!PortalAreaFence.TryCapture(native, loadingPtr.ToInt64(), inGamePtr.ToInt64(), out _));
    });
    void RecoveryPortal()
    {
        Metadata("Metadata/MiscellaneousObjects/MapPortal"); DebugFlags();
        var valid = entityData; valid.IsValid = 12; Write(entity, valid);
        Marshal.WriteByte(target, 0x6A, 0);
    }
    bool ValidateRecovery() => PortalRecoveryLayout.ValidatePortal(native, identity,
        debugImage.ToInt64() - PortalRecoveryLayout.October5.VtableRva, (int)PortalRecoveryLayout.October5.VtableRva + 4096, PortalRecoveryLayout.October5);
    Test("Recovery validates the live vector, semantic flag offsets, and portal owner", () =>
    {
        RecoveryPortal(); Check(ValidateRecovery());
        Marshal.WriteIntPtr(vector, 8, target); Check(!ValidateRecovery());
        RecoveryPortal(); Marshal.WriteIntPtr(gate, 8, details); Check(!ValidateRecovery());
        RecoveryPortal(); Marshal.WriteByte(target, 0x6A, 5); Check(!ValidateRecovery());
        RecoveryPortal(); Marshal.WriteByte(target, 0x73, 1); Check(!ValidateRecovery());
    });
    Test("A different semantic layout cannot reuse a previously verified recovery profile", () =>
    {
        RecoveryPortal(); DebugFlags(0x70, 0x77, 0x75, 0x76);
        var valid = entityData; valid.IsValid = 12; Write(entity, valid);
        Check(!ValidateRecovery());
    });
    Test("Portal validation uses the vtable belonging to the selected client profile", () =>
    {
        RecoveryPortal();
        var layouts = new[] { PortalRecoveryLayout.October5, PortalRecoveryLayout.October6, PortalRecoveryLayout.October10 };
        foreach (var selected in layouts)
        {
            var image = debugImage.ToInt64() - selected.VtableRva;
            var size = (int)selected.VtableRva + 4096;
            foreach (var candidate in layouts)
                Check(PortalRecoveryLayout.ValidatePortal(native, identity, image, size, candidate) == (candidate == selected));
        }
    });
    var profileImage = Allocate(4096);
    PortalRecoveryLayout.ClientProfile SyntheticProfile(string name, int codeStart, byte seed)
    {
        var parts = new List<PortalRecoveryLayout.CodeFingerprint>();
        for (var i = 0; i < 4; i++)
        {
            var code = Enumerable.Range(0, 16).Select(n => (byte)(n + seed + i)).ToArray();
            Marshal.Copy(code, 0, profileImage + codeStart + i * 32, code.Length);
            parts.Add(new(codeStart + i * 32, code.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(code))));
        }
        return new(new(name, 64), parts);
    }
    Test("Client profiles are selected only after all four block hashes match", () =>
    {
        var old = SyntheticProfile("old", 0x100, 1); var updated = SyntheticProfile("updated", 0x300, 20);
        var profiles = new[] { updated, old };
        Check(PortalRecoveryLayout.IdentifyImage(native, profileImage.ToInt64(), 4096, profiles) == updated.Layout);
        Marshal.WriteByte(profileImage, updated.Code[3].Rva, 255);
        Check(PortalRecoveryLayout.IdentifyImage(native, profileImage.ToInt64(), 4096, profiles) == old.Layout);
    });
    Test("Mixed old and updated instruction blocks cannot authorize recovery", () =>
    {
        var old = SyntheticProfile("old", 0x100, 1); var updated = SyntheticProfile("updated", 0x300, 20);
        Marshal.WriteByte(profileImage, old.Code[0].Rva, 255);
        Marshal.WriteByte(profileImage, updated.Code[3].Rva, 255);
        Check(PortalRecoveryLayout.IdentifyImage(native, profileImage.ToInt64(), 4096, new[] { updated, old }) == null);
    });
    Test("Relative profile matching survives image relocation", () =>
    {
        var profile = SyntheticProfile("relocated", 0x100, 1); var relocated = Allocate(4096);
        var copy = new byte[4096]; Marshal.Copy(profileImage, copy, 0, copy.Length); Marshal.Copy(copy, 0, relocated, copy.Length);
        Check(PortalRecoveryLayout.MatchesProfile(native, relocated.ToInt64(), 4096, profile));
    });
    Test("Profile blocks and vtable must remain within the reported image", () =>
    {
        var profile = SyntheticProfile("bounds", 0x100, 1);
        Check(!PortalRecoveryLayout.MatchesProfile(native, profileImage.ToInt64(), 0x100, profile));
        Check(!PortalRecoveryLayout.MatchesProfile(native, profileImage.ToInt64(), 4096, profile with { Layout = new("outside", 4096) }));
        Check(!PortalRecoveryLayout.MatchesProfile(native, profileImage.ToInt64(), 4096, profile with { Layout = new("negative", -1) }));
    });
    Test("Empty, partial, invalid-length and missing profiles never match", () =>
    {
        var profile = SyntheticProfile("invalid", 0x100, 1);
        Check(!PortalRecoveryLayout.MatchesProfile(native, profileImage.ToInt64(), 4096, profile with { Code = Array.Empty<PortalRecoveryLayout.CodeFingerprint>() }));
        Check(!PortalRecoveryLayout.MatchesProfile(native, profileImage.ToInt64(), 4096, profile with { Code = profile.Code.Take(3).ToArray() }));
        var parts = profile.Code.ToArray(); parts[0] = parts[0] with { Length = -1 };
        Check(!PortalRecoveryLayout.MatchesProfile(native, profileImage.ToInt64(), 4096, profile with { Code = parts }));
        Check(PortalRecoveryLayout.IdentifyImage(native, profileImage.ToInt64(), 4096, Array.Empty<PortalRecoveryLayout.ClientProfile>()) == null);
    });
    Test("Profile selection respects read cancellation and cannot use fabricated zeros", () =>
    {
        var profile = SyntheticProfile("budget", 0x100, 1);
        var budget = new ScanBudget(native, () => true, maxReads: 3, milliseconds: 1000);
        Check(!PortalRecoveryLayout.MatchesProfile(budget, profileImage.ToInt64(), 4096, profile));
        Check(budget.ReadsUsed == 3);
    });
    Test("Disposed handle cannot read or write", () =>
    {
        native.Dispose(); Check(!native.ReadByte(target.ToInt64(), out _)); Check(!native.WriteByte(target.ToInt64(), 1));
    });
}
finally { foreach (var p in allocations) Marshal.FreeHGlobal(p); }
Console.WriteLine($"Passed {passed} tests.");

sealed class FakeMemory : IByteMemory
{
    public readonly Dictionary<long, byte> Bytes = new();
    public int Writes;
    public bool FailWrites;
    public bool IgnoreWrites;
    public long FailedAddress;
    public bool ReadByte(long address, out byte value) => this.Bytes.TryGetValue(address, out value);
    public bool WriteByte(long address, byte value)
    {
        this.Writes++;
        if (this.FailWrites || address == this.FailedAddress) return false;
        if (!this.IgnoreWrites) this.Bytes[address] = value;
        return true;
    }
}
