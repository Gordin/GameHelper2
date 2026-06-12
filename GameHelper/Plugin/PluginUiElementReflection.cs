// <copyright file="PluginUiElementReflection.cs" company="None">
// Copyright (c) None. All rights reserved.
// </copyright>

namespace GameHelper.Plugin
{
    using System;
    using System.Reflection;
    using GameHelper.RemoteEnums;

    /// <summary>
    ///     Resolves internal GameHelper UI types for plugins loaded from a separate assembly.
    /// </summary>
    public static class PluginUiElementReflection
    {
        private static readonly Assembly GameHelperAssembly = typeof(Core).Assembly;

        public static Type? UiElementParentsType { get; } =
            GameHelperAssembly.GetType("GameHelper.Cache.UiElementParents");

        public static Type? UiElementBaseType { get; } =
            GameHelperAssembly.GetType("GameHelper.RemoteObjects.UiElement.UiElementBase");

        public static PropertyInfo? UiElementPositionProperty =>
            UiElementBaseType?.GetProperty("Position");

        public static PropertyInfo? UiElementSizeProperty =>
            UiElementBaseType?.GetProperty("Size");

        public static object? CreateParents(string name = "fake") =>
            UiElementParentsType == null
                ? null
                : Activator.CreateInstance(
                    UiElementParentsType,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null,
                    new object?[] { null, GameStateTypes.InGameState, GameStateTypes.EscapeState, name },
                    null);

        public static object? CreateUiElement(IntPtr address, object parents) =>
            UiElementBaseType == null
                ? null
                : Activator.CreateInstance(
                    UiElementBaseType,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                    null,
                    new object?[] { address, parents },
                    null);
    }
}
