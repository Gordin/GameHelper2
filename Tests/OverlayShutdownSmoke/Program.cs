using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using GameHelper;
using PortalAccess;
using System.Text.Json;

// Exercise the actual window and renderer without a game or the user's plugins/config.
if (Process.GetProcessesByName("PathOfExile").Length > 0)
    throw new InvalidOperationException("Close the game before running the standalone shutdown smoke check.");
Directory.SetCurrentDirectory(AppContext.BaseDirectory);
Directory.CreateDirectory("configs");
File.WriteAllText("configs/core_settings.json", """
{"ShowPerfStats":false,"FontPathName":"C:\\Windows\\Fonts\\arial.ttf","FontLanguage":0,"CloseWhenGameExit":false}
""");
var portalRoot = Path.Combine(AppContext.BaseDirectory, "portal-migration-check");
Directory.CreateDirectory(Path.Combine(portalRoot, "config"));
var portalSettings = Path.Combine(portalRoot, "config", "settings.json");
File.WriteAllText(portalSettings, """{"RestoreInteraction":true,"RefreshIntervalMs":1,"AreaDelayMs":99999}""");
var portalPlugin = new PortalAccessCore();
portalPlugin.SetPluginDllLocation(portalRoot);
portalPlugin.OnEnable(false);
portalPlugin.DrawUI();
portalPlugin.OnDisable();
using (var config = JsonDocument.Parse(File.ReadAllText(portalSettings)))
{
    if (!config.RootElement.GetProperty("RestoreInteraction").GetBoolean() ||
        portalPlugin.Settings.RefreshIntervalMs != 500 || portalPlugin.Settings.AreaDelayMs != 10000)
        throw new Exception("Explicit recovery preference or clamped limits were not preserved");
}
Console.WriteLine("PASS explicit recovery preference persists, samples offline and disables safely without a game");
var overlay = (GameOverlay)Activator.CreateInstance(typeof(GameOverlay), BindingFlags.Instance | BindingFlags.NonPublic,
    null, new object[] { "GameHelper shutdown check" }, null)!;
typeof(Core).GetProperty(nameof(Core.Overlay))!.SetValue(null, overlay);
var run = overlay.Run();
var wait = Stopwatch.StartNew();
while (overlay.window == null || overlay.window.Handle == IntPtr.Zero)
{
    if (wait.ElapsedMilliseconds > 10000) throw new TimeoutException("Window did not initialize");
    await Task.Delay(10);
}
var originalHandle = overlay.window.Handle;
// The test's transparent window does not need to remain on the desktop.
ShowWindow(originalHandle, 0);
await Task.Delay(100);
if (args.FirstOrDefault() == "render")
    Core.GHSettings.IsOverlayRunning = false; // the normal Render() close path
else
    overlay.Close(); // intentionally request shutdown from outside the window owner thread
await run.WaitAsync(TimeSpan.FromSeconds(10));
if (overlay.window.Handle != IntPtr.Zero || IsWindow(originalHandle))
    throw new Exception("The window survived render-thread shutdown");
overlay.Dispose();
overlay.Dispose(); // repeated cleanup must be harmless
if (Core.Process.Pid != 0) throw new Exception("The offline check attached to a game");
Console.WriteLine($"PASS owner-thread window destruction, {(args.FirstOrDefault() == "render" ? "render-thread" : "external")} close request and repeated disposal");

[DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
[DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
