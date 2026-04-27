# GameHelper2 — .NET 10 Migration + Bug Audit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate **all 9 csproj** in the repository (`GameHelper`, `GameOffsets`, `Launcher`, and 6 `Plugins/*`) from `net8.0` to `net10.0-windows` LTS with `Nullable` enabled and zero warnings on the whole solution, then produce a categorized bug-audit document covering 12 defect classes — but the audit is **only** for `GameHelper` + `GameOffsets` (134 C# files), per spec.

**Architecture:** Two ordered phases on a single branch. Phase 1 migrates all 9 csproj uniformly, then runs one solution-wide fix-build-fix loop with `TreatWarningsAsErrors=true` until the build is green. Phase 2 walks each engine-core file group against a fixed 12-category checklist and appends findings to one audit markdown. Launcher + Plugins are migrated and compile-clean but receive no audit.

**Tech Stack:** .NET 10 SDK 10.0.203 (installed at `C:\Program Files\dotnet\dotnet.exe`), MSBuild via `dotnet build`, Newtonsoft.Json 13.0.3, ClickableTransparentOverlay 11.1.0, Coroutine 2.1.5, ProcessMemoryUtilities.Net 1.3.4, AsmResolver.PE.Win32Resources 5.5.1 (Launcher), ImGui.NET 1.91.6.1 + System.Linq.Dynamic.Core 1.7.1 (plugins). Spec: `docs/superpowers/specs/2026-04-27-gamehelper-net10-migration-and-audit-design.md`.

**Build-command bug found in pre-flight (Task 1):** `dotnet build` MUST NOT pass `-p:Platform=x64`. Several csproj declare `<OutputType>` only inside `'Configuration|Platform'=='X|AnyCPU'` `PropertyGroup`s; passing `-p:Platform=x64` makes those groups not match, leading to errors like CS2017. Use `dotnet build GameOverlay.sln -c <Config> --no-restore` (no `-p:Platform=…`) — `AnyCPU` is the default and the csproj emit x64 binaries via the conditional `<PlatformTarget>` declarations.

---

## Working directory

**ALL commands run from `C:\Users\D\Desktop\GameHelper2` unless noted otherwise.** A different repo is the operator's CWD; the agent must explicitly `cd` into `C:\Users\D\Desktop\GameHelper2` (or pass it as the working dir to `dotnet`/`git`).

Verify before starting:
```bash
cd /c/Users/D/Desktop/GameHelper2
pwd                                # → /c/Users/D/Desktop/GameHelper2
dotnet --list-sdks                 # must include 10.0.203
git status --short                 # should be clean (only docs/ from prior brainstorm)
```

If `git status` shows unexpected modifications, stop and report. Do not start migration on a dirty tree.

---

## Migration boundary rule (applies across Phase 1)

For every compiler/analyzer warning encountered:

- **Mechanical fix allowed when** the original code unambiguously assumed a specific value (e.g. dereferencing a property the surrounding logic proves non-null). Examples: add `?` to a return type whose body has `return null;`, add `!` after a property access guarded by an `if (x != null)`, add `using System.Drawing;` if missing, fix obvious P/Invoke marshaling annotations the analyzer asks for.

- **In `GameHelper/` or `GameOffsets/` (audit-in-scope), defer to audit** when fixing requires a logic decision (e.g. "this could be null but the caller assumes not — what should we do on null?"). Action: silence the warning at the call site with `#pragma warning disable <CODE>` ... `#pragma warning restore <CODE>` and add a `// TODO: see audit F-XXX` comment. The corresponding finding **must** be added to the audit doc in Phase 2 with the matching ID.

- **In `Launcher/` or `Plugins/` (audit-out-of-scope), escalate** when fixing requires a logic decision. There is no audit doc to point to, and silently silencing warnings in non-audited code creates hidden tech debt the user can't easily find later. Action: report `BLOCKED` to the controller with the specific file:line, the warning code, and the question. The controller asks the user. Do NOT use `#pragma warning disable` here without explicit user approval.

Track silenced-warning sites in `GameHelper/` + `GameOffsets/` as you create them (a running list in your scratch). Phase 2 cross-references them.

---

## File Structure

### Phase 1 modifies (all 9 csproj)

| File | Change |
|------|--------|
| `GameHelper/GameHelper.csproj` | TFM → `net10.0-windows`, add `<Nullable>enable</Nullable>`, `<LangVersion>latest</LangVersion>`, hoist `NoWarn 1591` to top-level |
| `GameOffsets/GameOffsets.csproj` | Same |
| `Launcher/Launcher.csproj` | Same |
| `Plugins/AutoHotKeyTrigger/AutoHotKeyTrigger.csproj` | Same |
| `Plugins/HealthBars/HealthBars.csproj` | Same |
| `Plugins/PreloadAlert/PreloadAlert.csproj` | Same |
| `Plugins/Radar/Radar.csproj` | Same |
| `Plugins/SamplePluginTemplate/Changeme/Changeme.csproj` | Same |
| `Plugins/WorldDrawing/WorldDrawing.csproj` | Same |
| Source files anywhere in the solution | Mechanical compiler/analyzer fixes only |

### Phase 1 creates

(Nothing new.)

### Phase 2 creates

| File | Responsibility |
|------|----------------|
| `docs/audit/2026-04-27-bug-audit.md` | Categorized findings, severity-sorted, with index. **Engine-core only** — no entries for Launcher or Plugins. |

### Audit-out-of-scope (NOT audited; receive only mechanical migration cleanup)

- `Launcher/` (the launcher Exe project)
- `Plugins/` (the 6 user-loadable plugin projects)
  - The host-side `GameHelper/Plugin/` (inside the GameHelper csproj) IS audit-in-scope.

### Out of scope entirely (do NOT modify)

- `GameOverlay.sln` (no edits required; csproj TFM changes are picked up automatically)
- `.editorconfig`
- `Directory.Build.props` (already pins `RuntimeIdentifier=win-x64` correctly)

---

# Phase 0 — Pre-flight

## Task 1: Verify baseline build on net8.0

**Files:** none (verification only)

- [ ] **Step 1: Confirm working directory and SDK availability**

```bash
cd /c/Users/D/Desktop/GameHelper2
pwd
dotnet --list-sdks
```

Expected: `pwd` ends in `GameHelper2`. `dotnet --list-sdks` includes `8.0.x`, `9.0.x`, `10.0.203`.

- [ ] **Step 2: Restore current dependencies on net8.0**

```bash
cd /c/Users/D/Desktop/GameHelper2
dotnet restore GameOverlay.sln
```

Expected: "Restore complete" with 0 errors. Warnings about `Launcher` or `Plugins` are acceptable — those are out of scope.

- [ ] **Step 3: Build current state on net8.0 (baseline)**

```bash
cd /c/Users/D/Desktop/GameHelper2
dotnet build GameHelper/GameHelper.csproj -c Debug --no-restore 2>&1 | tee /tmp/baseline-build.log
```

(NOTE: do NOT pass `-p:Platform=x64`. That makes MSBuild evaluate the conditional `<PropertyGroup>` blocks against `Platform==x64` — but the csproj declares `<OutputType>` only inside `Platform==AnyCPU` groups, so passing `x64` produces CS2017 errors. The csproj emit x64 binaries via the `<PlatformTarget>x64</PlatformTarget>` declarations inside the AnyCPU-conditional groups; build with default `Platform=AnyCPU`.)

Expected: "Build succeeded" with 0 errors. Note the warning count (record in your notes; it's the baseline). If errors, **stop the plan** — the spec assumes net8.0 builds clean. Report failures back to the user.

- [ ] **Step 4: Tag the pre-migration state for safety**

```bash
cd /c/Users/D/Desktop/GameHelper2
git tag pre-net10-migration
```

Expected: silent success. Verify with `git tag --list pre-net10-migration` → prints the tag.

This tag is a rollback anchor. Do not push it.

- [ ] **Step 5: Mark Phase 0 complete (no commit)**

No code changed; nothing to commit.

---

# Phase 1 — Migration

**Important:** plugins (under `Plugins/`) `<ProjectReference>` `GameHelper.csproj`. If GameHelper migrates to net10 while plugins stay on net8, plugin builds break (a net8 lib cannot consume a net10 reference). Therefore the migration must touch all 9 csproj **in a single commit** — there is no clean intermediate state.

## Task 2: Migrate all 9 csproj to `net10.0-windows`

**Files:**
- Modify: `GameOffsets/GameOffsets.csproj`
- Modify: `GameHelper/GameHelper.csproj`
- Modify: `Launcher/Launcher.csproj`
- Modify: `Plugins/AutoHotKeyTrigger/AutoHotKeyTrigger.csproj`
- Modify: `Plugins/HealthBars/HealthBars.csproj`
- Modify: `Plugins/PreloadAlert/PreloadAlert.csproj`
- Modify: `Plugins/Radar/Radar.csproj`
- Modify: `Plugins/SamplePluginTemplate/Changeme/Changeme.csproj`
- Modify: `Plugins/WorldDrawing/WorldDrawing.csproj`

For every csproj, perform the following operations:
1. Change `<TargetFramework>net8.0</TargetFramework>` to `<TargetFramework>net10.0-windows</TargetFramework>`.
2. Add `<Nullable>enable</Nullable>` and `<LangVersion>latest</LangVersion>` to the top-level `PropertyGroup`.
3. If the csproj has `<GenerateDocumentationFile>true</GenerateDocumentationFile>` (which generates CS1591 warnings for missing XML doc), and `<NoWarn>` does NOT yet appear in the top-level `PropertyGroup`: add `<NoWarn>1701;1702; 1591</NoWarn>` to the top-level `PropertyGroup`. If `<NoWarn>` already exists in a conditional `PropertyGroup` (Debug/Release), hoist it to top-level so it covers Release too.

These three operations are applied uniformly. The order of `PropertyGroup` elements should match the original file's style; do not reformat unrelated areas.

- [ ] **Step 1: Read every csproj first**

```bash
for f in \
  /c/Users/D/Desktop/GameHelper2/GameOffsets/GameOffsets.csproj \
  /c/Users/D/Desktop/GameHelper2/GameHelper/GameHelper.csproj \
  /c/Users/D/Desktop/GameHelper2/Launcher/Launcher.csproj \
  /c/Users/D/Desktop/GameHelper2/Plugins/AutoHotKeyTrigger/AutoHotKeyTrigger.csproj \
  /c/Users/D/Desktop/GameHelper2/Plugins/HealthBars/HealthBars.csproj \
  /c/Users/D/Desktop/GameHelper2/Plugins/PreloadAlert/PreloadAlert.csproj \
  /c/Users/D/Desktop/GameHelper2/Plugins/Radar/Radar.csproj \
  /c/Users/D/Desktop/GameHelper2/Plugins/SamplePluginTemplate/Changeme/Changeme.csproj \
  /c/Users/D/Desktop/GameHelper2/Plugins/WorldDrawing/WorldDrawing.csproj; do
  echo "=== $f ===";
  cat "$f";
done
```

Sanity-check what's there before editing.

- [ ] **Step 2: Replace `GameOffsets/GameOffsets.csproj`**

Write the entire file as:

```xml
<Project Sdk="Microsoft.NET.Sdk">

    <PropertyGroup>
        <TargetFramework>net10.0-windows</TargetFramework>
        <Nullable>enable</Nullable>
        <LangVersion>latest</LangVersion>
        <NoWarn>1701;1702; 1591</NoWarn>
        <RunAnalyzersDuringBuild>false</RunAnalyzersDuringBuild>
    </PropertyGroup>

    <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Debug|AnyCPU'">
        <PlatformTarget>x64</PlatformTarget>
    </PropertyGroup>

    <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Release|AnyCPU'">
        <PlatformTarget>x64</PlatformTarget>
    </PropertyGroup>

</Project>
```

- [ ] **Step 3: Replace `GameHelper/GameHelper.csproj`**

Write the entire file as:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <NoWarn>1701;1702; 1591</NoWarn>
    <StartupObject>GameHelper.Program</StartupObject>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <Authors>gamehelper</Authors>
    <Description>An overlay to help play the game.</Description>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Debug|AnyCPU'">
    <OutputType>Exe</OutputType>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <WarningsAsErrors>;NU1605</WarningsAsErrors>
    <PlatformTarget>x64</PlatformTarget>
  </PropertyGroup>

  <PropertyGroup Condition="'$(Configuration)|$(Platform)'=='Release|AnyCPU'">
    <OutputType>WinExe</OutputType>
    <PlatformTarget>x64</PlatformTarget>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="ClickableTransparentOverlay" Version="11.1.0" />
    <PackageReference Include="Coroutine" Version="2.1.5" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="ProcessMemoryUtilities.Net" Version="1.3.4" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\GameOffsets\GameOffsets.csproj" />
  </ItemGroup>

  <Target Name="DeleteDebugSymbolFiles" AfterTargets="Build" Condition="'$(Configuration)|$(Platform)'=='Release|AnyCPU'">
    <Delete Files="$(OutDir)GameHelper.xml;$(OutDir)GameHelper.runtimeconfig.dev.json;$(OutDir)GameOffsets.pdb;" />
  </Target>

  <Target Name="RemoveDirectories" AfterTargets="Build">
    <RemoveDir Directories="$(OutDir)runtimes\win-x86;$(OutDir)runtimes\osx;$(OutDir)runtimes\linux-x64;" />
  </Target>

  <Target Name="CopyDocumentationFiles" AfterTargets="Build">
    <Copy SourceFiles="$(SolutionDir)README.md" DestinationFolder="$(OutDir)" Condition="Exists('$(SolutionDir)README.md')" />
    <Copy SourceFiles="$(SolutionDir)LICENSE" DestinationFolder="$(OutDir)" Condition="Exists('$(SolutionDir)LICENSE')" />
  </Target>

</Project>
```

- [ ] **Step 4: Edit `Launcher/Launcher.csproj`**

Edit the existing file in place (preserve its post-build `<Target>` blocks). Inside the top-level `<PropertyGroup>`:
- Change `<TargetFramework>net8.0</TargetFramework>` → `<TargetFramework>net10.0-windows</TargetFramework>`
- After the `<TargetFramework>` line, add `<Nullable>enable</Nullable>` and `<LangVersion>latest</LangVersion>`
- Launcher does NOT have `<GenerateDocumentationFile>` set, so the NoWarn hoist isn't required. Skip step 3 of the uniform operations for this csproj.

- [ ] **Step 5: Edit each plugin csproj**

Apply all three uniform operations to each of these 6 files:
- `Plugins/AutoHotKeyTrigger/AutoHotKeyTrigger.csproj`
- `Plugins/HealthBars/HealthBars.csproj`
- `Plugins/PreloadAlert/PreloadAlert.csproj`
- `Plugins/Radar/Radar.csproj`
- `Plugins/SamplePluginTemplate/Changeme/Changeme.csproj`
- `Plugins/WorldDrawing/WorldDrawing.csproj`

Each plugin csproj has `<GenerateDocumentationFile>true</GenerateDocumentationFile>`, so the `NoWarn 1591` hoist IS required for each. Add `<NoWarn>1701;1702; 1591</NoWarn>` to the top-level `<PropertyGroup>` of each plugin csproj.

Preserve any plugin-specific elements (`<EnableDynamicLoading>`, `<PlatformTarget>` in the conditional groups, `<ItemGroup>` with `<ProjectReference>` and `<PackageReference>`, post-build `<Target>` blocks) verbatim.

- [ ] **Step 6: Restore on the new TFM (whole solution)**

```bash
cd /c/Users/D/Desktop/GameHelper2
dotnet restore GameOverlay.sln
```

Expected: "Restore complete" with 0 errors. NU1701/NU1702 may appear (e.g. `ClickableTransparentOverlay` published as net8.0); these are forward-compatibility informational warnings — acceptable.

- [ ] **Step 7: Sanity check — solution builds (errors expected, warnings counted)**

```bash
cd /c/Users/D/Desktop/GameHelper2
dotnet build GameOverlay.sln -c Debug --no-restore 2>&1 | tee /tmp/post-migrate-build.log
grep -cE 'error |warning ' /tmp/post-migrate-build.log
```

The build may **fail** at this step — that's acceptable. Nullable annotation errors are expected because `Nullable=enable` is freshly turned on. Record:
- The total error/warning count.
- Whether the failure is in `GameHelper`/`GameOffsets` (audit-in-scope) or in `Launcher`/`Plugins` (audit-out-of-scope).

We will fix these in Task 4. Don't fix anything yet.

- [ ] **Step 8: Commit the migration**

```bash
cd /c/Users/D/Desktop/GameHelper2
git add GameOffsets/GameOffsets.csproj GameHelper/GameHelper.csproj \
        Launcher/Launcher.csproj \
        Plugins/AutoHotKeyTrigger/AutoHotKeyTrigger.csproj \
        Plugins/HealthBars/HealthBars.csproj \
        Plugins/PreloadAlert/PreloadAlert.csproj \
        Plugins/Radar/Radar.csproj \
        Plugins/SamplePluginTemplate/Changeme/Changeme.csproj \
        Plugins/WorldDrawing/WorldDrawing.csproj
git status --short    # verify only csproj files staged
git commit -m "chore(net): migrate all 9 csproj to .NET 10 LTS (net10.0-windows)

- TargetFramework: net8.0 → net10.0-windows uniformly across the solution
  (GameHelper, GameOffsets, Launcher, and 6 Plugins).
- Enable Nullable reference types and LangVersion=latest everywhere.
- Hoist NoWarn 1591 (missing XML doc) to top-level PropertyGroup on
  csproj that have GenerateDocumentationFile=true, so the warning is
  also suppressed in Release once Task 3 turns on WAE.
- Migration is single-commit because plugins ProjectReference GameHelper;
  any partial state has broken plugin builds (TFM mismatch)."
```

Expected: commit succeeds. Verify with `git log -1 --oneline` → prints the commit subject.

## Task 3: Enable warnings-as-errors across all 9 csproj

This task applies WAE uniformly so the cleanup loop in Task 4 surfaces every warning as a hard error.

**Files:**
- Modify: all 9 csproj listed in Task 2.

For each csproj, add `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` to the top-level `<PropertyGroup>`. (Top-level — not per-configuration — because we want it to apply to both Debug and Release uniformly during cleanup.)

For `GameHelper.csproj` specifically, the existing `<TreatWarningsAsErrors>false</TreatWarningsAsErrors>` line lives in the Debug `<PropertyGroup>`. Leave that line alone (it'll be inert because top-level `true` wins for Release; for Debug, MSBuild evaluation order means whichever `<PropertyGroup>` parses last wins. To avoid ambiguity, change the Debug `false` line to `true` as well, OR remove it. Easier: change it to `true` to match top-level intent.)

- [ ] **Step 1: Edit GameHelper.csproj (top-level + Debug override)**

Add to the top-level `<PropertyGroup>` (after `<GenerateDocumentationFile>true</GenerateDocumentationFile>`):
```xml
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

In the Debug `<PropertyGroup>`, change:
```xml
<TreatWarningsAsErrors>false</TreatWarningsAsErrors>
```
to:
```xml
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

- [ ] **Step 2: Edit the other 8 csproj**

For each of:
- `GameOffsets/GameOffsets.csproj`
- `Launcher/Launcher.csproj`
- 6 plugin csproj

Add `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` to the top-level `<PropertyGroup>` (after the existing `<NoWarn>` line where present, otherwise after `<LangVersion>` from Task 2).

- [ ] **Step 3: Run a discovery build to capture the warning surface**

```bash
cd /c/Users/D/Desktop/GameHelper2
dotnet build GameOverlay.sln -c Debug --no-restore 2>&1 | tee /tmp/wae-discovery.log
```

Expected: build **fails** with errors (the warnings are now errors). This is intended — we're collecting the work surface.

Count the unique error codes (Windows-friendly grep):
```bash
grep -oE 'CS[0-9]+|CA[0-9]+|Nullable warning' /tmp/wae-discovery.log | sort -u
```

Note the codes and rough counts in your scratch. Common expected codes:
- `CS8600`/`CS8601`/`CS8602`/`CS8604`/`CS8618` — nullability
- `CS0114`/`CS0108` — overrides/hides
- `CA1416` — platform compatibility (should be silent now since target is `-windows`)
- `CS1591` — missing XML doc (suppressed via NoWarn 1591 already in csproj that have GenerateDocumentationFile)

Also note where errors cluster: GameHelper/GameOffsets (audit-in-scope) vs. Launcher/Plugins (audit-out-of-scope). Helpful for routing fixes/deferrals during Task 4.

- [ ] **Step 4: Commit the WAE flip (no source changes yet)**

```bash
cd /c/Users/D/Desktop/GameHelper2
git add GameOffsets/GameOffsets.csproj GameHelper/GameHelper.csproj \
        Launcher/Launcher.csproj \
        Plugins/AutoHotKeyTrigger/AutoHotKeyTrigger.csproj \
        Plugins/HealthBars/HealthBars.csproj \
        Plugins/PreloadAlert/PreloadAlert.csproj \
        Plugins/Radar/Radar.csproj \
        Plugins/SamplePluginTemplate/Changeme/Changeme.csproj \
        Plugins/WorldDrawing/WorldDrawing.csproj
git commit -m "chore(net): enable warnings-as-errors during migration cleanup

Temporary; will be reverted in a later commit after the mechanical
fix pass leaves the whole-solution build green."
```

## Task 4: Mechanical cleanup loop (whole solution)

**Files:** any source under any of the 9 in-scope csproj that the WAE-enabled build flags as warning. The boundary rule splits behavior:
- `GameHelper/` + `GameOffsets/` (audit-in-scope): mechanical fix OR `#pragma + // TODO: see audit F-XXX`.
- `Launcher/` + `Plugins/` (audit-out-of-scope): mechanical fix OR escalate `BLOCKED` to controller.

This task is iterative. Repeat the inner loop until the whole-solution build is 0 errors / 0 warnings.

- [ ] **Step 1: Run build to surface the next batch of errors**

```bash
cd /c/Users/D/Desktop/GameHelper2
dotnet build GameOverlay.sln -c Debug --no-restore 2>&1 | tee /tmp/cleanup-iter.log
```

If the build succeeds with 0 errors / 0 warnings, jump to Step 6.

- [ ] **Step 2: Pick the next file with errors**

Take the first file path reported in the log. Read it (`Read` tool, full file). Read the surrounding logic enough to understand whether each warning is mechanically fixable or needs deferral per the boundary rule above.

- [ ] **Step 3: Apply fixes**

For each warning in that file, classify and apply:

| Code | Mechanical fix |
|------|----------------|
| `CS8600` (literal-null to non-null type) | Change target type to `T?` if the field/parameter is genuinely nullable; otherwise add `??` default or initialize. |
| `CS8601` (possible null assignment) | If source can be null and target should accept it, mark target `T?`. If target is required non-null, decide per boundary rule. |
| `CS8602` (dereference of possibly-null reference) | If a guard exists upstream, add `!`. If not, add a guard (`if (x is null) return;` or `?.`) — only if the surrounding logic supports skipping. Else defer. |
| `CS8603` (possible null return) | Mark return type `T?` if returning null is intentional; else investigate. |
| `CS8604` (possible null arg) | Same logic as CS8602 at the call site. |
| `CS8618` (uninitialized non-nullable field) | Initialize at declaration (`= new()`, `= ""`, `= null!` for late-init), or mark `?`, or constructor-init. Pick the option that matches existing code shape. |
| `CS8625` (literal null to non-nullable parameter) | Mark parameter `T?` or fix the call site. |
| `CS0114`/`CS0108` (hide vs override) | Add `new` or `override` keyword based on the inheritance intent. Read the base class to decide. |
| Other CS warnings | Apply standard guidance; if non-trivial, defer per boundary rule. |
| `CA*` analyzer warnings | `GameOffsets.csproj` has `RunAnalyzersDuringBuild=false` so `CA*` won't appear there. Other csproj have analyzers enabled — handle as in this table. |

For deferrals **in `GameHelper/` or `GameOffsets/`** (audit-in-scope), use:
```csharp
#pragma warning disable CS8602 // TODO: see audit F-XXX
var v = maybeNull.Property;
#pragma warning restore CS8602
```
Append a one-liner to your scratch list: `F-XXX | path/to/file.cs:lineNumber | CS8602 | <one-line summary>`. You will populate the audit doc with these in Phase 2.

For warnings **in `Launcher/` or `Plugins/`** (audit-out-of-scope) that need a logic decision, **report `BLOCKED` to the controller** with the file path, line, warning code, and the question. Do not silently `#pragma`-disable in non-audited code.

- [ ] **Step 4: Re-run build, confirm error count dropped**

```bash
cd /c/Users/D/Desktop/GameHelper2
dotnet build GameOverlay.sln -c Debug --no-restore 2>&1 | grep -cE 'error |warning '
```

Expected: number is strictly lower than the previous iteration. If it isn't, your fix introduced new issues — examine the new errors and either back out or extend the fix.

- [ ] **Step 5: Loop**

Return to Step 1. Continue until Step 1 reports 0 errors / 0 warnings on `Debug`.

- [ ] **Step 6: Verify Release also builds clean (whole solution)**

```bash
cd /c/Users/D/Desktop/GameHelper2
dotnet build GameOverlay.sln -c Release --no-restore 2>&1 | tee /tmp/cleanup-release.log
grep -cE 'error |warning ' /tmp/cleanup-release.log
```

Expected: 0 errors and 0 warnings. If Release surfaces additional issues (rare; usually due to `OutputType` differences or Release-only targets), apply the same fix loop until clean.

- [ ] **Step 7: Commit the fix pass (one commit, may be split if very large)**

```bash
cd /c/Users/D/Desktop/GameHelper2
git add GameHelper/ GameOffsets/ Launcher/ Plugins/
git status --short    # verify only source files in scope are staged
git commit -m "fix(net10): resolve compiler warnings from net10 migration

Mechanical fixes for the warning surface that appeared after enabling
Nullable across all 9 csproj. Logic-decision warnings in audit-in-scope
code (GameHelper, GameOffsets) were deferred behind #pragma disables
with TODOs pointing at audit IDs to be filled in by Phase 2.
Logic-decision warnings in non-audited code (Launcher, Plugins) were
either resolved mechanically or escalated case-by-case."
```

If the diff is unwieldy (>30 files, hard to review), split by area:
```bash
git reset                     # unstage all
git add GameOffsets/
git commit -m "fix(net10): resolve nullable warnings in GameOffsets"
git add GameHelper/RemoteObjects/
git commit -m "fix(net10): resolve nullable warnings in GameHelper/RemoteObjects"
git add Launcher/
git commit -m "fix(net10): resolve nullable warnings in Launcher"
git add Plugins/Radar/
git commit -m "fix(net10): resolve nullable warnings in Plugins/Radar"
# ... etc, until clean
```

Each split commit must leave the whole-solution build green (re-run `dotnet build GameOverlay.sln` between commits).

## Task 5: Restore non-WAE state for normal development

**Files:** all 9 csproj that received `TreatWarningsAsErrors=true` in Task 3.

- [ ] **Step 1: Revert WAE in every csproj**

For each of the 9 csproj:
- `GameHelper/GameHelper.csproj`: revert top-level `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` to `false` and revert the Debug-block `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` back to `<TreatWarningsAsErrors>false</TreatWarningsAsErrors>` (matches original).
- `GameOffsets/GameOffsets.csproj`, `Launcher/Launcher.csproj`, and the 6 plugin csproj: remove the top-level `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` line that Task 3 Step 2 added. The originals had no such line.

Keep `<Nullable>enable</Nullable>` and `<LangVersion>latest</LangVersion>` everywhere — those stay.

- [ ] **Step 2: Verify build still green without WAE (whole solution)**

```bash
cd /c/Users/D/Desktop/GameHelper2
dotnet build GameOverlay.sln -c Debug --no-restore 2>&1 | tail -10
dotnet build GameOverlay.sln -c Release --no-restore 2>&1 | tail -10
```

Expected: both end with "Build succeeded" and `0 Warning(s)` `0 Error(s)`. (Without WAE, warnings would not error — but since we already drove the count to zero in Task 4, they should remain zero.)

- [ ] **Step 3: Commit**

```bash
cd /c/Users/D/Desktop/GameHelper2
git add GameOffsets/GameOffsets.csproj GameHelper/GameHelper.csproj \
        Launcher/Launcher.csproj \
        Plugins/AutoHotKeyTrigger/AutoHotKeyTrigger.csproj \
        Plugins/HealthBars/HealthBars.csproj \
        Plugins/PreloadAlert/PreloadAlert.csproj \
        Plugins/Radar/Radar.csproj \
        Plugins/SamplePluginTemplate/Changeme/Changeme.csproj \
        Plugins/WorldDrawing/WorldDrawing.csproj
git commit -m "chore(net): relax warnings-as-errors post-migration

Keeps Nullable=enable for ongoing protection across all 9 csproj,
removes the temporary WAE flag used during the migration cleanup
pass. Future warnings will not block builds but remain visible."
```

---

# Phase 2 — Manual Audit

The audit produces ONE markdown document, populated incrementally across the next several tasks, then committed once at the end.

## Audit checklist (apply to every file in every audit task)

For each source file you read in Phase 2, scan for items in these 12 categories. **Skip any category that doesn't apply** to the file (e.g. an enum file rarely has concurrency findings) — but explicitly think through each category before moving on.

1. **Concurrency / coroutine sync** — shared mutable state across coroutines, infinite `while(true)` without cancellation, multi-consumer events, non-atomic compound operations.
2. **Process / memory handle lifecycle** — `SafeMemoryHandle` leaks, double-`Open`, missed `Close`/`Dispose`, races between handle teardown and consumers.
3. **Exception handling** — silent `catch { }`, `catch (Exception)` without rethrow/log, return values that conflate "no data" with "error".
4. **Resource lifecycle / Dispose** — classes owning unmanaged state without `IDisposable`, asymmetric `using`, missing finalizers where needed.
5. **P/Invoke correctness** — missing `SetLastError`, missing `CharSet`, calling-convention mismatches, `IntPtr` where `SafeHandle` would be safer.
6. **Logic bugs** — `private set { }` (effectively read-only with confusing setter), unreachable branches, dead code, off-by-one in offset arithmetic.
7. **Culture / globalization** — `string.ToLower()` / `ToUpper()` without `Invariant`, parsing without `CultureInfo.InvariantCulture`.
8. **Performance in hot loops** — reflection per frame, `Process.GetProcesses()` on a tight schedule, allocations/boxing in 60fps coroutine bodies.
9. **API misuse** — `Process.MainModule.*` without elevation/exit guards, `MainWindowTitle` after `HasExited`, reading from a closed handle.
10. **Coroutine semantics** — multi-consumer events, missing tear-down on `OnClose`, `while(true)` without `yield break` paths.
11. **Settings / IO** — relative paths that depend on CWD, JSON parse without `try`, save races.
12. **GameOffsets struct correctness** — `[StructLayout(Pack=...)]` / sizes / alignment, `unsafe` block invariants, `fixed` buffer bounds.

For each finding, append to the audit doc under the matching category section using this template:

```markdown
### F-NNN — Short title

- **File:** `relative/path.cs:lineStart-lineEnd`
- **Severity:** critical | high | medium | low | nit
- **Category:** <one of the 12 above>
- **Description:** What the code does today.
- **Why it's a bug:** What goes wrong, under what conditions.
- **Suggested fix:** Concrete change.
- **Risk if left:** What user-visible effect persists if we don't fix.
```

**Severity rubric:**
- **critical** — crash, hang, memory corruption, data loss in a normal session.
- **high** — race, leak, or wrong-behavior triggered in everyday use, no crash.
- **medium** — incorrect behavior in edge cases (specific game states, locales, multi-monitor, etc.).
- **low** — defensive-coding or hygiene issue, no observable user impact.
- **nit** — purely cosmetic.

`F-NNN` is a zero-padded sequential ID assigned in order of discovery (F-001, F-002, …). When you create a deferred-warning `#pragma` in Phase 1 with a placeholder ID, fix the ID here so the placeholder matches.

## Task 6: Create audit document skeleton

**Files:**
- Create: `docs/audit/2026-04-27-bug-audit.md`

- [ ] **Step 1: Create the directory**

```bash
mkdir -p /c/Users/D/Desktop/GameHelper2/docs/audit
```

- [ ] **Step 2: Create the audit doc skeleton**

Write to `docs/audit/2026-04-27-bug-audit.md`:

```markdown
# GameHelper2 — Bug Audit (2026-04-27)

**Scope:** `GameHelper/` and `GameOffsets/` projects.
**Out of scope:** `Launcher/`, `Plugins/`.
**Source baseline:** post `chore(net): relax warnings-as-errors post-migration` commit.

## Summary

| Severity | Count |
|----------|-------|
| critical | 0 |
| high     | 0 |
| medium   | 0 |
| low      | 0 |
| nit      | 0 |
| **Total**| **0** |

Coverage: 0 / 134 source files audited.

## Index

(Populated as findings are added. Format: `F-NNN — title (severity, file)`.)

## Findings

### Concurrency / coroutine sync

(none)

### Process / memory handle lifecycle

(none)

### Exception handling

(none)

### Resource lifecycle / Dispose

(none)

### P/Invoke correctness

(none)

### Logic bugs

(none)

### Culture / globalization

(none)

### Performance in hot loops

(none)

### API misuse

(none)

### Coroutine semantics

(none)

### Settings / IO

(none)

### GameOffsets struct correctness

(none)
```

- [ ] **Step 3: Do not commit yet**

The audit doc accumulates across Tasks 7–16 and is committed once at Task 17.

## Task 7: Audit GameOffsets — Natives, Pattern, root files

**Files (read-only):**
- `GameOffsets/Natives/StdBucket.cs`
- `GameOffsets/Natives/StdList.cs`
- `GameOffsets/Natives/StdMap.cs`
- `GameOffsets/Natives/StdString.cs`
- `GameOffsets/Natives/StdTuple2D.cs`
- `GameOffsets/Natives/StdTuple3D.cs`
- `GameOffsets/Natives/StdVector.cs`
- `GameOffsets/Natives/StdWString.cs`
- `GameOffsets/Natives/Util.cs`
- `GameOffsets/Pattern.cs`
- `GameOffsets/StaticOffsetsPatterns.cs`
- `GameOffsets/GameProcessName.cs`

- [ ] **Step 1: Read each file in turn (full file), apply the 12-category checklist**

For each file, walk the categories. Pay extra attention to:
- **Category 12 (struct correctness):** verify `[StructLayout(LayoutKind.Sequential, Pack = ...)]` matches expected size, no implicit padding bugs.
- **Category 5 (P/Invoke):** even though these are mostly POCO structs, check any `unsafe` or marshal attributes.
- **Category 8 (perf):** `StdMap`/`StdVector` traversal patterns may iterate in hot paths.

- [ ] **Step 2: Append findings to `docs/audit/2026-04-27-bug-audit.md`**

For each finding, append under the appropriate category section using the F-NNN template.

- [ ] **Step 3: Update coverage counter**

In the Summary section, change `Coverage: X / 134` to reflect this group (after this task: `12 / 134`).

- [ ] **Step 4: Update severity counts and Index**

Increment the relevant severity rows. Add each `F-NNN — title (severity, file)` to the Index section in numeric order.

## Task 8: Audit GameOffsets — Objects (excluding nested)

**Files (read-only):**
- `GameOffsets/Objects/AreaChangeOffset.cs`
- `GameOffsets/Objects/GameStateOffsets.cs`
- `GameOffsets/Objects/InGameStateOffset.cs`
- `GameOffsets/Objects/LoadedFilesOffset.cs`

- [ ] **Step 1: Read and audit each file (12-category checklist)**
- [ ] **Step 2: Append findings**
- [ ] **Step 3: Update coverage counter** (after this task: `16 / 134`)
- [ ] **Step 4: Update severity counts and Index**

## Task 9: Audit GameOffsets — Components, FilesStructures, States, UiElement

**Files (read-only):** all 18 component offsets, 4 FilesStructures, 7 States (incl. nested InGameState/), 3 UiElement.

```
GameOffsets/Objects/Components/Actor.cs
GameOffsets/Objects/Components/Animated.cs
GameOffsets/Objects/Components/Buffs.cs
GameOffsets/Objects/Components/Charges.cs
GameOffsets/Objects/Components/Chest.cs
GameOffsets/Objects/Components/ComponentHeader.cs
GameOffsets/Objects/Components/Life.cs
GameOffsets/Objects/Components/ModsAndObjectMagicProperties.cs
GameOffsets/Objects/Components/Player.cs
GameOffsets/Objects/Components/Positioned.cs
GameOffsets/Objects/Components/Render.cs
GameOffsets/Objects/Components/Shrine.cs
GameOffsets/Objects/Components/StackOffsets.cs
GameOffsets/Objects/Components/StateMachine.cs
GameOffsets/Objects/Components/Stats.cs
GameOffsets/Objects/Components/Targetable.cs
GameOffsets/Objects/Components/Transitionable.cs
GameOffsets/Objects/Components/TriggerableBlockage.cs
GameOffsets/Objects/FilesStructures/BuffDefinitionsOffset.cs
GameOffsets/Objects/FilesStructures/GrantedEffectsDatOffset.cs
GameOffsets/Objects/FilesStructures/GrantedEffectsPerLevelDatOffset.cs
GameOffsets/Objects/FilesStructures/WorldAreaDatOffsets.cs
GameOffsets/Objects/States/AreaLoadingStateOffset.cs
GameOffsets/Objects/States/InGameState/AreaInstanceOffsets.cs
GameOffsets/Objects/States/InGameState/EntityOffsets.cs
GameOffsets/Objects/States/InGameState/ImportantUiElementsOffsets.cs
GameOffsets/Objects/States/InGameState/InventoryOffset.cs
GameOffsets/Objects/States/InGameState/ServerDataOffset.cs
GameOffsets/Objects/States/InGameState/WorldDataOffset.cs
GameOffsets/Objects/UiElement/MapUiElement.cs
GameOffsets/Objects/UiElement/SkillTreeNodeUiElement.cs
GameOffsets/Objects/UiElement/UiElementBaseOffset.cs
```

- [ ] **Step 1: Read and audit each file (12-category checklist)**
- [ ] **Step 2: Append findings**
- [ ] **Step 3: Update coverage counter** (after this task: `48 / 134` — finishes GameOffsets)
- [ ] **Step 4: Update severity counts and Index**

## Task 10: Audit GameHelper — Utils + Cache + CoroutineEvents

**Files (read-only):**
```
GameHelper/Utils/ImGuiHelper.cs
GameHelper/Utils/JsonHelper.cs
GameHelper/Utils/MathHelper.cs
GameHelper/Utils/MiscHelper.cs
GameHelper/Utils/PatternFinder.cs
GameHelper/Utils/RemoteObjectPropertyDetail.cs
GameHelper/Utils/SafeMemoryHandle.cs
GameHelper/Cache/DisappearingEntity.cs
GameHelper/Cache/GgpkAddresses.cs
GameHelper/Cache/UiElementParents.cs
GameHelper/CoroutineEvents/GameHelperEvents.cs
GameHelper/CoroutineEvents/HybridEvents.cs
GameHelper/CoroutineEvents/RemoteEvents.cs
```

- [ ] **Step 1: Read and audit each file (12-category checklist)**

Pay extra attention to:
- `SafeMemoryHandle.cs` — categories 2 (handle lifecycle), 5 (P/Invoke), 4 (Dispose).
- `JsonHelper.cs` — category 11 (settings/IO), category 3 (exception handling on parse).
- `PatternFinder.cs` — category 8 (performance), category 9 (API misuse on memory reads).
- `Cache/*` — category 1 (concurrency, since caches are shared across coroutines).
- `CoroutineEvents/*` — categories 1 and 10 (coroutine semantics, multi-consumer events).

- [ ] **Step 2: Append findings**
- [ ] **Step 3: Update coverage counter** (after this task: `61 / 134`)
- [ ] **Step 4: Update severity counts and Index**

## Task 11: Audit GameHelper — Core, GameProcess, GameOverlay, Program

**Files (read-only):**
```
GameHelper/Core.cs
GameHelper/GameProcess.cs
GameHelper/GameOverlay.cs
GameHelper/Program.cs
```

These are the heart of the engine and likely produce the most findings.

- [ ] **Step 1: Read and audit each file (12-category checklist)**

Pay extra attention across all categories. Specifically:
- `GameProcess.cs` — categories 1, 2, 3, 9, 10. The `Pid` getter, `Address` getter with empty `private set`, `processesInfo` mutation across coroutines, `MainModule` access without exit guard, `Open()`/`Close()` symmetry.
- `Core.cs` — categories 1, 10. Multiple coroutines yielding on the same `OnStaticAddressFound` event, infinite `while(true)` loops, static singletons.
- `GameOverlay.cs` — categories 4 (Dispose), 8 (perf, render loop).
- `Program.cs` — category 11 (`File.AppendAllText("Error.log")` with relative path).

- [ ] **Step 2: Append findings**
- [ ] **Step 3: Update coverage counter** (after this task: `65 / 134`)
- [ ] **Step 4: Update severity counts and Index**

## Task 12: Audit GameHelper — Plugin (host-side loader, NOT user plugins)

**Files (read-only):**
```
GameHelper/Plugin/IPCore.cs
GameHelper/Plugin/IPSettings.cs
GameHelper/Plugin/PCore.cs
GameHelper/Plugin/PluginAssemblyLoadContext.cs
GameHelper/Plugin/PluginMetadata.cs
GameHelper/Plugin/PManager.cs
```

These are the host-side plugin loader inside the GameHelper project. The user-plugin DIRECTORY at the repo root (`Plugins/`) is out of scope — but this internal Plugin/ folder IS in scope.

- [ ] **Step 1: Read and audit each file (12-category checklist)**

Pay extra attention to:
- `PluginAssemblyLoadContext.cs` — categories 4 (assembly unload symmetry), 3 (exception handling on type discovery).
- `PManager.cs` — categories 1 (concurrency on plugin list), 3 (silent failures), 11 (settings IO).

- [ ] **Step 2: Append findings**
- [ ] **Step 3: Update coverage counter** (after this task: `71 / 134`)
- [ ] **Step 4: Update severity counts and Index**

## Task 13: Audit GameHelper — RemoteEnums + RemoteObjects (root + base + helpers)

**Files (read-only):**
```
GameHelper/RemoteEnums/Animation.cs
GameHelper/RemoteEnums/GameStateTypes.cs
GameHelper/RemoteEnums/GameStats.cs
GameHelper/RemoteEnums/InventoryName.cs
GameHelper/RemoteEnums/Rarity.cs
GameHelper/RemoteEnums/Entity/EntityFilterType.cs
GameHelper/RemoteEnums/Entity/EntityStates.cs
GameHelper/RemoteEnums/Entity/EntitySubtypes.cs
GameHelper/RemoteEnums/Entity/EntityTypes.cs
GameHelper/RemoteEnums/Entity/NearbyZones.cs
GameHelper/RemoteObjects/RemoteObjectBase.cs
GameHelper/RemoteObjects/AreaChangeCounter.cs
GameHelper/RemoteObjects/GameStates.cs
GameHelper/RemoteObjects/GameWindowCull.cs
GameHelper/RemoteObjects/GameWindowScale.cs
GameHelper/RemoteObjects/LoadedFiles.cs
GameHelper/RemoteObjects/TerrainHeightHelper.cs
```

- [ ] **Step 1: Read and audit each file (12-category checklist)**

Pay extra attention to:
- `RemoteObjectBase.cs` — categories 1 (concurrency on Address mutation), 8 (reflection in `GetToImGuiMethods`), 6 (logic).
- `GameStates.cs`, `LoadedFiles.cs`, `TerrainHeightHelper.cs` — category 9 (memory reads on possibly-stale addresses).

- [ ] **Step 2: Append findings**
- [ ] **Step 3: Update coverage counter** (after this task: `88 / 134`)
- [ ] **Step 4: Update severity counts and Index**

## Task 14: Audit GameHelper — RemoteObjects/Components (entity components)

**Files (read-only):**
```
GameHelper/RemoteObjects/Components/Actor.cs
GameHelper/RemoteObjects/Components/Animated.cs
GameHelper/RemoteObjects/Components/Buffs.cs
GameHelper/RemoteObjects/Components/Charges.cs
GameHelper/RemoteObjects/Components/Chest.cs
GameHelper/RemoteObjects/Components/ComponentBase.cs
GameHelper/RemoteObjects/Components/DiesAfterTime.cs
GameHelper/RemoteObjects/Components/Life.cs
GameHelper/RemoteObjects/Components/MinimapIcon.cs
GameHelper/RemoteObjects/Components/Mods.cs
GameHelper/RemoteObjects/Components/NPC.cs
GameHelper/RemoteObjects/Components/ObjectMagicProperties.cs
GameHelper/RemoteObjects/Components/Player.cs
GameHelper/RemoteObjects/Components/Positioned.cs
GameHelper/RemoteObjects/Components/Render.cs
GameHelper/RemoteObjects/Components/Shrine.cs
GameHelper/RemoteObjects/Components/Stack.cs
GameHelper/RemoteObjects/Components/StateMachine.cs
GameHelper/RemoteObjects/Components/Stats.cs
GameHelper/RemoteObjects/Components/Targetable.cs
GameHelper/RemoteObjects/Components/Transitionable.cs
GameHelper/RemoteObjects/Components/TriggerableBlockage.cs
```

- [ ] **Step 1: Read and audit each file (12-category checklist)**

Pay extra attention to:
- `ComponentBase.cs` — base class invariants.
- Each component — categories 6 (logic on memory read), 8 (perf in hot read paths), 9 (stale address reads).

- [ ] **Step 2: Append findings**
- [ ] **Step 3: Update coverage counter** (after this task: `110 / 134`)
- [ ] **Step 4: Update severity counts and Index**

## Task 15: Audit GameHelper — RemoteObjects/States (incl. InGameStateObjects) + UiElement + FilesStructures

**Files (read-only):**
```
GameHelper/RemoteObjects/States/AreaLoadingState.cs
GameHelper/RemoteObjects/States/InGameState.cs
GameHelper/RemoteObjects/States/InGameStateObjects/AreaInstance.cs
GameHelper/RemoteObjects/States/InGameStateObjects/Entity.cs
GameHelper/RemoteObjects/States/InGameStateObjects/ImportantUiElements.cs
GameHelper/RemoteObjects/States/InGameStateObjects/Inventory.cs
GameHelper/RemoteObjects/States/InGameStateObjects/Item.cs
GameHelper/RemoteObjects/States/InGameStateObjects/ServerData.cs
GameHelper/RemoteObjects/States/InGameStateObjects/WorldData.cs
GameHelper/RemoteObjects/UiElement/ChatParentUiElement.cs
GameHelper/RemoteObjects/UiElement/LargeMapUiElement.cs
GameHelper/RemoteObjects/UiElement/MapUiElement.cs
GameHelper/RemoteObjects/UiElement/SkillTreeNodeUiElement.cs
GameHelper/RemoteObjects/UiElement/UiElementBase.cs
GameHelper/RemoteObjects/FilesStructures/WorldAreaDat.cs
```

- [ ] **Step 1: Read and audit each file (12-category checklist)**

Pay extra attention to:
- `Entity.cs` — categories 1, 6, 8 (component cache, lazy resolution).
- `AreaInstance.cs`, `WorldData.cs`, `ServerData.cs` — category 8 (perf, large reads).
- `Inventory.cs`, `Item.cs` — category 6 (logic on item parsing).

- [ ] **Step 2: Append findings**
- [ ] **Step 3: Update coverage counter** (after this task: `125 / 134`)
- [ ] **Step 4: Update severity counts and Index**

## Task 16: Audit GameHelper — Settings + Ui

**Files (read-only):**
```
GameHelper/Settings/SettingsWindow.cs
GameHelper/Settings/State.cs
GameHelper/Ui/DataVisualization.cs
GameHelper/Ui/GameUiExplorer.cs
GameHelper/Ui/KrangledPassiveDetector.cs
GameHelper/Ui/NearbyVisualization.cs
GameHelper/Ui/OverlayKiller.cs
GameHelper/Ui/PerformanceProfiler.cs
GameHelper/Ui/PerformanceStats.cs
```

- [ ] **Step 1: Read and audit each file (12-category checklist)**

Pay extra attention to:
- `Settings/State.cs` — category 11 (JSON serialization, default values, save races).
- `Ui/*` — category 8 (per-frame allocations, ImGui hot path).

- [ ] **Step 2: Append findings**
- [ ] **Step 3: Update coverage counter** (after this task: `134 / 134` — done)
- [ ] **Step 4: Update severity counts and Index**

## Task 17: Final pass and commit

**Files:**
- Modify: `docs/audit/2026-04-27-bug-audit.md`

- [ ] **Step 1: Reconcile deferred-warning IDs**

Cross-reference your scratch list of `#pragma warning disable` sites with the audit findings. Every silenced warning in source must have a matching `F-NNN` in the audit doc; every `// TODO: see audit F-XXX` comment must point to an existing finding ID.

If any mismatch exists, fix it now:
- If a deferred warning lacks a finding, add one under the appropriate category.
- If a `// TODO` references a placeholder ID, replace it with the real F-NNN.

Verify with:
```bash
cd /c/Users/D/Desktop/GameHelper2
grep -r "TODO: see audit" GameHelper/ GameOffsets/ 2>&1 | sort
grep -E '^### F-' docs/audit/2026-04-27-bug-audit.md | sort
```

Every TODO ID should appear in the second list.

- [ ] **Step 2: Verify totals and coverage**

In the Summary section:
- Sum the per-severity counts and confirm the **Total** row matches.
- Confirm `Coverage: 134 / 134`.

- [ ] **Step 3: Verify build is still green (whole solution)**

```bash
cd /c/Users/D/Desktop/GameHelper2
dotnet build GameOverlay.sln -c Debug --no-restore 2>&1 | tail -5
dotnet build GameOverlay.sln -c Release --no-restore 2>&1 | tail -5
```

Expected: both end with `0 Warning(s)` `0 Error(s)` across all 9 csproj. The audit is read-only on source, so this should be unchanged from Task 5 — but verify.

- [ ] **Step 4: Update any source `// TODO: see audit F-XXX` placeholder IDs that were finalized**

If Step 1 changed source comments (renaming placeholder IDs to real ones), stage those changes too.

- [ ] **Step 5: Commit the audit**

```bash
cd /c/Users/D/Desktop/GameHelper2
# `docs/` is in .gitignore, so the audit file needs -f to be added.
git add -f docs/audit/2026-04-27-bug-audit.md
# If source TODO IDs changed in Step 4:
git add GameHelper/ GameOffsets/
git commit -m "docs(audit): add 2026-04-27 bug audit report

Categorized findings across 12 defect classes for the GameHelper +
GameOffsets in-scope sources. Each finding has severity, file:line,
description, suggested fix, and risk-if-left."
```

- [ ] **Step 6: Verify final state**

```bash
cd /c/Users/D/Desktop/GameHelper2
git log --oneline pre-net10-migration..HEAD
```

Expected: commits roughly matching this sequence —
1. (preceding) `docs(spec): add .NET 10 migration + audit design`
2. (preceding) `docs(plan): add .NET 10 migration + audit implementation plan`
3. (preceding) `chore: gitignore docs/ and adjust plan to use git add -f`
4. `chore(net): migrate all 9 csproj to .NET 10 LTS (net10.0-windows)`
5. `chore(net): enable warnings-as-errors during migration cleanup`
6. `fix(net10): resolve compiler warnings from net10 migration` (possibly split per area)
7. `chore(net): relax warnings-as-errors post-migration`
8. `docs(audit): add 2026-04-27 bug audit report`

```bash
git status --short
```

Expected: empty (clean tree).

---

# Phase 3 — Hand-off

The plan ends here. Phase 3 (user triages findings → fixes are planned via a fresh `writing-plans` invocation per chosen fix or batch) is **out of scope** for this plan.

Report to the user:
- Build is green on `net10.0-windows` Debug + Release.
- Audit document committed at `docs/audit/2026-04-27-bug-audit.md`.
- Total findings count by severity (read from the Summary section).
- Number of `#pragma warning disable` sites that reference audit IDs (so user can grep for them).
- The `pre-net10-migration` git tag still exists locally as a rollback anchor.

Ask the user which findings to fix next; each batch becomes a new plan.
