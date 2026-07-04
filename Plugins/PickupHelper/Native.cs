// <copyright file="Native.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace PickupHelper
{
    using System.Runtime.InteropServices;
    using System.Threading;

    /// <summary>
    ///     Minimal user32 P/Invoke wrappers for mouse clicking and key-state polling.
    ///     GameHelper core has no mouse-click helper, so we send the click ourselves at the
    ///     current cursor position (we never move the cursor).
    /// </summary>
    internal static class Native
    {
        private const int MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const int MOUSEEVENTF_LEFTUP = 0x0004;

        /// <summary>
        ///     Set to true to suppress real OS input (useful for safe manual testing).
        /// </summary>
        internal static bool DisableNativeInput = false;

        [DllImport("user32.dll")]
        private static extern void mouse_event(int dwFlags, int dx, int dy, int cButtons, int dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        /// <summary>
        ///     Performs a left mouse down/up at the current cursor position.
        /// </summary>
        internal static void LeftClick()
        {
            if (DisableNativeInput)
            {
                return;
            }

            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
            Thread.Sleep(1);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        }

        /// <summary>
        ///     Returns whether the given virtual key is currently held down.
        /// </summary>
        /// <param name="vKey">virtual key code.</param>
        /// <returns>true when the key is down.</returns>
        internal static bool IsKeyDown(int vKey)
        {
            return (GetAsyncKeyState(vKey) & 0x8000) != 0;
        }
    }
}
