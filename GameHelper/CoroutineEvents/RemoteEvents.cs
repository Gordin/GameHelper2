// <copyright file="RemoteEvents.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.CoroutineEvents
{
    using Coroutine;

    /// <summary>
    ///     Co-Routine events specific to the game.
    /// </summary>
    public static class RemoteEvents
    {
        /// <summary>
        ///     Gets the event that is triggered 50ms after the area change detection.
        ///     Area change is detected by checking if the time spend on the
        ///     loading screen (given to us by the game) is greater than the
        ///     last recorded time and IsLoading bool is set to false.
        ///     NOTE: when this is triggered the preloads might not be up to date.
        /// </summary>
        public static readonly Event AreaChanged = new();

        /// <summary>
        ///     Gets the Current State Changed event.
        /// </summary>
        internal static readonly Event StateChanged = new();
    }
}