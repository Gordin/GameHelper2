// <copyright file="HybridEvents.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.CoroutineEvents
{
    using Coroutine;

    /// <summary>
    ///     Events specific to the Game and the GameHelper state.
    /// </summary>
    public static class HybridEvents
    {
        /// <summary>
        ///     Gets the event that is triggered after all the preloads
        ///     of the current area/zone are updated.
        /// </summary>
        public static readonly Event PreloadsUpdated = new();
    }
}