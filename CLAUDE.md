# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

GameHelper2 is a Windows x64 .NET 10 overlay for Path of Exile 2. It attaches to the running game process, reads game state out of its memory, and renders an ImGui overlay (via `ClickableTransparentOverlay`). Functionality is delivered through dynamically-loaded plugins. There are no automated tests in this repo.

## Build & run

- **Target:** `net10.0-windows`, `win-x64`, `PlatformTarget=x64` (solution platform is shown as `Any CPU` but output is always x64). Requires the **.NET 10 SDK** (Desktop Runtime alone is not enough).
- **Always build the whole solution**, not individual projects. `Launcher` and each plugin have `AfterTargets="Build"` MSBuild `Copy` steps that stage their DLLs/assets into the GameHelper output folder. Building just `GameHelper` produces an app with no launcher and no plugins.

```powershell
dotnet build GameOverlay.sln -c Release       # or -c Debug while developing
```

- **Handling build errors from locked files (`MSB3021`/`MSB3027`, "being used by another process"):** these happen because GameHelper is running and holds the output DLLs. This is expected — do **not** try to kill GameHelper, and do **not** explain that or why it happened. The compile itself still succeeded (no `error CS`); only the copy-to-output step failed. Just check for real `error CS` lines and tell the user whether they need to rebuild the solution themselves (i.e. whether the change requires a rebuild to take effect) — nothing more.
- **Output / run dir:** `GameHelper\bin\<Config>\net10.0-windows\win-x64\`. Launch `Launcher.exe` from there (it locates and starts `GameHelper.exe`). Run with the same privilege level as the game — if the game is elevated, run elevated, or the overlay won't attach.
- `NuGet.config` (repo root) adds the `veldrid-prereleases` myget feed required by `ClickableTransparentOverlay`.

## Architecture

**Three core projects + plugins:**
- `GameHelper` — the overlay executable and all core runtime services.
- `GameOffsets` — native struct layouts (`Objects/`, `Components/`) and the signature `Pattern[]` (`StaticOffsetsPatterns.cs`) used to locate game data in memory. Has no dependency on `GameHelper`.
- `Launcher` — thin wrapper that finds/updates/launches GameHelper; copied into GameHelper's output.
- `Plugins/*` — each is a separate class library referencing `GameHelper` with `<Private>false</Private>` (do not copy GameHelper's DLLs), then a `CopyFiles` target stages the plugin DLL + assets into `...\win-x64\Plugins\<ProjectName>\`.

**`Core` (static, `Core.cs`)** is the central hub. It exposes everything plugins read: `Core.Process` (the attached `GameProcess`), `Core.States` (game state tree), `Core.CurrentAreaLoadedFiles`, settings, and caches. It owns startup coroutines that wire each `RemoteObject`'s `Address` to a resolved static address.

**Coroutine event loop (not async/await for the hot path).** The whole app runs on `Coroutine` (Daniel Cronqvist's lib) driven from `GameOverlay.Render()`, which ticks the handler and raises events each frame in order: `PerFrameDataUpdate` → `PostPerFrameDataUpdate` → `OnRender` → `OnPostRender`. Code subscribes by `yield return new Wait(SomeEvent)` in an `IEnumerator<Wait>` coroutine. Events live in `CoroutineEvents/` (`GameHelperEvents` = lifecycle/render, `RemoteEvents`, `HybridEvents`). Render and event dispatch wrap every handler in try/catch — a throwing coroutine logs but doesn't crash the frame.

**Memory reading model.** `GameProcess` continuously finds the game process and resolves the signature patterns into `StaticAddresses`, firing `OnStaticAddressFound`. `RemoteObjects/` types derive from `RemoteObjectBase`: you set their `.Address` and they read/parse memory lazily (re-reading only when the address changes, unless `forceUpdate`). `RemoteObjects/Components/` mirrors the game's entity-component layout (`Actor`, `Life`, `Positioned`, `Render`, etc.), with matching byte layouts in `GameOffsets/Objects/Components/`. Actual reads go through `Utils/SafeMemoryHandle` and `ProcessMemoryUtilities`.

**Settings/config.** `Core.GHSettings` and plugin settings are JSON loaded via `Utils/JsonHelper` (Newtonsoft). At runtime, config is written next to the exe: `configs\core_settings.json`, `configs\plugins.json`, and `Plugins\<Name>\config\`. These are gitignored runtime data.

## Plugins

To author a plugin, copy `Plugins/SamplePluginTemplate/Changeme`. A plugin is exactly **one `sealed` class deriving from `PCore<TSettings>`** (where `TSettings : IPSettings, new()`) — `PManager` rejects assemblies that don't have exactly one. Override `OnEnable(bool isGameOpened)`, `OnDisable`, `DrawSettings`, `DrawUI`, `SaveSettings`.

- `PManager` loads each plugin into its **own collectible `AssemblyLoadContext`** so it can be hot-unloaded (DEBUG builds support reload; unload runs repeated GC to release the ALC).
- The plugin DLL filename **must match its directory name** (`PluginName*.dll` in `Plugins\PluginName\`), or it won't be discovered.
- `DrawUI` is called every frame for enabled plugins via the `OnRender` coroutine (wrapped in profiling + try/catch). Read game data through `Core.*`.

## Localization

Overlay text is translated through keyed JSON resources. There are **two parallel systems** — pick the one that owns the text:

- **`GameHelper.Localization.OverlayLocalization`** (static) — **core/overlay** text (settings window, core popups). Resources live in `GameHelper/Localization/<lang>.json` and are staged to the output root by a `None Include="Localization\*.json"` in `GameHelper.csproj`.
- **`GameHelper.Localization.PluginLocalization`** (instance) — **plugin** text. Every `PCore<TSettings>` exposes it as `protected PluginLocalization PluginText`. Each plugin owns its resources under `Plugins/<Name>/Localization/<lang>.json`, staged next to the plugin DLL by `Plugins/Directory.Build.targets` (auto-imported for every plugin project — no per-plugin `.csproj` change needed). A `static` helper (see AutoHotKeyTrigger's `AhkText`) is the pattern when a static class needs `PluginText` it can't reach through `this`.

Both expose the same four functions:

- `T(key, fallback)` — plain text.
- `F(key, fallback, args...)` — `string.Format` with the current culture.
- `Label(key, fallback, id)` — appends a hidden `##id` so the ImGui ID stays stable across languages while the visible text changes.
- `Title(key, fallback, id)` — appends `###id` for windows / tabs / collapsing headers (visible title localizes, ID stable).

`fallback` is the English string, returned only when the key is missing from both the current language and `en-US.json`; keep it matching `en-US.json`. Language is chosen by the user via `Core.GHSettings.UiLanguage` (an `OverlayLanguage`) — a dropdown in Settings → General. Supported: `en-US`, `fr-FR`, `de-DE`, `es-ES`, `ja-JP`, `ko-KR`, `pt-BR`, `ru-RU`, `th-TH`, `zh-CN`, `zh-Hant`; `en-US.json` is the baseline and other files may be partial (missing keys fall back to English).

Key naming: `settings.<section>.<name>` for the settings window, `<feature>.<name>` elsewhere — lower-case dotted, descriptive rather than a copy of the English text. A plugin's `plugin.description` key is shown in the plugin-manager list via `PCore.GetDescription()` (override it to change the source). Full authoring guide: `GameHelper/Localization/README.md`.

Do **not** reintroduce the old `OverlayLocalization.L(english, german)` / `IsGerman` bilingual shim — it was removed in 2.5.0; all text goes through the keyed system above.

## Memory offset / patch-day recovery

When PoE2 patches, the app breaks until offsets are refreshed — this is a recurring core maintenance task. Two independent layers:
1. **Signature patterns** (`GameOffsets/StaticOffsetsPatterns.cs`, 6 patterns) — recovered via Ghidra + the `OffsetRecoveryFramework.java` script in `C:\Users\Gordin\GameHelper2-Offset-Recovery-Framework`. The `^` token in a pattern marks `BytesToSkip` (logic in `GameOffsets/Pattern.cs`). Usually survive minor patches.
2. **Struct field offsets** (`AreaInstanceOffsets.cs`, `Actor.cs`, `InGameStateOffset.cs`, etc.) — NOT covered by the script; recover from **live memory** using the in-code debug scanners (e.g. in `AreaInstance`) that signature-match struct layouts, seeded with a base address from the DataVisualization debug window.

See the `patch-day-offset-recovery` and `ghidra-mcp-setup` entries in Claude memory for the full procedure.

## Conventions

- File-scoped `using` inside the namespace block, XML doc comments, `// <copyright>` header on each file. Nullable enabled; `1591` (missing-doc) warning suppressed.
- Code comments referencing `F-0xx` / audit findings point to a prior security/correctness audit — preserve the reasoning when touching that code (e.g. the no-`Environment.Exit` rule in `Program.cs`, the ALC unload dance in `PManager`).
- Only some plugins are in `GameOverlay.sln`. `Plugins/Atlas` is its own separate solution (`Atlas.sln`). `SamplePluginTemplate` and `WorldDrawing` exist in the repo but aren't built by the main solution by default.
