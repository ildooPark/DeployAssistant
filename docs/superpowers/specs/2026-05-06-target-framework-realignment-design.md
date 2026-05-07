# Target-Framework Realignment — Design

**Date:** 2026-05-06
**Status:** Approved (brainstorming → ready for implementation plan)
**Owner:** ildooPark
**Revision:** 2 — incorporates SOLID review findings (see §6).

## 1. Context

Current targets (post-PR #20):

| Project | Target |
|---|---|
| `DeployAssistant.Core` | `net10.0-windows` |
| `DeployAssistant.Core.Standard` | `netstandard2.0` (links files from Core; provides platform-neutral variants of `MetaDataManager`, `FileManager`, `FileHandlerTool`, `HashTool`, plus `CompatHelper` shim) |
| `DeployAssistant.ViewModel` | `net10.0-windows` |
| `DeployAssistant` (WPF GUI) | `net10.0-windows` |
| `DeployAssistant.CLI` | `net8.0` |
| `DeployAssistant.Tests` | `net10.0-windows` |

Two pain points motivate this change:

- **CLI deployment.** The CLI must run as a small `.exe` + Core `.dll` on Windows machines that do not have .NET 8 runtime installed, and may eventually be loaded as a plugin into a .NET Framework host process.
- **Sync drift between the two Core projects.** Core.Standard mirrors Core via `<Compile Include="..\..." Link="..." />` plus a CI workflow (`sync-core-standard.yml`). Drift has occurred in practice (PRs #13, #16; the metafile-diff methods from #20 and the missed `UpdateIgnoreListEventHandler` dispatch from #17 are both visible in `git diff Core/DataComponent/MetaDataManager.cs Core.Standard/DataComponent/MetaDataManager.cs`). Tests run against Core, not Core.Standard, so drift is not caught automatically.

Audit finding: **Core has no Windows-only API calls today** (`grep -E 'MessageBox|System\.Windows|Forms|Dispatcher|Application\.'` in `DeployAssistant.Core/` returns zero matches). The remaining differences between Core and Core.Standard are:

- `Path.GetRelativePath` (Core) vs `PathCompat.GetRelativePath` (Standard, in `CompatHelper.cs`).
- A handful of methods added in PR #20 not yet linked into Standard.
- An event-dispatch fix from PR #17 not propagated.

The original `spec.md §3` rationale ("Core targets net10.0-windows because it can use WPF/WinForms-dependent APIs") no longer reflects the code.

## 2. Decision

**Option C: Single netstandard2.0 Core. Project count drops from 6 → 4.**

Rejected alternatives:
- **A. Multi-target Core (`net8.0-windows;netstandard2.0`).** Real but small upside (`MD5.HashData` static, `FrozenDictionary`); Core's hot path is I/O-bound, so the BCL gains do not move the needle. Adds `#if NET8_0_OR_GREATER` ceremony.
- **B. "Rich Core + ported Core" (current pattern, retargeted).** Worsens the existing drift problem. Tests still don't cover the binary the CLI consumes. Two source trees for one workload.

C wins because (1) it eliminates an entire class of bug (sync drift) by deleting the second project, (2) tests now exercise the same binary the CLI ships, and (3) the architectural pressure to keep UI concerns out of Core is welcome — `spec.md` already calls that out as tech debt.

## 3. End-State Architecture

```
DeployAssistant.sln
│
├── DeployAssistant.Core              netstandard2.0
│     LangVersion=latest
│     PackageReferences:
│       DocumentFormat.OpenXml 3.0.2
│       System.Text.Json 8.0.5
│       System.Buffers 4.5.1
│       System.IO.Packaging 8.0.1
│
├── DeployAssistant                   net8.0-windows, UseWPF=true
│     • View/ (XAML, code-behind)
│     • ViewModel/ (folded in from DeployAssistant.ViewModel)
│     • Services/WpfDialogService.cs, WpfUiDispatcher.cs
│     • AppServices.cs (composition root, replaces App.* statics)
│     References: DeployAssistant.Core, Extended.Wpf.Toolkit 4.6.0
│
├── DeployAssistant.CLI               net472, OutputType=Exe
│     • Program.cs
│     • ConsoleDialogService.cs, ImmediateUiDispatcher.cs
│     References: DeployAssistant.Core, Spectre.Console (last netstandard2.0-supporting version)
│     Ships as: deployassistant.exe + DeployAssistant.Core.dll (+ NuGet runtime DLLs)
│
└── DeployAssistant.Tests             net8.0-windows
       References: DeployAssistant.Core, DeployAssistant
       Test priority: Core coverage primary, ViewModel surface secondary,
       CLI behavior covered by .github/workflows/cli-smoke-test.yml
```

Deleted from the repo:

- `DeployAssistant.Core/` (the old `net10.0-windows` Core)
- `DeployAssistant.Core.Standard/` (Core.Standard becomes the new Core)
- `DeployAssistant.ViewModel/` (folded into the GUI exe)
- `.github/workflows/sync-core-standard.yml`
- `auto/sync-core-standard` branch (after merge)

## 4. Modern C# in netstandard2.0 — Without PolySharp

`<LangVersion>latest</LangVersion>` already provides, with no polyfills required:

- file-scoped namespaces, raw string literals, list & relational patterns
- primary constructors (C# 12), collection expressions for `List<T>` / arrays
- pattern matching enhancements, `nameof` improvements, `nint`/`nuint`

Features that *do* require polyfill attributes are **not used in the codebase today** and are out of scope for this change:

- `init` setters / `record` types → require `IsExternalInit`
- `required` members → require `RequiredMemberAttribute` + friends

If a future change wants them, add a single `Core/Internal/Polyfills.cs` with the ~3 internal attribute classes. No NuGet dependency.

## 5. Service Seams

The realignment's stated goal of headless-testable ViewModels requires three small abstractions in Core, not one. (Original revision had only `IDialogService`. The SOLID review (§6) showed two more leaks of `System.Windows` surface that block the same goal.)

```csharp
// DeployAssistant.Core/Services/IDialogService.cs
public interface IDialogService
{
    DialogChoice Confirm(string title, string message, DialogChoice defaultChoice = DialogChoice.No);
    void Inform(string title, string message);
    string? PickFolder(string title, string? initialPath = null);
    void OpenInShell(string path);   // covers Process.Start("explorer.exe", ...) — see §6 finding 8
}
public enum DialogChoice { Yes, No, Cancel }

// DeployAssistant.Core/Services/IUiDispatcher.cs
public interface IUiDispatcher
{
    void Post(Action work);          // marshals a callback to the UI thread (or runs synchronously)
    void Invoke(Action work);
}
```

Implementations:

| | WPF GUI | CLI | Tests |
|---|---|---|---|
| `IDialogService` | `WpfDialogService` — `System.Windows.MessageBox`, WPF `OpenFolderDialog` (.NET 8 native, no WinForms), `Process.Start` for `OpenInShell` | `ConsoleDialogService` — `Confirm` reads stdin (auto-yes under `--yes`); `Inform` writes to stderr; `PickFolder` returns null (CLI takes paths as args); `OpenInShell` is no-op or stderr message | `FakeDialogService` with programmable defaults |
| `IUiDispatcher` | `WpfUiDispatcher` wraps `Application.Current.Dispatcher` | `ImmediateUiDispatcher` invokes synchronously | `ImmediateUiDispatcher` (reused) |

Direct call sites that need refactoring (audit verified):

- `MessageBox` / dialog calls: `MetaDataViewModel.cs`, `MetaFileDiffViewModel.cs`, `BackupViewModel.cs`, `FileTrackViewModel.cs`. (Core has none.)
- `Application.Current.Dispatcher.Invoke`: 9 sites across `MetaDataViewModel.cs:176-181`, `BackupViewModel.cs:239-243,248-251`, `FileTrackViewModel.cs:251-254,278-281,286-290,294-304`. (Core has none.)
- `Process.Start("explorer.exe", ...)`: `BackupViewModel.cs:229`. (Single site.)
- Drop gratuitous `MainWindow?.UpdateLayout()` calls (3 sites, in the same VM functions as the Dispatcher.Invoke calls) — re-layout per state change is a UI smell unrelated to the abstraction.

## 6. SOLID-Driven Cleanups Bundled In

A dedicated SOLID review of the current class graph (general-purpose subagent, 2026-05-06) identified items that either *block* a clean realignment or are *cheap to bundle* while files are already being touched. Items below are scoped into the migration order in §8.

### 6.1 BLOCKING — must fix during the realignment

**B1. Duplicate `_updateManager.Awake()` call in `MetaDataManager.Awake`**
`MetaDataManager.cs:159-160` invokes `_updateManager.Awake()` twice. Cosmetic in current behavior because `Awake` is idempotent, but it is the kind of bug a god-class wiring section produces and it lives in the exact code path the realignment touches. *Fix during step 2 of §8.*

**B2. Dead `ConfirmationCallback` properties on three managers**
`Func<string,string,bool> ConfirmationCallback` is declared on `MetaDataManager.cs:105`, `BackupManager.cs:34`, `SettingManager.cs:30`, and `FileManager.cs:56` — but only the `SettingManager` one is ever wired (in `MetaDataManager.Awake`, line 161). The other three are dead code. This is the exact surface `IDialogService.Confirm` replaces; leaving the dead properties through the realignment makes step 5 (CLI retarget) ambiguous about whose callback the CLI implements. *Fix during step 3 of §8: delete the dead properties on `BackupManager`/`FileManager`/`MetaDataManager`; inject `IDialogService` into `SettingManager` (and into `MetaDataManager.RequestProjectUpdate` at lines 453/459/473).*

**B3. Split `IProjectData` per ISP**
`Interfaces/IProjectData.cs` is implemented by `ProjectFile` (real) and `RecordedFile` (mostly `[JsonIgnore]` no-op auto-properties for `DataState`, `DataRelPath`, `DataSrcPath`, `DataAbsPath`, `DataHash` — see `Model/RecordedFile.cs:17-28`). A consumer holding `IProjectData` cannot tell whether `DataHash` is meaningful. Once Core becomes the single binary consumed by both GUI and CLI, this becomes a public-API break to fix later. *Fix during step 2 of §8: split into `IProjectDataIdentity` (Type, Name, RelPath, UpdatedTime) and `IProjectDataContent` (Hash, Size, AbsPath, BuildVersion). `RecordedFile` implements only the first.*

### 6.2 RECOMMENDED — cheap add-ons during the realignment

**R1. `AppServices` composition root replacing `App.*` statics**
`App.xaml.cs:10-17` exposes `App.MetaDataManager` as a static lazy singleton, referenced from 4 View code-behinds (`MainWindow`, `IntegrityLogWindow`, `OverlapFileWindow`, `VersionDiffWindow`). Service Locator anti-pattern, but the blast radius is small. With the GUI being retargeted and the ViewModel project being folded in anyway, replace with a single `AppServices` POCO constructed in `App.OnStartup` holding the manager + service-seam impls. Pass it down through `MainWindow`; child windows receive only what they need. **No DI container** — explicit construction is simpler at this scale.

**R2. `ViewModelBase : IDisposable` for child-window cleanup**
The four ViewModels subscribe to `MetaDataManager` events in their constructors; nothing ever unsubscribes. Today the *main* VMs are app-lifetime so they don't leak, but child windows (`OverlapFileWindow`, `VersionDiffWindow`, `IntegrityLogWindow`) instantiate fresh VMs per open and *do* leak via `MetaDataManager` event handlers. Make `ViewModelBase : IDisposable`, store subscriptions, unsubscribe on `Dispose`; child windows call `Dispose` on `Closed`.

**R3. Trim or delete `IManager`**
`IManager.Awake()` is a no-op for `BackupManager` (`BackupManager.cs:43`); `ManagerStateEventHandler` is declared but several managers never raise it. The interface is not consumed polymorphically anywhere. Either delete it or narrow to just `event Action<MetaDataState> ManagerStateEventHandler`. Tiny win, but the file moves anyway.

### 6.3 DEFERRED — explicit out-of-scope callouts (see §10)

- **D1.** `MigrationPipeline<T>` parameterized on the *final* target type, with `MigrateTo` returning `T` while `RollbackTo` returns `object` — asymmetric. Pipeline is OCP-clean enough to add V3 today; the asymmetry is real but cosmetic.
- **D2.** `ProjectFile` already carries `[Obsolete]` pointing at `Model/V2/FileRecord` (line 13 of `Model/ProjectFile.cs`); 9 constructor overloads encode 9 states. The V2 migration *is* the existing answer; the realignment shouldn't try to also kill V1 types.
- **D3.** Managers `new FileHandlerTool()` / `new HashTool()` directly. Untestable serialization seam. Defer until tests need it (current tests use real disk).
- **D4.** Pulling the file-walking + hashing body out of `RequestProjectInitialization` (`MetaDataManager.cs:204-315`) into `FileManager` (which already owns the dictionaries). This is the real "god method" inside the god class; the right shape is clear but the change is large enough to warrant its own PR.

## 7. CLI on net472 — Dependency Vetting

| Dependency | net472 status |
|---|---|
| `System.Text.Json` 8.x | ✓ multi-targets to netstandard2.0; works on net472 with the package referenced. |
| `DocumentFormat.OpenXml` 3.0.2 | ✓ supports net46+. |
| `System.Buffers`, `System.IO.Packaging` | ✓ supports net46+ via NuGet. |
| `Spectre.Console` | Verify last version supporting netstandard2.0 (≈ 0.49.x). Pin in CLI's csproj. If a hard floor of net6+ blocks us, fall back to plain `Console.WriteLine` for help/status — output is not visually critical. |
| `System.Security.Cryptography.MD5` / `IncrementalHash` | ✓ in mscorlib on net472. |
| `Span<T>` / `Memory<T>` | ✓ via `System.Memory` NuGet (already a transitive dep). |

CI: `cli-smoke-test.yml` currently uses `actions/setup-dotnet@v4` with `dotnet-version: '10.0.x'` and runs `dotnet publish ... -r win-x64 --self-contained true -p:PublishSingleFile=true`. After retargeting:

- `dotnet build`/`publish` on `windows-latest` can target `net472` because the .NET Framework targeting packs are preinstalled on GitHub-hosted Windows runners.
- The `--self-contained` and `-p:PublishSingleFile=true` flags do not apply to .NET Framework. New publish step: `dotnet publish DeployAssistant.CLI -c Release -o ./publish/DeployAssistant.CLI` produces `deployassistant.exe` + Core dll + NuGet dlls in a single folder. The smoke test continues to invoke `./publish/DeployAssistant.CLI/deployassistant.exe`.

Verification of these CI assumptions happens in the implementation plan's first PR; if `setup-dotnet` does not surface the net472 targeting pack on `windows-latest`, switch the CLI build step to `microsoft/setup-msbuild@v2` + `msbuild`.

## 8. Migration Order

Each numbered step is intended to land as one PR, building cleanly and passing tests at every step.

1. **Strengthen Core test coverage** for the platform-neutral managers and the V1↔V2 migration pipeline. Goal: any behavioral regression introduced by promoting Core.Standard surfaces as a test failure. (Tests today reference Core, not Core.Standard, so the behavior currently verified is the rich-Core variant; we want assurance the Standard variants behave identically.) Out-of-scope items below stay unchanged in this step.

2. **Promote Core.Standard to the new Core, with cleanups B1 + B3.**
   - Pull in the drifted methods from old Core: `MetaDataManager.LoadExternalMetaFile`, `ComputeMetaFileDiff`, `RequestExportDiffPackage`; the `UpdateIgnoreListEventHandler?.Invoke(newIgnoreData)` dispatch; `_settingManager.ConfirmationCallback = ConfirmationCallback;` wiring.
   - **B1**: collapse the duplicate `_updateManager.Awake()` call in `MetaDataManager.Awake`.
   - **B3**: split `IProjectData` into `IProjectDataIdentity` + `IProjectDataContent`; update `RecordedFile` to implement only the identity half; update consumers.
   - Move `PathCompat`/`CompatHelper` shims into the new Core. Set `<LangVersion>latest</LangVersion>` (already on Core.Standard).
   - Switch the test project's reference from old Core → Core.Standard. Tests must stay green — proves Core.Standard is feature-complete.

3. **Introduce the service seams (IDialogService + IUiDispatcher) and apply B2.**
   - Add `IDialogService`, `IUiDispatcher`, and `DialogChoice` enum in `Core/Services/`.
   - **B2**: delete the dead `ConfirmationCallback` properties on `BackupManager`, `FileManager`, and `MetaDataManager`. Inject `IDialogService` into `SettingManager` and the relevant `MetaDataManager.Request*` methods that currently use the callback.
   - Refactor the four ViewModel files (`MetaDataViewModel`, `MetaFileDiffViewModel`, `BackupViewModel`, `FileTrackViewModel`) to depend on `IDialogService` + `IUiDispatcher` instead of `MessageBox` / `FolderBrowserDialog` / `Application.Current.Dispatcher` / `Process.Start`. Drop the gratuitous `MainWindow?.UpdateLayout()` calls.
   - Test fakes: `FakeDialogService` + `ImmediateUiDispatcher` in `DeployAssistant.Tests/Fakes/`. Add at least one ViewModel test that runs headless to prove the seam works.

4. **Retarget GUI to `net8.0-windows`; fold ViewModel; apply R1, R2, R3.**
   - Move `DeployAssistant.ViewModel/*.cs` into `DeployAssistant/ViewModel/`.
   - Add `WpfDialogService` and `WpfUiDispatcher` under `DeployAssistant/Services/`.
   - **R1**: introduce `AppServices` POCO; replace `App.MetaDataManager` static with constructor-style passdown through `MainWindow`. Update the 4 child-window code-behinds (`IntegrityLogWindow`, `OverlapFileWindow`, `VersionDiffWindow`, `MainWindow`) to receive what they need from the parent, not from `App.*`.
   - **R2**: make `ViewModelBase : IDisposable`; track event subscriptions; child windows call `Dispose` on `Closed`.
   - **R3**: trim or delete `IManager` per the audit finding.
   - Replace any `FolderBrowserDialog` usage with WPF `OpenFolderDialog`.
   - Remove `DeployAssistant.ViewModel.csproj` from the solution.

5. **Retarget CLI to `net472`.** Pin Spectre.Console version. Add `ConsoleDialogService` and `ImmediateUiDispatcher`. Update `cli-smoke-test.yml` publish command (drop `--self-contained` / `PublishSingleFile`). Confirm green CI run.

6. **Retarget Tests to `net8.0-windows`.** Reference the new Core and the merged GUI exe. Verify both Core tests and at least one headless ViewModel test pass.

7. **Rename and delete.** Rename `Core.Standard/` directory and project to `Core/` (or do this earlier in step 2 — implementation plan's call). Delete the old `DeployAssistant.Core/` directory, the old `DeployAssistant.Core.Standard.csproj` once renamed, and `DeployAssistant.ViewModel.csproj`. Delete `.github/workflows/sync-core-standard.yml`.

8. **Update `spec.md` §2 (Tech Stack), §3 (Project Layout), §5 (`IProjectData` shape), §8 (manager wiring)** and `CLAUDE.md` to match the new world.

## 9. Risks & Verification

| Risk | Mitigation |
|---|---|
| Spectre.Console newer versions drop netstandard2.0 | Pin in csproj; fall back to plain Console output if needed. CLI output is help text + status, not critical UX. |
| `setup-dotnet` on `windows-latest` runner does not surface net472 targeting pack | Verified on first CI run. Fallback: switch CLI build to `microsoft/setup-msbuild@v2` + `msbuild`. |
| V1↔V2 metadata regression during Core.Standard promotion | Step 1 strengthens coverage before any code moves. `DeployAssistant.Tests/Migration/*` runs against the unified Core. |
| `IProjectData` split (B3) breaks consumers | Find-references pass during step 2; both `ProjectFile` and `RecordedFile` are internal types, so no external consumer impact. Tests cover the major code paths. |
| Child-window VM event leak fixed by R2 surfaces a latent ordering bug (e.g. `Dispose` running before in-flight callback) | Tests with `IUiDispatcher` are synchronous, so race conditions are reproducible. Defensive: store the subscription `Action`, null-check before invoking. |
| `dotnet test` on a machine without .NET 10 SDK | Already the local-machine state (only .NET 9 installed). Post-migration the project needs only .NET 8 SDK + .NET Framework 4.7.2 targeting pack — easier to set up. |

**Done definition:**
- `dotnet build DeployAssistant.sln` clean across all 4 projects.
- `dotnet test DeployAssistant.Tests/DeployAssistant.Tests.csproj` green, including at least one headless ViewModel test using `FakeDialogService` + `ImmediateUiDispatcher`.
- `cli-smoke-test.yml` green: no-args exit 0 with "DeployAssistant" in output, `--help` exit 0, unknown command exit 1.
- WPF GUI launches and round-trips a project end-to-end (manual; no UI test harness).
- `spec.md` and `CLAUDE.md` reflect the new architecture.

## 10. Out of Scope (Deferred Work)

- **Namespace consolidation** (`DeployManager.*` → `DeployAssistant.*`). Tracked in `spec.md §3`.
- **AOT / source-generated `System.Text.Json` serializers** — not needed under C; revisit only if startup or memory becomes a concern.
- **Replacing MD5 with a stronger hash** — orthogonal.
- **Spectre.Console upgrade or replacement.**
- **D1.** `MigrationPipeline<T>` return-type asymmetry (`MigrateTo` returns `T`, `RollbackTo` returns `object`). Future scope: non-generic `IMigrationPipeline` plus a typed wrapper.
- **D2.** Retiring V1 `ProjectFile` in favor of `Model/V2/FileRecord` is the existing tech-debt answer; out-of-scope here.
- **D3.** `IFileHandler` test seam for `FileHandlerTool`/`HashTool`. Defer until a test needs it.
- **D4.** Decomposing `MetaDataManager.RequestProjectInitialization` (the file-walking/hashing body, lines 204-315) into `FileManager`. Right shape is clear but the change is large enough to be its own PR.

## 11. References

- `spec.md` — feature specification (ground truth for behavior preservation).
- `CLAUDE.md` — operating notes for Claude Code in this repo.
- Brainstorming subagent SOLID review, 2026-05-06 (findings catalog above; raw report in conversation transcript).
- PRs: #12 (V2 + migration framework), #13/#16 (sync drift chores), #17 (missed event dispatch), #18 (`DiffViewModelBase` extraction), #19 (Spectre escape), #20 (WinForms removal + metafile compare).
- `.github/workflows/cli-smoke-test.yml`, `release.yml`, `sync-core-standard.yml` (last to be deleted).
