# Target-Framework Realignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Collapse the two-Core (`net10.0-windows` Core + `netstandard2.0` Core.Standard) split into a single `netstandard2.0` Core consumed by both a `net8.0-windows` WPF GUI (absorbing the ViewModel project) and a `net472` CLI (lightweight `.exe` + DLL, plugin-host friendly), while bundling SOLID cleanups (`IDialogService` + `IUiDispatcher` seams, `IProjectData` ISP split, `AppServices` composition root, `ViewModelBase: IDisposable`, `IManager` trim, dead callback removal, duplicate `Awake` bug fix).

**Architecture:** Single `netstandard2.0` Core hosts all business logic. Two new abstractions in `Core/Services/` (`IDialogService`, `IUiDispatcher`) replace direct `MessageBox`/`FolderBrowserDialog`/`Application.Current.Dispatcher`/`Process.Start` calls in ViewModels. WPF GUI (now `net8.0-windows`) provides `WpfDialogService` + `WpfUiDispatcher` and wires everything via a single `AppServices` POCO constructed in `App.OnStartup`. CLI (now `net472`) provides `ConsoleDialogService` + `ImmediateUiDispatcher` and a small `Program.cs`. Tests at `net8.0-windows` instantiate ViewModels with `FakeDialogService` + `ImmediateUiDispatcher` to prove headless-testability.

**Tech Stack:** C# 14 syntax via `<LangVersion>latest</LangVersion>` (Core targets `netstandard2.0` — no PolySharp; `init`/`record`/`required` not in current code). xUnit 2.9.2 with `Microsoft.NET.Test.Sdk` 17.11.1. WPF + `Extended.Wpf.Toolkit` 4.6.0 + `Dirkster.AvalonDock` 4.74.0 for GUI. `Spectre.Console` 0.49.x for CLI (last netstandard2.0-supporting version per CLI dependency vetting in spec §7). `DocumentFormat.OpenXml` 3.0.2, `System.Text.Json` 8.0.5, `System.Buffers` 4.5.1, `System.IO.Packaging` 8.0.1.

**Spec reference:** `docs/superpowers/specs/2026-05-06-target-framework-realignment-design.md` is the design source-of-truth. Read it once before starting Task 1; revisit §6 (SOLID-driven cleanups) when implementing B1/B2/B3 and R1/R2/R3.

**Branching:** All work in this plan happens on a single feature branch `feature/target-framework-realignment` cut from `master`. Each task is one PR, intended to land green-on-green sequentially. Master must remain buildable on the *current* (pre-realignment) layout until Task 7 lands.

---

## Task 0 — Preflight: cut branch and retarget master from net10 → net8

**Why this task:** Establish a buildable baseline that requires **only .NET 9 SDK + net8 runtime** (already present on the developer machine and on `windows-latest` CI runners), instead of the .NET 10 SDK that current master demands. The realignment's end-state is net8.0-windows for GUI/Tests anyway (per spec §3); doing the retarget *now* as a no-functional-change commit means subsequent tasks can run their TDD cycles locally without any SDK install. This collapses what would otherwise be Task 0 (install SDK) + Task 4 step 1 + Task 6 step 1 into one short PR.

**Files:**
- Read: `docs/superpowers/specs/2026-05-06-target-framework-realignment-design.md`
- Modify: `DeployAssistant/DeployAssistant.csproj`
- Modify: `DeployAssistant.Core/DeployAssistant.Core.csproj`
- Modify: `DeployAssistant.ViewModel/DeployAssistant.ViewModel.csproj`
- Modify: `DeployAssistant.Tests/DeployAssistant.Tests.csproj`
- Modify: `DeployAssistant.CLI/DeployAssistant.CLI.csproj`
- Modify: `.github/workflows/cli-smoke-test.yml`

- [ ] **Step 1: Verify SDK + runtime availability.**

```bash
dotnet --list-sdks
dotnet --list-runtimes | grep -E "WindowsDesktop|NETCore" | grep " 8\."
```

Expected: at least one SDK at version 8.x or 9.x or higher (a 9.x SDK can target net8.0 fine), and `Microsoft.WindowsDesktop.App 8.x.x` + `Microsoft.NETCore.App 8.x.x` runtimes present. If runtimes for net8 aren't there, install **the .NET 8 Desktop Runtime + .NET 8 Hosting Bundle** from https://dotnet.microsoft.com/download/dotnet/8.0 (no SDK needed).

Task 5 also needs the .NET Framework 4.7.2 reference assemblies — install **.NET Framework 4.8 Developer Pack** (includes 4.7.2 ref assemblies) from https://aka.ms/msbuild/developerpacks before Task 5. Not blocking for Tasks 0-4.

- [ ] **Step 2: Cut the feature branch from master.**

```bash
git checkout master
git pull --ff-only
git checkout -b feature/target-framework-realignment
```

- [ ] **Step 3: Retarget the four `net10.0-windows` projects to `net8.0-windows`.**

Each csproj has one line to change. Apply the same replacement in each:

```xml
    <TargetFramework>net10.0-windows</TargetFramework>
```
→
```xml
    <TargetFramework>net8.0-windows</TargetFramework>
```

Files: `DeployAssistant/DeployAssistant.csproj`, `DeployAssistant.Core/DeployAssistant.Core.csproj`, `DeployAssistant.ViewModel/DeployAssistant.ViewModel.csproj`, `DeployAssistant.Tests/DeployAssistant.Tests.csproj`.

- [ ] **Step 4: Retarget the CLI from `net10.0` → `net8.0`.**

Edit `DeployAssistant.CLI/DeployAssistant.CLI.csproj`:

```xml
    <TargetFramework>net10.0</TargetFramework>
```
→
```xml
    <TargetFramework>net8.0</TargetFramework>
```

(The CLI goes to net472 in Task 5; this temporary net8 step is just to align with the rest of the solution during Tasks 1–4.)

- [ ] **Step 5: Update `cli-smoke-test.yml` to use .NET 8 SDK.**

Edit `.github/workflows/cli-smoke-test.yml` lines ~22-25:

```yaml
      - name: Set up .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'
```
→
```yaml
      - name: Set up .NET 8
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
```

(The `--self-contained` and `-p:PublishSingleFile=true` flags stay — they still work on net8 self-contained publish. They get dropped in Task 5 when CLI moves to net472.)

- [ ] **Step 6: Drop the developer-machine-local PNG reference from the GUI csproj.**

While we're touching `DeployAssistant.csproj`, remove this stale block (it references a path on the original developer's `Downloads/` folder and breaks anywhere else):

```xml
    <None Include="..\..\..\..\Downloads\flexible-deployment.png">
      <Pack>True</Pack>
      <PackagePath>\</PackagePath>
    </None>
```

Also remove the matching `<PackageIcon>flexible-deployment.png</PackageIcon>` line from the `<PropertyGroup>`. The GUI is an exe, not a NuGet package — `PackageIcon` was orphan metadata anyway.

- [ ] **Step 7: Verify the solution builds and all tests pass on net8.**

```bash
dotnet build DeployAssistant.sln -c Debug
dotnet test  DeployAssistant.Tests/DeployAssistant.Tests.csproj
```

Expected: both succeed. WPF API surface is identical between net8 and net10; the only likely build error would be a C# 14 language feature in the codebase, which would surface as `CS9XXX` errors.

If build fails on a C# version mismatch: the projects don't currently set `<LangVersion>`, so they default to the SDK's max. With the .NET 9 SDK, the default is C# 13. If a `record` or pattern uses C# 14-only syntax, either rewrite it or pin `<LangVersion>13.0</LangVersion>` in the affected csproj. Don't bypass — track down the actual incompatibility.

- [ ] **Step 8: Smoke-test the GUI launches.**

```bash
dotnet run --project DeployAssistant/DeployAssistant.csproj
```

Expected: WPF window opens; quit immediately. Confirms the runtime swap doesn't break anything visible.

- [ ] **Step 9: Commit.**

```bash
git add DeployAssistant/DeployAssistant.csproj \
        DeployAssistant.Core/DeployAssistant.Core.csproj \
        DeployAssistant.ViewModel/DeployAssistant.ViewModel.csproj \
        DeployAssistant.Tests/DeployAssistant.Tests.csproj \
        DeployAssistant.CLI/DeployAssistant.CLI.csproj \
        .github/workflows/cli-smoke-test.yml
git commit -m "chore: retarget master from net10 to net8 baseline

Establishes a buildable baseline for the target-framework realignment
that requires only the .NET 8 runtime / 9-SDK already present locally
and on windows-latest CI runners. End-state for GUI / Tests is
net8.0-windows anyway (spec §3); doing this now means subsequent
realignment tasks can TDD locally without an SDK install detour.

- DeployAssistant, DeployAssistant.Core, DeployAssistant.ViewModel,
  DeployAssistant.Tests: net10.0-windows → net8.0-windows.
- DeployAssistant.CLI: net10.0 → net8.0 (further retargeted to net472
  in Task 5 of the plan).
- cli-smoke-test.yml: setup-dotnet 10.0 → 8.0.
- Drop developer-machine-local PNG reference and orphan PackageIcon
  from DeployAssistant.csproj.

Refs: docs/superpowers/plans/2026-05-06-target-framework-realignment.md Task 0."
```

- [ ] **Step 10: Push and open PR; wait for CI green and merge before Task 1.**

```bash
git push -u origin feature/target-framework-realignment
gh pr create --base master --head feature/target-framework-realignment \
    --title "chore: retarget master from net10 to net8 baseline" \
    --body "Preflight commit for the target-framework realignment effort. Pure framework retarget — no functional change. End-state for GUI/Tests is net8.0-windows per spec §3; this lands the retarget early so subsequent tasks can TDD locally without an SDK install. Plan: docs/superpowers/plans/2026-05-06-target-framework-realignment.md"
```

---

## Task 1 — Strengthen Core test coverage at the seams the realignment touches

**Why this task:** The Core/Core.Standard split has drifted (PRs #13, #16, #17, #20 evidence). Tests today reference Core, not Core.Standard, so the binary the CLI ships has *no* automated coverage. Before promoting Core.Standard to "the new Core" (Task 2), add tests that pin down the behaviors the upcoming changes will touch: the dead-callback wiring (B2), the `IProjectData` consumers (B3), and the duplicate `_updateManager.Awake()` (B1). These tests must pass against *both* Core and Core.Standard's MetaDataManager — proving they are interchangeable before we collapse them.

**Files:**
- Create: `DeployAssistant.Tests/Integration/MetaDataManagerWiringTests.cs`
- Create: `DeployAssistant.Tests/Models/RecordedFileTests.cs` (if not present — verify first)
- Test: same files

- [ ] **Step 1: Verify whether `RecordedFileTests.cs` exists already.**

```bash
ls DeployAssistant.Tests/Models/RecordedFileTests.cs 2>&1 || echo "MISSING"
```

If present: read it and skip step 2's create — augment instead. If missing: continue to step 2.

- [ ] **Step 2: Write failing tests for `RecordedFile`'s identity-only contract.**

Create `DeployAssistant.Tests/Models/RecordedFileTests.cs` (or add `[Fact]`s to an existing file). The tests assert the *current* observable behavior, which Task 2's ISP split must preserve.

```csharp
using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using Xunit;

namespace DeployAssistant.Tests.Models
{
    public class RecordedFileTests
    {
        [Fact]
        public void Constructor_NameTypeIgnore_StampsUpdatedTime()
        {
            var rf = new RecordedFile("ProjectMetaData.bin", ProjectDataType.File, IgnoreType.All);

            Assert.Equal("ProjectMetaData.bin", rf.DataName);
            Assert.Equal(ProjectDataType.File, rf.DataType);
            Assert.Equal(IgnoreType.All, rf.IgnoreType);
            Assert.NotEqual(default, rf.UpdatedTime);
        }

        [Fact]
        public void RecordedFile_AsIProjectData_HasEmptyContentFields()
        {
            // Today's behavior: RecordedFile satisfies IProjectData but the content
            // half (Hash/RelPath/AbsPath/SrcPath/State) is meaningless. Task 2 (B3)
            // splits IProjectData so RecordedFile only implements the identity half;
            // this test pins down that the content fields default to empty strings
            // / DataState.None, so the split produces no behavioral change.
            IProjectData projData = new RecordedFile("x", ProjectDataType.File, IgnoreType.All);

            Assert.Equal(string.Empty, projData.DataHash);
            Assert.Equal(string.Empty, projData.DataRelPath);
            Assert.Equal(string.Empty, projData.DataSrcPath);
            Assert.Equal(string.Empty, projData.DataAbsPath);
            Assert.Equal(DataState.None, projData.DataState);
        }
    }
}
```

- [ ] **Step 3: Run the new tests; they must pass on current `master`.**

```bash
dotnet test DeployAssistant.Tests/DeployAssistant.Tests.csproj --filter "FullyQualifiedName~RecordedFileTests" -v normal
```

Expected: PASS (the assertions match current behavior — Task 2's split must preserve them).

- [ ] **Step 4: Write a failing test for `MetaDataManager.Awake()` idempotency.**

This pins the duplicate `_updateManager.Awake()` call (Core: lines 158-160, Core.Standard: similar). After B1 fix, Awake is called exactly once per child manager. Today, calling it multiple times happens to be safe because all child `Awake()` bodies are empty. The test ensures *callers* (i.e. tests) can rely on `Awake()` being idempotent across the refactor.

Create `DeployAssistant.Tests/Integration/MetaDataManagerWiringTests.cs`:

```csharp
using DeployAssistant.DataComponent;
using Xunit;

namespace DeployAssistant.Tests.Integration
{
    public class MetaDataManagerWiringTests
    {
        [Fact]
        public void Awake_CalledTwice_StaysIdempotent()
        {
            var m = new MetaDataManager();
            m.Awake();
            m.Awake();
            // No exception, no observable double-wiring side-effects.
            // (The existing duplicate _updateManager.Awake() is masked by empty
            // child Awake() bodies, but this test prevents a future Awake() body
            // from silently being invoked twice.)
        }

        [Fact]
        public void Awake_DoesNotThrow_WhenConfirmationCallbackUnset()
        {
            var m = new MetaDataManager();
            // ConfirmationCallback intentionally left null — pin that current Awake
            // tolerates this. After Task 3 (B2), this property is gone entirely.
            m.Awake();
        }
    }
}
```

- [ ] **Step 5: Run wiring tests on current master.**

```bash
dotnet test DeployAssistant.Tests/DeployAssistant.Tests.csproj --filter "FullyQualifiedName~MetaDataManagerWiringTests" -v normal
```

Expected: PASS on current code (these are characterization tests, not regression-detection tests).

- [ ] **Step 6: Run the full suite to confirm nothing was broken by the additions.**

```bash
dotnet test DeployAssistant.Tests/DeployAssistant.Tests.csproj
```

Expected: PASS.

- [ ] **Step 7: Commit.**

```bash
git add DeployAssistant.Tests/Models/RecordedFileTests.cs \
        DeployAssistant.Tests/Integration/MetaDataManagerWiringTests.cs
git commit -m "test: pin RecordedFile identity contract and MetaDataManager.Awake idempotency

Characterization tests for the surfaces upcoming Task 2 changes will
touch: B1 duplicate Awake call, B3 IProjectData split for RecordedFile.
These pass against current master and must continue to pass after the
realignment."
```

- [ ] **Step 8: Open PR for Task 1.**

```bash
git push
gh pr create --base master --head feature/target-framework-realignment \
    --title "test: characterization tests for realignment seams" \
    --body "$(cat <<'EOF'
## Summary
- Characterization tests for `RecordedFile`'s identity-only contract and `MetaDataManager.Awake()` idempotency.
- These pin down the behavior Tasks 2 and 3 will preserve.

## Test plan
- [x] `dotnet test` passes locally on master baseline.

Refs: docs/superpowers/specs/2026-05-06-target-framework-realignment-design.md §1, §8 step 1.
EOF
)"
```

Wait for CI to go green and the PR to be merged before starting Task 2.

---

## Task 2 — Promote Core.Standard to "the new Core" with B1 + B3 fixes

**Why this task:** Bring Core.Standard up to feature parity with Core, fix the duplicate `_updateManager.Awake()` call (B1), and split `IProjectData` per ISP (B3). At the end, the test project still references *old* Core (untouched). Switching the test reference happens in step 9 below, proving Core.Standard is feature-complete.

**Files:**
- Modify: `DeployAssistant.Core.Standard/DeployAssistant.Core.Standard.csproj`
- Modify: `DeployAssistant.Core.Standard/DataComponent/MetaDataManager.cs`
- Create: `DeployAssistant.Core.Standard/Interfaces/IProjectDataIdentity.cs` (file lives in `DeployAssistant.Core/`, linked into Standard)
- Create: `DeployAssistant.Core/Interfaces/IProjectDataIdentity.cs`
- Create: `DeployAssistant.Core/Interfaces/IProjectDataContent.cs`
- Modify: `DeployAssistant.Core/Interfaces/IProjectData.cs`
- Modify: `DeployAssistant.Core/Model/RecordedFile.cs`
- Modify: `DeployAssistant.Core/DataComponent/MetaDataManager.cs`
- Modify: `DeployAssistant.Tests/DeployAssistant.Tests.csproj`

- [ ] **Step 1: Pull the drifted methods from old Core into Core.Standard's MetaDataManager.**

The drifted methods (`LoadExternalMetaFile`, `ComputeMetaFileDiff`, `RequestExportDiffPackage`) and the missing `UpdateIgnoreListEventHandler?.Invoke(newIgnoreData)` dispatch + `_settingManager.ConfirmationCallback = ConfirmationCallback;` line live in `DeployAssistant.Core/DataComponent/MetaDataManager.cs`. Diff against `DeployAssistant.Core.Standard/DataComponent/MetaDataManager.cs`:

```bash
diff -u DeployAssistant.Core.Standard/DataComponent/MetaDataManager.cs \
       DeployAssistant.Core/DataComponent/MetaDataManager.cs
```

For each diff hunk that exists in Core but not in Standard, copy the change into Standard. The substantive diffs (per onboarding audit) are:
1. After `_updateManager.Awake();` add `_settingManager.ConfirmationCallback = ConfirmationCallback;` — but **drop** the duplicate `_updateManager.Awake()` while you're there (B1).
2. In the ignore-list refresh path, add `UpdateIgnoreListEventHandler?.Invoke(newIgnoreData);` after the `_settingManager` assignment.
3. Append `LoadExternalMetaFile`, `ComputeMetaFileDiff`, `RequestExportDiffPackage` (plus the doc-comments) to the bottom of the class. Source: `DeployAssistant.Core/DataComponent/MetaDataManager.cs:504-537`.

After editing, the Standard variant's `Awake()` body should look like:

```csharp
            _backupManager.Awake();
            _updateManager.Awake();          // B1: was duplicated, now once
            _settingManager.ConfirmationCallback = ConfirmationCallback;
            _settingManager.Awake();
```

Apply the same Awake fix to `DeployAssistant.Core/DataComponent/MetaDataManager.cs:158-162` (delete the duplicate `_updateManager.Awake();` on line 159 or 160).

- [ ] **Step 2: Run the test suite (tests still reference old Core).**

```bash
dotnet test DeployAssistant.Tests/DeployAssistant.Tests.csproj
```

Expected: PASS, including the new `Awake_CalledTwice_StaysIdempotent` test.

- [ ] **Step 3: Add the Migration steps file links to Core.Standard.csproj** (if a step file was added to Core after the last sync but isn't listed in `Core.Standard.csproj`). Audit:

```bash
ls DeployAssistant.Core/Migration/Steps/*.cs
grep "Migration/Steps/" DeployAssistant.Core.Standard/DeployAssistant.Core.Standard.csproj
```

For any step in `Core/Migration/Steps/` not already in `Core.Standard.csproj`, add a `<Compile Include="..." Link="..." />` entry under the existing migration ItemGroup (lines 65-96 of `Core.Standard.csproj`).

- [ ] **Step 4: Write failing tests for the new `IProjectDataIdentity` / `IProjectDataContent` interfaces.**

Add to `DeployAssistant.Tests/Models/RecordedFileTests.cs`:

```csharp
        [Fact]
        public void RecordedFile_ImplementsIdentityOnly()
        {
            var rf = new RecordedFile("z", ProjectDataType.Directory, IgnoreType.Integration);

            Assert.IsAssignableFrom<IProjectDataIdentity>(rf);
            Assert.False(rf is IProjectDataContent,
                "RecordedFile must NOT implement IProjectDataContent — it has no meaningful hash/path");
        }

        [Fact]
        public void ProjectFile_ImplementsBothIdentityAndContent()
        {
            var pf = new ProjectFile(
                DataSize: 1, BuildVersion: "1.0", DataName: "a.dll",
                DataSrcPath: @"C:\X", DataRelPath: "a.dll");

            Assert.IsAssignableFrom<IProjectDataIdentity>(pf);
            Assert.IsAssignableFrom<IProjectDataContent>(pf);
        }
```

Run:

```bash
dotnet test DeployAssistant.Tests/DeployAssistant.Tests.csproj --filter "FullyQualifiedName~RecordedFileTests" -v normal
```

Expected: COMPILE FAIL — `IProjectDataIdentity` and `IProjectDataContent` don't exist yet. That's the red bar.

- [ ] **Step 5: Create `DeployAssistant.Core/Interfaces/IProjectDataIdentity.cs`.**

```csharp
namespace DeployAssistant.Interfaces
{
    /// <summary>
    /// Identifies a tracked entry by its name and type. The minimum surface area
    /// every IProjectData participant must satisfy. <see cref="Model.RecordedFile"/>
    /// implements only this; <see cref="Model.ProjectFile"/> additionally implements
    /// <see cref="IProjectDataContent"/>.
    /// </summary>
    public interface IProjectDataIdentity
    {
        ProjectDataType DataType { get; }
        string DataName { get; }
        DateTime UpdatedTime { get; set; }
    }
}
```

- [ ] **Step 6: Create `DeployAssistant.Core/Interfaces/IProjectDataContent.cs`.**

```csharp
using DeployAssistant.DataComponent;

namespace DeployAssistant.Interfaces
{
    /// <summary>
    /// Carries the path/hash/state content of a tracked file or directory.
    /// Implemented by <see cref="Model.ProjectFile"/> only — not by
    /// <see cref="Model.RecordedFile"/>, whose values for these fields are meaningless.
    /// </summary>
    public interface IProjectDataContent
    {
        DataState DataState { get; set; }
        string DataRelPath { get; }
        string DataSrcPath { get; set; }
        string DataAbsPath { get; }
        string DataHash { get; set; }
    }
}
```

- [ ] **Step 7: Update `DeployAssistant.Core/Interfaces/IProjectData.cs` to compose the two halves (back-compat).**

```csharp
namespace DeployAssistant.Interfaces
{
    public enum ProjectDataType
    {
        File,
        Directory
    }

    /// <summary>
    /// Compatibility umbrella combining identity and content. Existing call sites
    /// that take <see cref="IProjectData"/> continue to work; new code should accept
    /// <see cref="IProjectDataIdentity"/> when only identity is needed (e.g. ignore lists).
    /// </summary>
    public interface IProjectData : IProjectDataIdentity, IProjectDataContent { }
}
```

- [ ] **Step 8: Update `DeployAssistant.Core/Model/RecordedFile.cs` to implement only `IProjectDataIdentity`.**

```csharp
using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;

namespace DeployAssistant.Model
{
    public class RecordedFile : IProjectDataIdentity
    {
        public ProjectDataType DataType { get; set; }

        public IgnoreType IgnoreType { get; set; }

        public DateTime UpdatedTime { get; set; }

        public string DataName { get; set; }

        [System.Text.Json.Serialization.JsonConstructor]
        public RecordedFile(ProjectDataType dataType, IgnoreType ignoreType, DateTime updatedTime, string dataName)
        {
            DataType = dataType;
            IgnoreType = ignoreType;
            UpdatedTime = updatedTime;
            DataName = dataName;
        }

        public RecordedFile(string DataName, ProjectDataType DataType, IgnoreType IgnoreType)
        {
            this.DataType = DataType;
            this.DataName = DataName;
            this.IgnoreType = IgnoreType;
            UpdatedTime = DateTime.Now;
        }
    }
}
```

(The `[JsonIgnore]`-tagged content fields are deleted entirely — they were never serialized, never written by class methods, and are no longer required to satisfy the interface.)

- [ ] **Step 9: Add the new interface files to `Core.Standard.csproj` link list.**

Edit `DeployAssistant.Core.Standard/DeployAssistant.Core.Standard.csproj`. Inside the existing `<ItemGroup>` that holds Interface links (around line 19), add:

```xml
    <Compile Include="../DeployAssistant.Core/Interfaces/IProjectDataIdentity.cs" Link="Interfaces/IProjectDataIdentity.cs" />
    <Compile Include="../DeployAssistant.Core/Interfaces/IProjectDataContent.cs" Link="Interfaces/IProjectDataContent.cs" />
```

- [ ] **Step 10: Run the full suite. Compile errors at any consumer of `RecordedFile`'s deleted content properties are EXPECTED — fix them by changing the consumer to take `IProjectDataIdentity`, or to use `ProjectFile` directly when content is needed.**

```bash
dotnet build DeployAssistant.sln
```

Likely impacted call sites (audit before fixing):
```bash
grep -rn "RecordedFile" DeployAssistant.Core/ DeployAssistant.Core.Standard/ DeployAssistant.ViewModel/ DeployAssistant/ | grep -v "/obj/" | grep -v "/bin/"
```

For each consumer that reads `recordedFile.DataHash` / `.DataRelPath` / `.DataSrcPath` / `.DataAbsPath` / `.DataState`: that code is dead-reading-empty-string and was a latent bug. Replace with the appropriate constant or branch. Compile until green.

- [ ] **Step 11: Run tests.**

```bash
dotnet test DeployAssistant.Tests/DeployAssistant.Tests.csproj
```

Expected: all tests pass, including the new `RecordedFile_ImplementsIdentityOnly` and `ProjectFile_ImplementsBothIdentityAndContent`.

- [ ] **Step 12: Switch the Tests project's reference from `DeployAssistant.Core` → `DeployAssistant.Core.Standard`.**

Edit `DeployAssistant.Tests/DeployAssistant.Tests.csproj`. Replace:

```xml
    <ProjectReference Include="..\DeployAssistant.Core\DeployAssistant.Core.csproj" />
```

with:

```xml
    <ProjectReference Include="..\DeployAssistant.Core.Standard\DeployAssistant.Core.Standard.csproj" />
```

- [ ] **Step 13: Run tests against Core.Standard.**

```bash
dotnet test DeployAssistant.Tests/DeployAssistant.Tests.csproj
```

Expected: PASS. This proves Core.Standard is feature-complete and that the realignment can safely promote it to the only Core. If a test fails: that's a drift bug — *do not* paper over it. Fix Core.Standard until all tests pass.

- [ ] **Step 14: Commit.**

```bash
git add DeployAssistant.Core/Interfaces/IProjectDataIdentity.cs \
        DeployAssistant.Core/Interfaces/IProjectDataContent.cs \
        DeployAssistant.Core/Interfaces/IProjectData.cs \
        DeployAssistant.Core/Model/RecordedFile.cs \
        DeployAssistant.Core/DataComponent/MetaDataManager.cs \
        DeployAssistant.Core.Standard/DataComponent/MetaDataManager.cs \
        DeployAssistant.Core.Standard/DeployAssistant.Core.Standard.csproj \
        DeployAssistant.Tests/DeployAssistant.Tests.csproj \
        DeployAssistant.Tests/Models/RecordedFileTests.cs
git commit -m "refactor: split IProjectData per ISP, fix duplicate _updateManager.Awake()

- B1: drop the duplicate _updateManager.Awake() call in MetaDataManager.Awake().
- B3: split IProjectData into IProjectDataIdentity + IProjectDataContent;
  RecordedFile now implements only the identity half, dropping its stack of
  meaningless [JsonIgnore] content properties.
- Sync drift cleanup: pull LoadExternalMetaFile / ComputeMetaFileDiff /
  RequestExportDiffPackage / UpdateIgnoreListEventHandler dispatch /
  _settingManager.ConfirmationCallback wiring from Core into Core.Standard.
- Switch DeployAssistant.Tests reference from Core to Core.Standard — proves
  feature parity by running the full xUnit suite against the netstandard2.0
  binary the CLI consumes.

Refs: docs/superpowers/specs/2026-05-06-target-framework-realignment-design.md §6.1, §8 step 2."
```

- [ ] **Step 15: Open PR for Task 2; wait for merge before starting Task 3.**

```bash
gh pr create --base master --head feature/target-framework-realignment \
    --title "refactor: ISP split + B1 fix + Core.Standard feature parity" \
    --body "$(cat <<'EOF'
## Summary
- Split `IProjectData` into `IProjectDataIdentity` + `IProjectDataContent`; `RecordedFile` now implements only identity (B3).
- Drop duplicate `_updateManager.Awake()` call (B1).
- Pull drifted methods from Core into Core.Standard so the two are observably identical.
- Switch test project to reference Core.Standard so the netstandard2.0 binary is now under test.

## Test plan
- [x] `dotnet test` passes against Core.Standard (was: against Core).
- [x] New tests verify the identity/content split.
- [x] No deletions of old Core or Core.Standard yet — those happen in Task 7 after retargeting.

Refs: spec §6.1, §8 step 2.
EOF
)"
```

---

## Task 3 — Service seams (`IDialogService` + `IUiDispatcher`) and B2 dead-callback removal

**Why this task:** Introduce the two abstractions the realignment plan promises (spec §5), refactor the four ViewModels to depend on them, and delete the dead `ConfirmationCallback` properties on three managers (B2). At the end of this task, ViewModels are headless-testable using `FakeDialogService` + `ImmediateUiDispatcher`.

**Files:**
- Create: `DeployAssistant.Core/Services/IDialogService.cs`
- Create: `DeployAssistant.Core/Services/IUiDispatcher.cs`
- Create: `DeployAssistant.Core/Services/DialogChoice.cs`
- Modify: `DeployAssistant.Core/DataComponent/MetaDataManager.cs` (+ Standard variant)
- Modify: `DeployAssistant.Core/DataComponent/BackupManager.cs` (+ Standard if applicable)
- Modify: `DeployAssistant.Core/DataComponent/FileManager.cs` (+ Standard variant)
- Modify: `DeployAssistant.Core/DataComponent/SettingManager.cs`
- Modify: `DeployAssistant.Core.Standard/DeployAssistant.Core.Standard.csproj` (link new Services files)
- Modify: `DeployAssistant.ViewModel/MetaDataViewModel.cs`
- Modify: `DeployAssistant.ViewModel/MetaFileDiffViewModel.cs`
- Modify: `DeployAssistant.ViewModel/BackupViewModel.cs`
- Modify: `DeployAssistant.ViewModel/FileTrackViewModel.cs`
- Create: `DeployAssistant.Tests/Fakes/FakeDialogService.cs`
- Create: `DeployAssistant.Tests/Fakes/ImmediateUiDispatcher.cs`
- Create: `DeployAssistant.Tests/ViewModel/MetaDataViewModelDialogTests.cs`

- [ ] **Step 1: Create `DeployAssistant.Core/Services/DialogChoice.cs`.**

```csharp
namespace DeployAssistant.Services
{
    public enum DialogChoice
    {
        Yes,
        No,
        Cancel
    }
}
```

- [ ] **Step 2: Create `DeployAssistant.Core/Services/IDialogService.cs`.**

```csharp
namespace DeployAssistant.Services
{
    /// <summary>
    /// Abstracts user-facing dialog interactions so ViewModels are not coupled to
    /// System.Windows. WPF GUI provides WpfDialogService; CLI provides
    /// ConsoleDialogService; tests provide FakeDialogService.
    /// </summary>
    public interface IDialogService
    {
        /// <summary>Yes/No/Cancel confirmation dialog.</summary>
        DialogChoice Confirm(string title, string message, DialogChoice defaultChoice = DialogChoice.No);

        /// <summary>Informational dialog with OK button (or stderr write in CLI).</summary>
        void Inform(string title, string message);

        /// <summary>Folder picker. Returns null if the user cancels or the surface does not support it.</summary>
        string? PickFolder(string title, string? initialPath = null);

        /// <summary>Open a path in the platform shell (Explorer on Windows). No-op in headless contexts.</summary>
        void OpenInShell(string path);
    }
}
```

- [ ] **Step 3: Create `DeployAssistant.Core/Services/IUiDispatcher.cs`.**

```csharp
using System;

namespace DeployAssistant.Services
{
    /// <summary>
    /// Abstracts UI-thread marshalling. WPF impl wraps Application.Current.Dispatcher;
    /// CLI/test impls invoke synchronously.
    /// </summary>
    public interface IUiDispatcher
    {
        /// <summary>Posts the work to run on the UI thread without waiting.</summary>
        void Post(Action work);

        /// <summary>Runs the work on the UI thread, blocking until it completes.</summary>
        void Invoke(Action work);
    }
}
```

- [ ] **Step 4: Link the three new files into `Core.Standard.csproj`.**

Inside the appropriate `<ItemGroup>`, add:

```xml
    <Compile Include="../DeployAssistant.Core/Services/IDialogService.cs" Link="Services/IDialogService.cs" />
    <Compile Include="../DeployAssistant.Core/Services/IUiDispatcher.cs" Link="Services/IUiDispatcher.cs" />
    <Compile Include="../DeployAssistant.Core/Services/DialogChoice.cs" Link="Services/DialogChoice.cs" />
```

- [ ] **Step 5: Build Core to confirm the abstractions compile.**

```bash
dotnet build DeployAssistant.Core/DeployAssistant.Core.csproj
dotnet build DeployAssistant.Core.Standard/DeployAssistant.Core.Standard.csproj
```

Expected: both succeed.

- [ ] **Step 6: Delete the dead `ConfirmationCallback` property from `BackupManager`.**

Edit `DeployAssistant.Core/DataComponent/BackupManager.cs` line 34. Remove:

```csharp
        public Func<string, string, bool>? ConfirmationCallback { get; set; }
```

(and any blank-line cleanup). It's never wired by anyone. Confirm with:

```bash
grep -rn "_backupManager.ConfirmationCallback" DeployAssistant.Core/ DeployAssistant.Core.Standard/ DeployAssistant.ViewModel/ DeployAssistant/ | grep -v "/obj/" | grep -v "/bin/"
```

Expected: empty output.

- [ ] **Step 7: Delete the dead `ConfirmationCallback` property from `FileManager` (Core + Core.Standard variants).**

Edit `DeployAssistant.Core/DataComponent/FileManager.cs` line 56 and the equivalent line in `DeployAssistant.Core.Standard/DataComponent/FileManager.cs`. Same removal as step 6. Confirm via grep:

```bash
grep -rn "_fileManager.ConfirmationCallback" DeployAssistant.Core/ DeployAssistant.Core.Standard/ DeployAssistant.ViewModel/ DeployAssistant/
```

Expected: empty.

- [ ] **Step 8: Delete the dead `ConfirmationCallback` property from `MetaDataManager` (Core + Core.Standard variants); replace via constructor injection of `IDialogService`.**

In `DeployAssistant.Core/DataComponent/MetaDataManager.cs`:
- Delete lines 100-105 (the property and its doc comment).
- Add a constructor parameter `IDialogService dialogService`. The default constructor `public MetaDataManager()` is preserved (calls into a `NullDialogService` — see step 11). The new constructor:

```csharp
        private readonly IDialogService _dialogService;
        // ...
        public MetaDataManager() : this(new NullDialogService()) { }

        public MetaDataManager(IDialogService dialogService)
        {
            _dialogService = dialogService;
            _fileHandlerTool = new FileHandlerTool();
            _hashTool = new HashTool();
        }
```

In `Awake()`, replace `_settingManager.ConfirmationCallback = ConfirmationCallback;` with:

```csharp
            _settingManager.DialogService = _dialogService;
```

(and pass it to `MetaDataManager.RequestProjectUpdate` / `RequestProjectCleanRestore` via field access — those methods currently read `ConfirmationCallback`; switch to reading `_dialogService.Confirm(...)`).

Mirror in `DeployAssistant.Core.Standard/DataComponent/MetaDataManager.cs`.

- [ ] **Step 9: Update `SettingManager` to take `IDialogService` instead of `ConfirmationCallback`.**

Edit `DeployAssistant.Core/DataComponent/SettingManager.cs`. Replace the `ConfirmationCallback` property (around line 30) with:

```csharp
        public IDialogService DialogService { get; set; } = new NullDialogService();
```

Find the call site that uses `ConfirmationCallback?.Invoke(...)` and replace with `DialogService.Confirm(...)` returning `DialogChoice.Yes` / `No`.

- [ ] **Step 10: Create `DeployAssistant.Core/Services/NullDialogService.cs` (default for headless / unwired construction).**

```csharp
namespace DeployAssistant.Services
{
    /// <summary>
    /// Default IDialogService when no UI surface is available. Confirm always
    /// returns the supplied default; Inform / PickFolder / OpenInShell are no-ops.
    /// </summary>
    internal sealed class NullDialogService : IDialogService
    {
        public DialogChoice Confirm(string title, string message, DialogChoice defaultChoice = DialogChoice.No) => defaultChoice;
        public void Inform(string title, string message) { }
        public string? PickFolder(string title, string? initialPath = null) => null;
        public void OpenInShell(string path) { }
    }
}
```

Add the file to `Core.Standard.csproj` link list.

- [ ] **Step 11: Build everything to surface compile errors at every former `ConfirmationCallback` consumer.**

```bash
dotnet build DeployAssistant.sln
```

Fix each compile error by replacing `Func<string, string, bool>?` callback usage with `IDialogService.Confirm` calls returning `DialogChoice`.

- [ ] **Step 12: Refactor `MetaDataViewModel` to inject `IDialogService` and `IUiDispatcher`; remove direct WPF references.**

Audit:
```bash
grep -n "MessageBox\|FolderBrowserDialog\|Application\.Current\.Dispatcher\|Process\.Start\|UpdateLayout" \
    DeployAssistant.ViewModel/MetaDataViewModel.cs
```

For each `MessageBox.Show(...)`, replace with `_dialogService.Confirm(...)` or `_dialogService.Inform(...)`. For each `Application.Current?.Dispatcher.Invoke(...)`, replace with `_uiDispatcher.Invoke(...)`. Remove all `MainWindow?.UpdateLayout()` calls (gratuitous re-layout, not connected to abstraction).

Constructor change:

```csharp
    public class MetaDataViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogService;
        private readonly IUiDispatcher _uiDispatcher;

        public MetaDataViewModel(MetaDataManager metaDataManager,
                                 IDialogService dialogService,
                                 IUiDispatcher uiDispatcher)
        {
            _dialogService = dialogService;
            _uiDispatcher = uiDispatcher;
            // ... existing wiring
        }
```

- [ ] **Step 13: Repeat step 12 for `BackupViewModel`, `FileTrackViewModel`, `MetaFileDiffViewModel`.**

For `BackupViewModel.cs:229`'s `Process.Start("explorer.exe", exportPath)`, replace with `_dialogService.OpenInShell(exportPath)`.

- [ ] **Step 14: Build and surface upstream compile errors at the GUI/CLI ctor sites.**

```bash
dotnet build DeployAssistant.sln
```

Expected: failures at `DeployAssistant/View/*.xaml.cs` and `DeployAssistant.CLI/Program.cs` because the VM constructors changed signatures. Don't fix those upstream sites yet — that's Task 4 (GUI) and Task 5 (CLI). Instead, **temporarily** add a parameterless ctor overload on each VM that builds with `NullDialogService` + a synchronous fallback dispatcher so the GUI continues to compile through Tasks 3 → 4. Mark with `[Obsolete("Temporary scaffold — removed in Task 4")]`.

```csharp
        [Obsolete("Temporary scaffold — replaced by AppServices wiring in Task 4")]
        public MetaDataViewModel(MetaDataManager metaDataManager)
            : this(metaDataManager, new NullDialogService(), new SyncFallbackDispatcher())
        { }
```

Where `SyncFallbackDispatcher` is a temporary helper class in the ViewModel project's `Internal/` folder:

```csharp
        internal sealed class SyncFallbackDispatcher : IUiDispatcher
        {
            public void Post(Action work) => work?.Invoke();
            public void Invoke(Action work) => work?.Invoke();
        }
```

This scaffold is deleted in Task 4 step 6.

- [ ] **Step 15: Confirm full solution builds.**

```bash
dotnet build DeployAssistant.sln
```

Expected: GREEN.

- [ ] **Step 16: Create test fakes.**

`DeployAssistant.Tests/Fakes/FakeDialogService.cs`:

```csharp
using System.Collections.Generic;
using DeployAssistant.Services;

namespace DeployAssistant.Tests.Fakes
{
    /// <summary>
    /// Programmable IDialogService for headless tests.
    /// Queue Confirm answers via <see cref="EnqueueConfirm"/> / <see cref="EnqueueFolder"/>.
    /// Inspect <see cref="ShownInfos"/> / <see cref="OpenedShellPaths"/> for assertions.
    /// </summary>
    public sealed class FakeDialogService : IDialogService
    {
        private readonly Queue<DialogChoice> _confirmAnswers = new();
        private readonly Queue<string?> _folderAnswers = new();

        public List<(string Title, string Message)> ShownInfos { get; } = new();
        public List<string> OpenedShellPaths { get; } = new();

        public void EnqueueConfirm(DialogChoice answer) => _confirmAnswers.Enqueue(answer);
        public void EnqueueFolder(string? answer) => _folderAnswers.Enqueue(answer);

        public DialogChoice Confirm(string title, string message, DialogChoice defaultChoice = DialogChoice.No)
            => _confirmAnswers.Count > 0 ? _confirmAnswers.Dequeue() : defaultChoice;

        public void Inform(string title, string message) => ShownInfos.Add((title, message));

        public string? PickFolder(string title, string? initialPath = null)
            => _folderAnswers.Count > 0 ? _folderAnswers.Dequeue() : null;

        public void OpenInShell(string path) => OpenedShellPaths.Add(path);
    }
}
```

`DeployAssistant.Tests/Fakes/ImmediateUiDispatcher.cs`:

```csharp
using System;
using DeployAssistant.Services;

namespace DeployAssistant.Tests.Fakes
{
    /// <summary>Runs callbacks synchronously on the calling thread. Use for headless ViewModel tests.</summary>
    public sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public void Post(Action work) => work?.Invoke();
        public void Invoke(Action work) => work?.Invoke();
    }
}
```

- [ ] **Step 17: Write a headless ViewModel test that proves the seam works.**

`DeployAssistant.Tests/ViewModel/MetaDataViewModelDialogTests.cs`:

```csharp
using DeployAssistant.DataComponent;
using DeployAssistant.Services;
using DeployAssistant.Tests.Fakes;
using DeployAssistant.ViewModel;
using Xunit;

namespace DeployAssistant.Tests.ViewModel
{
    public class MetaDataViewModelDialogTests
    {
        [Fact]
        public void Construct_WithFakeDialogAndDispatcher_DoesNotTouchWpf()
        {
            // Given: a real MetaDataManager and the fake services.
            var manager = new MetaDataManager(new FakeDialogService());
            manager.Awake();

            var fakeDialog = new FakeDialogService();
            var dispatcher = new ImmediateUiDispatcher();

            // When: VM is constructed without a live WPF Application.
            var vm = new MetaDataViewModel(manager, fakeDialog, dispatcher);

            // Then: construction completes; no Application.Current dependency.
            Assert.NotNull(vm);
        }
    }
}
```

Run:

```bash
dotnet test DeployAssistant.Tests/DeployAssistant.Tests.csproj --filter "FullyQualifiedName~MetaDataViewModelDialogTests" -v normal
```

Expected: PASS. If it fails because `Application.Current` is touched somewhere, that's a real coupling site missed in step 12-13 — find it via stack trace and refactor.

- [ ] **Step 18: Run the full suite.**

```bash
dotnet test DeployAssistant.Tests/DeployAssistant.Tests.csproj
```

Expected: PASS.

- [ ] **Step 19: Commit.**

```bash
git add DeployAssistant.Core/Services/ \
        DeployAssistant.Core/DataComponent/MetaDataManager.cs \
        DeployAssistant.Core/DataComponent/BackupManager.cs \
        DeployAssistant.Core/DataComponent/FileManager.cs \
        DeployAssistant.Core/DataComponent/SettingManager.cs \
        DeployAssistant.Core.Standard/DeployAssistant.Core.Standard.csproj \
        DeployAssistant.Core.Standard/DataComponent/ \
        DeployAssistant.ViewModel/ \
        DeployAssistant.Tests/Fakes/ \
        DeployAssistant.Tests/ViewModel/
git commit -m "refactor: introduce IDialogService + IUiDispatcher seams; remove dead callbacks

Service seams (spec §5):
- IDialogService (Confirm/Inform/PickFolder/OpenInShell)
- IUiDispatcher (Post/Invoke)
- NullDialogService default for headless construction
- ViewModels (MetaData, Backup, FileTrack, MetaFileDiff) refactored to depend
  on the seams; gratuitous MainWindow.UpdateLayout() calls dropped.

B2 dead-callback removal (spec §6.1):
- Delete ConfirmationCallback from BackupManager / FileManager / MetaDataManager.
- SettingManager now takes IDialogService.

Headless ViewModel test (spec done-definition):
- FakeDialogService + ImmediateUiDispatcher fakes added.
- MetaDataViewModelDialogTests proves construction without a live
  WPF Application.

Temporary scaffold: each refactored VM keeps an [Obsolete] parameterless
ctor that wires NullDialogService + synchronous fallback dispatcher so the
GUI continues to compile through Task 4. The scaffold is deleted in Task 4.

Refs: spec §5, §6.1 (B2), §8 step 3."
```

- [ ] **Step 20: Open PR; wait for merge before Task 4.**

```bash
gh pr create --base master --head feature/target-framework-realignment \
    --title "refactor: IDialogService + IUiDispatcher seams + B2 callback removal" \
    --body "(see commit message)"
```

---

## Task 4 — Fold ViewModel into the GUI exe; apply R1 + R2 + R3

**Why this task:** The GUI is already at `net8.0-windows` after Task 0. This task absorbs `DeployAssistant.ViewModel/*` into the GUI exe, replaces `App.MetaDataManager` static singletons with an explicit `AppServices` composition root (R1), makes `ViewModelBase : IDisposable` for child-window cleanup (R2), and trims/deletes `IManager` (R3).

**Files:**
- Modify: `DeployAssistant/DeployAssistant.csproj` (remove `<ProjectReference>` to ViewModel; switch Core ref to Core.Standard)
- Move: all files in `DeployAssistant.ViewModel/` → `DeployAssistant/ViewModel/`
- Delete (Task 7): `DeployAssistant.ViewModel/DeployAssistant.ViewModel.csproj`
- Modify: `DeployAssistant.sln`
- Create: `DeployAssistant/Services/WpfDialogService.cs`
- Create: `DeployAssistant/Services/WpfUiDispatcher.cs`
- Create: `DeployAssistant/AppServices.cs`
- Modify: `DeployAssistant/App.xaml.cs`
- Modify: `DeployAssistant/View/MainWindow.xaml.cs`
- Modify: `DeployAssistant/View/IntegrityLogWindow.xaml.cs`
- Modify: `DeployAssistant/View/OverlapFileWindow.xaml.cs`
- Modify: `DeployAssistant/View/VersionDiffWindow.xaml.cs`
- Modify: `DeployAssistant/ViewModel/ViewModelBase.cs`
- Modify: `DeployAssistant.Core/Interfaces/IManager.cs`
- Modify: each `*Manager.cs` (delete empty `Awake` if R3 path chosen)

- [ ] **Step 1: Confirm GUI builds at net8 baseline.**

```bash
dotnet build DeployAssistant/DeployAssistant.csproj
```

Expected: PASS. (The retarget happened in Task 0; this is a sanity check before structural changes.)

- [ ] **Step 2 (intentionally blank).** Step skipped — retarget is already done in Task 0. Continue to Step 3.

- [ ] **Step 3: Move every `.cs` file from `DeployAssistant.ViewModel/` into `DeployAssistant/ViewModel/`.**

```bash
mkdir -p DeployAssistant/ViewModel
git mv DeployAssistant.ViewModel/*.cs DeployAssistant/ViewModel/
git mv DeployAssistant.ViewModel/Utils DeployAssistant/ViewModel/Utils
```

Update each moved `.cs`'s file-scoped namespace if necessary — they should already be `namespace DeployAssistant.ViewModel { ... }` and that namespace name doesn't change. Just the *project* containing them does.

- [ ] **Step 4: Remove the `<ProjectReference>` to `DeployAssistant.ViewModel.csproj` from `DeployAssistant.csproj`.**

Edit `DeployAssistant/DeployAssistant.csproj`. Delete:

```xml
    <ProjectReference Include="..\DeployAssistant.ViewModel\DeployAssistant.ViewModel.csproj" />
```

Keep the Core reference, but switch it from `DeployAssistant.Core` to `DeployAssistant.Core.Standard` (consistency with what Tests + CLI now reference; old Core directory is deleted in Task 7):

```xml
    <ProjectReference Include="..\DeployAssistant.Core.Standard\DeployAssistant.Core.Standard.csproj" />
```

- [ ] **Step 5: Build the GUI; expect compile errors at View code-behinds that referenced the now-internal-namespace ViewModel types.**

```bash
dotnet build DeployAssistant/DeployAssistant.csproj
```

Expected: green (the namespace `DeployAssistant.ViewModel` is preserved; only the project boundary changed). If errors, they're typically `using DeployAssistant.ViewModel;` already-present statements pointing to types now in the same assembly — those continue to work.

- [ ] **Step 6: Create `DeployAssistant/Services/WpfDialogService.cs`.**

```csharp
using System.Diagnostics;
using System.Windows;
using DeployAssistant.Services;
using Microsoft.Win32;

namespace DeployAssistant.Services.Wpf
{
    public sealed class WpfDialogService : IDialogService
    {
        public DialogChoice Confirm(string title, string message, DialogChoice defaultChoice = DialogChoice.No)
        {
            var defaultBtn = defaultChoice switch
            {
                DialogChoice.Yes => MessageBoxResult.Yes,
                DialogChoice.Cancel => MessageBoxResult.Cancel,
                _ => MessageBoxResult.No
            };
            var result = MessageBox.Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question, defaultBtn);
            return result switch
            {
                MessageBoxResult.Yes => DialogChoice.Yes,
                MessageBoxResult.No => DialogChoice.No,
                _ => DialogChoice.Cancel
            };
        }

        public void Inform(string title, string message)
            => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

        public string? PickFolder(string title, string? initialPath = null)
        {
            var dlg = new OpenFolderDialog { Title = title };
            if (!string.IsNullOrEmpty(initialPath)) dlg.InitialDirectory = initialPath;
            return dlg.ShowDialog() == true ? dlg.FolderName : null;
        }

        public void OpenInShell(string path)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true }); }
            catch { /* shell invocation must not crash the app */ }
        }
    }
}
```

- [ ] **Step 7: Create `DeployAssistant/Services/WpfUiDispatcher.cs`.**

```csharp
using System;
using System.Windows;
using System.Windows.Threading;
using DeployAssistant.Services;

namespace DeployAssistant.Services.Wpf
{
    public sealed class WpfUiDispatcher : IUiDispatcher
    {
        private readonly Dispatcher _dispatcher;
        public WpfUiDispatcher(Dispatcher dispatcher) => _dispatcher = dispatcher;
        public WpfUiDispatcher() : this(Application.Current.Dispatcher) { }

        public void Post(Action work) => _dispatcher.BeginInvoke(work);
        public void Invoke(Action work) => _dispatcher.Invoke(work);
    }
}
```

- [ ] **Step 8: Create `DeployAssistant/AppServices.cs`.**

```csharp
using DeployAssistant.DataComponent;
using DeployAssistant.Services;
using DeployAssistant.Services.Wpf;

namespace DeployAssistant
{
    /// <summary>
    /// Composition root for the WPF GUI. Constructed once in App.OnStartup and
    /// passed down to MainWindow, then to child windows. Replaces the old
    /// App.MetaDataManager static singleton.
    /// </summary>
    public sealed class AppServices
    {
        public MetaDataManager MetaDataManager { get; }
        public IDialogService DialogService { get; }
        public IUiDispatcher UiDispatcher { get; }

        public AppServices()
        {
            DialogService = new WpfDialogService();
            UiDispatcher = new WpfUiDispatcher();
            MetaDataManager = new MetaDataManager(DialogService);
            MetaDataManager.Awake();
        }
    }
}
```

- [ ] **Step 9: Replace `App.xaml.cs` with the composition-root form.**

```csharp
namespace DeployAssistant
{
    public partial class App : System.Windows.Application
    {
        public AppServices? Services { get; private set; }

        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            base.OnStartup(e);
            Services = new AppServices();
        }
    }
}
```

- [ ] **Step 10: Update `MainWindow.xaml.cs` to receive `AppServices` from `App.Current`.**

Find `App.MetaDataManager` references in `MainWindow.xaml.cs` and replace with `((App)Application.Current).Services!.MetaDataManager`. Same pattern for `IntegrityLogWindow.xaml.cs`, `OverlapFileWindow.xaml.cs`, `VersionDiffWindow.xaml.cs`.

ViewModel construction sites also need the new ctor parameters:
```csharp
var services = ((App)Application.Current).Services!;
var vm = new MetaDataViewModel(services.MetaDataManager, services.DialogService, services.UiDispatcher);
```

- [ ] **Step 11: Delete the temporary `[Obsolete]` parameterless ctors and `SyncFallbackDispatcher` introduced in Task 3 step 14.**

Find with:
```bash
grep -rn "SyncFallbackDispatcher\|Temporary scaffold" DeployAssistant/ DeployAssistant.ViewModel/ 2>/dev/null
```

Delete each.

- [ ] **Step 12: Make `ViewModelBase : IDisposable` for child-window cleanup (R2).**

Replace `DeployAssistant/ViewModel/ViewModelBase.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DeployAssistant.ViewModel
{
    public class ViewModelBase : INotifyPropertyChanged, IDisposable
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly List<Action> _unsubscribers = new();
        private bool _disposed;

        protected virtual void OnPropertyChanged([CallerMemberName] string? property = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// Registers an unsubscribe action that runs on Dispose. Use when subscribing
        /// to long-lived events (e.g. MetaDataManager events) from a per-window VM.
        /// </summary>
        protected void TrackUnsubscribe(Action unsubscribe) => _unsubscribers.Add(unsubscribe);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var u in _unsubscribers) u();
            _unsubscribers.Clear();
            GC.SuppressFinalize(this);
        }
    }
}
```

- [ ] **Step 13: Wire `TrackUnsubscribe` calls in the four refactored ViewModels.**

For each `MetaDataManager.SomeEvent += SomeHandler;` line in the four VMs, add `TrackUnsubscribe(() => MetaDataManager.SomeEvent -= SomeHandler);` immediately below. Audit:

```bash
grep -n "EventHandler += \| += _" \
    DeployAssistant/ViewModel/MetaDataViewModel.cs \
    DeployAssistant/ViewModel/BackupViewModel.cs \
    DeployAssistant/ViewModel/FileTrackViewModel.cs \
    DeployAssistant/ViewModel/MetaFileDiffViewModel.cs
```

- [ ] **Step 14: Make child windows call `Dispose` on close.**

Pattern, applied to `OverlapFileWindow.xaml.cs`, `VersionDiffWindow.xaml.cs`, `IntegrityLogWindow.xaml.cs`:

```csharp
        public OverlapFileWindow(...) {
            InitializeComponent();
            DataContext = vm;
            Closed += (_, _) => (vm as IDisposable)?.Dispose();
        }
```

- [ ] **Step 15: Apply R3 — trim or delete `IManager`.**

Audit polymorphic use:

```bash
grep -rn ": IManager\| IManager " DeployAssistant.Core/ DeployAssistant.Core.Standard/ DeployAssistant.ViewModel/ DeployAssistant/ DeployAssistant.CLI/ DeployAssistant.Tests/ | grep -v "/obj/" | grep -v "/bin/"
```

If no consumer holds `IManager` polymorphically (likely), **delete** `DeployAssistant.Core/Interfaces/IManager.cs` and remove the `: IManager` from each manager class declaration. Also delete the empty `public void Awake() { }` bodies in `BackupManager`, `ExportManager`, `FileManager`, `UpdateManager`. Keep `MetaDataManager.Awake()` and `SettingManager.Awake()` — they have real bodies.

In `MetaDataManager.Awake()`, delete the now-no-op calls `_backupManager.Awake();` `_updateManager.Awake();` (only `_settingManager.Awake();` remains).

Delete the `.csproj` link entry for `IManager.cs` in `Core.Standard.csproj`.

- [ ] **Step 16: Build everything.**

```bash
dotnet build DeployAssistant.sln
```

Expected: GREEN.

- [ ] **Step 17: Run tests.**

```bash
dotnet test DeployAssistant.Tests/DeployAssistant.Tests.csproj
```

Expected: GREEN. The headless `MetaDataViewModelDialogTests` continues to pass.

- [ ] **Step 18: Smoke-test the WPF GUI.**

```bash
dotnet run --project DeployAssistant/DeployAssistant.csproj
```

Manually exercise: open a project, retrieve metadata, stage a change, view a diff window, close it. Confirm no stack traces, no leaked subscriptions visible in Visual Studio's diagnostics if you have it attached.

- [ ] **Step 19: Commit.**

```bash
git add DeployAssistant/ DeployAssistant.Core/ DeployAssistant.Core.Standard/ DeployAssistant.ViewModel/ DeployAssistant.sln
git commit -m "refactor(gui): fold ViewModel; AppServices + R2 + R3

- Move all DeployAssistant.ViewModel/*.cs into DeployAssistant/ViewModel/.
  Project DeployAssistant.ViewModel.csproj is no longer referenced (the
  empty project directory is deleted in Task 7).
- Switch GUI's Core reference from DeployAssistant.Core to
  DeployAssistant.Core.Standard (consistency with Tests + CLI; old Core
  directory is deleted in Task 7).
- R1: AppServices composition root replaces App.MetaDataManager static
  singleton. Constructed once in App.OnStartup; passed to MainWindow;
  child windows pull from ((App)Application.Current).Services.
- WpfDialogService and WpfUiDispatcher implement the seams.
  WPF OpenFolderDialog (.NET 8 native) replaces WinForms FolderBrowserDialog.
- R2: ViewModelBase now IDisposable; tracks subscriptions; child windows
  call Dispose on Closed — fixes latent event-handler leak.
- R3: IManager interface deleted; empty Awake() bodies on 4 managers
  removed. Only MetaDataManager.Awake() and SettingManager.Awake() remain.

(Framework retarget from net10 → net8 happened in Task 0.)

Refs: spec §5, §6.2, §8 step 4."
```

- [ ] **Step 20: Open PR; wait for merge before Task 5.**

---

## Task 5 — Retarget CLI to `net472`

**Files:**
- Modify: `DeployAssistant.CLI/DeployAssistant.CLI.csproj`
- Create: `DeployAssistant.CLI/ConsoleDialogService.cs`
- Create: `DeployAssistant.CLI/ImmediateUiDispatcher.cs`
- Modify: `DeployAssistant.CLI/Program.cs`
- Modify: `.github/workflows/cli-smoke-test.yml`

- [ ] **Step 1: Verify current Spectre.Console version supports netstandard2.0.**

The CLI csproj pins `Spectre.Console 0.49.1`. Per https://www.nuget.org/packages/Spectre.Console/0.49.1, that version multi-targets `netstandard2.0;net6.0;net7.0` so it's net472-compatible. Newer (0.50+) versions drop netstandard2.0 — keep the pin.

- [ ] **Step 2: Retarget `DeployAssistant.CLI.csproj` to net472.**

Replace the file contents:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net472</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <AssemblyName>deployassistant</AssemblyName>
    <RootNamespace>DeployAssistant.CLI</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Spectre.Console" Version="0.49.1" />
    <PackageReference Include="System.Memory" Version="4.5.5" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\DeployAssistant.Core.Standard\DeployAssistant.Core.Standard.csproj" />
  </ItemGroup>

</Project>
```

(Drop `<ImplicitUsings>` — net472 + ImplicitUsings is awkward. Use explicit `using` statements.)

- [ ] **Step 3: Create `DeployAssistant.CLI/ConsoleDialogService.cs`.**

```csharp
using System;
using DeployAssistant.Services;

namespace DeployAssistant.CLI
{
    public sealed class ConsoleDialogService : IDialogService
    {
        private readonly bool _autoYes;

        public ConsoleDialogService(bool autoYes = false) => _autoYes = autoYes;

        public DialogChoice Confirm(string title, string message, DialogChoice defaultChoice = DialogChoice.No)
        {
            if (_autoYes) return DialogChoice.Yes;
            Console.Error.Write($"[{title}] {message} [y/N/c]: ");
            var line = Console.ReadLine()?.Trim().ToLowerInvariant();
            return line switch
            {
                "y" or "yes" => DialogChoice.Yes,
                "c" or "cancel" => DialogChoice.Cancel,
                _ => defaultChoice
            };
        }

        public void Inform(string title, string message)
            => Console.Error.WriteLine($"[{title}] {message}");

        public string? PickFolder(string title, string? initialPath = null)
        {
            Console.Error.WriteLine($"[{title}] CLI cannot pick a folder interactively. Pass the path as a command argument.");
            return null;
        }

        public void OpenInShell(string path)
            => Console.Error.WriteLine($"(would open in shell: {path})");
    }
}
```

- [ ] **Step 4: Create `DeployAssistant.CLI/ImmediateUiDispatcher.cs`.**

```csharp
using System;
using DeployAssistant.Services;

namespace DeployAssistant.CLI
{
    internal sealed class ImmediateUiDispatcher : IUiDispatcher
    {
        public void Post(Action work) => work?.Invoke();
        public void Invoke(Action work) => work?.Invoke();
    }
}
```

- [ ] **Step 5: Update `Program.cs` to wire the seams.**

Find `Program.Main` and ensure any `MetaDataManager` construction takes the new constructor:

```csharp
            var dialog = new ConsoleDialogService(autoYes: args.Contains("--yes"));
            var dispatcher = new ImmediateUiDispatcher();
            var manager = new MetaDataManager(dialog);
            manager.Awake();
            // ... existing CLI command dispatch
```

- [ ] **Step 6: Build the CLI.**

```bash
dotnet build DeployAssistant.CLI/DeployAssistant.CLI.csproj
```

Expected: PASS. If `dotnet` complains about missing 4.7.2 reference assemblies on Windows, install **.NET Framework 4.8 Developer Pack** (which contains 4.7.2 reference assemblies) and retry.

- [ ] **Step 7: Publish the CLI and smoke-test locally.**

```bash
dotnet publish DeployAssistant.CLI/DeployAssistant.CLI.csproj -c Release -o ./publish/DeployAssistant.CLI
ls publish/DeployAssistant.CLI/
./publish/DeployAssistant.CLI/deployassistant.exe
echo "exit code: $?"
./publish/DeployAssistant.CLI/deployassistant.exe --help
echo "exit code: $?"
./publish/DeployAssistant.CLI/deployassistant.exe _bogus_
echo "exit code: $?"
```

Expected: no-args → exit 0 with "DeployAssistant" in output; `--help` → exit 0; unknown → exit 1.

- [ ] **Step 8: Update `.github/workflows/cli-smoke-test.yml` publish step.**

`setup-dotnet` is already at 8.0.x after Task 0. Replace the publish step:

```yaml
      - name: Publish CLI (self-contained, win-x64)
        run: |
          dotnet publish DeployAssistant.CLI/DeployAssistant.CLI.csproj `
            -c Release `
            -r win-x64 `
            --self-contained true `
            -p:PublishSingleFile=true `
            -o ./publish/DeployAssistant.CLI
```

with:

```yaml
      - name: Publish CLI (net472 framework-dependent)
        run: |
          dotnet publish DeployAssistant.CLI/DeployAssistant.CLI.csproj `
            -c Release `
            -o ./publish/DeployAssistant.CLI
```

(`--self-contained` and `-p:PublishSingleFile=true` do not apply to .NET Framework. The Windows runner has 4.7.2 targeting packs preinstalled.)

- [ ] **Step 9: Commit.**

```bash
git add DeployAssistant.CLI/ .github/workflows/cli-smoke-test.yml
git commit -m "refactor(cli): retarget to net472; ConsoleDialogService + ImmediateUiDispatcher

- DeployAssistant.CLI now targets net472 (was net10.0).
- Spectre.Console pinned to 0.49.1 (last netstandard2.0-supporting release).
- ConsoleDialogService + ImmediateUiDispatcher implement the Core service seams.
- Program.cs wires the new MetaDataManager(IDialogService) constructor.
  --yes flag enables auto-confirm for non-interactive runs.
- cli-smoke-test.yml: setup-dotnet 10.0 → 8.0; drop --self-contained and
  PublishSingleFile (not applicable to .NET Framework).

Ships as: deployassistant.exe + DeployAssistant.Core.Standard.dll + NuGet
runtime DLLs (Spectre.Console, System.Text.Json, etc.) — total ~5-7 MB,
runs on machines with .NET Framework 4.7.2+ preinstalled (Win10 1803+).

Refs: spec §3, §7, §8 step 5."
```

- [ ] **Step 10: Push and confirm CI's `cli-smoke-test.yml` passes.**

If the runner cannot find net472 reference assemblies, fall back per spec §9: switch the CLI build step to `microsoft/setup-msbuild@v2` + `msbuild`. Open PR after CI is green.

---

## Task 6 — Tests project tidy-up + reference shift

**Why this task:** Tests are already at `net8.0-windows` after Task 0 and already reference `DeployAssistant.Core.Standard` after Task 2. This task drops the now-unused `<UseWindowsForms>` flag and adds a reference to the merged `DeployAssistant` exe so ViewModel-surface tests can find the absorbed types.

**Files:**
- Modify: `DeployAssistant.Tests/DeployAssistant.Tests.csproj`

- [ ] **Step 1: Edit tests csproj.**

The current state after Tasks 0 + 2 is approximately:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>True</UseWindowsForms>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\DeployAssistant.Core.Standard\DeployAssistant.Core.Standard.csproj" />
    <ProjectReference Include="..\DeployAssistant.ViewModel\DeployAssistant.ViewModel.csproj" />
  </ItemGroup>
</Project>
```

Apply two changes:

1. Delete `<UseWindowsForms>True</UseWindowsForms>` — WinForms isn't used anywhere after PR #20 + Task 4.
2. Replace the `DeployAssistant.ViewModel.csproj` reference with the merged `DeployAssistant.csproj`:

```xml
    <ProjectReference Include="..\DeployAssistant\DeployAssistant.csproj" />
```

(The Core.Standard reference stays as-is; Task 7 will rename Core.Standard → Core and the path will update then.)

- [ ] **Step 2: Build tests.**

```bash
dotnet build DeployAssistant.Tests/DeployAssistant.Tests.csproj
```

Expected: PASS.

- [ ] **Step 3: Run tests.**

```bash
dotnet test DeployAssistant.Tests/DeployAssistant.Tests.csproj
```

Expected: GREEN, including the headless ViewModel test.

- [ ] **Step 4: Commit.**

```bash
git add DeployAssistant.Tests/DeployAssistant.Tests.csproj
git commit -m "refactor(tests): drop UseWindowsForms; reference merged GUI exe

- UseWindowsForms removed (WinForms is no longer used in any project
  after PR #20 + Task 4).
- ProjectReference shifts from DeployAssistant.ViewModel.csproj (deleted
  in Task 4 step 3 by file-move; csproj entry deleted in Task 7) to the
  merged DeployAssistant.csproj.
- Tests target was retargeted to net8.0-windows in Task 0; no change here.

Refs: spec §8 step 6."
```

---

## Task 7 — Rename Core.Standard → Core; delete dead projects and workflow

**Why this task:** The realignment's terminal goal — one Core project, four projects total, no sync workflow.

**Files:**
- Rename: `DeployAssistant.Core.Standard/` → `DeployAssistant.Core/` (delete the old Core first)
- Delete: `DeployAssistant.ViewModel/` (now empty — verify before deleting)
- Delete: `.github/workflows/sync-core-standard.yml`
- Modify: `DeployAssistant.sln`
- Modify: csproj refs in `DeployAssistant/`, `DeployAssistant.CLI/`, `DeployAssistant.Tests/`

- [ ] **Step 1: Confirm old Core directory has no untracked changes that must be preserved.**

```bash
git status DeployAssistant.Core/
```

Expected: clean. If anything is uncommitted, investigate before proceeding.

- [ ] **Step 2: Delete the old `DeployAssistant.Core/` directory.**

```bash
git rm -r DeployAssistant.Core/
```

- [ ] **Step 3: Confirm `DeployAssistant.ViewModel/` is empty (all `.cs` moved in Task 4 step 3).**

```bash
ls DeployAssistant.ViewModel/
```

Expected: only the `.csproj` and `obj/`/`bin/` dirs. If any `.cs` remains, that's an oversight from Task 4 — `git mv` it now.

- [ ] **Step 4: Delete `DeployAssistant.ViewModel/`.**

```bash
git rm -r DeployAssistant.ViewModel/
```

- [ ] **Step 5: Rename `DeployAssistant.Core.Standard/` to `DeployAssistant.Core/`.**

```bash
git mv DeployAssistant.Core.Standard DeployAssistant.Core
```

- [ ] **Step 6: Rename the csproj inside.**

```bash
git mv DeployAssistant.Core/DeployAssistant.Core.Standard.csproj DeployAssistant.Core/DeployAssistant.Core.csproj
```

- [ ] **Step 7: Update the renamed csproj — change `AssemblyName` and the `<Compile Include="../DeployAssistant.Core/...">` paths inside the link list.**

Open `DeployAssistant.Core/DeployAssistant.Core.csproj` and:
- Change `<AssemblyName>DeployAssistant.Core.Standard</AssemblyName>` → `<AssemblyName>DeployAssistant.Core</AssemblyName>` (or remove the line — it'll default to the csproj name).
- All the `<Compile Include="../DeployAssistant.Core/Interfaces/IManager.cs" Link="..." />`-style entries point to a directory that **no longer exists** because old Core was deleted. The linked files were physically inside the old Core; they're orphaned now.

The fix: physically move the linked files from old-Core's tree (already deleted) — wait, this won't work because Step 2 deleted old Core. The correct approach is:

  **Reorder steps 2 and 5-7**: First *physically move* every linked file from old `DeployAssistant.Core/` into the new (Core.Standard-rename → Core) tree as actual residents (not links), then delete the old directory.

Concretely (revised order — you should follow this rather than the surface order above):

```bash
# (Pre-step 2): Materialize linked files into Core.Standard's own tree.
# Find each <Compile Include="../DeployAssistant.Core/..." Link="X" /> and
# physically move the source from ../DeployAssistant.Core/X to ./X relative
# to Core.Standard/.

# Example for one file:
mkdir -p DeployAssistant.Core.Standard/Interfaces
git mv DeployAssistant.Core/Interfaces/IProjectData.cs DeployAssistant.Core.Standard/Interfaces/IProjectData.cs
git mv DeployAssistant.Core/Interfaces/IProjectDataIdentity.cs DeployAssistant.Core.Standard/Interfaces/IProjectDataIdentity.cs
git mv DeployAssistant.Core/Interfaces/IProjectDataContent.cs DeployAssistant.Core.Standard/Interfaces/IProjectDataContent.cs
# ... repeat for every <Compile Include="..." Link="..." /> entry in the csproj.

# Then in the csproj, replace each:
#     <Compile Include="../DeployAssistant.Core/Foo.cs" Link="Foo.cs" />
# with the file simply being part of the project (no entry needed — SDK auto-includes).

# After that, the old DeployAssistant.Core/ directory contains only files that
# were Core-specific (the ones never linked into Standard, e.g. the rich
# MetaDataManager / FileManager / FileHandlerTool / HashTool variants).
# Verify by inspection — those are exactly the variants we are *replacing* with
# Core.Standard's versions, so they go too:
git rm -r DeployAssistant.Core/

# Finally, rename Standard → Core:
git mv DeployAssistant.Core.Standard DeployAssistant.Core
git mv DeployAssistant.Core/DeployAssistant.Core.Standard.csproj DeployAssistant.Core/DeployAssistant.Core.csproj
```

The csproj after this round trip should have **no** `<Compile Include="../...">` link entries — every source file is local.

- [ ] **Step 8: Update every `<ProjectReference>` in the solution that points at `Core.Standard.csproj` to point at the renamed `Core.csproj`.**

```bash
grep -rln "DeployAssistant.Core.Standard.csproj" DeployAssistant/ DeployAssistant.CLI/ DeployAssistant.Tests/
```

For each match, edit the csproj and replace:

```xml
    <ProjectReference Include="..\DeployAssistant.Core.Standard\DeployAssistant.Core.Standard.csproj" />
```

with:

```xml
    <ProjectReference Include="..\DeployAssistant.Core\DeployAssistant.Core.csproj" />
```

- [ ] **Step 9: Update `DeployAssistant.sln`.**

The `.sln` has GUID-keyed entries for each project. Open it in VS or edit it textually:

```bash
grep -n "DeployAssistant\." DeployAssistant.sln | head
```

Remove the lines for `DeployAssistant.Core.Standard` and `DeployAssistant.ViewModel` (project entries + their build configurations). Update the path for the renamed Core if necessary.

Easier alternative — let `dotnet sln` do it:

```bash
dotnet sln DeployAssistant.sln remove DeployAssistant.Core.Standard/DeployAssistant.Core.Standard.csproj 2>/dev/null || true
dotnet sln DeployAssistant.sln remove DeployAssistant.ViewModel/DeployAssistant.ViewModel.csproj 2>/dev/null || true
dotnet sln DeployAssistant.sln remove DeployAssistant.Core/DeployAssistant.Core.csproj 2>/dev/null || true
dotnet sln DeployAssistant.sln add DeployAssistant.Core/DeployAssistant.Core.csproj
```

- [ ] **Step 10: Delete `.github/workflows/sync-core-standard.yml`.**

```bash
git rm .github/workflows/sync-core-standard.yml
```

- [ ] **Step 11: Build the entire solution.**

```bash
dotnet build DeployAssistant.sln
```

Expected: GREEN. If there are stale `using DeployAssistant.Core.Standard;` namespace imports anywhere, the namespace was always `DeployAssistant`, so this is unlikely — but if seen, fix them.

- [ ] **Step 12: Run tests.**

```bash
dotnet test DeployAssistant.Tests/DeployAssistant.Tests.csproj
```

Expected: GREEN.

- [ ] **Step 13: Smoke-test CLI publish + run again.**

```bash
dotnet publish DeployAssistant.CLI/DeployAssistant.CLI.csproj -c Release -o ./publish/DeployAssistant.CLI
./publish/DeployAssistant.CLI/deployassistant.exe --help
```

Expected: exit 0.

- [ ] **Step 14: Smoke-test the GUI.**

```bash
dotnet run --project DeployAssistant/DeployAssistant.csproj
```

Open a project, retrieve, stage, exit. No regressions.

- [ ] **Step 15: Commit.**

```bash
git add -A
git commit -m "chore: rename Core.Standard to Core; delete dead projects + sync workflow

- Materialize Core.Standard's linked files into its own tree.
- Delete old DeployAssistant.Core/ (the net10.0-windows variant) — its
  rich-Core MetaDataManager/FileManager/FileHandlerTool/HashTool variants
  are no longer needed; Core.Standard's variants are now the only Core.
- Rename DeployAssistant.Core.Standard/ → DeployAssistant.Core/ and the
  csproj inside it. AssemblyName updated.
- Delete empty DeployAssistant.ViewModel/ directory and csproj.
- Update solution and all <ProjectReference> entries.
- Delete .github/workflows/sync-core-standard.yml — no second project to sync.

Project count: 6 → 4 (Core, GUI, CLI, Tests).

Refs: spec §3, §8 step 7."
```

- [ ] **Step 16: Open PR; wait for green CI; merge before Task 8.**

---

## Task 8 — Update `spec.md` and `CLAUDE.md`

**Files:**
- Modify: `spec.md` (sections 2, 3, 5, 8.1)
- Modify: `CLAUDE.md`

- [ ] **Step 1: Update `spec.md` §2 (Tech Stack).**

Replace the `.NET 10.0 (Windows) for WPF/ViewModel/Core; .NET 8.0 for CLI; .NET Standard 2.0 for Core.Standard` cell with:

```
| Language | C# 14 syntax via <LangVersion>latest</LangVersion> |
| Frameworks | .NET 8.0-windows (GUI / Tests), .NET 4.7.2 (CLI), .NET Standard 2.0 (Core) |
```

Drop the "Why two Core libraries?" subsection. Drop the namespace-irregularities and `LogManager`/empty-`IManager` notes if those tech-debt items are addressed by the realignment.

- [ ] **Step 2: Update `spec.md` §3 (Project Layout).**

Replace the project-layout tree with the four-project version from spec doc §3 ([docs/superpowers/specs/...md](docs/superpowers/specs/2026-05-06-target-framework-realignment-design.md) §3). Drop references to `DeployAssistant.Core.Standard/` and `DeployAssistant.ViewModel/`.

- [ ] **Step 3: Update `spec.md` §5 (Interfaces).**

Replace the `IProjectData` documentation with the new `IProjectDataIdentity` + `IProjectDataContent` split. Note that `IProjectData` is preserved as the umbrella for back-compat. Remove `IManager` from the section if it was deleted.

- [ ] **Step 4: Update `spec.md` §8.1 (MetaDataManager wiring).**

The duplicate `_updateManager.Awake()` line is gone. The empty child-Awake calls are gone (only `_settingManager.Awake()` remains). The `ConfirmationCallback` properties on Backup/File/MetaData managers are gone — replaced by `IDialogService` injection on `MetaDataManager` and `SettingManager`.

- [ ] **Step 5: Update `CLAUDE.md`.**

Remove the "Two Core libraries" section. Replace with:

```markdown
## Single Core, four projects

`DeployAssistant.Core` is one netstandard2.0 library consumed by both the
net8.0-windows GUI (`DeployAssistant`, which now owns the ViewModels) and
the net472 CLI (`DeployAssistant.CLI`). Tests target net8.0-windows.
The previous Core.Standard / ViewModel / sync-core-standard workflow is gone.
```

Update the "SDK requirement" section: now needs **.NET 8 SDK** + **.NET Framework 4.8 Developer Pack** (which contains 4.7.2 reference assemblies). Drop the .NET 10 references.

Update the "CLI specifics" section: still mentions Spectre.Console; add that the binary now ships as a net472 framework-dependent app — `deployassistant.exe` + DLLs in a single folder, no runtime install needed beyond what Win10 1803+ ships with.

- [ ] **Step 6: Build / test / smoke once more for sanity.**

```bash
dotnet build DeployAssistant.sln
dotnet test DeployAssistant.Tests/DeployAssistant.Tests.csproj
```

Expected: GREEN.

- [ ] **Step 7: Commit.**

```bash
git add spec.md CLAUDE.md
git commit -m "docs: update spec.md and CLAUDE.md for the post-realignment architecture

- spec.md §2 (Tech Stack), §3 (Project Layout), §5 (Interfaces), §8.1 (wiring).
- Removes 'two Core libraries' section.
- Documents new IProjectDataIdentity / IProjectDataContent split.
- CLAUDE.md SDK requirement now .NET 8 + .NET Framework 4.8 Developer Pack
  (was: .NET 10).

Refs: spec §8 step 8."
```

- [ ] **Step 8: Final PR — merge to master.**

```bash
gh pr create --base master --head feature/target-framework-realignment \
    --title "docs: post-realignment spec.md + CLAUDE.md update" \
    --body "Closes the realignment effort. After this lands, the branch can be deleted."
```

After merge:

```bash
git checkout master
git pull --ff-only
git branch -d feature/target-framework-realignment
git push origin --delete feature/target-framework-realignment
```

Delete the `auto/sync-core-standard` remote branch as well — it's the working area for the deleted workflow.

---

## Done definition (across all tasks)

- `dotnet build DeployAssistant.sln` clean across the four remaining projects.
- `dotnet test` GREEN, including the headless `MetaDataViewModelDialogTests`.
- CI's `cli-smoke-test.yml` GREEN against the net472 CLI.
- WPF GUI launches and round-trips a project end-to-end.
- `spec.md` and `CLAUDE.md` reflect the new architecture.
- Repo no longer contains `DeployAssistant.Core/` (old net10), `DeployAssistant.Core.Standard/`, `DeployAssistant.ViewModel/`, or `.github/workflows/sync-core-standard.yml`.
