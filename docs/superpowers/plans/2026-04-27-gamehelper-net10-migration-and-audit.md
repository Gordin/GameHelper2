# GameHelper2 — .NET 10 Migration + Bug Audit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate `GameHelper/` and `GameOffsets/` (134 C# files) from `net8.0` to `net10.0-windows` LTS with `Nullable` enabled and zero warnings, then produce a categorized bug-audit document covering 12 defect classes the compiler cannot detect.

**Architecture:** Two ordered phases on a single branch. Phase 1 is mechanical: bump TFM, enable diagnostics, run a fix-build-fix loop with `TreatWarningsAsErrors=true` until the build is green. Phase 2 walks each in-scope file group against a fixed 12-category checklist and appends findings to one audit markdown.

**Tech Stack:** .NET 10 SDK 10.0.203 (already installed at `C:\Program Files\dotnet\dotnet.exe`), MSBuild via `dotnet build`, Newtonsoft.Json 13.0.3, ClickableTransparentOverlay 11.1.0, Coroutine 2.1.5, ProcessMemoryUtilities.Net 1.3.4. Spec: `docs/superpowers/specs/2026-04-27-gamehelper-net10-migration-and-audit-design.md`.

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
- **Defer to audit** when fixing requires a logic decision (e.g. "this could be null but the caller assumes not — what should we do on null?"). Action: silence the warning at the call site with `#pragma warning disable <CODE>` ... `#pragma warning restore <CODE>` and add a `// TODO: see audit F-XXX` comment. The corresponding finding **must** be added to the audit doc in Phase 2 with the matching ID.

Track silenced-warning sites as you create them (a running list in your scratch). Phase 2 cross-references them.

---

## File Structure

### Phase 1 modifies

| File | Change |
|------|--------|
| `GameHelper/GameHelper.csproj` | TFM → `net10.0-windows`, add `<Nullable>enable</Nullable>`, `<LangVersion>latest</LangVersion>` |
| `GameOffsets/GameOffsets.csproj` | TFM → `net10.0-windows`, add `<Nullable>enable</Nullable>`, `<LangVersion>latest</LangVersion>` |
| Source files under `GameHelper/` and `GameOffsets/` | Mechanical compiler/analyzer fixes only |

### Phase 1 creates

(Nothing new.)

### Phase 2 creates

| File | Responsibility |
|------|----------------|
| `docs/audit/2026-04-27-bug-audit.md` | Categorized findings, severity-sorted, with index |

### Out of scope (do NOT modify)

- `Launcher/` (entire directory)
- `Plugins/` (entire directory; this is the user-plugins folder, not the host-side `GameHelper/Plugin/` which IS in scope)
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
dotnet build GameHelper/GameHelper.csproj -c Debug -p:Platform=x64 --no-restore 2>&1 | tee /tmp/baseline-build.log
```

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

## Task 2: Migrate `GameOffsets.csproj` to `net10.0-windows`

**Files:**
- Modify: `GameOffsets/GameOffsets.csproj`

- [ ] **Step 1: Read current contents**

```bash
cat /c/Users/D/Desktop/GameHelper2/GameOffsets/GameOffsets.csproj
```

Confirm the file matches the snapshot below (it should). If not, reconcile with the user before proceeding.

- [ ] **Step 2: Replace file contents**

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

Notes:
- `RunAnalyzersDuringBuild=false` preserved from the original.
- `NoWarn 1591` (missing XML doc) hoisted from the Debug block to the top-level `PropertyGroup` so it also covers Release. This matters once Task 4 turns on `TreatWarningsAsErrors` for Release; otherwise CS1591 would error out on every undocumented public member.
- `Nullable=enable` and `LangVersion=latest` added per spec.

- [ ] **Step 3: Verify GameOffsets restores on net10.0-windows**

```bash
cd /c/Users/D/Desktop/GameHelper2
dotnet restore GameOffsets/GameOffsets.csproj
```

Expected: "Restore complete" with 0 errors. There may be NU1701/NU1702 warnings about TFM-mismatched dependencies — note them but proceed.

- [ ] **Step 4: Verify GameOffsets builds standalone (best-effort)**

```bash
cd /c/Users/D/Desktop/GameHelper2
dotnet build GameOffsets/GameOffsets.csproj -c Debug -p:Platform=x64 --no-restore
```

Expected: build may succeed or fail. **Failure is acceptable here** because we haven't yet enabled WAE — record any new errors/warnings to your notes. We will fix them during the cleanup loop in Task 5.

## Task 3: Migrate `GameHelper.csproj` to `net10.0-windows`

**Files:**
- Modify: `GameHelper/GameHelper.csproj`

- [ ] **Step 1: Read current contents**

```bash
cat /c/Users/D/Desktop/GameHelper2/GameHelper/GameHelper.csproj
```

Confirm the file matches the snapshot referenced in the spec. If altered unexpectedly, stop.

- [ ] **Step 2: Replace file contents**

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

`TreatWarningsAsErrors=false` is **left at its current value** — it will be flipped to `true` for the cleanup loop in Task 4 and reverted in Task 6.

- [ ] **Step 3: Restore on the new TFM**

```bash
cd /c/Users/D/Desktop/GameHelper2
dotnet restore GameHelper/GameHelper.csproj
```

Expected: "Restore complete" with 0 errors. NU1701/NU1702 may appear (e.g. `ClickableTransparentOverlay` published as net8.0); these are forward-compatibility informational warnings — acceptable.

- [ ] **Step 4: Commit the migration baseline**

```bash
cd /c/Users/D/Desktop/GameHelper2
git add GameHelper/GameHelper.csproj GameOffsets/GameOffsets.csproj
git commit -m "chore(net): migrate to .NET 10 LTS (net10.0-windows)

- TargetFramework: net8.0 → net10.0-windows in both projects
- Enable Nullable reference types and LangVersion=latest
- Package versions held; transitive deps restored on new TFM"
```

Expected: commit succeeds. Verify with `git log -1 --oneline` → prints the commit subject.

## Task 4: Enable warnings-as-errors for the cleanup loop

**Files:**
- Modify: `GameHelper/GameHelper.csproj` (Debug & Release `PropertyGroup`s)
- Modify: `GameOffsets/GameOffsets.csproj` (Debug & Release `PropertyGroup`s)

- [ ] **Step 1: Edit `GameHelper.csproj`**

In the Debug `PropertyGroup`, change:
```xml
<TreatWarningsAsErrors>false</TreatWarningsAsErrors>
```
to:
```xml
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

In the Release `PropertyGroup`, add (after `<PlatformTarget>x64</PlatformTarget>`):
```xml
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

- [ ] **Step 2: Edit `GameOffsets.csproj`**

In the Debug `PropertyGroup`, after `<NoWarn>1701;1702; 1591</NoWarn>`, add:
```xml
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

In the Release `PropertyGroup`, after `<PlatformTarget>x64</PlatformTarget>`, add:
```xml
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

- [ ] **Step 3: Run a discovery build to capture the warning surface**

```bash
cd /c/Users/D/Desktop/GameHelper2
dotnet build GameHelper/GameHelper.csproj -c Debug -p:Platform=x64 --no-restore 2>&1 | tee /tmp/wae-discovery.log
```

Expected: build **fails** with errors (the warnings are now errors). This is intended — we're collecting the work surface.

Count the unique error codes:
```bash
grep -oE 'CS[0-9]+|CA[0-9]+|Nullable warning' /tmp/wae-discovery.log | sort -u
```

Note the codes and rough counts in your scratch. Common expected codes:
- `CS8600`/`CS8601`/`CS8602`/`CS8604`/`CS8618` — nullability
- `CS0114`/`CS0108` — overrides/hides
- `CA1416` — platform compatibility (should be silent now since we have `-windows`)
- `CS1591` — missing XML doc (suppressed via NoWarn 1591 already)

- [ ] **Step 4: Commit the WAE flip (no source changes yet)**

```bash
cd /c/Users/D/Desktop/GameHelper2
git add GameHelper/GameHelper.csproj GameOffsets/GameOffsets.csproj
git commit -m "chore(net): enable warnings-as-errors during migration cleanup

Temporary; will be reverted after the mechanical fix pass leaves the
build green."
```

## Task 5: Mechanical cleanup loop

**Files:** any source under `GameHelper/` and `GameOffsets/` that the build flags. Scope guard: do NOT edit anything under `Launcher/` or `Plugins/`.

This task is iterative. Repeat the inner loop until `dotnet build` is clean.

- [ ] **Step 1: Run build to surface the next batch of errors**

```bash
cd /c/Users/D/Desktop/GameHelper2
dotnet build GameHelper/GameHelper.csproj -c Debug -p:Platform=x64 --no-restore 2>&1 | tee /tmp/cleanup-iter.log
```

If the build succeeds with 0 errors / 0 warnings, jump to Step 6.

- [ ] **Step 2: Pick the next file with errors**

Take the first file path reported in the log. Read it (`Read` tool, full file). Read the surrounding logic enough to understand whether each warning is mechanically fixable or needs an audit deferral per the boundary rule above.

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
| `CA*` analyzer warnings | If `RunAnalyzersDuringBuild=false` is set (true for GameOffsets) they shouldn't appear. For GameHelper, treat as the table above. |

For deferrals, use:
```csharp
#pragma warning disable CS8602 // TODO: see audit F-XXX
var v = maybeNull.Property;
#pragma warning restore CS8602
```

Append a one-liner to your scratch list: `F-XXX | path/to/file.cs:lineNumber | CS8602 | <one-line summary>`. You will populate the audit doc with these in Phase 2.

- [ ] **Step 4: Re-run build, confirm error count dropped**

```bash
cd /c/Users/D/Desktop/GameHelper2
dotnet build GameHelper/GameHelper.csproj -c Debug -p:Platform=x64 --no-restore 2>&1 | grep -cE 'error |warning '
```

Expected: number is strictly lower than the previous iteration. If it isn't, your fix introduced new issues — examine the new errors and either back out or extend the fix.

- [ ] **Step 5: Loop**

Return to Step 1. Continue until Step 1 reports 0 errors / 0 warnings on `Debug|x64`.

- [ ] **Step 6: Verify Release also builds clean**

```bash
cd /c/Users/D/Desktop/GameHelper2
dotnet build GameHelper/GameHelper.csproj -c Release -p:Platform=x64 --no-restore 2>&1 | tee /tmp/cleanup-release.log
grep -cE 'error |warning ' /tmp/cleanup-release.log
```

Expected: 0 errors and 0 warnings (excluding the `Build succeeded` summary line). If Release surfaces additional issues (rare; usually due to `OutputType` differences or Release-only targets), apply the same fix loop until clean.

- [ ] **Step 7: Commit the fix pass (one commit, may be split if very large)**

```bash
cd /c/Users/D/Desktop/GameHelper2
git add GameHelper/ GameOffsets/
git status --short    # confirm only files in scope are staged
git commit -m "fix(net10): resolve compiler warnings from net10 migration

Mechanical fixes for the warning surface that appeared after enabling
Nullable and bumping to net10.0-windows. Logic-decision warnings were
deferred behind #pragma disables with TODOs that point to audit IDs
to be filled in by Phase 2."
```

If the diff is unwieldy (>30 files, hard to review), split by directory:
```bash
git reset                     # unstage all
git add GameOffsets/
git commit -m "fix(net10): resolve nullable warnings in GameOffsets"
git add GameHelper/RemoteObjects/
git commit -m "fix(net10): resolve nullable warnings in GameHelper/RemoteObjects"
# ... etc, until clean
```

Each split commit must leave the build green (re-run `dotnet build` between commits).

## Task 6: Restore non-WAE state for normal development

**Files:**
- Modify: `GameHelper/GameHelper.csproj`
- Modify: `GameOffsets/GameOffsets.csproj`

- [ ] **Step 1: Revert WAE flag in `GameHelper.csproj`**

In the Debug `PropertyGroup`, change `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` back to `<TreatWarningsAsErrors>false</TreatWarningsAsErrors>`.

In the Release `PropertyGroup`, **remove** the `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` line that was added in Task 4 Step 1 (Release didn't have it originally).

- [ ] **Step 2: Revert WAE flag in `GameOffsets.csproj`**

Remove the `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` line from both Debug and Release `PropertyGroup`s. The original file had no such line, and we want to match.

- [ ] **Step 3: Verify build still green without WAE**

```bash
cd /c/Users/D/Desktop/GameHelper2
dotnet build GameHelper/GameHelper.csproj -c Debug -p:Platform=x64 --no-restore 2>&1 | tail -10
dotnet build GameHelper/GameHelper.csproj -c Release -p:Platform=x64 --no-restore 2>&1 | tail -10
```

Expected: both end with "Build succeeded" and `0 Warning(s)` `0 Error(s)`. (Without WAE, warnings would not error — but since we already drove the count to zero, they should remain zero.)

- [ ] **Step 4: Commit**

```bash
cd /c/Users/D/Desktop/GameHelper2
git add GameHelper/GameHelper.csproj GameOffsets/GameOffsets.csproj
git commit -m "chore(net): relax warnings-as-errors post-migration

Keeps Nullable=enable for ongoing protection, removes the temporary
WAE flag used during the migration cleanup pass. Future warnings
will not block builds but remain visible."
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

## Task 7: Create audit document skeleton

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

The audit doc accumulates across Tasks 8–17 and is committed once at Task 18.

## Task 8: Audit GameOffsets — Natives, Pattern, root files

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

## Task 9: Audit GameOffsets — Objects (excluding nested)

**Files (read-only):**
- `GameOffsets/Objects/AreaChangeOffset.cs`
- `GameOffsets/Objects/GameStateOffsets.cs`
- `GameOffsets/Objects/InGameStateOffset.cs`
- `GameOffsets/Objects/LoadedFilesOffset.cs`

- [ ] **Step 1: Read and audit each file (12-category checklist)**
- [ ] **Step 2: Append findings**
- [ ] **Step 3: Update coverage counter** (after this task: `16 / 134`)
- [ ] **Step 4: Update severity counts and Index**

## Task 10: Audit GameOffsets — Components, FilesStructures, States, UiElement

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

## Task 11: Audit GameHelper — Utils + Cache + CoroutineEvents

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

## Task 12: Audit GameHelper — Core, GameProcess, GameOverlay, Program

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

## Task 13: Audit GameHelper — Plugin (host-side loader, NOT user plugins)

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

## Task 14: Audit GameHelper — RemoteEnums + RemoteObjects (root + base + helpers)

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

## Task 15: Audit GameHelper — RemoteObjects/Components (entity components)

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

## Task 16: Audit GameHelper — RemoteObjects/States (incl. InGameStateObjects) + UiElement + FilesStructures

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

## Task 17: Audit GameHelper — Settings + Ui

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

## Task 18: Final pass and commit

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

- [ ] **Step 3: Verify build is still green**

```bash
cd /c/Users/D/Desktop/GameHelper2
dotnet build GameHelper/GameHelper.csproj -c Debug -p:Platform=x64 --no-restore 2>&1 | tail -5
dotnet build GameHelper/GameHelper.csproj -c Release -p:Platform=x64 --no-restore 2>&1 | tail -5
```

Expected: both end with `0 Warning(s)` `0 Error(s)`. The audit is read-only on source, so this should be unchanged from Task 6 — but verify.

- [ ] **Step 4: Update any source `// TODO: see audit F-XXX` placeholder IDs that were finalized**

If Step 1 changed source comments (renaming placeholder IDs to real ones), stage those changes too.

- [ ] **Step 5: Commit the audit**

```bash
cd /c/Users/D/Desktop/GameHelper2
git add docs/audit/2026-04-27-bug-audit.md
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
2. `chore(net): migrate to .NET 10 LTS (net10.0-windows)`
3. `chore(net): enable warnings-as-errors during migration cleanup`
4. `fix(net10): resolve compiler warnings from net10 migration` (possibly split into multiple commits)
5. `chore(net): relax warnings-as-errors post-migration`
6. `docs(audit): add 2026-04-27 bug audit report`

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
