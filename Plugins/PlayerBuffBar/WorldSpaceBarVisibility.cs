namespace PlayerBuffBar
{
    using System;
    using System.Reflection;
    using GameHelper.RemoteObjects.States.InGameStateObjects;

    /// <summary>
    ///     Hides world-space overlays when fullscreen map UIs are open (Gordin + community forks).
    /// </summary>
    internal static class WorldSpaceBarVisibility
    {
        private static readonly Func<ImportantUiElements, bool>? ForkShouldHide;

        static WorldSpaceBarVisibility()
        {
            var prop = typeof(ImportantUiElements).GetProperty(
                "ShouldHideWorldSpaceBars",
                BindingFlags.Public | BindingFlags.Instance);
            if (prop?.PropertyType == typeof(bool))
            {
                ForkShouldHide = ui => (bool)prop.GetValue(ui)!;
            }
        }

        internal static bool ShouldHide(ImportantUiElements gameUi)
        {
            if (ForkShouldHide != null)
            {
                return ForkShouldHide(gameUi);
            }

            return gameUi.IsAnyLargePanelOpen || gameUi.Atlas.IsVisible;
        }
    }
}
