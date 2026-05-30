// <copyright file="GameHelperEvents.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.CoroutineEvents
{
    using Coroutine;

    /// <summary>
    ///     Co-Routine events specific to the GameHelper.
    /// </summary>
    public static class GameHelperEvents
    {
        /// <summary>
        ///     Gets the event raised when the game has changed its size, position or both.
        /// </summary>
        public static readonly Event OnMoved = new();

        /// <summary>
        ///     Gets the event raised when the game Foreground property has changed.
        /// </summary>
        public static readonly Event OnForegroundChanged = new();

        /// <summary>
        ///     Gets the event raised just before the GameProcess has closed the game.
        /// </summary>
        public static readonly Event OnClose = new();

        /// <summary>
        ///     To Update data every frame before rendering.
        /// </summary>
        internal static readonly Event PerFrameDataUpdate = new();

        /// <summary>
        ///     To submit ImGui code for generating the UI.
        /// </summary>
        internal static readonly Event OnRender = new();

        /// <summary>
        ///     Gets the event raised after all OnRender coroutines have executed.
        /// </summary>
        internal static readonly Event OnPostRender = new();

        /// <summary>
        ///     Gets the event raised when GameProcess has opened a new game.
        /// </summary>
        internal static readonly Event OnOpened = new();

        /// <summary>
        ///     Gets the Game Helper closing event. The event is called whenever
        ///     all the settings have to be saved.
        /// </summary>
        internal static readonly Event TimeToSaveAllSettings = new();

        /// <summary>
        ///     Gets the Game Helper event that is executed very close to rendering of frame
        ///     so the data read in this event is as fresh as possible.
        /// </summary>
        internal static Event PostPerFrameDataUpdate = new();
    }
}