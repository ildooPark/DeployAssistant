# DeployAssistant — Feature Specification

> **Purpose of this document:** Derived from the current implementation as a ground-truth reference for future refactoring. Agents or developers performing a refactor should use this document to verify that all described behaviors are preserved and the architecture conforms to the patterns described here.

---

## 1. Overview

**DeployAssistant** (internal assembly name `SimpleBinaryVCS`) is a Windows desktop application that provides lightweight, binary-level deployment version control for a directory of files. It is aimed at teams that ship compiled binary applications and need to track what changed, to and from which version, without a full source-control system on the deployment target.

Core responsibilities:
- Track which files (and directories) exist at a deployment path, keyed by MD5 hash.
- Record snapshots of the deployment directory as named "versions" in a `ProjectMetaData.bin` file.
- Detect differences between the current directory state and the recorded snapshot.
- Stage, commit ("update"), check out, export, rename and delete any recorded version.
- Support merging/integrating an external build tree into the tracked deployment directory.

---

## 2. Technology Stack

| Item | Detail |
|---|---|
| Language | C# / .NET 8.0-windows for WPF GUI + Tests; .NET Framework 4.7.2 (net472) for CLI; .NET Standard 2.0 for Core |
| UI Framework | WPF (`UseWPF=true`) |
| Folder picking / message boxes | Pure WPF — `Microsoft.Win32.OpenFolderDialog` and `System.Windows.MessageBox`, behind `IDialogService`. WinForms was removed in PR #20; no project sets `UseWindowsForms` |
| Architecture pattern | MVVM (Model / ViewModel / View) with event-driven data flow |
| Serialization | `System.Text.Json` for all persistence |
| File hashing | MD5 via `System.Security.Cryptography.MD5` |
| Export — spreadsheet | `DocumentFormat.OpenXml` v3.0.2 (xlsx generation) |
| Export — archive | `System.IO.Compression.ZipFile` |
| Concurrency | `Task`, `Parallel.ForEach`, `SemaphoreSlim` (max 12 concurrent hash tasks) |
| CLI output | `Spectre.Console` v0.49.1 — ANSI color, progress bars, panels; drives the full-screen TUI |
| App icon | `app.ico`, shared by both front ends (`<ApplicationIcon>` in each `.csproj`; also a WPF `Resource` so `Icon="/app.ico"` resolves from the assembly) |

---

## 3. Project Layout

The solution contains four compilable projects plus one test project:

```
DeployAssistant.sln
│
├── DeployAssistant.Core/              ← netstandard2.0 class library; all business logic
│   ├── Interfaces/
│   │   ├── IProjectData.cs            ← umbrella: IProjectDataIdentity + IProjectDataContent
│   │   ├── IProjectDataIdentity.cs    ← Type, Name, UpdatedTime (light surface for ignore lists)
│   │   └── IProjectDataContent.cs     ← State, RelPath, SrcPath, AbsPath, Hash (ProjectFile only)
│   ├── Services/
│   │   ├── IDialogService.cs          ← confirm / inform / pick-folder / open-in-shell seam
│   │   ├── IUiDispatcher.cs           ← post / invoke on the UI thread; sync in CLI/tests
│   │   ├── DialogChoice.cs
│   │   └── NullDialogService.cs       ← fallback; Confirm returns the default choice
│   ├── Model/                         ← pure data classes (serializable)
│   │   ├── ProjectMetaData.cs
│   │   ├── ProjectData.cs
│   │   ├── ProjectFile.cs
│   │   ├── DataState.cs               ← DataState flags enum
│   │   ├── ChangedFile.cs
│   │   ├── RecordedFile.cs
│   │   ├── ProjectIgnoreData.cs
│   │   ├── ProjectSimilarity.cs
│   │   ├── DeployData.cs
│   │   └── LocalConfigData.cs
│   ├── DataComponent/                 ← service/manager layer
│   │   ├── MetaDataManager.cs         ← central orchestrator; accepts IDialogService on ctor
│   │   ├── FileManager.cs
│   │   ├── BackupManager.cs
│   │   ├── UpdateManager.cs
│   │   ├── ExportManager.cs
│   │   ├── SettingManager.cs
│   │   └── LogManager.cs              ← empty stub
│   └── Utils/
│       ├── FileHandlerTool.cs
│       ├── HashTool.cs
│       └── LogTool.cs
│
├── DeployAssistant/                   ← net472 WPF exe (main GUI application)
│   │                                    References: DeployAssistant.Core
│   ├── App.xaml / App.xaml.cs         ← application entry-point
│   ├── AppServices.cs                 ← composition root; wires WpfDialogService + WpfUiDispatcher
│   ├── AssemblyInfo.cs
│   ├── Services/Wpf/
│   │   ├── WpfDialogService.cs        ← IDialogService impl (MessageBox, FolderBrowserDialog)
│   │   └── WpfUiDispatcher.cs         ← IUiDispatcher impl (Application.Current.Dispatcher)
│   ├── ViewModel/
│   │   ├── ViewModelBase.cs           ← INotifyPropertyChanged + IDisposable; TrackUnsubscribe
│   │   ├── MainViewModel.cs
│   │   ├── MetaDataViewModel.cs
│   │   ├── FileTrackViewModel.cs
│   │   ├── BackupViewModel.cs
│   │   ├── VersionDiffViewModel.cs
│   │   ├── VersionIntegrationViewModel.cs
│   │   ├── VersionCheckViewModel.cs
│   │   ├── VersionCompatibilityViewModel.cs
│   │   ├── OverlapFileViewModel.cs
│   │   └── Utils/
│   │       └── RelayCommand.cs
│   └── View/
│       ├── MainWindow.xaml / .cs
│       ├── IntegrityLogWindow.xaml / .cs
│       ├── ErrorLogWindow.xaml / .cs
│       ├── VersionDiffWindow.xaml / .cs
│       ├── VersionIntegrationView.xaml / .cs
│       ├── CompatibleVersionWindow.xaml
│       ├── OverlapFileWindow.xaml / .cs
│       └── VersionComparisonWindow.cs
│
├── DeployAssistant.CLI/               ← net472 console exe; runs on Win10 1803+ with .NET Framework 4.7.2
│   │                                    References: DeployAssistant.Core
│   │                                    Binary name: deployassistant
│   └── Program.cs
│
└── DeployAssistant.Tests/             ← net472 unit test project (xUnit)
    │                                    References: DeployAssistant.Core
    ├── Models/
    │   ├── ChangedFileTests.cs
    │   ├── ProjectDataTests.cs
    │   ├── ProjectFileTests.cs
    │   ├── ProjectIgnoreDataTests.cs
    │   └── ProjectMetaDataTests.cs
    └── Utils/
        ├── FileHandlerToolTests.cs
        ├── HashToolTests.cs
        └── IntegrityCheckRobustnessTests.cs
```

### Namespace irregularities (known technical debt)
The codebase uses **two namespace prefixes** across files in the same project:
- `DeployAssistant` / `DeployAssistant.Interfaces` / `DeployAssistant.Model` / `DeployAssistant.DataComponent` / `DeployAssistant.ViewModel` / `DeployAssistant.View` / `DeployAssistant.Utils` — the vast majority of files
- `DeployManager.DataComponent` — `SettingManager.cs` only
- `DeployManager.Model` — `LocalConfigData.cs` only

All live in a single compiled assembly. A refactor should consolidate to the `DeployAssistant.*` namespace hierarchy.

---

## 4. Global Application Objects

The WPF GUI uses `AppServices` as its composition root (see `DeployAssistant/AppServices.cs`). It is constructed once in `App.OnStartup` and passed to `MainWindow`, then threaded down to child windows.

| Property | Type | Purpose |
|---|---|---|
| `AppServices.MetaDataManager` | `MetaDataManager` | Central service hub; constructed with `IDialogService` |
| `AppServices.DialogService` | `IDialogService` | WPF impl: `WpfDialogService` (MessageBox + FolderBrowserDialog) |
| `AppServices.UiDispatcher` | `IUiDispatcher` | WPF impl: `WpfUiDispatcher` (wraps `Application.Current.Dispatcher`) |

`AppServices` calls `MetaDataManager.Awake()` on construction, which constructs all sub-managers and wires up all event subscriptions. The old `App.MetaDataManager` / `App.FileHandlerTool` / `App.HashTool` static singletons have been replaced by `AppServices`.

---

## 5. Interfaces

### `IProjectDataIdentity`
The minimum identity surface every tracked entry must expose.
```csharp
ProjectDataType DataType { get; }
string DataName { get; }
DateTime UpdatedTime { get; set; }
```
Implemented by both `ProjectFile` and `RecordedFile`. Use this type in code that only needs to match by name/type (e.g. ignore-list filtering) to avoid coupling to path/hash fields that `RecordedFile` does not meaningfully populate.

### `IProjectDataContent`
Carries the path, hash, and state of a tracked file or directory.
```csharp
DataState DataState { get; set; }
string DataRelPath { get; }
string DataSrcPath { get; set; }
string DataAbsPath { get; }   // computed: Path.Combine(DataSrcPath, DataRelPath)
string DataHash { get; set; }
```
Implemented by `ProjectFile` only. `RecordedFile` does not implement this interface.

### `IProjectData : IProjectDataIdentity, IProjectDataContent`
Compatibility umbrella combining both interfaces. Existing call sites that accept `IProjectData` continue to work unchanged. New code should prefer the narrower interface (`IProjectDataIdentity` or `IProjectDataContent`) where only part of the surface is needed.

### `ProjectDataType` (enum, defined in `IProjectData.cs`)
```
File | Directory
```

---

## 6. Data Model

### 6.1 `ProjectFile`
The atomic unit of tracking. Represents one file or directory.

| Property | Type | Persisted | Description |
|---|---|---|---|
| `DataType` | `ProjectDataType` | ✓ | File or Directory |
| `DataName` | `string` | ✓ | Filename / directory name |
| `DataRelPath` | `string` | ✓ | Path relative to the project root |
| `DataSrcPath` | `string` | ✓ | Absolute path to root (changes on path reconfiguration) |
| `DataAbsPath` | `string` | computed | `Path.Combine(DataSrcPath, DataRelPath)` |
| `DataRelDir` | `string` | computed | Parent directory of `DataRelPath` |
| `DataHash` | `string` | ✓ | MD5 hex string (empty for directories) |
| `DataSize` | `long` | ✓ | File size in bytes |
| `BuildVersion` | `string` | ✓ | `FileVersionInfo.FileVersion` extracted at scan time |
| `ProductVersion` | `string` | ✓ | `FileVersionInfo.ProductVersion` — carries the build's commit id (e.g. `0.0.1682+HEAD.f05eda3`). Serialized by the shipped 3.6.1; restored 2026-08-25 |
| `VersionDisplay` | `string` | computed | `ProductVersion` when non-empty, else `BuildVersion`; the GUI's version columns bind this |
| `DeployedProjectVersion` | `string` | ✓ | Version string of the `ProjectData` it was recorded in |
| `UpdatedTime` | `DateTime` | ✓ | Timestamp of last record action |
| `DataState` | `DataState` | ✓ | Bitflag describing current lifecycle state |
| `IsDstFile` | `bool` | ✓ | True when the file is the destination (deployed) side of a `ChangedFile` |

### 6.2 `DataState` (flags enum, defined in `DataState.cs`)
```
None           = 0
Added          = 1
Deleted        = 1 << 1
Restored       = 1 << 2
Modified       = 1 << 3
PreStaged      = 1 << 4
IntegrityChecked = 1 << 5
Backup         = 1 << 6
Overlapped     = 1 << 7
Integrate      = 1 << 8   // written by the shipped 3.6.1 integration flow; reserved, never reuse
```
`ProjectFiles` and `BackupFiles` dictionaries are rebuilt with `StringComparer.OrdinalIgnoreCase` on every assignment (`DictionaryCompat.WithOrdinalIgnoreCaseKeys`, last-entry-wins on case-variant duplicates). The shipped 3.6.1 kept these dictionaries case-insensitive; an ordinal store can emit case-duplicate keys that 3.6.1 then fails to load.
`IntegrityChecked` is a persistent flag on `ChangedFile` entries — they survive a `ClearStagedFiles` call and are not reapplied on a normal update.

### 6.3 `ChangedFile`
A record of a single file change, always carrying:
- `SrcFile` — the "before" side (may be null for Add-only)
- `DstFile` — the "after" / destination side
- `DataState` — the change type (Added / Deleted / Modified / Restored / etc.)

`IsDstFile` on `SrcFile` is always `false`; on `DstFile` it is `true` unless `Overlapped` is set.

### 6.4 `ProjectData`
A versioned snapshot of the deployment directory.

| Property | Type | Persisted |
|---|---|---|
| `ProjectName` | `string` | ✓ |
| `ProjectPath` | `string` | ✓ |
| `UpdaterName` | `string` | ✓ |
| `ConductedPC` | `string` | ✓ |
| `UpdatedTime` | `DateTime` | ✓ |
| `UpdatedVersion` | `string` | ✓ |
| `UpdateLog` | `string` | ✓ |
| `ChangeLog` | `string` | ✓ |
| `RevisionNumber` | `int` | ✓ |
| `NumberOfChanges` | `int` | ✓ |
| `ChangedFiles` | `List<ChangedFile>` | ✓ |
| `ProjectFiles` | `Dictionary<string, ProjectFile>` | ✓ — key = `DataRelPath` |
| `IsProjectMain` | `bool` | ✗ (runtime only) |

Computed views (not serialized):
- `ProjectFilesObs` — `ObservableCollection<ProjectFile>` wrapping all values
- `ProjectRelFilePathsList` — relative paths of files only
- `ProjectRelDirsList` — relative paths of directories only
- `ProjectDirFileList` — list of directory-type `ProjectFile` entries
- `ProjectFilesDict_NameSorted` — grouped by filename
- `ProjectFilesDict_RelDirSorted` — grouped by relative directory
- `ChangedDstFileList`, `ChangedProjectFileObservable` — changed file projections

**Version name format:** `{ProjectName}_{MachineName}_{yyyy_MM_dd}_v{revisionNumber}`

### 6.5 `ProjectMetaData`
The top-level container stored in `ProjectMetaData.bin`.

| Property | Type | Description |
|---|---|---|
| `ProjectName` | `string` | Display name (= folder name) |
| `ProjectPath` | `string` | Absolute path to the managed directory |
| `LocalUpdateCount` | `int` | Monotonically increasing revision counter |
| `ProjectMain` | `ProjectData` | The currently active version |
| `ProjectDataList` | `LinkedList<ProjectData>` | Full version history, newest first |
| `BackupFiles` | `Dictionary<string, ProjectFile>` | Global dedup backup store, key = `DataHash` |

`ReconfigureProjectPath(string)` updates all embedded absolute paths when the project is opened from a different machine or drive.

**`LocalUpdateCount` is monotonic and must NEVER be decremented.** It is the source of the `v{n+1}` suffix in generated version names. Deleting a version does not give its number back: reusing a retired revision number would produce two snapshots with the same `UpdatedVersion` tag, and `ProjectData.Equals` compares that tag alone — every list lookup, backup-folder path and main-pointer comparison would then collide. `RequestDeleteVersion` therefore only removes the list node; the counter is untouched, so version numbers may legitimately have gaps.

**Read-once completion flags on `MetaDataManager`** (runtime only — none of these are serialized). Screens that sit above an operation need to know it finished without subscribing to an event across a screen push, so the manager parks a one-shot flag that the interested screen consumes:

| Flag | Set by | Consumed by |
|---|---|---|
| `LastCheckedOut` (`ProjectData?`) | successful `RequestCheckoutVersion` (both modes) | `ConsumeLastCheckedOut()` — CLI `RevisionListScreen` pops back to `MainScreen` |
| `LastUpdated` (`ProjectData?`) | successful `RequestProjectUpdate` | `ConsumeLastUpdated()` — CLI `IntegrityResultScreen` |
| `LastDeletedVersion` (`string?`) | successful `RequestDeleteVersion` | `ConsumeLastDeletedVersion()` — CLI `RevisionListScreen` rebuilds its row snapshot |

Read-once means the getter is a plain peek and `Consume*()` clears it. Intermediate screens (e.g. `RevisionDetailScreen`) deliberately **peek and never consume**, so the flag survives the hop down to the screen that actually owns it, and they pair the peek with a local "I requested this" bool so a stale flag from an earlier operation can never auto-pop the wrong screen.

### 6.6 `ProjectIgnoreData`
Defines which files and directories should be excluded from specific operations.

- Stored as `DeployAssistant.ignore` (JSON) in the project root.
- Contains `List<RecordedFile>` where each entry carries a `DataName` (supports glob `*`), `ProjectDataType`, and `IgnoreType` bitmask.

**Default ignore entries:**
| Pattern | Type | Operations |
|---|---|---|
| `ProjectMetaData.bin` | File | All |
| `*.ignore` | File | All |
| `*.deploy` | File | Deploy |
| `*.VersionLog` | File | All |
| `Export_XLSX` | Directory | All |
| `ProductionRecord.db` | File | All |
| `configfilepath.txt` | File | All |
| `msg_format.dat` | File | All |
| `en-US` | Directory | Integration |
| `ko-KR` | Directory | Integration |
| `Resources` | Directory | Integration |
| `Backup_{ProjectName}` | Directory | IntegrityCheck |
| `Export_{ProjectName}` | Directory | IntegrityCheck |

`ProductionRecord.db` / `configfilepath.txt` / `msg_format.dat` are the shipped 3.6.1 build's
defaults (that build came from a diverged source tree); `EnsureDefaultFlags()` also appends
them to older `.ignore` files on load so a mixed 3.6.1 + current fleet keeps identical
exclusions. An **unreadable** `.ignore` is never fatal: `SettingManager` preserves it as
`DeployAssistant.ignore.bak`, regenerates defaults, informs the user, and still raises
`IgnoreDataLoadedEventHandler`, so the session always has an ignore context.

**`IgnoreType`** (flags enum):
```
None=0, Integration=1, IntegrityCheck=2, Deploy=4, Initialization=8, All=~0
```

### 6.7 `DeployData`
Saved as `DeployAssistant.deploy` (JSON) in the source folder when file allocation has been manually resolved. Stores `ProjectName` and `Dictionary<string, ProjectFile> SortedTopFiles` (key = `DataRelPath`) so that the same allocation can be reused on the next deployment from the same folder.

### 6.8 `LocalConfigData`
Saved as `DeployAssistant.config` (JSON) in `%USERPROFILE%\Documents`. Contains `LastOpenedDstPath`, `Language` (`"ko-KR"` / `"en-US"`), `RecentProjects` (MRU project paths, newest first, capped at 8 — feeds the Project ▸ Recent Projects menu) and `FastIntegritySamplePercent` (nullable int, see §12.4; `null` reads as 100). All fields after `LastOpenedDstPath` are additive, so 3.6.1-era configs still load — but a 3.6.1 save drops them, resetting the choices. Loaded on startup to offer re-opening the last project and to pick the UI language (default Korean).

### 6.9 `RecordedFile`
A lightweight entry inside `ProjectIgnoreData.IgnoreFileList`. Implements `IProjectData` but most properties are `[JsonIgnore]`. Carries `DataName`, `DataType`, `IgnoreType`, `UpdatedTime`.

### 6.10 `ProjectSimilarity`
Transient (never serialized). Holds the result of comparing one `ProjectData` against another:
- `projData` — the candidate version
- `numDiffWithResources` — total diff count
- `numDiffWithoutResources` — diff count after applying integration ignore filter
- `fileDifferences` — the raw `List<ChangedFile>`

---

## 7. State Machine

`MetaDataState` is a public enum on `MetaDataManager` that all managers share via `ManagerStateEventHandler`:

```
Idle
Initializing
Retrieving
Processing
Updating
Reverting
CleanRestoring
Exporting
IntegrityChecking
IntegrationValidating
Integrating
Deleting
```

`Deleting` is held for the whole version-delete mutation window (`RequestDeleteVersion` → `BackupManager.DeleteVersion` → back to `Idle`). Like the rest of the enum it is runtime-only and never serialized, so extending it does not touch the on-disk format.

`Reverting` was previously declared but never raised — no code path assigned it. The unified checkout path now does: `RequestCheckoutVersion` sets `Processing` (Fast) or `CleanRestoring` (CleanRestore) while the diff is computed, then `Reverting` for the apply step, then `Idle`. Anything keying off the state badge or `CanExecute` will therefore see `Reverting` in practice for the first time.

`RequestCheckoutVersion` also owns the state machine end-to-end via a private `_suppressSubManagerState` flag: sub-managers drop the state back to `Idle` when their own step finishes, which would re-enable ViewModel commands halfway through a checkout, so their notifications are dropped for the duration.

ViewModels subscribe to `ManagerStateEventHandler` and cache the last state. Commands that mutate data (`CanExecute` checks) only allow execution when state is `Idle`.

---

## 8. DataComponent Layer (Service Managers)

### 8.1 `MetaDataManager` — Central Orchestrator

**Owns:** `ProjectMetaData`, `MainProjectData`, `_srcProjectData`.

**Construction:** `MetaDataManager(IDialogService dialogService)`. A parameterless overload delegates to `new NullDialogService()` for backward-compat (tests, CLI scaffold). `AppServices` passes a `WpfDialogService`; the CLI passes a `ConsoleDialogService`.

**`Awake()`:** constructs `FileManager`, `BackupManager`, `UpdateManager`, `ExportManager`, `SettingManager`; wires all inter-manager event subscriptions; assigns `_settingManager.DialogService = _dialogService`; calls `_settingManager.Awake()` (wiring only). The re-open-last-project prompt is deliberately **not** part of `Awake()`: it runs later through `RequestPreviousProjectRestore()`, which the GUI calls from `MainWindow.Loaded` — prompting during `Awake()` loaded the project before any ViewModel had subscribed, so the UI stayed empty. `RequestSavedLanguage()` / `RequestSaveLanguage(code)` pass through to `SettingManager`'s config accessors the same way.

All public `Request*` methods are the sole API surface that ViewModels call. The manager routes requests to the appropriate sub-manager and re-fires results as events.

#### Events fired to ViewModels
| Event | Payload | Meaning |
|---|---|---|
| `ProjLoadedEventHandler` | `object` (ProjectData) | A version has been set as `MainProjectData` |
| `MetaDataLoadedEventHandler` | `object` (ProjectMetaData) | Metadata loaded/initialized |
| `SrcProjectLoadedEventHandler` | `object` (ProjectData?) | Source directory scan loaded a `ProjectData` from a `.VersionLog` |
| `FileChangesEventHandler` | `ObservableCollection<ProjectFile>` | Staged change list updated |
| `PreStagedChangesEventHandler` | `object` | Pre-staged list updated |
| `StagedChangesEventHandler` | `object` | Staged changes finalized (sent to `UpdateManager`) |
| `OverlappedFileSortEventHandler` | `List<ChangedFile>, List<ChangedFile>` | Overlap resolution required |
| `IntegrityCheckCompleteEventHandler` | `string, ObservableCollection<ProjectFile>` | Integrity check done |
| `ProjComparisonCompleteEventHandler` | `ProjectData, ProjectData, List<ChangedFile>` | Version diff computed |
| `SimilarityCheckCompleteEventHandler` | `ProjectData, List<ProjectSimilarity>` | Compatibility check done |
| `FetchRequestEventHandler` | `object` (`ObservableCollection<ProjectData>`) | Version list fetched. Re-fired after **every** `BackupManager.FetchCompleteEventHandler`, which includes a successful `DeleteVersion` — so a delete refreshes the history list without the caller asking |
| `ProjExportEventHandler` | `string` (export path) | Export complete |
| `ProjectContextLoadedEventHandler` | `ProjectContext` | Fires once, after both `ProjectMetaData` and `ProjectIgnoreData` are loaded (or constructed during init). Replaces the prior `MetaDataLoaded` + `UpdateIgnoreList` two-event coordination that caused regression #17 |
| `IntegrityProgressEventHandler` | `int, int` (completed, total) | Forwarded from `FileManager`; `total` = 2 × intersecting-file count. Drives the CLI progress bar |
| `IntegrityFileProgressEventHandler` | `string, IntegrityFileOutcome` | Forwarded from `FileManager`: one final verdict per file (`Checked` / `Modified` / `Added` / `Deleted` / `HashFailed` / `MetadataFallback`) while an integrity check runs. Raised from worker threads — subscribers must marshal and must not do per-event UI work (the GUI batches through a queue + 10 Hz timer) |
| `CheckoutCompleteEventHandler` | `CheckoutResult` | Fires on **every** `RequestCheckoutVersion` exit, success and failure alike; `CheckoutResult.Messages` carries the reason so a waiting caller never hangs on a silently-rejected request |
| `VersionDeletePreviewEventHandler` | `VersionDeletePlan` | Fires with the plan produced by `RequestVersionDeletePreview`. Nothing has been mutated at this point |
| `VersionDeleteCompleteEventHandler` | `VersionDeleteResult` | Fires on **every** `RequestDeleteVersion` exit — `Deleted`, `Blocked` and `Failed` |
| `VersionRenameCompleteEventHandler` | `VersionRenameResult` | Fires on **every** `RequestRenameVersion` exit, including the no-op "nothing to change" success |
| `MetaDataLoadFailedEventHandler` | `MetaDataLoadResult` | Fires on every `RequestProjectRetrieval` failure exit, carrying the structured reason (`FileNotFound`, `NotBase64`, `MalformedJson`, `SchemaTooNew`, `EmptyDocument`, `Unknown`). Before this existed the failure was swallowed into a `Trace` warning and the user saw a silent no-op. **Currently only the tests subscribe** — see §14 |
| `ManagerStateEventHandler` | `MetaDataState` | State changed |

`BackupManager` raises its own `VersionDeletePlannedEventHandler` / `VersionDeletedEventHandler`; these are deliberately **not** wired in `Awake()`. `MetaDataManager` raises the ViewModel-facing pair itself so it can report blockers it knows about but the backup manager cannot (e.g. "manager is busy").

#### Public Request Methods
| Method | Action |
|---|---|
| `RequestProjectRetrieval(string path)` | Deserialize `ProjectMetaData.bin` from path; reconfigure paths if moved |
| `RequestProjectInitialization(string path)` | Bootstrap a new `ProjectMetaData` for a fresh project directory (parallel file scan + MD5 hashing) |
| `RequestSrcDataRetrieval(string path)` | Scan a source folder and load its `ProjectData` (if `.VersionLog` exists) plus pre-staged files |
| `RequestStagedFileListRefresh(string path)` | **Empty body, and unreachable from both front ends** — see §14 item 14 |
| `RequestStageChanges()` | Hash pre-staged files and compute diff against main project |
| `RequestClearStagedFiles()` | Clear staged changes (preserves `IntegrityChecked` entries) |
| `RequestProjectUpdate(updaterName, updateLog, path)` | Commit staged changes as a new version (with optional integration merge path) |
| `RequestCheckoutVersion(ProjectData? target, CheckoutMode mode = Fast) → bool` | **The single checkout entry point.** `Fast` diffs snapshot-to-snapshot (metadata only, no disk hashing); `CleanRestore` runs a full integrity scan of the working directory against the target first, repairing drift that happened outside the app. Both set `LastCheckedOut` on success and both raise `CheckoutCompleteEventHandler` on every exit. Returns `false` — with the reason in `CheckoutResult.Messages` — if target is null, no project is loaded, the diff could not be computed, or the apply pipeline throws. |
| `RequestCheckoutVersion(string versionName, CheckoutMode mode = Fast) → bool` | Tag-resolving overload; an unknown tag returns `false` and raises `CheckoutCompleteEventHandler` with `"Version '…' was not found."` |
| `RequestRevertProject(ProjectData? target) → bool` | **`[Obsolete]` wrapper** — delegates to `RequestCheckoutVersion(target, CheckoutMode.Fast)`. Kept so existing callers keep compiling; new code must not use it. |
| `RequestProjectCleanRestore(ProjectData? target)` | **`[Obsolete]` wrapper** — delegates to `RequestCheckoutVersion(target, CheckoutMode.CleanRestore)`. Note the `void` return: the result is only observable through `CheckoutCompleteEventHandler`. |
| `RequestVersionDeletePreview(ProjectData? target) → VersionDeletePlan?` | Computes — **without mutating anything, on disk or in memory** — what deleting `target` would cost: exclusive vs. shared backup hashes, blobs needing relocation, reclaimable bytes, and any `Blockers`. Raises `VersionDeletePreviewEventHandler` with the plan. Returns `null` **only** when no target was supplied or no project is loaded; a refused-but-computable delete comes back as a plan with `CanDelete == false`, not as `null`. |
| `RequestVersionDeletePreview(string versionName) → VersionDeletePlan?` | Tag-resolving overload; an unknown tag resolves to `null` target and therefore returns `null`. |
| `RequestDeleteVersion(ProjectData? target, bool confirmed = false) → bool` | Deletes one version: its exclusive backup blobs, its list entry and its backup folder. Builds a plan first and refuses on any blocker — the current project main, the last remaining version, a version not in the list, or a non-`Idle` manager. When `confirmed` is `false` it raises an `IDialogService.Confirm` prompt naming the version and the blob count; `confirmed: true` skips it (CLI `--yes`, tests, and the WPF path which has already shown its own confirm window). Always raises `VersionDeleteCompleteEventHandler` (`Deleted` / `Blocked` / `Failed`), always ends on `Idle`, and never decrements `LocalUpdateCount`. Returns `true` only on `Deleted`. |
| `RequestDeleteVersion(string versionName, bool confirmed = false) → bool` | Tag-resolving overload; an unknown tag reports `Blocked` rather than throwing. |
| `RequestRenameVersion(ProjectData? target, string? newVersionName, string? newUpdateLog) → bool` | Re-tags a stored version: a new `UpdatedVersion`, a new `UpdateLog`, or both. Passing `null` for either leaves it as-is, so a log-only edit never trips version-name validation. Refuses on: null target, no project loaded, a non-`Idle` manager, a target absent from the version list, an empty / whitespace-padded / period-terminated name, a name containing `Path.GetInvalidFileNameChars()`, and a name that already exists (a duplicate tag would silently merge two versions, because `ProjectData.Equals` compares `UpdatedVersion` only). When the target is also the project main, `ProjectMain` — stored separately from `ProjectDataList` — is re-tagged in the same breath, otherwise the main pointer would be orphaned. Persists through `BackupManager.PersistMetaData(takeBackup: true)`; **a failed persist rolls the in-memory edit back** so the store and the file stay in sync. A no-op request (`"Nothing to change."`) returns `true`. Always raises `VersionRenameCompleteEventHandler`. **The backup folder is deliberately not renamed** — it stays `Backup_<original name>` — because blobs are referenced by absolute `DataSrcPath` and moving a folder other snapshots point into has no atomic undo. |
| `RequestProjectIntegrityCheck(bool forceFullHash = false)` | Async hash comparison of all tracked files vs. disk. Sets `FileManager.IntegritySamplePercent` for the run: `forceFullHash` pins 100%, otherwise the saved fast-check sample percent applies (§12.4). Both pre-checkout gates pass `true`, so a sampled setting can never weaken a checkout decision |
| `RequestRecentProjects()` / `RequestFastIntegritySamplePercent()` / `RequestSaveFastIntegritySamplePercent(int)` | Pass-throughs to `SettingManager` (§8.6) for the GUI menu strip |
| `RequestFetchBackup()` | Populate backup version list |
| `RequestFileRestore(file, state)` | Queue a single file for restore |
| `RequestRevertChange(file) → bool` | Revert a single `IntegrityChecked`-flagged file. Returns `true` if the `IntegrityChecked` flag is cleared after the call (success); `false` if `FileManager.RevertChange` re-applied the flag due to missing backup or missing project file entry. |
| `RequestOverlappedFileAllocation(overlaps, newFiles)` | Accept resolved overlap decisions from the UI |
| `RequestProjVersionDiff(srcData)` | Compute diff between a given version and main |
| `RequestProjectCompatibility(srcData)` | Compute similarity of a source project against all history entries |
| `RequestExportProjectBackup(ProjectData)` | Export a version's files as a zip archive |
| `RequestExportProjectVersionLog(ProjectData)` | Export a `.VersionLog` file for a version |
| `RequestExportProjectFilesXLSX(files, projData)` | Export file list as `.xlsx` |

---

### 8.2 `FileManager`

`IntegrityFileOutcome` (runtime-only enum, never serialized, declared beside `DataState`) is streamed via `IntegrityFileProgressEventHandler(relPath, outcome)` — exactly one final verdict per file: the compare pass reports `Checked` / `Modified` / `MetadataFallback`, the add/delete passes report `Added` / `Deleted`, and hash-retry exhaustion additionally reports `HashFailed`. The payload is two by-value arguments, so streaming allocates nothing per event.

**Owns:** pre-staged dict, registered-changes dict, references to main project dicts, ignore data.

**Concurrency:** `SemaphoreSlim(12, 12)` limits concurrent MD5 operations. The explicit `maxCount` is load-bearing: an unbalanced `Release()` now throws `SemaphoreFullException` instead of silently raising the cap (a stray `Release` in a non-acquiring path had eroded the 12-way limit by one per staging call).

`IntegritySamplePercent` (1–100, set per run by `MetaDataManager`) is the share of intersecting files that get a full MD5 hash during `MainProjectIntegrityCheck`; unsampled files verify by size/version metadata (§12.4).

#### Internal Dictionaries
| Name | Key | Value | Purpose |
|---|---|---|---|
| `_preStagedFilesDict` | `DataRelPath` | `ProjectFile` | Files collected from the source folder before hashing |
| `_registeredChangesDict` | `DataRelPath` | `ChangedFile` | Hashed and categorized changes, ready for staging |
| `_projectFilesDict` | `DataRelPath` | `ProjectFile` | Mirror of `MainProjectData.ProjectFiles` |
| `_projectFilesDict_namesSorted` | `DataName` | `List<ProjectFile>` | For overlap detection of top-level source files |
| `_backupFilesDict` | `DataHash` | `ProjectFile` | Mirror of `ProjectMetaData.BackupFiles` |

#### Key Operations

**`RetrieveDataSrc(srcPath)`**
0. Purge stale `IntegrityChecked` entries from `_registeredChangesDict` (leftovers of earlier integrity runs would otherwise accumulate for the whole session and ride into the next update).
1. Check for a `.VersionLog` file; if found, deserialize it as `_srcProjectData`.
2. Check for a `DeployAssistant.deploy` file; if found and valid, restore previous file allocation.
3. Otherwise, scan all files/directories:
   - Files in **sub-directories** → added to `_preStagedFilesDict` directly with correct relative paths.
   - Files in the **top directory** → handled as "abnormal" (may be updates to files that live in sub-directories).
4. Fire `DataPreStagedEventHandler`.

**`HandleAbnormalFiles(srcPath, topDirFilePaths[])`** (overlap detection)
- For each top-level file:
  - If found in `_projectFilesDict_namesSorted` with **≥2 matches** → add to overlap list (open `OverlapFileWindow`).
  - If found with **exactly 1 match** → move/copy to the correct relative path automatically.
  - If **no match** → treat as a new file, show in new-file list alongside directory candidates.

**`StageNewFilesAsync()`**
1. Hash all un-hashed pre-staged files concurrently.
2. Call `UpdateStageFileList()`:
   - `Restored` files → look up backup by hash.
   - `Deleted` files → create delete change.
   - Files matching an existing project path with a different hash → `Modified`.
   - Files with no matching project path → `Added`.
3. Clear pre-staged dict, fire `DataStagedEventHandler`.

**`MainProjectIntegrityCheck()`** (async)
0. Refuses to run when the loaded `ProjectContext` belongs to a different project than
   `_dstProjectData` (stale-context guard, path compared OrdinalIgnoreCase). Every early-exit
   path — missing project, missing context, stale context — still fires
   `IntegrityCheckEventHandler` with the reason and an empty list, so consumers can never
   wait forever on a check that will not report.
1. Gather all files/dirs on disk, excluding the ignore list.
2. Compare against recorded `ProjectFiles`:
   - Added / deleted files & directories → generate `IntegrityChecked` change entries.
   - Intersecting files → parallel MD5 compare; mismatches → `Modified | IntegrityChecked`. When `IntegritySamplePercent < 100`, only a per-run random sample of that share is hashed; the rest go through the size/version metadata fallback (`VerifyByMetadata`).
3. Fire `IntegrityCheckEventHandler` with log and changed list.

**`FindVersionDifferences(src, dst, isRevert)`** — diff between two `ProjectData` snapshots, used for revert.

**`FindVersionDifferencesForIntegration(src, dst, out significantDiff)`** — diff for merge; applies integration ignore filter to compute a "significant diff" count alongside the full diff.

**`ProjectIntegrityCheck(targetProject)`** — synchronous version used by clean-restore; compares recorded state vs. disk and builds a change list that the backup manager then applies.

---

### 8.3 `BackupManager`

**Owns:** reference to `ProjectMetaData` (and its `BackupFiles` and `ProjectDataList`).

**`BackupProject(ProjectData)`** (called automatically on every `ProjLoadedCallback`)
1. If the version is not already in `ProjectDataList`, call `RegisterBackupFiles`.
2. Prepend to `ProjectDataList` (newest first via `AddFirst`).
3. Serialize `ProjectMetaData` to `ProjectMetaData.bin`.
4. Call `ProjectMetaData.SetProjectMain(projectData)`.

**`RegisterBackupFiles(ProjectData)`**
- For each `ChangedFile` in the version:
  - If the `DstFile`'s hash is not already in `BackupFiles`, copy the file to `Backup_{ProjectName}\Backup_{Version}\` and add to `BackupFiles` (key = hash).
  - Sets `DeployedProjectVersion` on `DstFile`.
  - If the snapshot contributed **no** new blobs, `RemoveEmptyBackupFolder` is called on the folder it just created.

**`RemoveEmptyBackupFolder(path)` — the delete landmine, now gated.** The delete here is a *recursive* `Directory.Delete(path, true)`, so it is not "delete the folder if it is empty" in any filesystem sense — an earlier revision of this spec described it that way and was wrong. It is now gated on a re-scan of `BackupFiles`: if **any** live backup entry still has `DataSrcPath` pointing at the folder, the folder is kept and a trace warning is emitted instead. Without that gate, a snapshot that reuses an existing folder (or a folder shared after a delete-time relocation) would have its bytes wiped out from under every surviving entry that references them. Any future code that creates or reuses a backup folder must preserve this invariant: **a folder referenced by a live `BackupFiles` entry is never removed.**

**`RevertProject(ProjectData target, List<ChangedFile> diffs)`**
- Calls `FileHandlerTool.TryApplyFileChanges(diffs)` with retry loop.
- On success, fires `ProjectRevertEventHandler` → routes to `MetaDataManager.ProjectChangeCallBack` → sets `MainProjectData`.

**`PersistMetaData(bool takeBackup = true) → bool`**
Extracted out of `BackupProject` so operations that mutate the store *without* changing the project main — version delete, version rename — can save without pretending to check out. Writes through `FileHandlerTool.TrySerializeProjectMetaData(data, path, backupPath)`: a temp file plus `File.Replace`, so a crash or a full disk can never leave a half-written `ProjectMetaData.bin`. When `takeBackup` is true the previous content is moved to the `.bak` path **as part of the same `Replace` call**, so the rollback copy is produced atomically too.

The `.bak` lives at `Backup_<ProjectName>\ProjectMetaData.bin.bak`, *not* beside `ProjectMetaData.bin` in the project root. This is deliberate: the integrity scan's ignore list matches the exact name `ProjectMetaData.bin`, so a sibling `.bak` would be staged as a brand-new project file on the next check. The temp file is placed in the same folder as the backup (same volume, which `File.Replace` requires) and removed in a `finally`.

**Backup directory layout:**
```
<ProjectPath>/
└── Backup_<ProjectName>/
    ├── ProjectMetaData.bin.bak      ← rollback copy written by PersistMetaData
    ├── Backup_<VersionName>/        ← per-version blob folder
    │   └── <files keyed by their state>
    └── Backup_Shared[n]/            ← created only by a delete-time relocation
```

#### 8.3.1 Version deletion

Three pieces: a pure planner, a metadata-first executor, and the refcount rule that ties them together.

**The `BackupFiles` hash-refcount rule.** `BackupFiles` is a *global, content-addressed dedup store* keyed by MD5 — one blob per distinct hash, shared by every snapshot that contains a file with that hash. There is no stored reference count, so deleting a version has to derive one:

- A blob's referents are computed from each snapshot's **`ProjectFiles`** (the complete file set), **never from `ChangedFiles`** (that version's delta). A delta list under-reports what a survivor still needs, and unregistering on that basis destroys live blobs.
- The survivor set is every other entry in `ProjectDataList` **plus** `ProjectMain`, which is serialized separately from the list.
- Hash referenced only by the target → **unregister the entry and delete the bytes**.
- Hash also referenced by a survivor → **must survive untouched**.
- Directory-type entries and entries with an empty `DataHash` are skipped.

**`BuildVersionDeletePlan(ProjectData target, ProjectData? currentMain) → VersionDeletePlan`**
Touches nothing — no disk, no metadata. Produces `ExclusiveHashes`, `SharedHashes`, `RelocationHashes`, `ReclaimableBytes`, `BackupFolderRemovable` and `Blockers`. Blockers it raises itself:

| Blocker | Why |
|---|---|
| target is not in `ProjectDataList` | nothing to remove |
| `ProjectDataList.Count <= 1` | the last remaining version cannot be deleted |
| target is the current project main | `ProjectMain` is stored separately; deleting it would leave the store pointing at a version that no longer exists |

`MetaDataManager.BuildDeletePlan` folds in one more that it can see and the backup manager cannot: a non-`Idle` manager. `CanDelete` is simply `Blockers.Count == 0`.

**The blob-relocation requirement.** Backup folders are per-version, so a blob that several snapshots share physically lives in whichever version's folder first registered it — which may well be the folder about to be deleted. Every *surviving* hash whose `DataSrcPath` equals the target's backup folder is listed in `RelocationHashes` and **must be copied out before that folder is removed**, otherwise the survivors end up pointing at deleted bytes. Relocation destinations are `Backup_<ProjectName>\Backup_Shared[_n]`: exactly one level under the backup root and still in `Backup_...` leaf form, because `ProjectMetaData.SetBackupFilesPath` rebuilds every backup path as `Backup_<ProjectName>\{Path.GetFileName(DataSrcPath)}` when the project moves. Destination slots are reserved up front against the whole store, because two per-version blobs can legitimately share a `DataRelPath` and would collide once merged into one folder. Copies are verified **by file length, not by re-hashing** — all MD5 work in this app is funnelled through `FileManager`'s `SemaphoreSlim(12)` limiter and an unbounded hashing path here would sidestep it. A blob whose bytes are already missing is repointed anyway, so no surviving entry is left pointing into a folder that is about to go.

**`DeleteVersion(VersionDeletePlan plan) → VersionDeleteResult`** — metadata-first, in this order:

1. Raise `ManagerStateEventHandler(Deleting)` for the whole mutation window.
2. Relocate every surviving blob living inside the doomed folder.
3. Unregister the exclusive blobs from `BackupFiles` **in place** (other managers cache that dictionary object).
4. Remove the `LinkedListNode` from `ProjectDataList`, preserving ordering.
5. **Persist.** Until this succeeds nothing on disk has been destroyed.
6. Only now delete the unregistered blob bytes, then the backup folder if it is still unreferenced.
7. Re-stamp `IsProjectMain` across the surviving list.
8. Raise `ManagerStateEventHandler(Idle)`, `FetchCompleteEventHandler` (which `MetaDataManager` re-fires as `FetchRequestEventHandler`) and `VersionDeletedEventHandler` exactly once, **outside** the try block — raising them inside meant a throwing subscriber landed in the rollback path after the store was already committed.

Failure semantics follow the persist boundary. **Before** the persist, everything rolls back byte-for-byte — relocations undone, backup entries restored, the list node re-inserted at its original position, `ProjectMetaData.bin` restored from the `.bak` — and the outcome is `Failed` with metadata and disk unchanged. **After** the persist the commit stands: rolling back would re-add entries the saved file does not have *and* delete the relocated blobs it does point at, so a post-persist failure is reported as `Deleted` with the cleanup problem in `Messages`.

`RemoveBackupFolderIfUnreferenced` re-verifies against `BackupFiles` rather than trusting the plan, for the same reason `RemoveEmptyBackupFolder` does — and for a second one: a deleted version must not leave its folder behind either, because `RegisterBackupFiles` would remove it wholesale on the next load.

---

### 8.4 `UpdateManager`

**`UpdateProjectMain(updaterName, updateLog, currentProjectPath)`**
1. Validate: metadata, project data, staged changes, path match.
2. Generate a new version name: `{ProjectName}_{MachineName}_{yyyy_MM_dd}_v{n+1}`.
3. Clone `_projectMain` into `updatedProjectData`.
4. `RegisterFileChanges(...)`: update `ProjectFiles` dict (add/modify/remove entries) and build changelog.
5. `FileHandlerTool.TryApplyFileChanges(_currentProjectFileChanges)` with retry loop.
6. Increment `LocalUpdateCount`, stamp all metadata fields, fire `ProjectUpdateEventHandler`.

**`MergeProjectMain(updaterName, updateLog, currentProjectPath)`**
- Integration path: applies changes from a loaded source `ProjectData` into main. Uses `_srcProjectData` instead of the current project as the base.

**`TryIntegrateSrcProject(srcProject, fileDifferences)`**
- Validates that each Added/Modified file in the diff has a matching hash in the currently staged changes. If any mismatch is found, fires `ReportFileDifferences` and returns false.

---

### 8.5 `ExportManager`

**`ExportProject(ProjectData)`**
1. Create `<ProjectPath>/Export_<ProjectName>/<VersionName>/` directory.
2. For each file in `ProjectFiles`: look up its backup by hash from `BackupFiles` and copy it.
3. Serialize `.VersionLog` into the export directory.
4. Zip the export directory.
5. Fire `ExportCompleteEventHandler` with the parent directory path.

**`ExportProjectVersionLog(ProjectData)`**
- Serialize the `ProjectData` as a Base64-encoded JSON `.VersionLog` file to `Export_<ProjectName>/<VersionName>/`.

**`ExportProjectFilesXLSX(ProjectData, ICollection<ProjectFile>)`** (two overloads)
- Writes a single-sheet workbook with columns: DataName, DataType, DataSize, BuildVersion, DeployedProjectVersion, UpdatedTime, DataState, DataSrcPath, DataRelPath, DataHash.
- Output path: `<ProjectPath>/Export_XLSX/<VersionName>_ProjectFiles.xlsx`.

**`ExportDiffPackage(ProjectData, List<ChangedFile>)`** — diff-only sync package (zip); serves both Metafile Compare's Export Sync Package and `VersionDiffWindow`'s Export Diff. (The old `ExportProjectChanges` stub was deleted.)

---

### 8.6 `SettingManager`

**Startup (`Awake()`):** wiring only. **`PromptPreviousProjectRestore()`** reads `DeployAssistant.config`, validates the stored path (must contain a `ProjectMetaData.bin`), and prompts to re-open — exposed as `MetaDataManager.RequestPreviousProjectRestore()` and called by the GUI only after the ViewModels have subscribed (`MainWindow.Loaded`).

**`SetRecentDstDirectory(string path)`** — read-merge-write: loads the existing config, updates `LastOpenedDstPath`, and re-serializes, so other fields (`Language`) survive. It also maintains the `RecentProjects` MRU: dedupe (OrdinalIgnoreCase), insert at front, cap 8. **`GetSavedLanguage()` / `SaveLanguage(code)`**, **`GetRecentProjects()`** and **`GetFastIntegritySamplePercent()` / `SaveFastIntegritySamplePercent(int)`** (clamped 1–100, default 100) use the same read-merge-write path.

**`ConfigDirectoryOverride`** (static) — test seam that redirects `DeployAssistant.config` away from the real `Documents` folder. Both test assemblies set it in a `[ModuleInitializer]` so test runs neither read nor pollute the developer's live settings (a saved fast-check sample percent had broken version-cut tests nondeterministically).

**On `MetaDataLoadedCallBack`:**
- Reads or creates `DeployAssistant.ignore` at the project root.
- Fires `IgnoreDataLoadedEventHandler(ProjectMetaData, ProjectIgnoreData)`. `MetaDataManager` composes the two into a `ProjectContext` and re-fires `ProjectContextLoadedEventHandler`, which `FileManager` consumes. (This replaced the old `UpdateIgnoreListEventHandler`; the two-event `MetaDataLoaded` + `UpdateIgnoreList` coordination it superseded was the cause of regression #17.)

**`RegisterSrcDeploy(path, registeredFiles)`** — Writes a `DeployAssistant.deploy` file at a source path.

---

## 9. Utility Layer

### 9.1 `FileHandlerTool`

Low-level I/O with typed serialization helpers.

| Method | Description |
|---|---|
| `TrySerializeProjectMetaData(data, path)` | JSON → Base64 → file, written atomically (temp + `File.Replace`); keeps no backup |
| `TrySerializeProjectMetaData(data, path, backupFilePath)` | Same, but the previous content is moved to `backupFilePath` as part of the same atomic swap (see §13) |
| `TryRestoreProjectMetaData(backupFilePath, path)` | Copies the `.bak` back over the store; `false` when the backup is absent or the copy fails |
| `TryDeserializeProjectMetaData(path, out data)` | file → Base64 → JSON. Forwards to `TryLoadProjectMetaData(...).Success`; the `bool` contract is unchanged |
| `TryLoadProjectMetaData(path, out data)` | Same load, returning a structured `MetaDataLoadResult` instead of a bare `bool` (see §15) |
| `MaxSupportedMetaDataSchemaVersion` (const = **1**) | Highest V1 store schema this build reads. V1 is still the runtime format — the V1→V2 migration pipeline is present but dormant (§15) |
| `TryLoadProjectStore` / `TrySerializeProjectStore` / `TryRollbackProjectStore` | V2 `ProjectStore` path, including V1→V2 migration. **Not called from the runtime path** |
| `TrySerializeProjectData(data, path)` | JSON → Base64 → file |
| `TryDeserializeProjectData(path, out data)` | file → Base64 → JSON |
| `TrySerializeJsonData<T>(path, obj)` | Indented JSON → file (used for `.config`, `.ignore`, `.deploy`) |
| `TryDeserializeJsonData<T>(path, out obj)` | Bytes → JSON deserialization |
| `TryApplyFileChanges(List<ChangedFile>)` | Iterates changes and calls `HandleData`; skips `IntegrityChecked` entries |
| `HandleData(...)` / `HandleFile(...)` / `HandleDirectory(...)` | Add = copy, Deleted = delete, other = overwrite copy |
| `MoveFile(src, dst)` | Move with directory creation |

**Serialization format for `ProjectMetaData` and `ProjectData`:** JSON serialized then Base64-encoded, stored as plain text with a `.bin` or `.VersionLog` extension.

**Serialization format for config/ignore/deploy:** Indented UTF-8 JSON (not Base64).

### 9.2 `HashTool`

| Method | Description |
|---|---|
| `GetFileMD5CheckSum(ProjectFile file)` | Synchronous; mutates `file.DataHash` in-place |
| `GetFileMD5CheckSum(projectPath, relPath)` | Returns MD5 hex string |
| `GetFileMD5CheckSumAsync(ProjectFile file)` | Async; mutates `file.DataHash` |
| `GetFileMD5CheckSumAsync(fullPath)` | Async; returns MD5 hex string |
| `TryCompareMD5CheckSum(src, dst, out result)` | Returns true if hashes match |
| `GetUniqueComputerID(userID)` | SHA-256 of string, first 5 bytes as hex |
| `GetUniqueProjectDataID(ProjectData)` | SHA-256 of concatenated `relPath\hash` strings |

> **Known bug in `TryCompareMD5CheckSum`:** `dstHashString` is computed from `srcHashBytes` instead of `dstHashBytes`, so the comparison always returns `true`.

### 9.3 `LogTool`

Static helper for building changelogs in `UpdateManager`:
- `RegisterUpdate(log, srcVersion, dstVersion)` — header line
- `RegisterChange(log, state, data)` — single file entry
- `RegisterChange(log, state, srcData, dstData)` — two-sided diff entry (shows hash and build version change)

### 9.4 `RelayCommand`
Standard `ICommand` implementation (lives in `DeployAssistant/ViewModel/Utils/RelayCommand.cs`). Constructor takes `Action<object> execute` and optional `Func<object, bool> canExecute`. `CanExecuteChanged` is raised on `CommandManager.RequerySuggested`.

---

## 10. ViewModel Layer

### 10.1 `ViewModelBase`
Implements `INotifyPropertyChanged` and `IDisposable`. Provides `OnPropertyChanged(string propertyName)` and `SetField<T>(ref field, value)`. Child classes register event-handler teardowns via `TrackUnsubscribe(Action)` — all registered unsubscribers are invoked when the ViewModel is disposed, preventing event-handler leaks when secondary windows are closed.

### 10.2 `MainViewModel`
Root ViewModel composed in `MainWindow`. Constructs and exposes:
- `MetaDataVM` (`MetaDataViewModel`)
- `FileTrackVM` (`FileTrackViewModel`)
- `BackupVM` (`BackupViewModel`)

Calls `App.AwakeModel()` in constructor.

### 10.3 `MetaDataViewModel`
**Bound to:** main panel metadata header.

**Observable state:** `CurrentProjectPath`, `ProjectName`, `CurrentVersion`, `ProjectFiles` (all files in main snapshot), `UpdaterName`, `UpdateLog`, `CurrentMetaDataState`, and the busy-overlay set: `IsBusy` (true for any non-`Idle` state), `BusyTitle`, `BusyProgress` / `BusyIndeterminate` (determinate only while `IntegrityChecking`), `BusyCountText`, `BusyLog` (ring of the last 200 `BusyLogEntry` rows). Per-file events are enqueued lock-free from worker threads and drained by a 100 ms `DispatcherTimer` in batches of ≤500, so UI churn is fixed at 10 Hz regardless of project size.

**Commands:**
| Command | Action |
|---|---|
| `GetProject` | `IDialogService.PickFolder`; call `RequestProjectRetrieval`. If no metadata found, prompt to initialize. |
| `ConductUpdate` | Validate `UpdaterName` and `UpdateLog`; call `RequestProjectUpdate`. |

### 10.4 `FileTrackViewModel`
**Bound to:** file staging panel.

`RevertAllChanges` reverts every `IntegrityChecked` staged entry after one confirmation (staged-list context menu "Revert All"). `CanRestoreFile` accepts `Deleted`, non-dst (historical `SrcFile`) rows, **and `Added` rows** — restoring an Added row brings that version's own stored file back. Folder picking goes through `IDialogService.PickFolder`, never a dialog type directly.

**Observable state:** `ChangedFileList` (staged + pre-staged), `SelectedItem`, `SrcProjectFile`.

**Commands:**
| Command | Enabled Condition | Action |
|---|---|---|
| `GetDeploySrcDir` | Idle & main project loaded | `FolderBrowserDialog`; `RequestSrcDataRetrieval` |
| `RefreshDeployFileList` | always | Clear staged; re-scan src dir |
| `StageChanges` | Idle & list not empty | `RequestStageChanges` |
| `ClearNewfiles` | List not empty | `RequestClearStagedFiles` |
| `CheckProjectIntegrity` | Idle | `RequestProjectIntegrityCheck` (on background thread) |
| `AddForRestore` | Idle & selected file is Deleted or not IsDstFile | `RequestFileRestore` |
| `RevertChange` | always | `RequestRevertChange` (selected must be `IntegrityChecked`) |
| `GetDeployedProjectInfo` | SrcProjectData loaded | Open `IntegrityLogWindow` |
| `CompareDeployedProjectWithMain` | Idle & SrcProjectData loaded | `RequestProjVersionDiff`; open `VersionDiffWindow` |
| `SrcSimilarityWithBackups` | Idle & SrcProjectData loaded | `RequestProjectCompatibility`; open `VersionComparisonWindow` |

**Callbacks from MetaDataManager:**
- `OverlappedFileSortEventHandler` → open `OverlapFileWindow`
- `SrcProjectLoadedEventHandler` → cache `_srcProjectData`
- `PreStagedChangesEventHandler` → update `ChangedFileList`
- `FileChangesEventHandler` → update `ChangedFileList`
- `IntegrityCheckCompleteEventHandler` → open `IntegrityLogWindow`
- `ProjComparisonCompleteEventHandler` → open `VersionDiffWindow`
- `SimilarityCheckCompleteEventHandler` → open `VersionComparisonWindow`
- `ManagerStateEventHandler` → cache state; force layout update

### 10.5 `BackupViewModel`
**Bound to:** backup/history panel.

**Observable state:** `BackupProjectDataList` (version history), `SelectedItem`, `UpdaterName`, `UpdateLog`, `DiffLog` (`ObservableCollection<DiffItem>` — **one row per `ChangedFile`**, not the old dst+src double rows; `DiffItem.RestoreTarget` = `SrcFile ?? DstFile` keeps Restore working from the single row). (`SafeCheckoutMode` is gone with the Safe toggle — see the naming note below.)

**Naming note — `CheckoutBackup` means *revert to*, not *check out a working copy*.** The command property is `CheckoutBackup`, its handler is `Revert`, its guard is `CanRevert`, and its UI labels are the toolbar "Checkout" button and the context-menu "Checkout" item (the separate "Clean Restore" menu item was merged into it, and the later Safe toggle was removed once the gate made every checkout safe — UI checkouts are always `Fast`). They are all the same operation: apply a stored snapshot to the working directory. `CanRevert` is also reused as the guard for `ViewFullLog`. Nothing here creates a branch or a working copy — there is only one working directory. New code should prefer the manager-side vocabulary (`RequestCheckoutVersion` / `CheckoutMode`) and treat the ViewModel's `Revert*` members as legacy names for the same thing.

**Commands:**
| Command | Enabled Condition | Action |
|---|---|---|
| `FetchBackup` | Idle & MetaData loaded | `RequestFetchBackup`. Bound to the ⟳ button in the Version History panel header (previously declared but not reachable from the UI) |
| `CheckoutBackup` | Idle & item selected & main loaded | `StartGatedCheckout(selected, CheckoutMode.Fast)` |
| `CleanRestoreBackup` | Idle & item selected & main loaded | `StartGatedCheckout(selected, CheckoutMode.CleanRestore)`. Bound to the "Clean Restore" context-menu item; the guard now also requires a selection, which it previously did not |
| `DeleteVersion` | Idle & item selected & selection is **not** the project main & more than one version exists | `RequestVersionDeletePreview` on a background thread → `VersionDeleteConfirmWindow` → `RequestDeleteVersion(plan.Target, confirmed: true)` |
| `RenameVersion` | Idle & item selected | `VersionRenameWindow.Prompt` → `RequestRenameVersion` on a background thread |
| `ExportVersion` | Idle | `RequestExportProjectBackup` (background) |
| `ExtractVersionLog` | always | `RequestExportProjectVersionLog` |
| `ViewFullLog` | Idle & item selected | Open `IntegrityLogWindow` with selected version |
| `CompareDeployedProjectWithMain` | Idle & item selected | `RequestProjVersionDiff`; the resulting `VersionDiffWindow` is opened by `FileTrackViewModel`'s `ProjComparisonComplete` callback — `BackupViewModel` no longer subscribes to that event itself (its duplicate subscription opened a second window per compare) |

**`StartGatedCheckout(target, mode)`** — both checkout commands funnel through it. It parks the request under a lock (a second checkout while one is pending is refused with a message, not queued) and kicks off `RequestProjectIntegrityCheck(forceFullHash: true)` on a background thread. Nothing is written until `ProjectIntegrityCheckCallBack` has shown the gate.

**Callbacks from MetaDataManager:**
- `FetchRequestEventHandler` → replace `BackupProjectDataList`
- `IntegrityCheckCompleteEventHandler` → `ProjectIntegrityCheckCallBack`, the pre-checkout gate. It only reacts to the integrity check *this* ViewModel started (the toolbar's own check leaves the pending slot null and passes straight through to `FileTrackViewModel`). Shows `CheckoutGateWindow.Ask`; on a `Fast` checkout with local modifications it discards each `IntegrityChecked` file via `RequestRevertChange` first — a snapshot-to-snapshot diff would otherwise leave drift in files that are identical between the two snapshots — and aborts with a file list if any discard fails. `CleanRestore` rescans the working directory itself and needs none of this.
- `CheckoutCompleteEventHandler` → report success (version + files applied) or failure (with `Messages`). No list refresh is done here: a successful checkout re-points `MainProjectData`, which `BackupManager` answers with its own fetch.
- `VersionDeletePreviewEventHandler` → if the preview is one this ViewModel asked for (guarded by an `Interlocked.Exchange` on the pending target), show `VersionDeleteConfirmWindow` and, on confirm, call `RequestDeleteVersion(..., confirmed: true)` so the manager does not raise a second prompt.
- `VersionDeleteCompleteEventHandler` → on `Deleted`, move the selection to the first surviving row (the list has already been replaced by the fetch callback) and report entries removed / files relocated / bytes reclaimed; on `Blocked` or `Failed`, report the reason.
- `VersionRenameCompleteEventHandler` → report the new tag, the no-op case, or the failure.

**`ExportRequestCallBack`** — Opens Windows Explorer at the exported folder path.

### 10.6 `VersionDiffViewModel`
**Created by:** `FileTrackViewModel` or `BackupViewModel` callback. Passed `srcProject`, `dstProject`, `diff` at construction.

**Observable state:** `SrcProject`, `DstProject`, `Diff` (list of changed files).

**Commands:** `ExportDiffFiles` — calls `RequestExportDiffPackage(dstProject, Diff)` (diff-only sync-package zip; replaced the deleted `RequestExportProjectVersionDiffFiles` stub).

### 10.7 `VersionIntegrationViewModel`
**Stub.** Constructor receives `srcProject`, `dstProject`, `diff` but no logic is implemented.

### 10.8 `VersionCheckViewModel`, `VersionCompatibilityViewModel`, `OverlapFileViewModel`
Stub or minimal implementations referenced by view windows. Details TBD in a future pass.

---

## 11. View Layer (Windows)

| Window | Created by | Purpose |
|---|---|---|
| `MainWindow` | App startup | Primary shell; hosts tab panels bound to `MainViewModel` |
| `IntegrityLogWindow` | `FileTrackViewModel`, `BackupViewModel` callbacks | Displays integrity check results or full version log |
| `VersionDiffWindow` | `FileTrackViewModel`, `BackupViewModel` callbacks | Shows file-level diff between two project versions |
| `VersionIntegrationView` | `VersionDiffWindow` or integration flow | Integration / merge confirmation UI |
| `OverlapFileWindow` | `FileTrackViewModel` callback | Lets user resolve file allocation conflicts |
| `VersionComparisonWindow` | `FileTrackViewModel` similarity callback | Displays compatibility scores vs. history entries |
| `CompatibleVersionWindow` | (referenced, wiring TBD) | Shows compatible versions |
| `ErrorLogWindow` | (referenced, wiring TBD) | Displays error logs |
| `CheckoutGateWindow` | `BackupViewModel.ProjectIntegrityCheckCallBack` | Pre-checkout gate: shows the target version, the mode, and the list of local modifications that will be discarded (Fast) or repaired (CleanRestore) |
| `VersionDeleteConfirmWindow` | `BackupViewModel.VersionDeletePreviewCallBack` | Renders a `VersionDeletePlan` — blast radius, reclaimable bytes, relocation count, blockers — and takes the explicit confirmation |
| `VersionRenameWindow` | `BackupViewModel.RenameSelectedVersion` | Edits `UpdatedVersion` and/or `UpdateLog`, pre-filled with the current values |

**Window policy, reaffirmed.** The dockable-islands plan (see §18) was dropped; secondary windows stay windows. The informational ones — `IntegrityLogWindow`, `VersionDiffWindow`, `VersionIntegrationView`, `OverlapFileWindow`, `VersionComparisonWindow` — are opened from `MainWindow.xaml.cs` as **owned, `WindowStartupLocation.CenterOwner`, non-modal `Show()`** windows, and are expected to stay that way: they are read-alongside surfaces, so blocking the shell would be wrong.

The three gate windows above are the deliberate exception. They exist to take a yes/no decision that a destructive operation is waiting on, so they are `ShowDialog()` modal, owned by `Application.Current.MainWindow`, and expose a `static bool Ask/Confirm/Prompt(...)` entry point rather than being constructed by the caller.

---

## 12. Key Workflows (End-to-End)

### 12.1 Open / Initialize a Project
1. User clicks **Open Project** → `MetaDataViewModel.RetrieveProject`.
2. `RequestProjectRetrieval(path)`:
   - Finds `ProjectMetaData.bin` → deserialize → fix paths if moved → set `MainProjectData`.
   - Not found → prompt → `RequestProjectInitialization(path)`:
     - Parallel file scan with ignore list applied.
     - Parallel MD5 hashing (75% of CPU cores).
     - Bootstrap `ProjectMetaData`, serialize to disk.
3. `SettingManager` saves path to `DeployAssistant.config`.
4. `BackupManager` receives `ProjLoadedCallback` → calls `BackupProject` → serializes `ProjectMetaData.bin`.

### 12.2 Stage → Update
1. User sets a **source directory** → `RequestSrcDataRetrieval`:
   - Loads `.VersionLog` if present.
   - Scans for sub-dir files → pre-staged.
   - Handles top-dir files → auto-routes or shows `OverlapFileWindow`.
2. User clicks **Stage** → `RequestStageChanges`:
   - Async hash all pre-staged files.
   - Classify each as Added / Modified / Deleted / Restored.
   - Send staged list to `UpdateManager`.
3. User fills **Updater Name** and **Update Log**, clicks **Update** → `RequestProjectUpdate`:
   - If `_srcProjectData` is set: offer integration merge path (`TryIntegrateSrcProject` → `MergeProjectMain`).
   - Otherwise: `UpdateManager.UpdateProjectMain`.
4. Files are physically copied/deleted by `FileHandlerTool.TryApplyFileChanges`.
5. New `ProjectData` fires through `ProjectUpdateEventHandler` → `MetaDataManager.MainProjectData` setter → `BackupManager.BackupProject` → serializes to disk.

### 12.3 Checkout a Version

Checkout applies a stored snapshot to the working directory. There is one entry point — `RequestCheckoutVersion(target, mode)` — and two modes. Neither front end exposes the mode any more: the pre-checkout gate already re-hashes the whole working directory, so a user-facing `CleanRestore` toggle only repeated that scan — the GUI checkbox and the CLI `C` binding were removed (2026-08-25) and every UI checkout runs `Fast` behind the gate. `CheckoutMode.CleanRestore` survives in Core for API stability and headless callers.

| | `CheckoutMode.Fast` | `CheckoutMode.CleanRestore` |
|---|---|---|
| Work list from | `FileManager.FindVersionDifferences(target, main, isRevert: true)` — snapshot-to-snapshot, metadata only | `FileManager.ProjectIntegrityCheck(target)` — full scan of the working directory against the target |
| Disk hashing | none | every intersecting file |
| Repairs drift caused outside DeployAssistant | no | yes |
| State sequence | `Processing` → `Reverting` → `Idle` | `CleanRestoring` → `Reverting` → `Idle` |

**The dirty-state gate.** Neither front end calls `RequestCheckoutVersion` directly from a menu. Both run a pre-checkout integrity check first and show what would be lost, because a `Fast` checkout only diffs the two snapshots: a file that is byte-identical between them but has been modified on disk is not in the diff at all, so the modification would silently survive a "restore".

1. Fetch the version list, select a version.
2. **GUI:** the command parks the request and runs `RequestProjectIntegrityCheck` on a background thread; `BackupViewModel.ProjectIntegrityCheckCallBack` then shows `CheckoutGateWindow`.
   **CLI:** `RevisionDetailScreen` pushes `CheckoutGateScreen`, which runs the same check behind a Spectre progress bar fed by `IntegrityProgressEventHandler`.
3. Clean working directory → plain confirm.
   Dirty working directory → the modifications are listed, and the two modes diverge:
   - **Fast** requires them to be discarded first. Each `IntegrityChecked` file goes through `RequestRevertChange`; if any fails (missing backup, missing project-file entry) the whole checkout aborts and the failed paths are reported. Nothing has been touched at that point.
   - **CleanRestore** repairs the drift as part of the checkout itself, so confirming *is* the whole gate.
4. `RequestCheckoutVersion(target, mode)` computes the work list, then `BackupManager.RevertProject` applies it via `FileHandlerTool.TryApplyFileChanges` (copies from the `BackupFiles` dict) with a retry loop.
5. On success `LastCheckedOut` is set, `MainProjectData` is re-pointed, and `BackupManager` re-fires its fetch so the history list re-renders. `CheckoutCompleteEventHandler` fires either way — a failed checkout reports the reason instead of doing nothing visible.

### 12.4 Integrity Check
1. User clicks **Check Integrity** in FileTrack panel.
2. `FileManager.MainProjectIntegrityCheck()` runs async:
   - Reads all files/dirs from disk; excludes ignore list.
   - Set-arithmetic vs. recorded list → Added/Deleted entries.
   - Parallel MD5 of intersecting files → Modified entries. **Fast check:** when the saved `FastIntegritySamplePercent` (GUI Settings menu, 1–100, default 100) is below 100, only a random sample of that share is hashed per run — a fresh `Random` each run, so a file sampled out this time can be caught the next — and the rest verify by size/version metadata. The pre-checkout gates always force 100% (`forceFullHash: true`), so sampling never weakens a checkout decision.
3. Result opens `IntegrityLogWindow` with a text log and list of deviant files.
4. User can select a deviant file and click **Revert Change** to restore it individually.

### 12.5 Export a Version
1. User selects a version in Backup panel, clicks **Export Version**.
2. `ExportManager.ExportProject`:
   - Looks up each file in the global `BackupFiles` dict by hash.
   - Copies to `Export_<ProjectName>/<VersionName>/`.
   - Writes `.VersionLog`.
   - Zips the directory.
3. `BackupViewModel.ExportRequestCallBack` opens Explorer at the export folder.

### 12.6 Version Diff / Compare
- **FileTrack panel:** After loading a source directory, **Compare with Main** → `FindVersionDifferences(src, main)` → `VersionDiffWindow`.
- **Backup panel:** Same flow with a selected history entry.
- **Similarity / Compatibility:** `RequestProjectCompatibility(src)` iterates all history entries, computes `FindVersionDifferencesForIntegration` for each, collects `ProjectSimilarity` objects → `VersionComparisonWindow`.

### 12.7 Delete a Version

Three phases: plan, confirm, execute. The plan phase mutates nothing, so it is always safe to run.

1. User selects a version in the history list and asks to delete it (GUI context menu "Delete Version...", CLI `x` on the revision detail screen).
2. **Plan.** `RequestVersionDeletePreview(target)` builds a `VersionDeletePlan` — which backup blobs are exclusive to this version, which are shared, which have to be relocated out of its folder, how many bytes come back, and any `Blockers`. Both front ends run this off the UI thread: it walks every backup entry and stats the blobs.
3. **Refuse or confirm.** A plan with blockers is rendered as a refusal and the delete key is never offered. Reasons: the version is the current project main (check out something else first), it is the last remaining version, it is not in the version list, or the manager is not `Idle`.
4. **Confirm.** GUI: `VersionDeleteConfirmWindow` shows the blast radius; CLI: `VersionDeleteGateScreen` shows the same and requires an explicit `y`. Both then call `RequestDeleteVersion(target, confirmed: true)` so the manager does not raise a second prompt. A caller that has *not* confirmed (a script, a test without `--yes`) gets the manager's own `IDialogService.Confirm`.
5. **Execute, metadata-first** (§8.3.1): relocate shared blobs out of the doomed folder → unregister exclusive blobs → drop the list node → **persist `ProjectMetaData.bin` with a `.bak`** → only then delete blob bytes and the backup folder → re-stamp `IsProjectMain`. A failure before the persist rolls everything back and reports `Failed`; a failure after it reports `Deleted` plus the cleanup problem.
6. `LastDeletedVersion` is set, `FetchRequestEventHandler` re-fires so both front ends re-render the list, and `VersionDeleteCompleteEventHandler` reports entries removed / files relocated / bytes reclaimed.

`LocalUpdateCount` is **not** decremented (§6.5), so revision numbers may have gaps after a delete.

### 12.8 Rename / Re-tag a Version

Renaming changes a snapshot's `UpdatedVersion` tag, its `UpdateLog`, or both. It never moves bytes.

1. User picks a version (GUI context menu "Rename / Re-tag...", CLI `r` on the revision detail screen) and edits two fields pre-filled with the current values.
2. A field left untouched is sent as `null`, which the manager reads as "leave alone" — so a log-only edit never trips version-name validation.
3. `RequestRenameVersion(target, newVersionName, newUpdateLog)` validates the new tag as a Windows path segment (non-empty, no leading/trailing whitespace, no trailing period, no `Path.GetInvalidFileNameChars()`) and rejects a tag that already exists — `ProjectData.Equals` compares `UpdatedVersion` alone, so a duplicate would silently merge two versions everywhere the list is searched.
4. If the target is also the project main, `ProjectMain` — serialized separately from `ProjectDataList` — is re-tagged in the same operation; otherwise the main pointer would be orphaned.
5. `BackupManager.PersistMetaData(takeBackup: true)` saves the store. **A failed persist rolls the in-memory edit back**, so the store and the file never disagree.
6. **The backup folder keeps its original name** (`Backup_<old tag>`), and the result message says so. Blobs are referenced by absolute `DataSrcPath`, and moving a folder that other snapshots' `BackupFiles` entries point into has no atomic undo. It costs nothing at runtime: `GetFileBackupSrcPath` is only consulted when registering a snapshot that is not yet in the version list, and a renamed snapshot is by definition already registered.

---

## 13. Persistence Files Summary

| Filename | Format | Location | Managed by |
|---|---|---|---|
| `ProjectMetaData.bin` | Base64(JSON(`ProjectMetaData`)) | `<ProjectPath>/` | `BackupManager` (write), `MetaDataManager` (read) |
| `ProjectMetaData.bin.bak` | Base64(JSON(`ProjectMetaData`)) | `<ProjectPath>/Backup_<Name>/` | `BackupManager.PersistMetaData` |
| `<Version>.VersionLog` | Base64(JSON(`ProjectData`)) | `<ProjectPath>/Export_<Name>/<Version>/` | `ExportManager` |
| `DeployAssistant.config` | Indented JSON (`LocalConfigData`) | `%USERPROFILE%/Documents/` | `SettingManager` |
| `DeployAssistant.ignore` | Indented JSON (`ProjectIgnoreData`) | `<ProjectPath>/` | `SettingManager` |
| `DeployAssistant.deploy` | Indented JSON (`DeployData`) | `<SourcePath>/` | `FileManager`, `SettingManager` |

**`ProjectMetaData.bin` is written atomically.** `FileHandlerTool.TrySerializeProjectMetaData` serialises to `ProjectMetaData.bin.tmp` and swaps it in with `File.Replace`, so a crash or a full disk can no longer leave a half-written store. The temp file sits on the same volume as the destination (a `File.Replace` requirement) and is removed in a `finally`.

**The `.bak` is opt-in and deliberately located.** `PersistMetaData(takeBackup: true)` passes a backup path, and `File.Replace` moves the previous content there as part of the same atomic swap. It lives inside `Backup_<ProjectName>/`, **not** beside `ProjectMetaData.bin`: the integrity scan's ignore list matches the exact filename `ProjectMetaData.bin`, so a sibling `.bak` in the project root would be staged as a brand-new project file on the very next check. `BackupManager.DeleteVersion` and `RequestRenameVersion` rely on this copy to roll the store back when a mutation has to be undone.

`%USERPROFILE%\Documents\DeployAssistant.layout` is **orphaned**: it was written by builds that shipped the AvalonDock layout (see §18) and nothing reads it any more. `MainWindow` deletes it on startup if it finds one.

---

## 14. Known Issues and Incomplete Implementations

The following issues were observed in the current implementation and should be addressed during or after refactoring:

0. *(resolved 2026-08-25)* The CLI pre-checkout gate previously treated an integrity check
   that never reported back (10-minute timeout) as "clean" and allowed the checkout. A
   timeout now cancels the checkout with an explicit error (`CheckoutGateScreen`,
   `IntegrityWaitTimeout`).

1. **Bug — `TryCompareMD5CheckSum`** (`HashTool.cs:43`): `dstHashString` is built from `srcHashBytes`, not `dstHashBytes`. The comparison therefore always returns `true`. This method is not currently called in hot paths but must be fixed.

2. **Stub — `LogManager`**: Class body is empty. Logging is done ad-hoc via `LogTool` (static) and inline `StringBuilder`. Should be consolidated.

3. *(resolved 2026-08-25)* `VersionDiffWindow`'s Export Diff is now wired to the implemented `RequestExportDiffPackage`; the dead `ExportProjectChanges` / `RequestExportProjectVersionDiffFiles` stubs were deleted.

4. **Stub — `VersionIntegrationViewModel`**: Constructor receives data but contains no logic.

6. **Inconsistent namespace** (`DeployAssistant.*` vs `DeployManager.*`): `SettingManager.cs` uses namespace `DeployManager.DataComponent` and `LocalConfigData.cs` uses `DeployManager.Model`. All files should be unified under the `DeployAssistant.*` hierarchy.

8. **CS8618 suppression**: Many constructors suppress the nullable reference warning. Most could be resolved with proper nullable annotations or constructor chaining.

9. **Direct `MessageBox` calls in DataComponent**: Business logic managers call `MessageBox.Show` and `WPF.MessageBox.Show` directly, coupling them to the UI. These should be replaced with events or exceptions.

10. **`FilterChangedFileList` bug** (`ProjectIgnoreData.cs:61`): The method filters into a local `changedFileList` variable assigned from a new LINQ query, but the original list parameter is not mutated (no `ref` or in-place removal). The filter has no effect.

11. ~~**`SettingManager.UpdateIgnoreListEventHandler` callback naming inconsistency**~~ — **resolved.** The event was replaced by `IgnoreDataLoadedEventHandler(ProjectMetaData, ProjectIgnoreData)`, which `MetaDataManager` composes into a `ProjectContext` and re-fires as `ProjectContextLoadedEventHandler`. Both are strongly typed; the `object`-taking callback is gone.

12. **No error logging infrastructure**: Errors are surfaced exclusively via `MessageBox.Show`. The `ErrorLogWindow` view exists but is not wired to any error source.

13. **No ignore-list UI in either front end**: `ProjectIgnoreData` is fully modelled, persisted as `DeployAssistant.ignore`, and honoured by initialization, integration, deploy and integrity check (§6.6). A user cannot see or edit it from anywhere. The WPF shell has no ignore panel and no ignore menu item; the CLI TUI has no ignore screen. The only way to change the exclusion set is to hand-edit the JSON file next to `ProjectMetaData.bin` — and only `SettingManager` writes it, so a malformed hand edit is silently replaced with defaults. This is the largest gap between the data model and what either UI exposes.

14. **`RequestStagedFileListRefresh(string)` is unreachable from both UIs**: the method exists on `MetaDataManager` with an empty body, and nothing in the WPF ViewModels or the CLI screens calls it. Either wire it up (a "re-scan the source directory without clearing staged state" action would be genuinely useful) or delete it — an empty public method on the manager's request surface reads as a supported operation.

15. **Possible orphaned blobs after a partial delete**: `BackupManager.DeleteVersion` commits at the persist step, and any failure *after* that point is reported but not rolled back (by design — see §8.3.1). If blob deletion or backup-folder removal then fails, the store is correct but the disk keeps bytes nothing references any more. Two shapes of leftover: unregistered blob files whose `DeleteUnregisteredBlobs` call threw, and a `Backup_<Version>` folder that could not be removed. There is no orphan sweeper and no "reclaim unreferenced backups" command; the wasted space is invisible until someone browses the backup root. Note the leftovers are inert — a stale file that no `BackupFiles` entry points at is never read back — so this is a disk-usage issue, not a correctness one.

16. **`MetaDataLoadFailedEventHandler` has no subscriber outside the tests**: `RequestProjectRetrieval` now reports a structured `MetaDataLoadResult` on every failure exit (§8.1, §15), but neither `MetaDataViewModel` nor the CLI's `ManagerFactory` subscribes. The CLI still surfaces a generic `"Could not load project at {path}."` and the GUI still shows nothing. The diagnosis exists and is asserted by `MetaDataBackwardCompatibilityTests`; it just is not displayed yet. Wiring both front ends to it is a small, high-value change.

---

## 15. Metadata Backward Compatibility

The on-disk store is the one thing in this application that cannot be recreated. Three rules govern it.

### The V1 wire format is pinned by golden-fixture tests

`DeployAssistant.Tests/Utils/MetaDataBackwardCompatibilityTests.cs` loads `DeployAssistant.Tests/Fixtures/v1_3_6_1_ProjectMetaData.bin.txt` — a Base64-wrapped V1 JSON document built to the model shape at commit `7935ba4` (2024-04-04, namespace `SimpleBinaryVCS`), the shape the 3.6.1 build wrote. It is reconstructed from that source rather than captured from a real 3.6.1 file, and was verified against it: the serialized property names on `ProjectMetaData` / `ProjectData` / `ProjectFile` / `ChangedFile`, the `[JsonConstructor]` parameter lists, and the `DataState` / `ProjectDataType` **ordinals** are all identical to today's.

What the fixture pins, and therefore what a change must not break:

- Every top-level `ProjectMetaData` field, `ProjectMain`, the whole `ProjectDataList`, every `ProjectFile` field, `BackupFiles`, and `ChangedFiles` round-trip.
- Enum members survive as the *same members* — so reordering `DataState` or `ProjectDataType` is a breaking change even though it compiles.
- The redundant `ProjectFilesObs` array is ignored on load. `ProjectData.ProjectFilesObs` is a computed property that lacks `[JsonIgnore]` — in 3.6.1 and still today — so every real file contains it. Loading must skip it, not choke on it.
- The read is deliberately forgiving of drift: unknown extra properties still load, property-name casing drift still loads, and a number written as a string still loads (`LegacyReadOptions`).
- An **absent** `SchemaVersion` is normalised to `1`, because a file written by 3.6.1 has no such field at all. The normalised value is stamped onto the loaded object so anything re-serialised from it is explicit.
- It is *not* forgiving in the other direction: a document declaring a schema newer than `MaxSupportedMetaDataSchemaVersion` is refused rather than partially read, so an older build can never silently downgrade a newer project.

**Any change to a persisted shape must keep these tests green, or ship a migration.**

### Load failures are structured, not silent

`FileHandlerTool.TryLoadProjectMetaData` returns a `MetaDataLoadResult` carrying `Success`, a `MetaDataLoadFailure` reason, a user-facing `Message`, the `FilePath`, and the normalised `SchemaVersion`:

| Reason | Meaning |
|---|---|
| `None` | load succeeded |
| `FileNotFound` | no `ProjectMetaData.bin` at that path — the folder is simply not a managed project |
| `NotBase64` | the file exists but its text is not valid Base64 (truncated or overwritten) |
| `MalformedJson` | the Base64 decoded but the bytes are not well-formed JSON, or contain a value that cannot be mapped onto the model |
| `SchemaTooNew` | written by a newer build; refused rather than partially read |
| `MigrationFailed` | a migration step was required and failed or produced nothing (V2 store path only) |
| `EmptyDocument` | the file, or the decoded JSON, is empty — or the document is a bare `null` |
| `Unknown` | I/O errors, permission errors, anything unexpected |

`TryDeserializeProjectMetaData(path, out meta)` still exists and still returns `bool`; it simply forwards to `TryLoadProjectMetaData(...).Success`, so the contract of existing callers is unchanged. `MetaDataManager.RequestProjectRetrieval` raises `MetaDataLoadFailedEventHandler` with the result on every failure exit and returns to `Idle`. (No front end subscribes yet — §14 item 16.)

### The Migration V1→V2 framework is NOT on the runtime path

This is the one that is easy to get wrong. `DeployAssistant.Core/Migration/` and `FileHandlerTool.TryLoadProjectStore` can already turn a V1 document into a V2 `ProjectStore` (`CurrentStoreSchemaVersion = 2`, with a `.bak` taken before migrating). **Nothing on the runtime path calls them.**

- `FileHandlerTool.MaxSupportedMetaDataSchemaVersion` is **`1`**. V1 is deliberately still the runtime on-disk format.
- `MetaDataManager.RequestProjectRetrieval` loads V1 through `TryLoadProjectMetaData`, and everything downstream — `FileManager`, `BackupManager`, `UpdateManager`, the ViewModels, the CLI screens — is V1-shaped. The V1 types carry `[Obsolete]`, and the version-management models suppress `CS0618` with a comment saying exactly that.
- `MetaDataBackwardCompatibilityTests` pins this fact, so a change that quietly flips the runtime to V2 will fail the suite rather than migrate a user's store by surprise.

Flipping the runtime to V2 is its own change, and a large one. Until it happens, treat `Migration/` as infrastructure that is built and tested but dormant.

---

## 16. Architectural Patterns for Refactoring Reference

### Event Bus Pattern
`MetaDataManager` acts as a mediator. Sub-managers communicate only through events subscribed in `Awake()`. ViewModels subscribe to `MetaDataManager` events, never to sub-manager events directly.

### Command pattern (MVVM)
All UI actions are bound to `ICommand` properties using `RelayCommand`. `CanExecute` guards check both `_metaDataState == Idle` and data preconditions.

### Deferred initialization
Managers use `Awake()` for post-constructor setup. `MetaDataManager.Awake()` is the single call site; sub-managers do not implement a shared interface for this — each is wired individually in that method.

### Immutable snapshot copies
`ProjectData` and `ProjectFile` are copied (not shared by reference) when stored in `ProjectMetaData.ProjectDataList`. Deep-copy constructors exist on both classes and must be maintained.

### Path abstraction
`ProjectFile` stores paths as `DataSrcPath` (absolute root) + `DataRelPath` (relative). `DataAbsPath` is always computed. Refactoring must not break this invariant, which is essential for path reconfiguration across machines.

---

## 17. CLI Reference (`DeployAssistant.CLI`)

Binary name: `deployassistant`. Targets `net472` (framework-dependent). References `DeployAssistant.Core`. Ships as `deployassistant.exe` plus supporting DLLs in a single output folder; requires .NET Framework 4.7.2 pre-installed on the target machine (present by default on Windows 10 1803 and later). Carries the shared `app.ico` via `<ApplicationIcon>`.

```
DeployAssistant CLI
Interactive TUI for binary deployment version control.

Usage:
  deployassistant            Launch the interactive TUI
  deployassistant --help     Show this help text
  deployassistant --version  Show version

The TUI requires a real terminal (cmd.exe, Windows Terminal, etc).
Output redirection / piping is not supported by the TUI.
```

### 17.1 The CLI is a screen-stack TUI, not a verb CLI

**This section previously listed `init` / `load` / `scan` / `stage` / `deploy` / `revert` / `export` / `list` / `integrity-check` as commands. None of them exist.** `Program.Main` accepts exactly three things and errors on everything else:

| Argument | Behaviour |
|---|---|
| *(none)* | Launch the interactive TUI. Exit code 0 |
| `--help` / `-h` / `help` | Print the usage block above. Exit code 0 |
| `--version` / `-v` | Print `DeployAssistant CLI <semver>`. Exit code 0 |
| anything else | `deployassistant: unknown command '<arg>'.` on **stderr**. Exit code 1 |

The version string comes from `<AssemblyVersion>` in `DeployAssistant.CLI.csproj`, trimmed from the 4-part CLR version to 3-part semver by `CliVersion`. The CLI versions independently of the GUI (see `docs/release-process.md`), and the same banner is rendered at the bottom of `TopMenuScreen` — the no-project-loaded landing screen — so a fresh user sees the build without passing `--version`.

The CI smoke test asserts exactly this contract: no-args → exit 0 with `"DeployAssistant"` in the output; `--help` → exit 0; unknown command → exit 1.

#### Startup

`Program.BuildRootScreen` reads the last-opened path from `DeployAssistant.config`. If there is one, it loads the project and starts on `MainScreen`; otherwise it starts on `TopMenuScreen(loaded: false)`, where `esc` exits instead of popping.

#### The engine

`App.Run(root)` drives a `Stack<Screen>`:

1. If the top-of-stack changed, call `OnEnter()` — long-running synchronous work (manager calls, progress bars) belongs there, not in `Render`.
2. `AnsiConsole.Clear()`, then `Render()`.
3. Call `AutoAdvance()`; a non-null `ScreenAction` transitions without waiting for a key.
4. Otherwise block on `Console.ReadKey(intercept: true)` and pass it to `Handle(key)`.

`ScreenAction` is a record hierarchy: `Stay`, `Pop`, `Push(next)`, `Replace(next)`, `Exit`. `OnExit()` runs on pop, replace and exit — that is where screens unsubscribe from manager events. Note that `OnEnter()` re-runs every time a pushed child screen pops, so a subscription made there must be idempotent (`-=` before `+=`) — a duplicate handler on the process-lifetime manager leaks every popped screen instance. `Screen` is an abstract class rather than an interface because `AutoAdvance` needs a virtual default and default interface methods are not available on net472.

Two guards sit in front of the loop, both returning exit code 0 rather than crashing:

- **Redirected stdout or stdin** → print a one-line banner to stdout (so redirect-to-file users and the CI smoke test still get output), diagnostics to stderr, and exit. Interactive rendering into a pipe is meaningless.
- **No console window / `Console.WindowHeight` throws** → same treatment. The probe is done once up front so the failure is a clean message instead of an `IOException` from deep inside the render loop. A terminal shorter than 10 rows renders "Terminal too small — please resize." and waits.

`Ctrl+C` is intercepted in two places (a `CancelKeyPress` handler and an explicit check on every `ReadKey`) and clears the stack for a clean exit — which is why `ConsoleKey.C` handling in a screen can safely treat a bare `c` as its own binding.

#### Screen map

```
TopMenuScreen ---- PathPickerScreen (Switch | Init) ---- InitRunScreen
     |
MainScreen
  |-- IntegrityRunScreen ---- IntegrityResultScreen ---- UpdateVersionPromptScreen
  +-- RevisionListScreen ---- RevisionDetailScreen --+-- CheckoutGateScreen
                                                     |-- VersionRenameScreen
                                                     +-- VersionDeleteGateScreen
```

#### Key bindings

| Screen | Keys |
|---|---|
| `TopMenuScreen` | `↑↓` move · `enter` select (Switch project / Initialize new project / Quit) · `esc` back-or-exit |
| `MainScreen` | `↑↓` move · `enter` select (Integrity check / List revisions) · `m` top menu · `q` quit |
| `PathPickerScreen` | `tab` complete · `↑↓` pick · `enter` open · `esc` cancel |
| `IntegrityResultScreen` | `↑↓` move · `d`/`u` half-page · `r` revert selected file · `u` update · `esc` back |
| `RevisionListScreen` | `↑↓` move · `d`/`u` half-page · `enter` inspect · `esc` back |
| **`RevisionDetailScreen`** | `↑↓` move · `d`/`u` half-page · **`c` checkout** · **`r` rename** · **`x` delete** · `esc` back |
| `CheckoutGateScreen` (clean) | `y` checkout · `n`/`esc` cancel |
| `CheckoutGateScreen` (dirty, Fast) | `↑↓` move · `u` half-page up · `d` discard & checkout · `esc` cancel |
| `CheckoutGateScreen` (dirty, CleanRestore) | `↑↓` move · `d`/`u` half-page · `y` clean restore · `esc` cancel |
| `VersionRenameScreen` | `tab` next field · `enter` submit · `esc` cancel |
| `VersionDeleteGateScreen` | `y` delete · `n`/`esc` cancel (offered only when the plan has no blockers) |

**`c` and `C` both mean Fast checkout now.** The uppercase `C` = `CleanRestore` binding was removed with the Safe toggle (§12.3); the dirty-CleanRestore gate row above survives only for the Core mode, which no key path reaches any more. `r` and `x` remain case-insensitive.

**`d` is overloaded by context, also deliberately.** On list screens it is half-page-down. On the dirty-Fast checkout gate it is "discard local changes and check out" — the destructive confirmation — and half-page-down is dropped from the footer there, leaving only `u`.

#### Screens that touch version management

**`RevisionListScreen`** holds a *snapshot* of `ProjectDataList`, so a delete performed further down the stack would leave a row pointing at a version that no longer exists. `OnEnter` runs again every time the screen returns to top-of-stack, and re-takes the snapshot; `SelectableList.SetItemCount` clamps the selection if the list shrank. `AutoAdvance` **consumes** `LastCheckedOut` (and pops back to `MainScreen`) and `LastDeletedVersion` (and rebuilds).

**`RevisionDetailScreen`** computes its diff against main by subscribing to `ProjComparisonCompleteEventHandler` *before* firing `RequestProjVersionDiff` — the fire is synchronous, so subscribing after would miss it — and unsubscribes in `OnExit`; the subscription is `-=`-then-`+=` because `OnEnter` re-runs on every child pop (see the engine note above). Its `AutoAdvance` **peeks and never consumes** the manager's read-once flags, because `RevisionListScreen` is the real consumer and needs them to survive this hop. Each peek is paired with a local `_checkoutRequested` / `_deleteRequested` bool so a stale flag from an earlier operation can never auto-pop this screen.

**`CheckoutGateScreen`** runs the pre-checkout integrity check inside an `AnsiConsole.Progress()` block driven by `IntegrityProgressEventHandler`, waiting on a `ManualResetEventSlim` with a 10-minute cap, then moves to `Clean`, `Dirty` or `Error`. It attaches `CheckoutCompleteEventHandler` around the request so a failure shows the manager's real reason instead of "see logs".

**`VersionDeleteGateScreen`** builds the plan through `RequestVersionDeletePreview` (which mutates nothing), renders the blast radius, and refuses outright when the plan carries blockers — in the `Blocked` phase the delete key is simply not offered.

**`VersionRenameScreen`** pre-fills both fields with the current values; a field left untouched is sent as `null`, which the manager reads as "leave alone", so a log-only edit never trips version-name validation.

### 17.2 Output Style

`Spectre.Console` v0.49.1 throughout. `TextStyle` centralises the vocabulary — selection marker, main marker, accent/dim/bold helpers, and `FormatFileState` for the per-file change rows.

| Concept | Symbol | Colour | Member |
|---|---|---|---|
| Success / completion | `✓` | Green | `TextStyle.SuccessGlyph` |
| Error | `✗` | Red | `TextStyle.ErrorGlyph` |
| Selected-row pointer | `›` | Cyan bold | `TextStyle.SelectionMarker` |
| Current version marker | `→` | Cyan bold | `TextStyle.MainMarker` |
| Added file | `+` prefix | Green | `TextStyle.FormatFileState` |
| Deleted file | `-` prefix | Red | `TextStyle.FormatFileState` |
| Modified file | `~` prefix | Yellow | `TextStyle.FormatFileState` |
| Restored file | `*` prefix | Magenta | `TextStyle.FormatFileState` |

`RevisionDetailScreen` additionally renders a literal `★ current` badge in its header. There are no warning or info glyphs — warnings are plain `[yellow]` / `[red]` markup at the call site.

**Markup escaping is mandatory.** Literal `[` and `]` in any user-facing string must be doubled (`[[options]]`), and every value that comes from data — version names, paths, updater names, change logs, file paths — must go through `Markup.Escape`. Failing to do so crashes at render time (PR #19).

---

## 18. UI Layout

The WPF shell is a fixed three-column workspace under a header bar. This section describes what `MainWindow.xaml` **is**, in present tense.

> **History — read this before reviving a docking plan.** Earlier revisions of this document specified a Visual Studio-style dockable-islands layout built on `Dirkster.AvalonDock`, written in future tense as a plan. AvalonDock was subsequently implemented, and has now been **removed again**; the user explicitly authorised dropping the goal. The package reference is gone, no project references AvalonDock or `Extended.Wpf.Toolkit`, and `%USERPROFILE%\Documents\DeployAssistant.layout` is an orphaned file that `MainWindow` deletes on startup (best-effort; a failure is logged and never blocks startup). The panel decomposition the plan produced was kept — it is good — but it is expressed in plain `Grid` + `GridSplitter` + `TabControl`.

### 18.1 Structure

`MainWindow` is a three-row `Grid`: a menu strip (`Auto`) over a header bar (`Auto`) over the workspace (`*`). The window is 1600×900 with `MinWidth="1100"` / `MinHeight="700"`, and its title is stamped with the running assembly version (`Deploy Assistant  v<major.minor.build>`) so the build in use is visible without an About box. `Icon="/app.ico"` resolves from the assembly, because `app.ico` is included as a WPF `Resource`.

The workspace is a five-column `Grid`: three content columns separated by two 5px `GridSplitter` columns.

| Column | Width | Content |
|---|---|---|
| 0 | `420`, `MinWidth="300"` | Version History (0.45\*) over Version Log (0.55\*), split by a row `GridSplitter` |
| 1 | `5` | `GridSplitter`, `ResizeDirection="Columns"` |
| 2 | `*`, `MinWidth="400"` | `TabControl` — **Project Files** and **Metafile Compare** tabs |
| 3 | `5` | `GridSplitter`, `ResizeDirection="Columns"` |
| 4 | `330`, `MinWidth="240"` | Actions (0.4\*) over Staged Changes (0.6\*), split by a row `GridSplitter` |

Every panel is a `DockPanel` with a `Border` header (`PanelHeaderStyle` + `HeaderLabelStyle`) docked to the top, so panels are visually consistent without a docking library.

Font sizes come from a four-level type scale in `SharedStyles.xaml` (`sys:Double` resources): `FsTitle` 16 / `FsHeader` 13 / `FsBody` 12 / `FsCaption` 11 — deliberately non-consecutive so each level reads as a different rank. XAML references them via `{StaticResource ...}` instead of literal `FontSize` values; new views must do the same.

The menu strip has two menus, populated on `SubmenuOpened` (no ViewModel state). **Project**: Open Destination Project… / Set Source Folder… (the former toolbar Set Dir buttons, relocated) · **Recent Projects** (the `RecentProjects` MRU from `DeployAssistant.config`; clicking one calls `MetaDataViewModel.OpenProjectPath`, which is `RetrieveProject` minus the folder picker and refuses when not `Idle`) · Exit. **Settings**: **Fast-check hash sample** (100/50/25/10%, checkmark on the saved value) — the `FastIntegritySamplePercent` described in §12.4.

The header bar is light (`#F0F0F0`, matching the 3.6.1-era neutral look): Integrity Check / Checkout buttons (Set Source/Dest moved into the Project menu; the **Safe** checkbox is gone — §12.3), then project/version info, and on the right a language dropdown (한국어/English) beside the state badge. The **Version Log** panel (renamed from "Diff Log") shows a summary card for the selected version — version name, date, author, commit message — above the single-row diff grid with Build V / Prev V columns; `VersionDisplay` prefers `ProductVersion` (the commit-id-bearing string) over `FileVersion` everywhere a build version is shown.

### 18.2 Integrity results dashboard (`IntegrityLogWindow`)

The window serves two modes from one layout. **Integrity-result mode**: a verdict banner first (green "모든 파일이 스냅샷과 일치합니다" or amber "{n}개 항목이 현재 버전과 다릅니다" with version · scanned-count subtitle), clickable tally chips (변경/추가/삭제 filter the grid; 해시 실패/메타데이터 검증 are informational counts from the `IntegrityFileOutcome` stream), then the results grid showing flagged files only, **grouped by date** — Added rows group on `CreationTime`, Modified rows on `LastWriteTime`, so files that arrived or were built together cluster under one header — with an 출처 badge per row (유입 when `CreationTime > LastWriteTime`, i.e. copied in; 수정 for in-place edits), Prev→Current version columns (`VersionDisplay`, commit-id first), and per-row / all Revert actions. The raw text log survives behind an expander. **Full-log mode** (opened from Version History) reuses the same date-grouped grid over a whole version's file list with a neutral banner. Grouping/filtering are view concerns (`ICollectionView`) in the window's code-behind.

### 18.3 Busy overlay

Whenever `MetaDataState != Idle`, a full-window scrim (`#66000000`, hit-test-visible so it physically absorbs clicks) covers both grid rows with a centered card: localized state title, progress bar (determinate with a `{done:N0} / {total:N0} files` counter during `IntegrityChecking`, indeterminate otherwise), and — for integrity checks — a 200-row auto-scrolling log of per-file verdicts colored by `IntegrityFileOutcome`. Commands were already `CanExecute`-gated on `Idle`; the overlay makes that lock visible. There is no cancel button: Core's integrity check has no `CancellationToken`.

### 18.4 Localization (ko-KR default / en-US)

All user-visible GUI strings live in `View/Strings.ko-KR.xaml` and `View/Strings.en-US.xaml` (`sys:String` dictionaries, keys `S.*`); **every key must exist in both files** — a missing `StaticResource` key crashes startup. `App.ApplyLanguage(code)` swaps the merged dictionary; the choice persists in `DeployAssistant.config` (`Language`, default `ko-KR`). `DynamicResource` labels switch live from the toolbar dropdown; `DataGridColumn` / `GridViewColumn` headers have no inheritance context, use `StaticResource`, and therefore update on the next start. Code-composed strings go through `Loc.T(key, englishFallback)` (`ViewModelBase.cs`) — the fallback keeps headless tests deterministic. Korean terminology follows the Pro Git Korean translation's hybrid style (`Checkout` / `Stage` / `Staging Area` kept in English; 커밋 메시지 · 버전 히스토리 · 배포 · 무결성 검사 · 되돌리기 · 복원 in Korean). Out of scope by design: Core-originated dialog/log text and the CLI (both English).

### 18.5 Panel Inventory

The decomposition below survived the AvalonDock removal; only the docking types went away.

| Panel | Location | Content |
|---|---|---|
| Version History | Col 0, top | `ListView`/`GridView` of `BackupVM.BackupProjectDataList` — Rev, Version, Date, By, Δ. The `IsProjectMain` row is tinted `#C8E6C9`. Header carries a `⟳` refresh button bound to `BackupVM.FetchBackup`. Right-click context menu carries the full version-management surface (see below) |
| Diff Log | Col 0, bottom | `DataGrid` of the selected version's changed files, plus Updater / Log fields and a per-row **Restore** button |
| Project Files | Col 2, tab 1 | `DataGrid` of the tracked file set with a keyword filter, plus a **폴더별 보기** (`S.GroupByFolder`) chip that swaps the grid for a `TreeView` folder hierarchy built by `ProjectFileTreeNode.Build` (Core): folder nodes show `(file count · total size)`, leaves show name + `VersionDisplay` + human-readable size + trimmed hash — no rel-path / deployed-version / time columns (names `FsHeader`, metadata `FsBody`, one step above the app norm for scanability). Selecting a file highlights every file with the same hash (`#FFF3C4`) and auto-expands the ancestor folders of each match without collapsing anything. The tree is rebuilt from the grid's view, so the keyword filter carries over, and refreshes on project (re)load |
| Metafile Compare | Col 2, tab 2 | Import / export sync-package flow against a `.VersionLog` metafile |
| Staged Changes | Col 4, top | Staged + pre-staged change list |
| Actions | Col 4, bottom | Updater / Update Log inputs and the Stage / Update / Clear / Refresh buttons |

**Version History context menu:** Export Full Version · Export Version Log · Compare With Main · Compare Src With Main · Checkout Version · Clean Restore · Full Log · Src Full Log · Src Version Similarities · ─ · **Rename / Re-tag...** · **Delete Version...** (tinted `#C62828`). The menu re-binds `DataContext` to `PlacementTarget.Tag` so the commands resolve against the window's `MainViewModel` rather than the selected row.

**`IntegrityLogWindow` and `VersionDiffWindow` stay owned windows.** The docking plan promised to convert them into centre tabs; that promise is void. Both are opened from `MainWindow.xaml.cs` as owned, `CenterOwner`, non-modal `Show()` windows, alongside `OverlapFileWindow`, `VersionComparisonWindow`, `VersionIntegrationView` and the source-project info window. See §11 for the full window policy, including the three modal gate windows that are the deliberate exception.

### 18.3 Header Bar

A three-column `Border` (`#2D2D2D`, `Padding="6,4"`):

- **Left — primary actions:** *Set Source Dir* (`FileTrackVM.GetDeploySrcDir`), *Set Dest Dir* (`MetaDataVM.GetProject`), *Integrity Check* (`FileTrackVM.CheckProjectIntegrity`), all on `ToolbarButtonStyle` with uniform `Padding="10,5"` / `FontSize="13"`.
- **Centre — project info:** `Project: <name>` and `Version: <current>`, the values in bold (version in `#4EC9B0`).
- **Right — state badge:** a rounded `Border` bound to `MetaDataVM.CurrentMetaDataState` via `StateBadgeStyle`. Idle = green `#388E3C`; Processing / Initializing / Updating / Retrieving = blue `#1976D2`; Reverting = orange `#F57C00`; Exporting = purple `#7B1FA2`; IntegrityChecking = teal `#0097A7`; anything else falls through to grey `#757575`.

Note that `Deleting` has no trigger of its own and therefore renders grey. Adding one is a one-line change to `StateBadgeStyle` in `MainWindow.xaml`.

### 18.4 Visual Style

Styling lives in `SharedStyles.xaml` (`PanelHeaderStyle`, `HeaderLabelStyle`, `ToolbarButtonStyle`, `ActionButtonStyle`) plus the `MainWindow`-local `StateBadgeStyle`, which is kept local because it binds to a property only this window exposes. There is **no** third-party theme — the AvalonDock `VS2013` theme reference is gone with the package.

- Panel title bars: slim, uniform, via `PanelHeaderStyle`.
- `DataGrid` rows keep the colour-coded `DataState` triggers (Added = light blue, Modified = light green, Deleted = light coral, Restored = gold).
- Buttons use consistent padding and font size rather than a mix of `Height="40"` and `Height="Auto"`.
- **`GridSplitter` is the resize mechanism.** The docking plan said splitters would be replaced by AvalonDock's built-in resizing handles; the reverse happened. There are four: two column splitters in the workspace and one row splitter inside each of the outer columns. Any layout change must keep them, and keep the `MinWidth` guards that stop a column from being dragged to zero.

There is no layout persistence and no layout file. Column and row sizes reset to the XAML defaults on every launch — an accepted trade for deleting the serialization path.

---
