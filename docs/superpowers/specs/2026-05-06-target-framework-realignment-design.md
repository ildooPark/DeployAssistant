# Target-Framework Realignment — Design

**Date:** 2026-05-06
**Status:** Approved (brainstorming → ready for implementation plan)
**Owner:** ildooPark

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
│     • Services/WpfDialogService.cs
│     References: DeployAssistant.Core, Extended.Wpf.Toolkit 4.6.0
│
├── DeployAssistant.CLI               net472, OutputType=Exe
│     • Program.cs
│     • ConsoleDialogService.cs
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

## 5. The IDialogService Seam

The only new abstraction. Lives in `DeployAssistant.Core` (e.g. `Core/Services/IDialogService.cs`):

```csharp
public interface IDialogService
{
    DialogChoice Confirm(string title, string message, DialogChoice defaultChoice = DialogChoice.No);
    void Inform(string title, string message);
    string? PickFolder(string title, string? initialPath = null);
}

public enum DialogChoice { Yes, No, Cancel }
```

Implementations:

- **WpfDialogService** in `DeployAssistant/Services/`. Uses `System.Windows.MessageBox` and the WPF `OpenFolderDialog` (.NET 8 native — no WinForms `FolderBrowserDialog` needed).
- **ConsoleDialogService** in `DeployAssistant.CLI/`. `Confirm` reads stdin; auto-yes under `--yes`. `Inform` writes to stderr. `PickFolder` is unsupported (returns null) — CLI commands accept paths as arguments.
- **Test fake** in `DeployAssistant.Tests/Fakes/`. Programmable defaults; verifies ViewModels are headless-testable.

Audit of current direct-dialog call sites that need refactoring:
`DeployAssistant.ViewModel/MetaDataViewModel.cs`, `MetaFileDiffViewModel.cs`, `BackupViewModel.cs`, `FileTrackViewModel.cs`. (Core has none.)

## 6. CLI on net472 — Dependency Vetting

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

## 7. Migration Order

1. **Strengthen Core test coverage** for the platform-neutral managers and the V1↔V2 migration pipeline. Goal: any behavioral regression introduced by promoting Core.Standard surfaces as a test failure. (Tests today reference Core, not Core.Standard, so behavior verified is the rich-Core variant; we want assurance the Standard variants behave identically.)
2. **Promote Core.Standard to the new Core.**
   - Pull in the drifted methods from old Core: `MetaDataManager.LoadExternalMetaFile`, `ComputeMetaFileDiff`, `RequestExportDiffPackage`; the `UpdateIgnoreListEventHandler?.Invoke(newIgnoreData)` dispatch; `_settingManager.ConfirmationCallback = ConfirmationCallback;` wiring.
   - Move `PathCompat`/`CompatHelper` shims into the new Core.
   - Set `<LangVersion>latest</LangVersion>` (already set on Core.Standard).
3. **Introduce `IDialogService`** in Core; refactor the four ViewModel files to depend on it.
4. **Retarget GUI to `net8.0-windows`.** Fold `DeployAssistant.ViewModel/*` into `DeployAssistant/ViewModel/`. Add `WpfDialogService`. Replace any `FolderBrowserDialog` usage with WPF `OpenFolderDialog`. Remove `DeployAssistant.ViewModel.csproj` from the solution.
5. **Retarget CLI to `net472`.** Pin Spectre.Console version. Add `ConsoleDialogService`. Update `cli-smoke-test.yml` publish command (drop `--self-contained` / `PublishSingleFile`). Confirm green CI run.
6. **Retarget Tests to `net8.0-windows`.** Reference the new Core and the merged GUI exe.
7. **Delete** old Core, Core.Standard, ViewModel projects from the solution; remove their `.csproj` files; delete `sync-core-standard.yml`.
8. **Update `spec.md` §2 (Tech Stack), §3 (Project Layout)** and `CLAUDE.md` to match the new world.

Each step is intended to land as one PR, building cleanly and passing tests at every step.

## 8. Risks & Verification

| Risk | Mitigation |
|---|---|
| Spectre.Console newer versions drop netstandard2.0 | Pin in csproj; fall back to plain Console output if needed. CLI output is help text + status, not critical UX. |
| `setup-dotnet` on `windows-latest` runner does not surface net472 targeting pack | Verified on first CI run. Fallback: switch CLI build to `microsoft/setup-msbuild@v2` + `msbuild`. |
| V1↔V2 metadata regression during Core.Standard promotion | Step 1 strengthens coverage before any code moves. `DeployAssistant.Tests/Migration/*` runs against the unified Core. |
| ViewModel tests currently instantiate WPF types directly | Step 3 audit; `IDialogService` covers `MessageBox`/`FolderBrowserDialog`. No other coupling expected. |
| `dotnet test` on a machine without .NET 10 SDK | Already the local-machine state (only .NET 9 installed). Post-migration the project needs only .NET 8 SDK + .NET Framework 4.7.2 targeting pack — easier to set up. |

**Done definition:**
- `dotnet build DeployAssistant.sln` clean across all 4 projects.
- `dotnet test DeployAssistant.Tests/DeployAssistant.Tests.csproj` green.
- `cli-smoke-test.yml` green: no-args exit 0 with "DeployAssistant" in output, `--help` exit 0, unknown command exit 1.
- WPF GUI launches and round-trips a project end-to-end (manual; no UI test harness).
- `spec.md` and `CLAUDE.md` reflect the new architecture.

## 9. Out of Scope

- Namespace consolidation (`DeployManager.*` → `DeployAssistant.*`). Tracked in `spec.md §3` as separate tech debt.
- AOT / source-generated `System.Text.Json` serializers — not needed under C; revisit only if startup or memory becomes a concern.
- Replacing MD5 with a stronger hash — orthogonal.
- Spectre.Console upgrade or replacement.

## 10. References

- `spec.md` — feature specification (ground truth for behavior preservation).
- `CLAUDE.md` — operating notes for Claude Code in this repo.
- PRs: #12 (V2 + migration framework), #13/#16 (sync drift chores), #17 (missed event dispatch), #19 (Spectre escape), #20 (WinForms removal + metafile compare).
- `.github/workflows/cli-smoke-test.yml`, `release.yml`, `sync-core-standard.yml` (last to be deleted).
