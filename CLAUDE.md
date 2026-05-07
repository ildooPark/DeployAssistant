# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Authoritative reference

`spec.md` at the repo root (~50 KB) is the ground-truth feature specification, derived from the implementation. **Read it before redesigning the data model, manager wiring, or persistence formats.** Behavior changes that conflict with `spec.md` should either confirm the spec already matches or update the spec in the same PR.

## SDK requirement

The WPF GUI and test project target `net8.0-windows`; the CLI targets `net472`. Builds require the **.NET 8 SDK** plus the **.NET Framework 4.8 Developer Pack** (which ships the 4.7.2 reference assemblies the CLI needs). Install the SDK from <https://aka.ms/dotnet/download> and the Developer Pack from <https://aka.ms/msbuild/developertools>. A machine without the Developer Pack will fail CLI builds with a missing-target-framework error. Default git branch is `master`, not `main`.

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
# net472 — framework-dependent; ships as deployassistant.exe + DLLs in a single folder
dotnet publish DeployAssistant.CLI/DeployAssistant.CLI.csproj \
  -c Release -o ./publish/DeployAssistant.CLI
```

xUnit 2.9.2 is the test framework. There is no enforced formatter; `.editorconfig` only suppresses `CS8604`.

## Architecture in one paragraph

DeployAssistant (assembly name `SimpleBinaryVCS`) is a Windows desktop VCS for binary deployment directories. State of a managed folder is captured as `ProjectMetaData` → many `ProjectData` snapshots → many `ProjectFile` entries keyed by `DataRelPath` and hashed via MD5. The whole thing is serialized as JSON into `ProjectMetaData.bin`. Three sibling JSON files coexist: `DeployAssistant.ignore` (per-operation exclusion lists), `DeployAssistant.deploy` (cached file allocation in a source folder), and `DeployAssistant.config` (in `%USERPROFILE%\Documents`, just remembers last opened path). A V1→V2 schema-migration framework lives under `DeployAssistant.Core/Migration/` — use it when changing persisted shapes; do not break V1 metadata silently.

## Manager / event-driven flow (the part you must understand)

ViewModels never instantiate managers. `AppServices` (constructed once in `App.OnStartup`) is the composition root: it creates a `WpfDialogService`, a `WpfUiDispatcher`, and a `MetaDataManager(dialogService)`, then calls `MetaDataManager.Awake()`, which constructs and wires all sub-managers (`FileManager`, `BackupManager`, `UpdateManager`, `ExportManager`, `SettingManager`).

ViewModels call **only** `MetaDataManager.Request*` methods. Results return as events on `MetaDataManager`. The full event surface is documented in `spec.md` §8.1; common ones include `ProjLoadedEventHandler`, `FileChangesEventHandler`, `IntegrityCheckCompleteEventHandler`, `ProjComparisonCompleteEventHandler`, and `UpdateIgnoreListEventHandler`. **When adding new manager interactions, dispatch through these events rather than direct calls** — regression #17 was a missed `UpdateIgnoreListEventHandler` dispatch causing integrity checks to be skipped.

A `MetaDataState` enum (`Idle`, `Initializing`, `Retrieving`, `Processing`, `Updating`, `Reverting`, `CleanRestoring`, `Exporting`, `IntegrityChecking`, `IntegrationValidating`, `Integrating`) flows through `ManagerStateEventHandler`. ViewModel `RelayCommand.CanExecute` typically gates on `state == Idle`; preserve that guard when adding commands.

`FileManager` enforces a `SemaphoreSlim(12)` limit on concurrent MD5 operations — keep new hashing paths inside that limiter to avoid disk thrash.

## Single Core library

`DeployAssistant.Core` targets `netstandard2.0` and contains all business logic. It is referenced by the WPF GUI, the CLI, and the tests directly — there is no separate `Core.Standard` project. Platform-specific behavior (dialogs, shell operations, UI dispatch) is injected via `IDialogService` and `IUiDispatcher` so Core stays UI-agnostic.

When adding new code that touches Windows UI, put the implementation in the WPF or CLI project under the appropriate `Services/` subfolder, not in Core.

## CLI specifics

`DeployAssistant.CLI` (binary `deployassistant`) targets `net472` and uses `Spectre.Console`. It is published framework-dependent: the output folder contains `deployassistant.exe` plus supporting DLLs; the machine must have .NET Framework 4.7.2 installed (default on Windows 10 1803+). Pass `--yes` to auto-confirm all prompts for non-interactive / CI runs.

Literal `[` / `]` in user-facing strings must be escaped by doubling (`[[options]]`); failing to do so crashes startup (PR #19). The CI smoke test asserts: no-args → exit 0 with "DeployAssistant" in output; `--help` → exit 0; unknown command → exit 1.

## UI testing

There is no automated UI test harness. WPF changes require manually running the GUI on Windows. If you cannot exercise the UI in your session, say so explicitly rather than claiming success.

## Known tech debt to avoid amplifying

- **Mixed namespaces:** `DeployManager.DataComponent` (only `SettingManager.cs`) and `DeployManager.Model` (only `LocalConfigData.cs`) coexist with the dominant `DeployAssistant.*` prefix. Use `DeployAssistant.*` for new code; consolidating the stragglers is welcome.
- `LogManager` is an empty stub.
- Generated files (`*.Designer.cs`, `*.g.cs`, `*.g.i.cs`) are ignored by Serena indexing — don't hand-edit them.
- WinForms was largely removed in PR #20; don't reintroduce a WinForms reference without strong justification.

## When work is "done"

1. `dotnet build DeployAssistant.sln -c Debug` clean across all four projects + test project.
2. `dotnet test DeployAssistant.Tests/DeployAssistant.Tests.csproj` green.
3. If CLI was touched, run the publish + smoke commands above (mirror `cli-smoke-test.yml`).
4. If behavior covered by `spec.md` changed, update `spec.md` in the same change.
