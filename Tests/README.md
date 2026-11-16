# Offline validation

On Windows x64 with .NET 10 SDK and Python 3, run from the repository root:

```powershell
dotnet build GameOverlay.sln -c Release
python Tests/verify_localization.py
dotnet run --project Tests/LocalizationSmoke -c Release -- .
dotnet run --project Tests/LootTrackerTests -c Release
dotnet run --project Tests/PortalAccessTests -c Release
dotnet run --project Tests/OverlayShutdownSmoke -c Release
dotnet run --project Tests/OverlayShutdownSmoke -c Release -- render
```

The console projects deliberately stay outside the solution so the full application's plugin
staging completes first. PortalAccessTests uses fake memory and allocated memory in its own
process, never the game. LootTrackerTests covers aggregation, interruption, persistence,
CSV and price-cache validity. LocalizationSmoke exercises both Chinese locales, Ritual terms,
language switching and stable ImGui IDs. The Python check requires no extra packages.

PortalAccess tests cover rejecting arbitrary foreign-process writes, bounding/cancelling scans,
client debug-label flag discovery, a two-field write gate, native area/loading/player fences,
unknown profiles, identity-validated undo, and resumable scans beyond 2,500 entities with budget interruption and tree replacement (68 checks, including complete-profile selection, mixed-profile rejection, image bounds and relocation). They never write to the game.
The layout evidence is documented in
[PortalAccess](../Plugins/PortalAccess/DIAGNOSTIC_LAYOUT.md). OverlayShutdownSmoke creates and closes the actual
overlay window using isolated settings and no plugins. Run it with the game closed; it checks
that the HWND is destroyed before the render thread exits and repeated disposal is harmless.
It does not validate in-game stability or reproduce every third-party input-method callback.

Optional ImGui rendering/font checks use the Windows Microsoft YaHei font
(`C:/Windows/Fonts/msyh.ttc`) and require no game or GPU window:

```powershell
dotnet run --project Tests/FontCheck -c Release -- .
dotnet run --project Tests/SettingsPreview -c Release -- . Tests/artifacts/settings-zh-CN zh-CN
dotnet run --project Tests/SettingsPreview -c Release -- . Tests/artifacts/settings-zh-Hant zh-Hant
dotnet run --project Tests/HudPreview -c Release -- . Tests/artifacts/hud
```

SettingsPreview renders the actual settings shell and drives ImGui input to check navigation,
search, enable/disable routing and confirmation behavior. HUD previews use synthetic sample
values. These checks do not verify current game offsets, live inventory sampling, or whether
a server accepts portal re-entry.

Native fixture allocations use the full unmanaged struct size, and fixture writes reject out-of-bounds destinations before copying.
