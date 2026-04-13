# DeployAssistant — Feature Specification

> **Purpose of this document:** Derived from the current implementation as a ground-truth reference for future refactoring. Agents or developers performing a refactor should use this document to verify that all described behaviors are preserved and the architecture conforms to the patterns described here.

---

## 1. Overview

**DeployAssistant** (internal assembly name `SimpleBinaryVCS`) is a Windows desktop application that provides lightweight, binary-level deployment version control for a directory of files. It is aimed at teams that ship compiled binary applications and need to track what changed, to and from which version, without a full source-control system on the deployment target.

Core responsibilities:
- Track which files (and directories) exist at a deployment path, keyed by MD5 hash.
- Record snapshots of the deployment directory as named "versions" in a `ProjectMetaData.bin` file.
- Detect differences between the current directory state and the recorded snapshot.
- Stage, commit ("update"), revert to, and export any recorded version.
- Support merging/integrating an external build tree into the tracked deployment directory.

---

## 2. Technology Stack

| Item | Detail |
|---|---|
| Language | C# 12 / .NET 8.0 (Windows) |
| UI Framework | WPF (`UseWPF=true`) |
| Auxiliary UI | Windows Forms (`UseWindowsForms=true`) — used for `FolderBrowserDialog`, `MessageBox`, `DialogResult` |
| Architecture pattern | MVVM (Model / ViewModel / View) with event-driven data flow |
| Serialization | `System.Text.Json` for all persistence |
| File hashing | MD5 via `System.Security.Cryptography.MD5` |
| Export — spreadsheet | `DocumentFormat.OpenXml` v3.0.2 (xlsx generation) |
| Export — archive | `System.IO.Compression.ZipFile` |
| Extended UI controls | `Extended.Wpf.Toolkit` v4.6.0 |
| Concurrency | `Task`, `Parallel.ForEach`, `SemaphoreSlim` (max 12 concurrent hash tasks) |

---

## 3. Project Layout

```
DeployAssistant.sln
└── SimpleBinaryVCS/                   ← single project, all source
    ├── App.xaml / App.xaml.cs         ← application entry-point; global singletons
    ├── AssemblyInfo.cs
    ├── Interfaces/
    │   ├── IManager.cs
    │   └── IProjectData.cs
    ├── Model/                         ← pure data classes (serializable)
    │   ├── ProjectMetaData.cs
    │   ├── ProjectData.cs
    │   ├── ProjectFile.cs
    │   ├── ChangedFile.cs
    │   ├── RecordedFile.cs
    │   ├── ProjectIgnoreData.cs
    │   ├── ProjectSimilarity.cs
    │   ├── DeployData.cs
    │   └── LocalConfigData.cs
    ├── DataComponent/                 ← service/manager layer (business logic)
    │   ├── MetaDataManager.cs         ← central orchestrator
    │   ├── FileManager.cs
    │   ├── BackupManager.cs
    │   ├── UpdateManager.cs
    │   ├── ExportManager.cs
    │   ├── SettingManager.cs
    │   └── LogManager.cs              ← empty stub
    ├── ViewModel/
    │   ├── ViewModelBase.cs
    │   ├── MainViewModel.cs
    │   ├── MetaDataViewModel.cs
    │   ├── FileTrackViewModel.cs
    │   ├── BackupViewModel.cs
    │   ├── VersionDiffViewModel.cs
    │   ├── VersionIntegrationViewModel.cs
    │   ├── VersionCheckViewModel.cs
    │   ├── VersionCompatibilityViewModel.cs
    │   └── OverlapFileViewModel.cs
    ├── View/
    │   ├── MainWindow.xaml / .cs
    │   ├── IntegrityLogWindow.xaml / .cs
    │   ├── ErrorLogWindow.xaml / .cs
    │   ├── VersionDiffWindow.xaml / .cs
    │   ├── VersionIntegrationView.xaml / .cs
    │   ├── CompatibleVersionWindow.xaml
    │   ├── OverlapFileWindow.xaml / .cs
    │   └── VersionComparisonWindow.cs
    └── Utils/
        ├── FileHandlerTool.cs
        ├── HashTool.cs
        ├── LogTool.cs
        └── RelayCommand.cs
```

### Namespace irregularities (known technical debt)
The codebase uses **three different namespaces** across files in the same project:
- `SimpleBinaryVCS` — most files (original namespace)
- `DeployAssistant` / `DeployAssistant.Model` / `DeployAssistant.ViewModel` — newer files
- `DeployManager` / `DeployManager.DataComponent` / `DeployManager.Model` — some model/manager files

All live in a single compiled assembly. A refactor should consolidate to a single namespace.

---

## 4. Global Application Objects (`App.xaml.cs`)

Three lazy singletons are exposed as static properties on `App`:

| Property | Type | Purpose |
|---|---|---|
| `App.MetaDataManager` | `MetaDataManager` | Central service hub; created once and shared by all ViewModels |
| `App.FileHandlerTool` | `FileHandlerTool` | Low-level file I/O utility shared by all managers |
| `App.HashTool` | `HashTool` | MD5/SHA-256 hashing utility shared by all managers |

`App.AwakeModel()` is called from `MainViewModel` constructor and triggers `MetaDataManager.Awake()`, which constructs all sub-managers and wires up all event subscriptions.

---

## 5. Interfaces

### `IManager`
```csharp
void Awake();
event Action<MetaDataState> ManagerStateEventHandler;
```
All five DataComponent managers implement this interface. `Awake()` is called after construction for deferred initialization. All managers must fire `ManagerStateEventHandler` to report their state to the UI.

### `IProjectData`
Implemented by `ProjectFile` and `RecordedFile`. Defines the common contract for a tracked data entry:
- `ProjectDataType DataType` — `File` or `Directory`
- `DataState DataState`
- `string DataName`, `DataRelPath`, `DataSrcPath`, `DataAbsPath` (computed), `DataHash`
- `DateTime UpdatedTime`

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
| `DeployedProjectVersion` | `string` | ✓ | Version string of the `ProjectData` it was recorded in |
| `UpdatedTime` | `DateTime` | ✓ | Timestamp of last record action |
| `DataState` | `DataState` | ✓ | Bitflag describing current lifecycle state |
| `IsDstFile` | `bool` | ✓ | True when the file is the destination (deployed) side of a `ChangedFile` |

### 6.2 `DataState` (flags enum, defined in `FileManager.cs`)
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
```
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
| `en-US` | Directory | Integration |
| `ko-KR` | Directory | Integration |
| `Resources` | Directory | Integration |
| `Backup_{ProjectName}` | Directory | IntegrityCheck |
| `Export_{ProjectName}` | Directory | IntegrityCheck |

**`IgnoreType`** (flags enum):
```
None=0, Integration=1, IntegrityCheck=2, Deploy=4, Initialization=8, All=~0
```

### 6.7 `DeployData`
Saved as `DeployAssistant.deploy` (JSON) in the source folder when file allocation has been manually resolved. Stores `ProjectName` and `Dictionary<string, ProjectFile> SortedTopFiles` (key = `DataRelPath`) so that the same allocation can be reused on the next deployment from the same folder.

### 6.8 `LocalConfigData`
Saved as `DeployAssistant.config` (JSON) in `%USERPROFILE%\Documents`. Contains only `LastOpenedDstPath`. Loaded on startup to auto-reopen the last project.

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
```

ViewModels subscribe to `ManagerStateEventHandler` and cache the last state. Commands that mutate data (`CanExecute` checks) only allow execution when state is `Idle`.

---

## 8. DataComponent Layer (Service Managers)

### 8.1 `MetaDataManager` — Central Orchestrator

**Owns:** `ProjectMetaData`, `MainProjectData`, `_srcProjectData`.

**Sub-managers wired in `Awake()`:** `FileManager`, `BackupManager`, `UpdateManager`, `ExportManager`, `SettingManager`.

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
| `FetchRequestEventHandler` | `object` | Backup list fetched |
| `ProjExportEventHandler` | `string` (export path) | Export complete |
| `UpdateIgnoreListEventHandler` | `ProjectIgnoreData` | Ignore list loaded/updated |
| `ManagerStateEventHandler` | `MetaDataState` | State changed |

#### Public Request Methods
| Method | Action |
|---|---|
| `RequestProjectRetrieval(string path)` | Deserialize `ProjectMetaData.bin` from path; reconfigure paths if moved |
| `RequestProjectInitialization(string path)` | Bootstrap a new `ProjectMetaData` for a fresh project directory (parallel file scan + MD5 hashing) |
| `RequestSrcDataRetrieval(string path)` | Scan a source folder and load its `ProjectData` (if `.VersionLog` exists) plus pre-staged files |
| `RequestStageChanges()` | Hash pre-staged files and compute diff against main project |
| `RequestClearStagedFiles()` | Clear staged changes (preserves `IntegrityChecked` entries) |
| `RequestProjectUpdate(updaterName, updateLog, path)` | Commit staged changes as a new version (with optional integration merge path) |
| `RequestRevertProject(ProjectData target)` | Compute and apply file diff to roll back to a previous version |
| `RequestProjectCleanRestore(ProjectData target)` | Full on-disk integrity check then restore to target version |
| `RequestProjectIntegrityCheck()` | Async hash comparison of all tracked files vs. disk |
| `RequestFetchBackup()` | Populate backup version list |
| `RequestFileRestore(file, state)` | Queue a single file for restore |
| `RequestRevertChange(file)` | Revert a single `IntegrityChecked`-flagged file |
| `RequestOverlappedFileAllocation(overlaps, newFiles)` | Accept resolved overlap decisions from the UI |
| `RequestProjVersionDiff(srcData)` | Compute diff between a given version and main |
| `RequestProjectCompatibility(srcData)` | Compute similarity of a source project against all history entries |
| `RequestExportProjectBackup(ProjectData)` | Export a version's files as a zip archive |
| `RequestExportProjectVersionLog(ProjectData)` | Export a `.VersionLog` file for a version |
| `RequestExportProjectFilesXLSX(files, projData)` | Export file list as `.xlsx` |

---

### 8.2 `FileManager`

**Owns:** pre-staged dict, registered-changes dict, references to main project dicts, ignore data.

**Concurrency:** `SemaphoreSlim(12)` limits concurrent MD5 operations.

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
1. Gather all files/dirs on disk, excluding the ignore list.
2. Compare against recorded `ProjectFiles`:
   - Added / deleted files & directories → generate `IntegrityChecked` change entries.
   - Intersecting files → parallel MD5 compare; mismatches → `Modified | IntegrityChecked`.
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
  - If the backup directory ends up empty, it is deleted.

**`RevertProject(ProjectData target, List<ChangedFile> diffs)`**
- Calls `FileHandlerTool.TryApplyFileChanges(diffs)` with retry loop.
- On success, fires `ProjectRevertEventHandler` → routes to `MetaDataManager.ProjectChangeCallBack` → sets `MainProjectData`.

**Backup directory layout:**
```
<ProjectPath>/
└── Backup_<ProjectName>/
    └── Backup_<VersionName>/
        └── <files keyed by their state>
```

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

**`ExportProjectChanges`** — **stub, not implemented.**

---

### 8.6 `SettingManager`

**Startup (`Awake()`):**
- Reads `%USERPROFILE%\Documents\DeployAssistant.config`.
- If found, prompts user to re-open last project path.

**`SetRecentDstDirectory(string path)`** — Updates and writes `DeployAssistant.config`.

**On `MetaDataLoadedCallBack`:**
- Reads or creates `DeployAssistant.ignore` at the project root.
- Fires `UpdateIgnoreListEventHandler` to propagate the ignore list to `FileManager`.

**`RegisterSrcDeploy(path, registeredFiles)`** — Writes a `DeployAssistant.deploy` file at a source path.

---

## 9. Utility Layer

### 9.1 `FileHandlerTool`

Low-level I/O with typed serialization helpers.

| Method | Description |
|---|---|
| `TrySerializeProjectMetaData(data, path)` | JSON → Base64 → file |
| `TryDeserializeProjectMetaData(path, out data)` | file → Base64 → JSON |
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
Standard `ICommand` implementation. Constructor takes `Action<object> execute` and optional `Func<object, bool> canExecute`. `CanExecuteChanged` is raised on `CommandManager.RequerySuggested`.

---

## 10. ViewModel Layer

### 10.1 `ViewModelBase`
Implements `INotifyPropertyChanged`. Provides `OnPropertyChanged(string propertyName)`.

### 10.2 `MainViewModel`
Root ViewModel composed in `MainWindow`. Constructs and exposes:
- `MetaDataVM` (`MetaDataViewModel`)
- `FileTrackVM` (`FileTrackViewModel`)
- `BackupVM` (`BackupViewModel`)

Calls `App.AwakeModel()` in constructor.

### 10.3 `MetaDataViewModel`
**Bound to:** main panel metadata header.

**Observable state:** `CurrentProjectPath`, `ProjectName`, `CurrentVersion`, `ProjectFiles` (all files in main snapshot), `UpdaterName`, `UpdateLog`, `CurrentMetaDataState`.

**Commands:**
| Command | Action |
|---|---|
| `GetProject` | Open `FolderBrowserDialog`; call `RequestProjectRetrieval`. If no metadata found, prompt to initialize. |
| `ConductUpdate` | Validate `UpdaterName` and `UpdateLog`; call `RequestProjectUpdate`. |

### 10.4 `FileTrackViewModel`
**Bound to:** file staging panel.

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

**Observable state:** `BackupProjectDataList` (version history), `SelectedItem`, `UpdaterName`, `UpdateLog`, `DiffLog` (changed files of selected version).

**Commands:**
| Command | Enabled Condition | Action |
|---|---|---|
| `FetchBackup` | MetaData loaded | `RequestFetchBackup` |
| `CheckoutBackup` | Idle & item selected | Confirm → `RequestRevertProject` |
| `CleanRestoreBackup` | Idle | Confirm → `RequestProjectCleanRestore` (background) |
| `ExportVersion` | Idle | `RequestExportProjectBackup` (background) |
| `ExtractVersionLog` | always | `RequestExportProjectVersionLog` |
| `ViewFullLog` | Idle & item selected | Open `IntegrityLogWindow` with selected version |
| `CompareDeployedProjectWithMain` | Idle & item selected | `RequestProjVersionDiff`; open `VersionDiffWindow` |

**`ExportRequestCallBack`** — Opens Windows Explorer at the exported folder path.

### 10.6 `VersionDiffViewModel`
**Created by:** `FileTrackViewModel` or `BackupViewModel` callback. Passed `srcProject`, `dstProject`, `diff` at construction.

**Observable state:** `SrcProject`, `DstProject`, `Diff` (list of changed files).

**Commands:** `ExportDiffFiles` — calls `RequestExportProjectVersionDiffFiles` (stub, not implemented).

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

All secondary windows are opened as owned, center-owner, non-modal `Show()` windows.

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

### 12.3 Revert to a Version
1. User opens **Backup** panel → clicks **Fetch** → `BackupProjectDataList` populated.
2. User selects a version, clicks **Checkout** → confirm → `RequestRevertProject(target)`:
   - `FindVersionDifferences(target, main, isRevert=true)` computes the file operations needed.
   - `BackupManager.RevertProject` applies them (copies from `BackupFiles` dict) with retry loop.
3. **Clean Restore** variant: runs a full disk scan (`ProjectIntegrityCheck`) first, then applies, to ensure a pristine state.

### 12.4 Integrity Check
1. User clicks **Check Integrity** in FileTrack panel.
2. `FileManager.MainProjectIntegrityCheck()` runs async:
   - Reads all files/dirs from disk; excludes ignore list.
   - Set-arithmetic vs. recorded list → Added/Deleted entries.
   - Parallel MD5 of intersecting files → Modified entries.
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

---

## 13. Persistence Files Summary

| Filename | Format | Location | Managed by |
|---|---|---|---|
| `ProjectMetaData.bin` | Base64(JSON(`ProjectMetaData`)) | `<ProjectPath>/` | `BackupManager` (write), `MetaDataManager` (read) |
| `<Version>.VersionLog` | Base64(JSON(`ProjectData`)) | `<ProjectPath>/Export_<Name>/<Version>/` | `ExportManager` |
| `DeployAssistant.config` | Indented JSON (`LocalConfigData`) | `%USERPROFILE%/Documents/` | `SettingManager` |
| `DeployAssistant.ignore` | Indented JSON (`ProjectIgnoreData`) | `<ProjectPath>/` | `SettingManager` |
| `DeployAssistant.deploy` | Indented JSON (`DeployData`) | `<SourcePath>/` | `FileManager`, `SettingManager` |

---

## 14. Known Issues and Incomplete Implementations

The following issues were observed in the current implementation and should be addressed during or after refactoring:

1. **Bug — `TryCompareMD5CheckSum`** (`HashTool.cs:43`): `dstHashString` is built from `srcHashBytes`, not `dstHashBytes`. The comparison therefore always returns `true`. This method is not currently called in hot paths but must be fixed.

2. **Stub — `LogManager`**: Class body is empty. Logging is done ad-hoc via `LogTool` (static) and inline `StringBuilder`. Should be consolidated.

3. **Stub — `ExportProjectChanges`** (`ExportManager.cs`): Method declared but returns `false` immediately. Export of diff files from `VersionDiffWindow` is non-functional.

4. **Stub — `VersionIntegrationViewModel`**: Constructor receives data but contains no logic.

5. **Stub — `RequestExportProjectVersionDiffFiles`** (`MetaDataManager.cs`): Empty body.

6. **Inconsistent namespace** (`SimpleBinaryVCS` vs `DeployAssistant` vs `DeployManager`): All three are used across files in the same project. Should be unified to one namespace.

7. **Double `_updateManager.Awake()` call** (`MetaDataManager.Awake()`, lines 154–155): `_updateManager.Awake()` is called twice in succession; `_fileManager.Awake()` and `_exportManager.Awake()` are never called.

8. **CS8618 suppression**: Many constructors suppress the nullable reference warning. Most could be resolved with proper nullable annotations or constructor chaining.

9. **Direct `MessageBox` calls in DataComponent**: Business logic managers call `MessageBox.Show` and `WPF.MessageBox.Show` directly, coupling them to the UI. These should be replaced with events or exceptions.

10. **`FilterChangedFileList` bug** (`ProjectIgnoreData.cs:61`): The method filters into a local `changedFileList` variable assigned from a new LINQ query, but the original list parameter is not mutated (no `ref` or in-place removal). The filter has no effect.

11. **`SettingManager.UpdateIgnoreListEventHandler` callback naming inconsistency**: The event is declared as `Action<ProjectIgnoreData>` but the callback `SettingManager_UpdateIgnoreListCallBack` takes `object` and casts it, unlike the other similarly structured events that are typed correctly.

12. **No error logging infrastructure**: Errors are surfaced exclusively via `MessageBox.Show`. The `ErrorLogWindow` view exists but is not wired to any error source.

---

## 15. Architectural Patterns for Refactoring Reference

### Event Bus Pattern
`MetaDataManager` acts as a mediator. Sub-managers communicate only through events subscribed in `Awake()`. ViewModels subscribe to `MetaDataManager` events, never to sub-manager events directly.

### Command pattern (MVVM)
All UI actions are bound to `ICommand` properties using `RelayCommand`. `CanExecute` guards check both `_metaDataState == Idle` and data preconditions.

### Deferred initialization
All managers implement `Awake()` for post-constructor setup. This pattern should be preserved or replaced with dependency injection during refactoring.

### Immutable snapshot copies
`ProjectData` and `ProjectFile` are copied (not shared by reference) when stored in `ProjectMetaData.ProjectDataList`. Deep-copy constructors exist on both classes and must be maintained.

### Path abstraction
`ProjectFile` stores paths as `DataSrcPath` (absolute root) + `DataRelPath` (relative). `DataAbsPath` is always computed. Refactoring must not break this invariant, which is essential for path reconfiguration across machines.
