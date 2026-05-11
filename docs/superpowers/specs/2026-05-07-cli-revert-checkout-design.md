# CLI Revert/Checkout UX — Design

**Date:** 2026-05-07
**Status:** Approved (brainstorming -> ready for implementation plan)
**Owner:** ildooPark

## 1. Context

The CLI TUI (PR f02c652, "interactive TUI redesign") currently exposes two read-only browsing surfaces that stop just short of being actionable:

- **`IntegrityResultScreen`** (`DeployAssistant.CLI/Screens/IntegrityResultScreen.cs`) lists every file that disagrees with `MainProjectData` after `RequestProjectIntegrityCheck()` fires `IntegrityCheckCompleteEventHandler`. The screen renders a scrollable summary (modified / deleted / added / restored) plus per-row glyphs from `TextStyle.FormatFileState`, but the only keystrokes it accepts are arrow/page navigation and `Esc`. A user who spots a bad file has no way to act on it without dropping back to the GUI.
- **`RevisionListScreen`** (`DeployAssistant.CLI/Screens/RevisionListScreen.cs`) enumerates `ProjectMetaData.ProjectDataList`, marks `MainProjectData` with `TextStyle.MainMarker`, and shows version/timestamp/updater/change-count per row. Again, `Esc` is the only exit. There is no detail view, no diff preview, and no way to roll back to a previous revision from the CLI.

Meanwhile both capabilities exist in `MetaDataManager`:

- `RequestRevertChange(ProjectFile)` (`DeployAssistant.Core/DataComponent/MetaDataManager.cs:347`) reverts one integrity-checked file by delegating to `FileManager.RevertChange`.
- `RequestRevertProject(ProjectData?)` (`MetaDataManager.cs:329`) rolls the whole project back to a snapshot via `BackupManager.RevertProject`.
- `RequestProjVersionDiff(ProjectData)` (`MetaDataManager.cs:483`) computes a diff against `MainProjectData` and re-fires `ProjComparisonCompleteEventHandler` with `(srcData, MainProjectData, List<ChangedFile>)`.

This spec wires those manager APIs into the TUI: a per-file revert on the integrity screen, and a new revision-detail sub-screen that owns project-level checkout. A small in-TUI confirmation helper is added so destructive actions cannot be triggered with a single keypress and so the prompt does not collide with the existing `ConsoleDialogService.Confirm` (which writes through `AnsiConsole` and would corrupt the render buffer of the currently-mounted screen).

## 2. Decision

Two capability options were on the table during the brainstorming session (2026-05-07).

- **A1 (chosen).** Per-file revert lives only on `IntegrityResultScreen`. The `r` keybind reverts the currently-selected row; bulk revert (`R`) and multi-select are explicitly deferred. Rationale: the integrity screen is the only context in the TUI today where a single dirty file's identity is unambiguous. Bulk revert adds a second confirmation pattern and an "are you sure for N files" cost-benefit story that we do not want to litigate in the first cut.
- **A2 / A3 rejected.** A2 (revert-all from the integrity screen footer) and A3 (revert from inside an exported diff view) overlap with B1 below; punting them keeps A1 a clean superset target for future bulk work.

For revision inspection / checkout:

- **B1 (chosen).** `Enter` on `RevisionListScreen` pushes a new `RevisionDetailScreen` that shows version metadata, changelog/update log, a scrollable diff against main, and exposes `c` for checkout. Rationale: the detail data set (metadata + diff list + log) is larger than fits cleanly inline, and `RequestProjVersionDiff` is async-shaped (event-driven via `ProjComparisonCompleteEventHandler`) which fits the existing `OnEnter` pattern in `DeployAssistant.CLI/Engine/Screen.cs`.
- **B2 rejected (bottom panel).** Would require splitting the render area; the engine does not have a region/pane abstraction and we do not want to introduce one for one screen.
- **B3 rejected (inline expand).** Tried mentally: collapses poorly when the diff is long, and forces the list and the diff to share a single `SelectableList` cursor.

Cross-cutting:

- All destructive actions go through `TuiPrompt.Confirm(title, message)`. `ConsoleDialogService.Confirm` is unsuitable because it writes directly to `AnsiConsole` underneath the active screen's render frame, scrolling the buffer and de-syncing the cursor. The new helper renders inline as part of the screen's `Render()` pass and reads a single key.
- Refresh-on-complete semantics: per-file revert removes the file from `IntegrityResultScreen`'s internal list (no full re-render of the integrity check); project-level checkout pops two screens (detail + list) back to `MainScreen` so the user lands on the surface that reflects the new `MainProjectData`.

## 3. Architecture

### 3.1 Files touched

| File | Status | Purpose |
|---|---|---|
| `DeployAssistant.CLI/Engine/TuiPrompt.cs` | NEW | `static bool Confirm(string title, string message)`. Renders inline Spectre markup, reads one key (`y`/`Y` -> true, anything else -> false), returns. |
| `DeployAssistant.CLI/Screens/IntegrityResultScreen.cs` | MODIFY | Add `r` keybind; call `mgr.RequestRevertChange(selected)` (now `bool`); on success remove the row and adjust `SelectableList`; on failure render a transient one-line red error. |
| `DeployAssistant.CLI/Screens/RevisionListScreen.cs` | MODIFY | Add `Enter` keybind that pushes `RevisionDetailScreen(_mgr, _rows[_list.SelectedIndex])`. Footer line updated. |
| `DeployAssistant.CLI/Screens/RevisionDetailScreen.cs` | NEW | Renders version metadata, changelog / update log, scrollable diff list. Subscribes to `ProjComparisonCompleteEventHandler` in ctor; calls `mgr.RequestProjVersionDiff(revision)` in `OnEnter`. `c` keybind -> `TuiPrompt.Confirm` -> `mgr.RequestRevertProject(revision)`. |
| `DeployAssistant.Core/DataComponent/MetaDataManager.cs` | MODIFY | `RequestRevertChange` and `RequestRevertProject` change return type from `void` to `bool`. Non-breaking; existing void callers continue to compile (return value simply ignored). |

Also touched only as part of the new screen's needs: `IntegrityResultScreen` and `RevisionListScreen` already depend on `MetaDataManager`; no constructor surface changes are needed for them beyond the keybinds. `RevisionListScreen`'s constructor already takes `MetaDataManager`, so passing it down to `RevisionDetailScreen` is a one-liner.

### 3.2 Screen stack diagram

```
MainScreen
  └─ TopMenuScreen
       ├─ IntegrityRunScreen           (existing)
       │    └─ IntegrityResultScreen   <-- adds 'r' (revert one)
       │
       └─ RevisionListScreen           <-- adds 'Enter' (inspect)
            └─ RevisionDetailScreen    NEW: 'c' (checkout)
                                            on success -> Pop x2 -> back to MainScreen
```

`Pop x2` on checkout is implemented as one `ScreenAction.Pop` returned from `RevisionDetailScreen.Handle`, followed by a second `Pop` from `RevisionListScreen` driven via a new `ScreenAction.PopN(int)` variant — *or* by storing a "checkout success" flag on the manager that `RevisionListScreen.AutoAdvance()` reads on re-entry and returns `Pop`. The implementation plan picks one; both are local to the CLI.

### 3.3 Core API surface used

From `MetaDataManager`:

- `RequestRevertChange(ProjectFile)` -> `bool` (post-change return type; see §7).
- `RequestRevertProject(ProjectData?)` -> `bool` (post-change return type; see §7).
- `RequestProjVersionDiff(ProjectData)` -> `void` (unchanged; result delivered via event).
- Event `ProjComparisonCompleteEventHandler` (`Action<ProjectData, ProjectData, List<ChangedFile>>`) — subscribed by `RevisionDetailScreen` (`spec.md §8.1`).

No new events, no new manager fields, no changes to sub-managers.

## 4. Per-file revert on IntegrityResultScreen (A1)

### 4.1 Footer change

Current footer (`IntegrityResultScreen.cs:45`):

```
↑↓ move · d/u half-page · esc back
```

New footer:

```
↑↓ move · d/u half-page · r revert · esc back
```

### 4.2 Keybind and flow

`Handle(ConsoleKeyInfo key)` adds, before delegating to `_list.Handle(key)`:

```csharp
if (key.KeyChar == 'r' && _files.Count > 0)
{
    var selected = _files[_list.SelectedIndex];
    if (!TuiPrompt.Confirm("Revert file", $"Revert '{selected.DataRelPath}' to backup?"))
        return ScreenAction.StayAction;

    bool ok = _mgr.RequestRevertChange(selected);
    if (ok)
    {
        _files.RemoveAt(_list.SelectedIndex);
        _list.SetItemCount(_files.Count);
    }
    else
    {
        _lastError = $"Revert failed for '{selected.DataRelPath}' (see trace log).";
    }
    return ScreenAction.StayAction;
}
```

The screen gains two private fields: `private readonly MetaDataManager _mgr;` (passed through the constructor) and `private string? _lastError;` (rendered as one dim/red line above the footer; cleared on the next keystroke).

### 4.3 Failure handling

`FileManager.RevertChange` (`FileManager.cs:1320`) early-returns and re-applies the `IntegrityChecked` flag whenever a precondition fails (missing recorded file, missing backup, etc.). Those failures emit a `Trace.TraceWarning` but no exception, so the TUI cannot distinguish them from success without inspecting the flag. The new `bool` return from `MetaDataManager.RequestRevertChange` (§7) carries exactly that signal. On `false`, we leave the row in place and show `_lastError`; the user can retry or `Esc` back.

### 4.4 Empty-list pop behavior

The existing screen already returns `ScreenAction.PopAction` on any key when `_files.Count == 0`. After `r` empties the list (last dirty file reverted), the next render shows the "All files match" success line; the next keystroke pops back to `IntegrityRunScreen`'s caller. No new branch needed.

## 5. RevisionDetailScreen + checkout (B1)

### 5.1 Sub-screen layout (text mock)

```
  CleProject (revision #4)
  ──────────────────────────────────────────────
  Version    v1.3.0
  Updater    idpark
  Updated    2026-05-06 14:22
  Changes    17
  Status     ★ current main

  Update log
    Bugfix release: corrected ignore-list ordering.

  Changes vs. current main
    + Tools/new_helper.exe                (added)
    ~ Bin/loader.dll                      (modified)
    - Docs/legacy_readme.txt              (deleted)
    ...                                   (scrollable via ↑↓ / d / u)

  ──────────────────────────────────────────────
  ↑↓ move · d/u half-page · c checkout · esc back
```

`★ current main` is shown only when the revision equals `mgr.MainProjectData`; in that case `c` is a no-op that surfaces a dim-line message instead of prompting ("Already on this revision"). Otherwise the bullet reads e.g. "Status     past revision". Diff list items reuse `TextStyle.FormatFileState` for visual consistency with `IntegrityResultScreen`.

### 5.2 OnEnter diff loading

`RevisionDetailScreen` subscribes to `ProjComparisonCompleteEventHandler` in its constructor and stores the most-recent `List<ChangedFile>` payload. `OnEnter` calls `_mgr.RequestProjVersionDiff(_revision)`. The event fires synchronously today (`FindVersionDifferences` is a synchronous dictionary diff, see `MetaDataManager.cs:490-496`), so by the time `OnEnter` returns, the diff field is populated. The screen does not need a "loading" state, but the render path null-checks the diff list and shows "(computing diff...)" defensively in case `MainProjectData` is null or the event is suppressed.

Subscription is released in a `Dispose`-style cleanup the screen runs from a sentinel — Screen does not currently expose an `OnExit` hook, so the cleanup is wired by having the subscription handler check a `_disposed` flag and bail. A small `OnExit() {}` virtual method on `Screen` would be the cleaner answer and is recommended as a bundled change; see §9 Risks.

### 5.3 `c` keybind flow

```csharp
if (key.KeyChar == 'c')
{
    if (ReferenceEquals(_revision, _mgr.MainProjectData))
    {
        _info = "Already on this revision.";
        return ScreenAction.StayAction;
    }
    string msg = $"Roll project back to '{_revision.UpdatedVersion}' " +
                 $"({_revision.UpdatedTime:yyyy-MM-dd HH:mm})? " +
                 $"{_diff?.Count ?? 0} files will change on disk.";
    if (!TuiPrompt.Confirm("Checkout revision", msg))
        return ScreenAction.StayAction;

    bool ok = _mgr.RequestRevertProject(_revision);
    if (!ok)
    {
        _lastError = "Checkout failed (see trace log).";
        return ScreenAction.StayAction;
    }
    return ScreenAction.PopToMain;  // see §3.2 for pop-2 mechanics
}
```

`ScreenAction.PopToMain` here is shorthand; the implementation plan chooses between (a) a new `PopN(2)` variant on `ScreenAction`, or (b) a one-shot success flag on `MetaDataManager` that `RevisionListScreen.AutoAdvance` reads to pop itself. Option (b) keeps `ScreenAction` minimal.

### 5.4 "This is current" no-op

When the user opens detail on the current main revision, `c` short-circuits with a dim info line ("Already on this revision."). The prompt is not shown — confirming a no-op is worse UX than blocking it outright.

## 6. TuiPrompt.Confirm helper

### 6.1 Why not reuse `ConsoleDialogService.Confirm`

`ConsoleDialogService.Confirm` writes via `AnsiConsole.MarkupLine` underneath whatever screen is currently rendered. The TUI loop in `App.Run` (`DeployAssistant.CLI/Engine/App.cs:50`) calls `AnsiConsole.Clear()` once per frame; any text emitted between frames scrolls the buffer and the next `Clear` cannot restore the prior frame's content. In practice this produces "ghost" prompt text smeared above the active screen.

`TuiPrompt.Confirm` instead renders the prompt **as part of** the active screen's `Render()` output for that frame — the engine clears and redraws normally, and the prompt is just two extra `MarkupLine` calls plus a `Console.ReadKey`.

### 6.2 Contract

```csharp
// DeployAssistant.CLI/Engine/TuiPrompt.cs
namespace DeployAssistant.CLI.Engine;

internal static class TuiPrompt
{
    /// <summary>
    /// Renders a one-line yes/no confirmation inline and reads exactly one key.
    /// Returns true on 'y' or 'Y'; anything else (including Esc) returns false.
    /// The caller is responsible for triggering a re-render afterward; the
    /// engine's main loop already re-renders after Handle() returns.
    /// </summary>
    public static bool Confirm(string title, string message)
    {
        AnsiConsole.MarkupLine($"  {TextStyle.Accent(title)}  {Markup.Escape(message)}");
        AnsiConsole.Markup($"  {TextStyle.Dim("(y to confirm, any other key to cancel) ")}");
        var key = Console.ReadKey(intercept: true);
        return key.KeyChar == 'y' || key.KeyChar == 'Y';
    }
}
```

### 6.3 Sample render

```
  Revert file  Revert 'Bin/loader.dll' to backup?
  (y to confirm, any other key to cancel)
```

The prompt does *not* call `AnsiConsole.Clear()`; it relies on the engine's next loop iteration to clear and redraw. A caller that does not intend to re-render immediately must do so itself, but in practice the only callers are inside `Screen.Handle` which is followed by the engine's render loop.

## 7. Core API change

### 7.1 Signatures

```csharp
// Before
public void RequestRevertChange(ProjectFile file);
public void RequestRevertProject(ProjectData? targetProject);

// After
public bool RequestRevertChange(ProjectFile file);
public bool RequestRevertProject(ProjectData? targetProject);
```

The change is binary-compatible at the source level: every existing caller (`MetaDataViewModel`, `BackupViewModel`, etc.) ignores the return value, so their call sites continue to compile without edits. At the IL level the method's return type changes, so any pre-compiled consumer would need a rebuild — but every consumer in this repo lives in the same solution and rebuilds together.

### 7.2 `RequestRevertChange` -> bool derivation

`FileManager.RevertChange` (`FileManager.cs:1320-1370`) clears `DataState.IntegrityChecked` at line 1323 and re-applies it on every failure branch (lines 1333, 1339, 1348, 1359). The flag is therefore an exact success indicator after the call returns. New implementation:

```csharp
public bool RequestRevertChange(ProjectFile file)
{
    _fileManager.RevertChange(file);
    return (file.DataState & DataState.IntegrityChecked) == 0;
}
```

### 7.3 `RequestRevertProject` -> bool derivation

`BackupManager.RevertProject` (`BackupManager.cs:131-151`) is the underlying void method and swallows exceptions internally; there is no externally observable success signal today. First cut wraps the call in a try/catch and reports success when no exception is thrown *and* a non-null target was supplied:

```csharp
public bool RequestRevertProject(ProjectData? targetProject)
{
    if (targetProject == null)
    {
        Trace.TraceWarning("Invalid Request For Backup: Targeting Project is Null");
        return false;
    }
    try
    {
        var fileDifferences = _fileManager.FindVersionDifferences(targetProject, MainProjectData, true);
        _backupManager.RevertProject(targetProject, fileDifferences);
        return true;
    }
    catch (Exception ex)
    {
        Trace.TraceError($"RequestRevertProject failed: {ex.Message}");
        return false;
    }
}
```

Note this is intentionally a weak success signal — `BackupManager.RevertProject` itself returns void after a failed `_fileHandlerTool.TryApplyFileChanges` (it traces a warning and falls through). A future cleanup should propagate `TryApplyFileChanges`'s bool out through `ProjectRevertEventHandler` or via an out-param; tracked in §10 as "richer RevertResult enum".

### 7.4 Why not throw on failure

Throwing would force every existing GUI caller (which currently ignores success) to add try/catch. The bool return is the minimum-disturbance signal that satisfies the new CLI screens without rippling into the WPF code.

## 8. Testing

### 8.1 Sandbox pattern

New tests follow the existing per-test temp-dir convention (e.g. `MetaDataManagerVersionDiffTests.cs:24`):

```csharp
_projectDir = Path.Combine(Path.GetTempPath(), "DA_RevertCkt_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(_projectDir);
```

Cleanup in `IDisposable.Dispose` deletes the directory recursively, ignoring `IOException` if a child file is still locked (matches the existing `DA_IntegTest_*` and `DA_DiffTest_*` cleanup style).

### 8.2 New test classes

| Class | File | Verifies |
|---|---|---|
| `MetaDataManagerRevertChangeTests` | `DeployAssistant.Tests/Integration/MetaDataManagerRevertChangeTests.cs` | `RequestRevertChange` returns `true` after a successful revert of a `Modified|IntegrityChecked` file (the `IntegrityChecked` flag is cleared and the on-disk content matches the backup). Returns `false` when no backup exists for the file's hash (we synthesize this by deleting `Backup_<proj>/Backup_<ver>/<file>` before the call). |
| `MetaDataManagerRevertProjectTests` | `DeployAssistant.Tests/Integration/MetaDataManagerRevertProjectTests.cs` | `RequestRevertProject(null)` returns `false`. `RequestRevertProject(prevVersion)` returns `true` after a clean two-version sequence; verifies on-disk file content matches the target version. Returns `false` when the target version's backup directory has been tampered with (file deleted). |
| `TuiPromptTests` | `DeployAssistant.CLI.Tests/Engine/TuiPromptTests.cs` | Pure unit: stub `Console.In` with a `StringReader` of "y\n" -> true; "n\n" -> false; "" (Esc) -> false. Note `TuiPrompt.Confirm` uses `Console.ReadKey` not `Console.ReadLine`; the test exercises the public helper by setting `Console.SetIn` and using a custom test seam, or — preferred — by extracting a `Confirm(Func<ConsoleKeyInfo> readKey)` overload that the unit test calls directly with a stubbed reader returning `y` / `n` / `Escape`. The public `Confirm(string, string)` delegates using `Console.ReadKey` as the default. Pattern follows the existing CLI tests in `DeployAssistant.CLI.Tests/Engine/` (e.g. `TextStyleTests.cs`, `SelectableListTests.cs`). |

`DeployAssistant.CLI.Tests` is a `net472` xUnit project that already exists (added in commit `f02c652`, ~45 tests across `Engine/` and `Screens/`) — `TuiPromptTests` joins the existing `Engine/` folder. No new test project needed.

### 8.3 xUnit specifics

xUnit 2.9.2 (per `CLAUDE.md`). No collection fixtures required — each test owns its temp directory and `MetaDataManager` instance. `[Fact]` only; no `[Theory]` parameterization needed.

## 9. Risks & Verification

| Risk | Mitigation |
|---|---|
| `RequestProjVersionDiff` is async-shaped via event, and a slow/missing event fire would leave `RevisionDetailScreen` rendering "(computing diff...)" forever. | The underlying `FindVersionDifferences` is a synchronous in-memory diff; the event fires before `RequestProjVersionDiff` returns. The render path tolerates a null diff (shows the dim placeholder) so the worst case is a one-frame flicker. |
| Missing backup file -> `RevertChange` silently re-applies `IntegrityChecked`, and the user sees only "Revert failed" with no detail. | The new bool return preserves trace logs; users running with `DOTNET_TRACE` or attached to a debug listener still see the warning. A richer `RevertResult` enum is §10 out-of-scope. |
| `ProjComparisonCompleteEventHandler` subscription leak when the screen is popped before the diff returns (or when `Esc` is pressed quickly). | Detail screen sets a `_disposed` flag in a soft cleanup; the event handler checks it. Cleaner: add `Screen.OnExit()` virtual to the engine in this PR; trivial change, three lines in `App.cs`. Treat as recommended-but-optional. |
| `Pop x2` mechanic surface in `ScreenAction` (§3.2) is novel; could collide with existing `Push`/`Replace` variants. | Prefer the "success flag" approach (option b in §3.2). Keeps `ScreenAction` minimal; the flag lives on `MetaDataManager` as a single nullable `ProjectData? LastCheckedOut` reset on read by `RevisionListScreen.AutoAdvance`. |
| Existing GUI consumers of `RequestRevertChange` / `RequestRevertProject` (4 ViewModels + their command bindings) become "silently informed" callers — they ignore the new bool. | Intentional. The GUI today does not present a success/failure indicator either; it relies on `IntegrityCheckCompleteEventHandler` re-running. We may revisit when the WPF UX is iterated. |
| `TuiPrompt.Confirm` is bypassed by `--yes` callers expecting the global "auto-confirm" behavior. | `--yes` is consumed by `ConsoleDialogService.Confirm` only (see `CLAUDE.md`); the TUI prompt is a separate concept tied to interactive screens. Document that `--yes` does not auto-confirm TUI prompts — the TUI requires a real terminal anyway (`App.Run` early-exits on redirected stdin). |

**Done definition for this change set:**

1. `dotnet build DeployAssistant.sln -c Debug` clean across all 4 projects + test project.
2. `dotnet test DeployAssistant.Tests/DeployAssistant.Tests.csproj` green, including the three new test classes from §8.2.
3. CLI smoke test green (no behavior change to top-level command surface).
4. Manual verification on Windows: kick off an integrity check that detects at least one modified file; press `r`; confirm; verify the file is restored on disk and removed from the list. Then open the revision list, `Enter` into a past revision, press `c`, confirm; verify the screen pops back to `MainScreen` and `MainProjectData` reflects the older version.
5. `spec.md §8.1` updated to show `RequestRevertChange` and `RequestRevertProject` returning `bool`.

## 10. Out of Scope

- **Bulk revert** (`R` for all integrity-checked files on the screen) — wait for user demand; will introduce its own confirmation pattern (per-file vs. all-at-once).
- **Multi-select on `IntegrityResultScreen`** (space-to-mark, `r` to revert marked) — requires extending `SelectableList`.
- **Inline-expand revision preview (B3)** on `RevisionListScreen` — rejected during brainstorming.
- **Arbitrary-revision-pair diff** (compare two non-main snapshots) — not exposed today even in the GUI; orthogonal change.
- **Content-level diff** (line-by-line or hex diff per changed file) — out of scope; CLI currently shows only file-level state glyphs.
- **Richer `RevertResult` enum** to distinguish "no backup", "FS error", "no-op already-clean" failures from a single bool — useful, but the GUI does not consume it today and the CLI surfaces traces.
- **`Screen.OnExit()` virtual hook on the engine** — recommended as a tiny bundled change but not strictly required (see §9). If excluded, the soft-cleanup `_disposed` flag in `RevisionDetailScreen` is sufficient.

## 11. References

- `spec.md §8.1` (`MetaDataManager` event surface and `Request*` methods, lines 340-389) — ground truth for the API table in §3.3.
- `CLAUDE.md` — operating notes (TUI screen pattern, `--yes` semantics for `ConsoleDialogService`).
- `docs/superpowers/specs/2026-05-06-target-framework-realignment-design.md` — prior spec document; this spec matches its section structure and tone.
- `docs/superpowers/specs/2026-05-07-cli-tui-design.md` — original CLI TUI redesign that introduced `IntegrityResultScreen` and `RevisionListScreen`.
- Brainstorming session 2026-05-07 (this design's approval).
- PR #22 (`fix/cli-release-crash-and-wpf-dual-publish`) — branch this spec lands on.
- Source files for accurate API shape: `DeployAssistant.Core/DataComponent/MetaDataManager.cs` (lines 329, 347, 483), `DeployAssistant.Core/DataComponent/FileManager.cs` (lines 1320-1370), `DeployAssistant.Core/DataComponent/BackupManager.cs` (lines 131-151), `DeployAssistant.CLI/Screens/IntegrityResultScreen.cs`, `DeployAssistant.CLI/Screens/RevisionListScreen.cs`, `DeployAssistant.CLI/Engine/App.cs`, `DeployAssistant.CLI/Engine/Screen.cs`, `DeployAssistant.CLI/Engine/TextStyle.cs`, `DeployAssistant.CLI/Engine/Widgets/SelectableList.cs`.
