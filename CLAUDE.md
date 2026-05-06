# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Authoritative reference

`spec.md` at the repo root (~50 KB) is the ground-truth feature specification, derived from the implementation. **Read it before redesigning the data model, manager wiring, or persistence formats.** Behavior changes that conflict with `spec.md` should either confirm the spec already matches or update the spec in the same PR.

## SDK requirement

Most projects target `net10.0-windows`; the CLI targets `net8.0`. CI uses `actions/setup-dotnet@v4` with `dotnet-version: '10.0.x'`. A machine with only .NET 9 SDK will fail every build with `NETSDK1045`. Install .NET 10 (and 8) from <https://aka.ms/dotnet/download> before attempting local builds. Default git branch is `master`, not `main`.

## Common commands

```bash
# build / test
dotnet restore DeployAssistant.sln
dotnet build DeployAssistant.sln -c Debug
dotnet test  DeployAssistant.Tests/DeployAssistant.Tests.csproj
dotnet test  DeployAssistant.Tests/DeployAssistant.Tests.csproj --filter "FullyQualifiedName~HashTool"

# run apps
dotnet run --project DeployAssistant/DeployAssistant.csproj         # WPF GUI (Windows only)
dotnet run --project DeployAssistant.CLI/DeployAssistant.CLI.csproj -- --help

# CLI publish (mirrors CI smoke test)
dotnet publish DeployAssistant.CLI/DeployAssistant.CLI.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -o ./publish/DeployAssistant.CLI
```

xUnit 2.9.2 is the test framework. There is no enforced formatter; `.editorconfig` only suppresses `CS8604`.

## Architecture in one paragraph

DeployAssistant (assembly name `SimpleBinaryVCS`) is a Windows desktop VCS for binary deployment directories. State of a managed folder is captured as `ProjectMetaData` → many `ProjectData` snapshots → many `ProjectFile` entries keyed by `DataRelPath` and hashed via MD5. The whole thing is serialized as JSON into `ProjectMetaData.bin`. Three sibling JSON files coexist: `DeployAssistant.ignore` (per-operation exclusion lists), `DeployAssistant.deploy` (cached file allocation in a source folder), and `DeployAssistant.config` (in `%USERPROFILE%\Documents`, just remembers last opened path). A V1→V2 schema-migration framework lives under `DeployAssistant.Core/Migration/` — use it when changing persisted shapes; do not break V1 metadata silently.

## Manager / event-driven flow (the part you must understand)

ViewModels never instantiate managers. There are three lazy singletons on `App` (`App.MetaDataManager`, `App.FileHandlerTool`, `App.HashTool`); `App.AwakeModel()` is invoked from the `MainViewModel` constructor and triggers `MetaDataManager.Awake()`, which constructs and wires all sub-managers (`FileManager`, `BackupManager`, `UpdateManager`, `ExportManager`, `SettingManager`). Every `DataComponent/*Manager` implements `IManager` (`Awake()` + `ManagerStateEventHandler`).

ViewModels call **only** `MetaDataManager.Request*` methods. Results return as events on `MetaDataManager`. The full event surface is documented in `spec.md` §8.1; common ones include `ProjLoadedEventHandler`, `FileChangesEventHandler`, `IntegrityCheckCompleteEventHandler`, `ProjComparisonCompleteEventHandler`, and `UpdateIgnoreListEventHandler`. **When adding new manager interactions, dispatch through these events rather than direct calls** — regression #17 was a missed `UpdateIgnoreListEventHandler` dispatch causing integrity checks to be skipped.

A `MetaDataState` enum (`Idle`, `Initializing`, `Retrieving`, `Processing`, `Updating`, `Reverting`, `CleanRestoring`, `Exporting`, `IntegrityChecking`, `IntegrationValidating`, `Integrating`) flows through `ManagerStateEventHandler`. ViewModel `RelayCommand.CanExecute` typically gates on `state == Idle`; preserve that guard when adding commands.

`FileManager` enforces a `SemaphoreSlim(12)` limit on concurrent MD5 operations — keep new hashing paths inside that limiter to avoid disk thrash.

## Two Core libraries (don't get this wrong)

- `DeployAssistant.Core` (net10.0-windows) — full business logic, may use WPF/WinForms-coupled APIs.
- `DeployAssistant.Core.Standard` (netstandard2.0) — cross-platform subset for the CLI. It links most files from `Core` directly via `<Compile Include="..\..\DeployAssistant.Core\..." Link="..." />`, and ships its own platform-neutral variants of `FileManager`, `MetaDataManager`, `FileHandlerTool`, `HashTool`, plus a `CompatHelper` shim.

When you add a file to `DeployAssistant.Core/`, decide whether it should be linked into `Core.Standard.csproj`. The `.github/workflows/sync-core-standard.yml` workflow auto-syncs additions on push (working branch `auto/sync-core-standard`), but verify the result rather than assuming. Anything that touches `MessageBox` / WPF / WinForms must **not** be linked into `Core.Standard`; provide a platform-neutral variant instead.

## CLI specifics

`DeployAssistant.CLI` (binary `deployassistant`) uses `Spectre.Console`. Literal `[` / `]` in user-facing strings must be escaped by doubling (`[[options]]`); failing to do so crashes startup (PR #19). The CI smoke test asserts: no-args → exit 0 with "DeployAssistant" in output; `--help` → exit 0; unknown command → exit 1.

## UI testing

There is no automated UI test harness. WPF changes require manually running the GUI on Windows. If you cannot exercise the UI in your session, say so explicitly rather than claiming success.

## Known tech debt to avoid amplifying

- **Mixed namespaces:** `DeployManager.DataComponent` (only `SettingManager.cs`) and `DeployManager.Model` (only `LocalConfigData.cs`) coexist with the dominant `DeployAssistant.*` prefix. Use `DeployAssistant.*` for new code; consolidating the stragglers is welcome.
- `LogManager` is an empty stub.
- Generated files (`*.Designer.cs`, `*.g.cs`, `*.g.i.cs`) are ignored by Serena indexing — don't hand-edit them.
- WinForms was largely removed in PR #20; don't reintroduce a WinForms reference without strong justification.

## When work is "done"

1. `dotnet build DeployAssistant.sln -c Debug` clean across all six projects.
2. `dotnet test DeployAssistant.Tests/DeployAssistant.Tests.csproj` green.
3. If CLI was touched, run the publish + smoke commands above (mirror `cli-smoke-test.yml`).
4. If a Core file was added/renamed, confirm `Core.Standard.csproj` is in sync (or that the linked file should not be in Standard).
5. If behavior covered by `spec.md` changed, update `spec.md` in the same change.
