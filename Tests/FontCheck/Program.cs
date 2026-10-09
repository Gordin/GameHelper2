using ImGuiNET;
using System.Text.Json;
var source = Path.GetFullPath(args[0]);
var chars = new HashSet<char>();
foreach(var locale in new[]{"zh-Hant.json","zh-CN.json"})
    foreach(var file in Directory.EnumerateFiles(source,locale,SearchOption.AllDirectories).Where(p=>!p.Contains("\\bin\\")&&!p.Contains("\\obj\\")))
        foreach(var text in JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(file))!.Values)
            foreach(var ch in text.Where(c=>c>=0x3000)) chars.Add(ch);
ImGui.CreateContext();
try
{
    var fonts=ImGui.GetIO().Fonts;
    var font=fonts.AddFontFromFileTTF("C:/Windows/Fonts/msyh.ttc",18,default,fonts.GetGlyphRangesChineseFull());
    if(!fonts.Build()) throw new Exception("Font atlas build failed");
    var missing=new List<char>();
    unsafe { foreach(var ch in chars) if(font.FindGlyphNoFallback(ch).NativePtr==null) missing.Add(ch); }
    Console.WriteLine($"ChineseFull enum value: {(int)ClickableTransparentOverlay.FontGlyphRangeType.ChineseFull}");
    Console.WriteLine($"Checked {chars.Count} distinct non-Latin characters in both Chinese locales. Missing glyphs: {string.Join(' ',missing)}");
    if(missing.Count>0) Environment.ExitCode=1;
}
finally { ImGui.DestroyContext(); }
