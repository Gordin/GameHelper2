# GameHelper2 — .NET 10 LTS migration + bug audit (design)

**Date:** 2026-04-27 (revised: scope expanded to migrate Launcher + Plugins; audit scope unchanged)
**Migration scope:** all 9 csproj in the repository — `GameHelper/`, `GameOffsets/`, `Launcher/`, and the 6 plugins under `Plugins/`.
**Audit scope:** `GameHelper/` and `GameOffsets/` projects only. `Launcher/` and `Plugins/` are **out of scope for the bug audit** (per the user's original instruction to exclude them from defect search).
**Outcome:** all 9 csproj build clean on `net10.0-windows` with current diagnostics enabled, plus a curated audit document listing bugs the compiler cannot detect (covering only GameHelper + GameOffsets), ready for the user to triage.

---

## 1. Background

The repository is a Path of Exile overlay written in C# targeting `net8.0`. The repository contains 9 csproj files:

- **GameHelper** — overlay app (`WinExe` Release / `Exe` Debug, x64). Uses `ClickableTransparentOverlay` for the rendering host, `ImGui.NET`, `Coroutine` for cooperative scheduling, `ProcessMemoryUtilities.Net` for memory reads, Win32 P/Invoke, and `System.Drawing` types (`Rectangle`, `Point`, `Size`).
- **GameOffsets** — plain class library with offset structs and pattern definitions, referenced by GameHelper.
- **Launcher** — separate Exe project that boots GameHelper. Depends on `AsmResolver.PE.Win32Resources` and `Newtonsoft.Json`.
- **6 plugins** under `Plugins/`: `AutoHotKeyTrigger`, `HealthBars`, `PreloadAlert`, `Radar`, `SamplePluginTemplate/Changeme`, `WorldDrawing`. Each is a class library that GameHelper loads dynamically.

The user wants three outcomes:
1. Move all 9 csproj to the latest stable LTS .NET (full-repository migration).
2. Get a categorized bug audit, but **only** for the engine core (GameHelper + GameOffsets, 134 source files) — the user explicitly excluded Launcher + Plugins from defect search.
3. Audit-driven fixes are a separate downstream activity (out of scope here).

## 2. Goals and non-goals

**Goals:**
- All 9 csproj target `net10.0-windows` (LTS, EOL 2028-11-14, `latest-sdk` 10.0.203 as of 2026-04-21).
- Full-solution build succeeds with `0 errors / 0 warnings` after migration.
- Modern diagnostics enabled (`Nullable=enable`, `LangVersion=latest`, default code analyzers active) on **all 9 csproj** uniformly.
- Compiler-/analyzer-visible defects fixed mechanically as part of migration (these clean-up commits do not re-appear in the audit).
- A separate audit document lists bugs that the compiler/analyzers cannot catch — **only** for GameHelper + GameOffsets (concurrency, lifecycle, logic, P/Invoke semantics, etc.).
- Audit document is sortable by severity and category so the user can triage.

**Non-goals:**
- No manual code fixes for audit findings during this work (those become a separate plan after triage).
- No bug audit for `Launcher/` or `Plugins/` — only mechanical migration cleanup applies there.
- No architectural rewrites (no DI introduction, no replacing Coroutine, no rewriting reflection-driven UI scaffolding).
- No mandatory migration off `Newtonsoft.Json`. May be flagged in audit; not implemented here.
- No runtime smoke-test against the live game. The user will validate that the overlay still attaches and renders correctly.
- No changes to plugin business logic — only what's required to compile clean on `net10.0-windows` with `Nullable` enabled.

## 3. Environment

- Local SDKs after install of .NET 10: `8.0.405`, `9.0.313`, `10.0.203`. Build will use `10.0.203`.
- A `global.json` is **not** added — using the latest installed SDK is acceptable. If determinism becomes an issue later, `global.json` can be added in a separate change.
- `Directory.Build.props` already pins `RuntimeIdentifier=win-x64` — kept as is.

## 4. Approach

Single development branch, ordered phases. No parallel tracks.

### Phase 1 — Migration (mechanical, all 9 csproj)

1. **csproj changes (apply uniformly to all 9 csproj)**
   - `TargetFramework` → `net10.0-windows`.
   - Add `<Nullable>enable</Nullable>` and `<LangVersion>latest</LangVersion>`.
   - Hoist `<NoWarn>1701;1702; 1591</NoWarn>` (or equivalent) to the top-level `PropertyGroup` where each csproj uses `<GenerateDocumentationFile>true</GenerateDocumentationFile>` so CS1591 is suppressed in Release as well as Debug. Without this, WAE on Release would error on every undocumented public member.
   - Defer `<ImplicitUsings>enable</ImplicitUsings>` to a separate step after audit (it would mass-edit `using` blocks and pollute the migration diff).

2. **Package versions (no upgrades required)**
   - `ClickableTransparentOverlay` stays at `11.1.0` (latest available; published with a `net8.0` target. NuGet's TFM compatibility rules treat `net8.0` as consumable from `net10.0` projects, so no shim is required).
   - `Coroutine` stays at `2.1.5` (latest; `net6.0`/`netstandard2.0` lib).
   - `Newtonsoft.Json` stays at `13.0.3` unless a newer 13.x patch is on NuGet at migration time (then bump to latest 13.x).
   - `ProcessMemoryUtilities.Net` stays at `1.3.4` (latest; `netstandard2.0`).
   - `AsmResolver.PE.Win32Resources` (Launcher) stays at `5.5.1` unless a newer release exists.
   - Plugin-specific packages (`ImGui.NET`, `System.Linq.Dynamic.Core`, etc.) stay at currently-pinned versions.
   - Transitive packages allowed to float; resolution is recorded by `dotnet restore`.

3. **Compile clean (whole-solution)**
   - Temporarily set `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in **all 9 csproj**.
   - Iterate `dotnet build GameOverlay.sln -c Debug --no-restore` until zero errors and zero warnings across the whole solution. (See note below about MSBuild platform handling.)
   - Mechanical fixes only: nullable annotations (`?`/`!`), null-coalescing where the original code already assumed non-null, replacing obsolete API call sites, fixing P/Invoke signatures the analyzer flags, etc.
   - **Boundary rule (different by area):**
     - For warnings in `GameHelper/` or `GameOffsets/` (audit-in-scope): if a warning requires a logic decision, do **not** decide it inline — capture it as a finding in the audit doc and silence the warning at the call site with `#pragma warning disable <CODE> // TODO: see audit F-XXX` and re-enable immediately after.
     - For warnings in `Launcher/` or `Plugins/` (audit-out-of-scope): prefer mechanical fixes. If a warning genuinely requires a logic decision and a safe mechanical fallback isn't obvious, **escalate to the user** (the implementer reports `BLOCKED` with the specific question). Do not silently silence in non-audited code, because there is no audit doc to track it.

4. **Relax WAE**
   - After phase 1 completes, switch `<TreatWarningsAsErrors>` back to `false` (matching original behavior) on every csproj that originally lacked it; keep it `false` on Debug for csproj that had it. Keep `<Nullable>enable</Nullable>` permanently across all 9 csproj. This avoids future contributors being blocked by every new warning, but keeps nullability protection.

5. **MSBuild `Platform` note (build-command bug found in pre-flight)**
   - Several csproj declare `<OutputType>` and other project-shape properties only inside `PropertyGroup Condition="'$(Configuration)|$(Platform)'=='<X>|AnyCPU'"`. Passing `-p:Platform=x64` to `dotnet build` causes those PropertyGroups not to match, leading to errors like CS2017 ("Cannot specify /main if building a module or library").
   - The csproj are authored to be built with MSBuild's `Platform=AnyCPU` (the default); they emit x64 binaries via `<PlatformTarget>x64</PlatformTarget>` declared inside the conditional groups.
   - Build commands in this plan therefore use `dotnet build GameOverlay.sln -c <Config> --no-restore` (no explicit `-p:Platform=…`) — `AnyCPU` is the default.

**Phase 1 exit criteria:** `dotnet build GameOverlay.sln -c Release --no-restore` and `dotnet build GameOverlay.sln -c Debug --no-restore` both succeed with 0 errors / 0 warnings across all 9 projects.

### Phase 2 — Manual audit (document only)

Walk the in-scope code file by file and capture findings the compiler cannot detect.

**Categories scanned:**

1. **Concurrency / coroutine sync** — `processesInfo` mutated by `FindAndOpen` while read by `AskUserToSelectClient`; multiple coroutines yielding on the same `OnStaticAddressFound` event; `while(true)` with no cancellation on game exit; ordering between handle-close and consumers.
2. **Process / memory handle lifecycle** — `SafeMemoryHandle` lifetime, double-`Open`, `Information.Close()` vs `Dispose`, leaked native handles on coroutine restart.
3. **Exception handling** — silent `catch { }` blocks, `catch (Exception)` swallowing P/Invoke errors, `Pid` getter returning `0` on failure (caller cannot distinguish "no game" from "access denied").
4. **Resource lifecycle / Dispose** — classes owning unmanaged state without `IDisposable`, asymmetric `using` usage.
5. **P/Invoke correctness** — missing `SetLastError`, missing `CharSet`, calling convention mismatches, unsafe handle marshaling, `IntPtr` vs `SafeHandle`.
6. **Logic bugs** — `private set { }` on `Address` (effectively read-only with confusing setter), unreachable branches, dead code, off-by-ones in offset math.
7. **Culture / globalization** — `string.ToLower()` without `Invariant`, parsing without `CultureInfo.InvariantCulture`, locale-dependent comparisons.
8. **Performance in hot loops** — reflection in render frame, repeated `Process.GetProcesses()` (expensive on Windows), boxing/allocation in 60fps coroutine bodies, repeated string formatting.
9. **API misuse** — `MainModule.ModuleMemorySize` access without elevation handling, `MainWindowTitle` after `HasExited`, `ReadProcessMemory` calls on a torn-down handle.
10. **Coroutine semantics** — multi-consumer events with shared subscribers, missing tear-down on `OnClose`, infinite `while` loops without `yield break` paths.
11. **Settings / IO** — `File.AppendAllText("Error.log")` with relative path (depends on CWD), JSON parse without `try`, settings save races.
12. **GameOffsets structs** — `[StructLayout(Pack=...)]` correctness, sizes against documented offsets, alignment, `unsafe` block invariants, `fixed` buffer bounds.

**Finding format (one per issue):**

```
### F-NNN — Short title (Severity: critical|high|medium|low|nit)
**File:** path/relative/to/repo:lineStart-lineEnd
**Category:** <one of the 12 above>
**Description:** What the code does today.
**Why it's a bug:** What goes wrong, under what conditions.
**Suggested fix:** Concrete change. May reference a class/method.
**Risk if left:** What user-visible effect persists if we don't fix.
```

**Severity rubric:**
- **critical** — causes a crash, hang, memory corruption, or data loss in a normal session.
- **high** — race, leak, or wrong-behavior that triggers in everyday use but doesn't crash.
- **medium** — incorrect behavior in edge cases (specific game states, locales, multi-monitor, etc.).
- **low** — style, defensive-coding, or hygiene issue with no observable impact.
- **nit** — purely cosmetic or pedantic.

**Output file:** `docs/audit/2026-04-27-bug-audit.md` (in the GameHelper2 repo). Structure:
1. Header: counts by severity, file coverage summary.
2. Findings grouped by category, sorted by severity descending within each category.
3. Index at top: list of `F-NNN — title (severity, file)` for fast scanning.

**Phase 2 exit criteria:** the audit document exists, every audit-in-scope file (134 files in GameHelper + GameOffsets) has been read at least once, and the document has been committed. Launcher and Plugins are NOT audited.

### Phase 3 — User triage and fixes (out of scope here)

The user reviews the audit document and selects findings to fix. Each selected fix becomes a separate task, planned via the `writing-plans` skill. This phase is not part of this design.

## 5. Commits

Granular commits, no squashing:

| # | Subject | Contents |
|---|---------|----------|
| 1 | `chore(net): migrate GameHelper + GameOffsets to .NET 10 LTS` | TFM bump on engine core, Nullable, LangVersion, NoWarn hoist |
| 2 | `chore(net): migrate Launcher to .NET 10 LTS` | TFM bump on Launcher, Nullable, LangVersion |
| 3 | `chore(net): migrate Plugins to .NET 10 LTS` | TFM bump on all 6 plugins, Nullable, LangVersion |
| 4 | `chore(net): enable warnings-as-errors during migration cleanup` | WAE=true across all 9 csproj |
| 5 | `fix(net10): resolve compiler/analyzer warnings from net10 migration` | The mechanical cleanup pass (whole solution) |
| 6 | `chore(net): relax WAE post-migration, keep nullable on` | WAE=false across all 9 csproj, Nullable stays |
| 7 | `docs(audit): add 2026-04-27 bug audit report` | The audit document (engine-core only) |

If commit 5 is large, it may be split per project or per area — but each split commit must still leave the whole solution build green.

## 6. Risks and assumptions

- **Risk: `ClickableTransparentOverlay` 11.1.0 was published targeting `net8.0`.** Runtime forward-compat with `net10.0-windows` is the stated NuGet behavior, but if the binary surfaces an issue (e.g. a Vortice transitive package conflicts), fallback is `net9.0-windows` (still LTS-adjacent — ships as STS though, EOL 2026-11). If that fails too, file an upstream issue and stay on `net8.0` for the migration commit and audit on `net8.0`. Decision lives with the user.
- **Risk: plugin packages targeting older TFMs.** `ImGui.NET`, `System.Linq.Dynamic.Core`, `AsmResolver.PE.Win32Resources` and similar plugin/launcher dependencies must remain consumable from net10.0 projects. Same NuGet TFM compatibility rules apply; if any package surfaces an actual incompatibility, escalate to user.
- **Risk: enabling `Nullable` may produce a large warning volume.** Treated as expected work for phase 1, not a blocker.
- **Risk: warnings in `Launcher/` or `Plugins/` requiring logic decisions.** Since those projects have no audit doc to track deferrals, the implementer escalates `BLOCKED` for case-by-case user input. We don't silently `#pragma`-disable warnings in non-audited code.
- **Risk: silenced warnings in audited code (`#pragma`) leak into audit-as-future-work.** Mitigated by the `// TODO: see audit F-XXX` requirement so every silenced warning has a corresponding finding.
- **Assumption: solution currently builds on `net8.0` with no errors across all 9 csproj.** Verified in pre-flight (Phase 0 confirmed 0 errors / 0 warnings on the engine core build).
- **Assumption: behavioral verification (overlay attaches, finds game, renders, plugins load) is performed by the user.** I cannot run the game from this environment. The migration commits are "build-green", not "runtime-verified".
- **Plan correction: `dotnet build` commands MUST NOT pass `-p:Platform=x64`.** Several csproj declare `<OutputType>` only inside `Platform==AnyCPU` conditional `PropertyGroup`s; passing `-p:Platform=x64` causes those groups not to match and produces errors like CS2017. Use `dotnet build GameOverlay.sln -c <Config> --no-restore` instead.

## 7. Out of scope (explicit)

- Bug audit findings for `Launcher/` or `Plugins/` (those receive only mechanical migration cleanup; no defect search).
- Plugin business-logic changes beyond what's needed to compile clean on `net10.0-windows`.
- Replacing `Newtonsoft.Json` with `System.Text.Json`.
- Replacing the `Coroutine` library or rewriting the cooperative scheduler.
- Adding unit tests, integration tests, or CI configuration.
- Adding analyzers beyond the .NET SDK defaults (no StyleCop, no SonarAnalyzer.CSharp).
- Updating the `.editorconfig`.
- Adding new solution platforms to `GameOverlay.sln` (the existing AnyCPU platform is what the project layout uses).

## 8. Deliverables

1. **All 9 csproj files** updated to `net10.0-windows` with `Nullable` enabled (GameHelper, GameOffsets, Launcher, AutoHotKeyTrigger, HealthBars, PreloadAlert, Radar, SamplePluginTemplate/Changeme, WorldDrawing).
2. Mechanical compiler-driven fixes across the whole solution source.
3. `docs/audit/2026-04-27-bug-audit.md` — the audit document covering only GameHelper + GameOffsets (134 files).
4. This design document, committed at `docs/superpowers/specs/2026-04-27-gamehelper-net10-migration-and-audit-design.md`.
5. (Implicit) A green `dotnet build GameOverlay.sln` on the new TFM, both Debug and Release, 0 errors / 0 warnings.

## 9. Hand-off

After user approval of this spec, control transfers to the `writing-plans` skill for an executable phased plan. No implementation skills are invoked from this brainstorming step directly.
