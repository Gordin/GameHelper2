// <copyright file="MapUiResolver.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteObjects.UiElement
{
    using System;
    using GameOffsets.Objects.UiElement;

    /// <summary>
    ///     Resolves the two map viewports from a map parent without retaining stale addresses.
    /// </summary>
    internal static class MapUiResolver
    {
        internal delegate bool TryReadMemory<T>(IntPtr address, out T value) where T : unmanaged;

        /// <summary>
        ///     PoE2 0.5.5 uses GameUi[6][0/1] for keyboard and GameUi[0][1/2]
        ///     for controller. The old controller manager +0xB98 is a different container.
        /// </summary>
        internal static bool TryResolveFromGameUi(
            IntPtr gameUiAddress,
            bool controllerMode,
            TryReadMemory<UiElementBaseOffset> readElement,
            TryReadMemory<IntPtr> readPointer,
            TryReadMemory<MapUiElementOffset> readMap,
            out IntPtr largeMapAddress,
            out IntPtr miniMapAddress)
        {
            largeMapAddress = miniMapAddress = IntPtr.Zero;
            return TryReadChild(gameUiAddress, controllerMode ? 0 : 6, readElement, readPointer, out var parent) &&
                   TryResolve(parent, controllerMode, readElement, readPointer, readMap,
                       out largeMapAddress, out miniMapAddress);
        }

        /// <summary>
        ///     Reads the live child vector instead of relying on the parent's inline child cache.
        ///     Both viewports must belong to this parent and have usable map transform data.
        ///     Visibility is deliberately not required: the closed large map still needs updating.
        /// </summary>
        internal static bool TryResolve(
            IntPtr parentAddress,
            bool controllerMode,
            TryReadMemory<UiElementBaseOffset> readElement,
            TryReadMemory<IntPtr> readPointer,
            TryReadMemory<MapUiElementOffset> readMap,
            out IntPtr largeMapAddress,
            out IntPtr miniMapAddress)
        {
            largeMapAddress = IntPtr.Zero;
            miniMapAddress = IntPtr.Zero;
            var firstMapIndex = controllerMode ? 1 : 0;
            if (!TryReadChild(parentAddress, firstMapIndex, readElement, readPointer, out var largeMap) ||
                !TryReadChild(parentAddress, firstMapIndex + 1, readElement, readPointer, out var miniMap) ||
                largeMap == miniMap ||
                !IsMapViewport(largeMap, parentAddress, readMap, out var largeVtable) ||
                !IsMapViewport(miniMap, parentAddress, readMap, out var miniVtable) ||
                largeVtable != miniVtable)
            {
                return false;
            }

            largeMapAddress = largeMap;
            miniMapAddress = miniMap;
            return true;
        }

        private static bool TryReadChild(IntPtr parentAddress, int index,
            TryReadMemory<UiElementBaseOffset> readElement, TryReadMemory<IntPtr> readPointer,
            out IntPtr childAddress)
        {
            childAddress = IntPtr.Zero;
            if (parentAddress == IntPtr.Zero ||
                !readElement(parentAddress, out var parent) || parent.Self != parentAddress)
            {
                return false;
            }

            var first = parent.ChildrensPtr.First.ToInt64();
            var last = parent.ChildrensPtr.Last.ToInt64();
            var end = parent.ChildrensPtr.End.ToInt64();
            // A torn/obsolete vector must never become an unbounded traversal or a bad read.
            if (first <= 0 || last < first || end < last ||
                last - first < (index + 1) * IntPtr.Size || last - first > 1024 * IntPtr.Size ||
                (last - first) % IntPtr.Size != 0)
            {
                return false;
            }

            return readPointer(parent.ChildrensPtr.First + index * IntPtr.Size, out childAddress) &&
                   childAddress != IntPtr.Zero;
        }

        private static bool IsMapViewport(
            IntPtr address, IntPtr parentAddress, TryReadMemory<MapUiElementOffset> readMap,
            out IntPtr vtable)
        {
            vtable = IntPtr.Zero;
            if (address != IntPtr.Zero &&
                   readMap(address, out var map) &&
                   map.UiElementBase.Self == address &&
                   map.UiElementBase.ParentPtr == parentAddress &&
                   map.UiElementBase.Vtable != IntPtr.Zero &&
                   // Ordinary UI containers can have tiny positive bit patterns here (e.g. 7).
                   // Finite-and-positive alone accepted them as maps in the first candidate.
                   float.IsFinite(map.Zoom) && map.Zoom >= 0.01f && map.Zoom <= 10f &&
                   float.IsFinite(map.Shift.X) && float.IsFinite(map.Shift.Y) &&
                   float.IsFinite(map.DefaultShift.X) && float.IsFinite(map.DefaultShift.Y))
            {
                vtable = map.UiElementBase.Vtable;
                return true;
            }

            return false;
        }
    }
}
