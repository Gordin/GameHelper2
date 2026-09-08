// <copyright file="Program.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

using GameHelper.RemoteObjects.UiElement;
using GameOffsets.Natives;
using GameOffsets.Objects.UiElement;

// Exercise the production resolver with synthetic memory. No game process or overlay is started.
var passed = 0;
Run("resolves the live child vector", memory => ExpectResolved(memory));
Run("resolves keyboard GameUi[6][0/1]", memory => ExpectGameUiResolved(memory, false));
Run("resolves controller GameUi[0][1/2] with a leading non-map child", memory =>
{
    memory.SetControllerLayout();
    ExpectGameUiResolved(memory, true);
});
Run("does not reuse keyboard child indices in controller mode", memory =>
{
    Assert(!MapUiResolver.TryResolveFromGameUi(Memory.GameUiAddress, true,
        memory.Read, memory.Read, memory.Read, out var large, out var mini), "Accepted keyboard layout as controller.");
    Assert(large == IntPtr.Zero && mini == IntPtr.Zero, "Retained stale map addresses.");
});
Run("rejects a missing controller minimap", memory =>
{
    memory.SetControllerLayout();
    memory.Parent.ChildrensPtr.Last = Memory.VectorAddress + 16;
    Assert(!MapUiResolver.TryResolveFromGameUi(Memory.GameUiAddress, true,
        memory.Read, memory.Read, memory.Read, out _, out _), "Accepted incomplete controller pair.");
});
Run("rejects different viewport classes", memory =>
{
    memory.MiniMap.UiElementBase.Vtable = new IntPtr(0x98760);
    ExpectRejected(memory);
});
Run("rejects a null viewport class", memory =>
{
    memory.LargeMap.UiElementBase.Vtable = IntPtr.Zero;
    ExpectRejected(memory);
});
Run("resolves hidden viewports", memory =>
{
    memory.LargeMap.UiElementBase.Flags = 0;
    memory.MiniMap.UiElementBase.Flags = 0;
    ExpectResolved(memory);
});
Run("rejects a null parent without reading", memory =>
{
    ExpectRejected(memory, IntPtr.Zero);
    Assert(memory.ReadCount == 0, "Null parent caused a memory read.");
});
Run("rejects an unreadable parent", memory => { memory.MissingParent = true; ExpectRejected(memory); });
Run("rejects a stale parent", memory => { memory.Parent.Self = new IntPtr(1); ExpectRejected(memory); });
Run("rejects a null vector", memory => { memory.Parent.ChildrensPtr.First = IntPtr.Zero; ExpectRejected(memory); });
Run("rejects a reversed vector", memory =>
{
    memory.Parent.ChildrensPtr.Last = Memory.VectorAddress - 8;
    ExpectRejected(memory);
});
Run("rejects a partial vector entry", memory =>
{
    memory.Parent.ChildrensPtr.Last = Memory.VectorAddress + 17;
    memory.Parent.ChildrensPtr.End = Memory.VectorAddress + 24;
    ExpectRejected(memory);
});
Run("rejects fewer than two children", memory =>
{
    memory.Parent.ChildrensPtr.Last = Memory.VectorAddress + 8;
    ExpectRejected(memory);
});
Run("rejects a vector beyond capacity", memory =>
{
    memory.Parent.ChildrensPtr.End = Memory.VectorAddress + 8;
    ExpectRejected(memory);
});
Run("rejects an implausible child count before reading children", memory =>
{
    memory.Parent.ChildrensPtr.Last = Memory.VectorAddress + 1025 * 8;
    memory.Parent.ChildrensPtr.End = memory.Parent.ChildrensPtr.Last;
    ExpectRejected(memory);
    Assert(memory.ReadCount == 1, "Invalid vector caused child reads.");
});
Run("rejects an unreadable child pointer", memory => { memory.MissingPointer = true; ExpectRejected(memory); });
Run("rejects duplicate viewports", memory => { memory.SecondChild = Memory.LargeAddress; ExpectRejected(memory); });
Run("rejects a missing viewport", memory => { memory.SecondChild = IntPtr.Zero; ExpectRejected(memory); });
Run("rejects unreadable map data", memory => { memory.MissingMap = true; ExpectRejected(memory); });
Run("rejects a stale viewport", memory => { memory.LargeMap.UiElementBase.Self = new IntPtr(1); ExpectRejected(memory); });
Run("rejects a viewport from another UI tree", memory =>
{
    memory.MiniMap.UiElementBase.ParentPtr = new IntPtr(0x90000);
    ExpectRejected(memory);
});
foreach (var zoom in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, BitConverter.Int32BitsToSingle(7), 1e20f })
{
    Run($"rejects invalid zoom {zoom}", memory => { memory.MiniMap.Zoom = zoom; ExpectRejected(memory); });
}
Run("rejects a nonfinite pan", memory => { memory.LargeMap.Shift.Y = float.NaN; ExpectRejected(memory); });
Run("rejects a nonfinite default shift", memory => { memory.MiniMap.DefaultShift.X = float.PositiveInfinity; ExpectRejected(memory); });
Run("refreshes the pair when the UI is rebuilt", memory =>
{
    ExpectResolved(memory);
    memory.FirstChild = new IntPtr(0x60000);
    memory.SecondChild = new IntPtr(0x70000);
    memory.LargeMap.UiElementBase.Self = memory.FirstChild;
    memory.MiniMap.UiElementBase.Self = memory.SecondChild;
    ExpectResolved(memory);
    memory.MissingParent = true;
    ExpectRejected(memory);
    memory.MissingParent = false;
    ExpectResolved(memory);
});
Console.WriteLine($"Passed {passed} map resolver regression checks.");

void Run(string name, Action<Memory> test)
{
    test(new Memory());
    passed++;
    Console.WriteLine($"PASS {name}");
}

static void ExpectResolved(Memory memory)
{
    Assert(MapUiResolver.TryResolve(Memory.ParentAddress, false, memory.Read, memory.Read, memory.Read,
        out var largeMap, out var miniMap), "Expected a valid map pair.");
    Assert(largeMap == memory.FirstChild && miniMap == memory.SecondChild, "Wrong map addresses.");
}

static void ExpectRejected(Memory memory, IntPtr? parent = null)
{
    Assert(!MapUiResolver.TryResolve(parent ?? Memory.ParentAddress, false, memory.Read, memory.Read, memory.Read,
        out var largeMap, out var miniMap), "Accepted an invalid map pair.");
    Assert(largeMap == IntPtr.Zero && miniMap == IntPtr.Zero, "Failure retained stale map addresses.");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void ExpectGameUiResolved(Memory memory, bool controllerMode)
{
    Assert(MapUiResolver.TryResolveFromGameUi(Memory.GameUiAddress, controllerMode,
        memory.Read, memory.Read, memory.Read, out var large, out var mini), "Expected mode-specific map pair.");
    Assert(large == memory.FirstChild && mini == memory.SecondChild, "Wrong viewport order.");
}

sealed class Memory
{
    public static readonly IntPtr ParentAddress = new(0x10000);
    public static readonly IntPtr VectorAddress = new(0x20000);
    public static readonly IntPtr LargeAddress = new(0x30000);
    public static readonly IntPtr MiniAddress = new(0x40000);
    public static readonly IntPtr GameUiAddress = new(0x80000);
    public static readonly IntPtr GameUiVector = new(0x90000);
    public bool ControllerLayout;
    public IntPtr FirstChild = LargeAddress;
    public IntPtr SecondChild = MiniAddress;
    public bool MissingParent, MissingPointer, MissingMap;
    public int ReadCount;
    public UiElementBaseOffset Parent = new()
    {
        Self = ParentAddress,
        ChildrensPtr = new StdVector { First = VectorAddress, Last = VectorAddress + 16, End = VectorAddress + 16 }
    };
    public MapUiElementOffset LargeMap = CreateMap(LargeAddress);
    public MapUiElementOffset MiniMap = CreateMap(MiniAddress);

    public void SetControllerLayout()
    {
        ControllerLayout = true;
        Parent.ChildrensPtr.Last = Parent.ChildrensPtr.End = VectorAddress + 24;
    }

    public bool Read<T>(IntPtr address, out T value) where T : unmanaged
    {
        ReadCount++;
        object? found = null;
        if (typeof(T) == typeof(UiElementBaseOffset) && address == ParentAddress && !MissingParent) found = Parent;
        if (typeof(T) == typeof(UiElementBaseOffset) && address == GameUiAddress)
            found = new UiElementBaseOffset
            {
                Self = GameUiAddress,
                ChildrensPtr = new StdVector { First = GameUiVector, Last = GameUiVector + 7*8, End = GameUiVector + 7*8 }
            };
        if (typeof(T) == typeof(IntPtr) && !MissingPointer)
        {
            if (address == GameUiVector + (ControllerLayout ? 0 : 6)*8) found = ParentAddress;
            if (address == GameUiVector + (ControllerLayout ? 6 : 0)*8) found = IntPtr.Zero;
            var firstMapOffset = ControllerLayout ? 8 : 0;
            if (ControllerLayout && address == VectorAddress) found = new IntPtr(0xA0000);
            if (address == VectorAddress + firstMapOffset) found = FirstChild;
            if (address == VectorAddress + firstMapOffset + 8) found = SecondChild;
        }
        if (typeof(T) == typeof(MapUiElementOffset) && !MissingMap)
        {
            if (address == FirstChild) found = LargeMap;
            if (address == SecondChild) found = MiniMap;
        }
        value = found is T result ? result : default;
        return found is T;
    }

    private static MapUiElementOffset CreateMap(IntPtr address) => new()
    {
        UiElementBase = new UiElementBaseOffset { Vtable = new IntPtr(0x50000), Self = address, ParentPtr = ParentAddress, Flags = 0x800 },
        Zoom = 0.75f,
        DefaultShift = new StdTuple2D<float>(0, -20)
    };
}
