using GameHelper;
using GameHelper.Localization;
using System.Text.Json;

var root = Path.GetFullPath(args[0]);
var pool = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Plugins/Atlas2/json/ritualmods.json")));
var expected = JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(root,"GameHelper/Localization/zh-Hant.json")))!;
void Equal(string a, string b) { if (a != b) throw new Exception($"Expected '{b}', got '{a}'"); }
Core.GHSettings.UiLanguage = OverlayLanguage.ChineseTraditional;
foreach (var row in pool.RootElement.GetProperty("rows").EnumerateArray())
{
    var english = row.GetProperty("text").GetString()!;
    var translated = GameText.Display(english);
    if (translated == english || !translated.Any(c => c >= 0x3400 && c <= 0x9fff)) throw new Exception("Untranslated Ritual modifier: " + english);
}
Equal(GameText.Display("unknown_game_metadata/123"), "unknown_game_metadata/123");
Equal(GameText.Display("繁體中文物品"), "繁體中文物品");
Equal(GameText.Display(null), "");
Equal(GameText.Lines("1 Divine Orb\r\n2 Divine Orbs"), GameText.Display("1 Divine Orb") + "\n" + GameText.Display("2 Divine Orbs"));
Equal(GameText.Flags("Normal, Magic"), "普通、魔法");
var atlas = new PluginLocalization(Path.Combine(root,"Plugins/Atlas2"));
Equal(atlas.FormatTitle("ui.renamepopup", "RenamePopup##{0}", 5), "重新命名群組###RenamePopup##5");
Core.GHSettings.UiLanguage = OverlayLanguage.English;
Equal(GameText.Flags("Normal, Magic"), "Normal, Magic");
Equal(GameText.Display("1 Divine Orb"), "1 Divine Orb");
Core.GHSettings.UiLanguage = OverlayLanguage.ChineseTraditional;
if (GameText.Display("1 Divine Orb")=="1 Divine Orb") throw new Exception("Language switch cache failed");
Core.GHSettings.UiLanguage = OverlayLanguage.ChineseSimplified;
foreach (var row in pool.RootElement.GetProperty("rows").EnumerateArray())
{
    var english = row.GetProperty("text").GetString()!;
    var translated = GameText.Display(english);
    if (translated == english || !translated.Any(c => c >= 0x3400 && c <= 0x9fff)) throw new Exception("Untranslated Simplified Ritual modifier: " + english);
}
Equal(GameText.Display("Atlas2"), "世界地图");
Equal(atlas.FormatTitle("ui.renamepopup", "RenamePopup##{0}", 5), "重命名群组###RenamePopup##5");
if (GameText.Display("1 Divine Orb")=="1 Divine Orb") throw new Exception("Simplified language lookup failed");
Core.GHSettings.UiLanguage = OverlayLanguage.ChineseTraditional;
Equal(GameText.Display("Atlas2"), "輿圖助手");
Console.WriteLine("PASS: 67 Ritual modifiers in both Chinese locales, unknown IDs, existing Chinese, line breaks, rarity flags, popup IDs and live locale switching.");

namespace GameHelper
{
    internal static class Core { public static SettingsStub GHSettings {get;} = new(); }
    internal sealed class SettingsStub { public OverlayLanguage UiLanguage {get;set;} }
}
