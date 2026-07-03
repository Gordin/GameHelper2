namespace AutoHotKeyTrigger
{
    using System.Globalization;
    using GameHelper.Localization;

    internal static class AhkText
    {
        internal static PluginLocalization? Current { private get; set; }

        internal static string T(string key, string fallback) =>
            Current?.T(key, fallback) ?? fallback;

        internal static string F(string key, string fallback, params object[] args) =>
            Current?.F(key, fallback, args) ?? string.Format(CultureInfo.CurrentCulture, fallback, args);

        internal static string Label(string key, string fallback, string id) =>
            Current?.Label(key, fallback, id) ?? $"{fallback}##{id}";

        internal static string Title(string key, string fallback, string id) =>
            Current?.Title(key, fallback, id) ?? $"{fallback}###{id}";
    }
}
