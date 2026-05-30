// <copyright file="GameStateTypes.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.RemoteEnums
{
    /// <summary>
    ///     Gets all known states of the game.
    /// </summary>
    public enum GameStateTypes
    {
        /// <summary>
        ///     When user is on the Area Loading Screen.
        /// </summary>
        AreaLoadingState,

        /// <summary>
        ///     Game State.
        /// </summary>
        ChangePasswordState,

        /// <summary>
        ///     When user is viewing the credit screen window (accessable from login screen)
        /// </summary>
        CreditsState,

        /// <summary>
        ///     When user has opened the escape menu.
        /// </summary>
        EscapeState,

        /// <summary>
        ///     When User is in Town/Hideout/Area/Zone etc.
        /// </summary>
        InGameState,

        /// <summary>
        ///     The user is watching the GGG animation that comes before the login screen.
        /// </summary>
        PreGameState,

        /// <summary>
        ///     When user is on the Login screen.
        /// </summary>
        LoginState,

        /// <summary>
        ///     When user is transitioning from <see cref="PreGameState"/> to <see cref="LoginState"/>.
        /// </summary>
        WaitingState,

        /// <summary>
        ///     When user is on the create new character screen.
        /// </summary>
        CreateCharacterState,

        /// <summary>
        ///     When user is on the select character screen.
        /// </summary>
        SelectCharacterState,

        /// <summary>
        ///     When user is on the delete character screen.
        /// </summary>
        DeleteCharacterState,

        /// <summary>
        ///     When user is transitioning from <see cref="SelectCharacterState"/> to <see cref="InGameState"/>.
        /// </summary>
        LoadingState,

        /// <summary>
        ///     This is a special State, changing to this state will not trigger State Change Event.
        ///     This is just for displaying purposes. It means Game isn't stared.
        /// </summary>
        GameNotLoaded
    }
}