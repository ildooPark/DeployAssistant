# DeployAssistant CLI — Interactive TUI Redesign

**Status:** Approved for implementation
**Date:** 2026-05-07
**Scope:** `DeployAssistant.CLI` only. No changes to `Core`, `Core.Standard`, `ViewModel`, or WPF.

---

## 1. Goal

Replace the current non-interactive subcommand CLI with a keyboard-driven interactive TUI whose look and feel evokes the git CLI: flat, color-coded, no boxy framework chrome. The redesigned CLI is scoped down — only `integrity-check` and `list revisions` are exposed as version-control operations. Everything else (`scan`, `stage`, `deploy`, `revert`, `export`) is removed.

## 2. Non-goals

The following are explicitly **out of scope** and must not be added during implementation:

- Scan / stage / deploy / revert / export operations
- Recents list (multi-project history); a single `LastOpenedDstPath` is enough for now
- Multi-pane diff view; mouse input; user-configurable themes
- New NuGet dependencies. Only the already-referenced `Spectre.Console` is used.

## 3. User-facing behavior

### 3.1 Launch flow

- If `LocalConfigData.LastOpenedDstPath` exists and the project loads successfully, land directly on **MainScreen**.
- Otherwise, land on **TopMenuScreen** (Switch / Initialize / Quit).

### 3.2 MainScreen

A compact project card sits on top, followed by an arrow-selectable action menu, with a hotkey hint footer.

```
┌────────────────────────────────────┐
│  ProjectName                       │
│  Path     C:\…\foo                 │
│  Version  v1.2.3   ← main          │
│  Updated  2026-05-07 14:32 by ido  │
│  Files    142   (7 revisions)      │
└────────────────────────────────────┘
 › Integrity check
   List revisions
─────────────────────────────────────
↑↓ move · enter select · m menu · q quit
```

- `Version` line uses cyan-bold for the version string and prints the literal tag `← main`.
- `m` opens **TopMenuScreen** (Switch project / Initialize new project / Quit). Esc dismisses.
- `q` quits the app.
- `Enter` on Integrity check → **IntegrityRunScreen** (spinner) → **IntegrityResultScreen**.
- `Enter` on List revisions → **RevisionListScreen**.

### 3.3 TopMenuScreen

A small selectable menu rendered over the current screen's bottom rows (or full-screen if no project is loaded). Items: `Switch project`, `Initialize new project`, `Quit`. Esc dismisses.

### 3.4 PathPickerScreen

Used by both Switch and Initialize.

```
Open project at:
> C:\Workspace\dep█

Tab to complete · ↑↓ pick · enter open · esc cancel
```

- Typing inserts at the cursor; Backspace deletes left.
- The candidate provider is a `Func<string, string[]>` that takes the full current input string and returns matching **directory names** (always — files are not candidates). Lookup splits the input at the last directory separator: the prefix is the parent directory to enumerate, the suffix is the partial name to match. Hidden directories (leading `.`) excluded by default.
- `Tab` rules:
  - 0 candidates → visual flash, no-op.
  - 1 candidate → inline complete: replace the partial suffix with the full directory name + a trailing path separator (since candidates are always directories, the separator is always appended).
  - 2+ candidates → show the candidate list under the input. `↑↓` selects; Enter inserts the highlighted candidate (with trailing separator) and returns focus to typing. Continuing to type while in candidate-list mode dismisses the list and re-runs the candidate query against the new partial.
- Edge cases (must be specified, not discovered):
  - **Case sensitivity**: matching is case-insensitive on all platforms (Windows is the primary target; the cross-platform consistency is intentional).
  - **Drive roots** on Windows: `C:` with no separator is treated as `C:\`. The candidate provider lists the root's top-level directories.
  - **UNC paths** (`\\server\share`): not specially supported in v1. If the user types one and `Tab`s, the candidate provider returns empty (treated as 0 candidates). Document this in the screen's hint footer or as a known limitation.
  - **Trailing separator**: a trailing `\` or `/` means "list children of this directory" (suffix is empty, all children are candidates).
- `Enter` (on the input itself) tries to load the path. On failure, show a red `✗ <reason>` line under the input and stay on the screen.
- `Esc` cancels and returns to the previous screen.

### 3.5 IntegrityRunScreen

A `Spectre.Console.Status` spinner labelled `Running integrity check…`, identical wording to today's `RunIntegrityCheck`. The screen takes over the console synchronously for the duration of the operation (see §4.4) — the engine's render/ReadKey loop is suspended while the spinner is active. Transition rule: the screen waits for `IntegrityCheckCompleteEventHandler` (which carries the file list) as the authoritative completion signal; `ManagerStateEventHandler == Idle` is used only as a timeout safety net so the screen unblocks if no files were produced. After completion, the screen replaces itself with **IntegrityResultScreen** carrying the captured file list (or an empty list).

### 3.6 IntegrityResultScreen

If no deviations:

```
✓ All files match — no deviations detected.
Press any key to return.
```

If deviations:

```
3 modified · 1 deleted · 0 added · 0 restored
─────────────────────────────────────────────
  ~ src/Foo.cs           (modified)
  ~ src/Bar.cs           (modified)
› - obsolete/Old.dll     (deleted)
  ~ README.md            (modified)
─────────────────────────────────────────────
↑↓ move · d/u half-page · esc back
```

Glyphs and colors match today's `FormatFileState`: `+` green added, `~` yellow modified, `-` red deleted, `*` magenta restored. Read-only — there are no per-file actions.

### 3.7 RevisionListScreen

Same data as today's `RunList` table, re-rendered as a scrollable selectable list. The row matching `MainProjectData` is marked with a cyan `→` and the version printed cyan-bold. Read-only — Enter does nothing for now (reserved for future revert/export). `d/u` half-page jumps. Esc returns.

### 3.8 Universal bindings

- `Esc` pops one screen off the stack.
- `Ctrl+C` exits cleanly (cancel any in-flight operation, print no stack trace).
- `q` on MainScreen quits.

## 4. Architecture

### 4.1 Boundaries

- `DeployAssistant.CLI` becomes a presentation layer over `MetaDataManager`. No business logic moves into the CLI project.
- `Spectre.Console` is used as a **rendering primitive library only**: `Markup`, `Panel`, `Table`, `Status`. We do **not** use `SelectionPrompt`, `TextPrompt`, or any other Spectre input prompt. Input is driven by our own engine.

### 4.2 Layout

```
DeployAssistant.CLI/
├─ Program.cs                         — thin entrypoint: build App, Run, exit code
├─ Engine/
│  ├─ App.cs                          — screen stack, render→ReadKey→handle loop, Ctrl+C
│  ├─ Screen.cs                       — interface: OnEnter(), Render(), Handle(ConsoleKeyInfo) → ScreenAction
│  ├─ ScreenAction.cs                 — small discriminated type: Stay / Pop / Push(Screen) / Replace(Screen) / Exit
│  ├─ TextStyle.cs                    — static markup helpers: Added(s), Removed(s), Modified(s), Restored(s), Accent(s), Dim(s), FormatFileState(state, relPath)
│  └─ Widgets/
│     ├─ SelectableList.cs            — viewport scroll, ↑↓ d/u, selected index
│     └─ LineInput.cs                 — typing/cursor/Tab completion against Func<string,string[]>
└─ Screens/
   ├─ MainScreen.cs                   — also owns the project-card panel rendering (single caller, no widget abstraction)
   ├─ TopMenuScreen.cs
   ├─ PathPickerScreen.cs
   ├─ IntegrityRunScreen.cs
   ├─ IntegrityResultScreen.cs
   └─ RevisionListScreen.cs
```

Each file has one clear purpose. Widgets know nothing about screens; screens compose widgets and translate user intent into manager calls + screen transitions.

### 4.3 The render loop

```
loop:
  current = stack.Peek()
  if current is new top-of-stack since last frame:
      current.OnEnter()               — may run synchronously to completion (see §4.4)
  Console.Clear()
  current.Render()                    — emits Spectre markup to AnsiConsole
  key = Console.ReadKey(intercept:true)
  if key is Ctrl+C: graceful exit
  action = current.Handle(key)
  apply(action)                       — Stay/Pop/Push/Replace/Exit
```

- `Console.Clear()` between frames keeps things simple; flicker is acceptable for the small frame sizes we render. If flicker proves objectionable in review, we can switch to anchored `Spectre.Console.Live` regions per screen — but immediate-mode is the default.
- The engine never blocks except on `Console.ReadKey` (interactive screens) or inside a screen's `OnEnter()` (long-op screens — see §4.4).
- **`OnEnter()` lifecycle**: called once when a screen first becomes top-of-stack, and again when it returns to top-of-stack via Pop. Screens that need first-vs-re-entry behavior track it themselves with a boolean field. Screens that kick off a long-running manager operation do so synchronously inside `OnEnter()` (the operation completes before `OnEnter()` returns), then `Render()` produces the post-operation view.

### 4.4 Long-running operations

`MetaDataManager` is event-driven and fires its events on background threads (initialization is async; the manager dispatches results back without marshaling to a UI thread). The CLI is single-threaded by design — only `OnEnter()` ever interacts with the manager, and only in a synchronous, blocking style:

```csharp
// Inside IntegrityRunScreen.OnEnter() (and analogously for InitScreen)
List<ProjectFile> result = new();
using var done = new ManualResetEventSlim(false);
void OnComplete(MetaDataManager _, IEnumerable<ProjectFile> files)
{
    lock (result) { result.Clear(); result.AddRange(files); }
    done.Set();
}
void OnState(MetaDataState s) { if (s == MetaDataState.Idle) done.Set(); }

mgr.IntegrityCheckCompleteEventHandler += OnComplete;
mgr.ManagerStateEventHandler += OnState;
try
{
    AnsiConsole.Status().Spinner(Spinner.Known.Dots).Start(
        "Running integrity check…",
        _ =>
        {
            mgr.RequestProjectIntegrityCheck();
            done.Wait(TimeSpan.FromMinutes(10));
        });
    capturedResult = result.ToList(); // safe: events have stopped firing for this op
}
finally
{
    mgr.IntegrityCheckCompleteEventHandler -= OnComplete;
    mgr.ManagerStateEventHandler -= OnState;
}
```

Key contract points the implementation must honor:

- **Console ownership.** `Spectre.Console.Status().Start()` takes exclusive ownership of the console for the duration of the spinner. The engine's outer render loop is **not** running during this call. This is intentional and is why long ops run inside `OnEnter()`, not inside `Render()`.
- **Event handler lifecycle.** Handlers are subscribed before the request and unsubscribed in `finally` — no leaks across screen activations.
- **Thread-safety.** Event handlers may fire on a worker thread. The list is mutated under a lock and only read after `done` is set; the screen never reads `result` from the worker thread itself.
- **No live re-render.** v1 screens that run long ops do **not** re-render mid-operation. The Status spinner is the only visible activity; the screen produces its real `Render()` only after the op completes.

This is identical in shape to the current `RunIntegrityCheck` implementation in `Program.cs:315`, with the addition of explicit `lock` and unsubscription.

## 5. Error handling & runtime concerns

- **Load failures** in PathPickerScreen show a red `✗ <reason>` line under the input. The input remains active; user can correct and retry.
- **`MetaDataManager` exceptions** are caught at the screen level, rendered as a red banner, and the user can press any key to acknowledge before returning to the previous screen.
- **`Ctrl+C`** during a long op: best-effort — let the in-flight op finish (the manager doesn't currently support cancellation), then exit. Document this limitation in the README.
- **Terminal too small** (height < ~10 rows): render a `Terminal too small — please resize` line and wait for keypress. Resize events are not actively monitored — the next key press triggers a re-render at the new size, so resizing while idle is automatically handled. No special mid-operation resize handling.
- **Null `MainProjectData`**: a project with no recorded revisions has `MainProjectData == null`. MainScreen renders the project card with `Version: Undefined` and `Files: 0`, matching the existing `PrintProjectCard` fallback (`Program.cs:382`). RevisionListScreen renders an empty list with a dim "No revisions recorded" line.
- **Exit codes**: `0` on clean quit (`q`, Ctrl+C, Esc out of root). `1` only if the app fails to start (e.g. unhandled exception during initial load). Operation outcomes (integrity-check finding deviations, etc.) do **not** affect the exit code — the new TUI is interactive, not script-driven.
- **`LastOpenedDstPath` write timing**: persisted to `LocalConfigData` immediately after a successful project load (both initial auto-load and Switch project). On Initialize new project, persisted on successful initialization. Failed loads do not update the path. This matches existing GUI behavior.

## 6. Testing strategy

Existing test conventions live in `DeployAssistant.Tests`. New tests go there.

- **Widget unit tests** (highest value):
  - `LineInput` — Tab completion: 0 / 1 / N candidates; cursor positioning; Backspace at start / mid; insertion of highlighted candidate.
  - `SelectableList` — viewport math: cursor moving below the visible window scrolls; `d/u` half-page math; selection clamps to bounds; empty list renders cleanly.
- **Screen tests**:
  - `PathPickerScreen` with a fake candidate provider — types `C:\Wo`, presses Tab → assert candidate list rendered; selects one → assert input updated.
  - `MainScreen` — presses Enter on Integrity check → asserts the right screen is pushed. Manager interaction is exercised via `MetaDataManager` directly, following the pattern already established in `DeployAssistant.Tests` (which constructs real managers against temp directories). Introducing a manager interface for mocking is out of scope for this redesign.
- **No end-to-end terminal tests.** Snapshot tests of widget render output are acceptable but not required.

## 7. Color palette

Aligned with git's CLI convention. The palette lives in `Engine/TextStyle.cs` as static helpers — not a token enum. Each helper wraps a string in the corresponding Spectre markup so callers write `TextStyle.Added(path)` instead of `[green]{path}[/]`.

| Helper         | Spectre style          | Usage                                    |
|----------------|------------------------|------------------------------------------|
| `Accent`       | `cyan bold`            | Current version, selected menu item arrow |
| `Added`        | `green`                | `+` glyph and added-file paths            |
| `Removed`      | `red`                  | `-` glyph, errors                         |
| `Modified`     | `yellow`               | `~` glyph, warnings                       |
| `Restored`     | `magenta`              | `*` glyph                                 |
| `Dim`          | `dim`                  | Path hints, footer hotkey strings         |

`FormatFileState(DataState, string)` also lives in `TextStyle.cs` and reuses these helpers internally — replacing the equivalent method in today's `Program.cs:393`. No other colors are introduced.

## 8. Removed surface

`Program.cs` shrinks dramatically. The following methods are deleted entirely (with their argument-parsing helpers): `RunInit` (folded into a screen), `RunLoad`, `RunScan`, `RunStage`, `RunDeploy`, `RunRevert`, `RunExport`, `RunList`, `RunIntegrityCheck`, `Help`, `UsageError`, `UnknownCommand`, `ShowHelp`, `ParseFlag`. Reused logic: `PrintProjectCard`'s body becomes the `ProjectCard` widget; `FormatFileState` moves into `Engine/Theme.cs` as a static formatter so both the integrity-result screen and any future result screen share it.

## 9. Risks the implementation should watch for

- **Render flicker** on Windows Terminal from `Console.Clear()`-per-frame. If perceptible, switch to a buffered draw (build the full frame as a single string, write once, then position-cursor without clear) before resorting to `Spectre.Console.Live`.
- **`Status().Start()` interleave**: if a screen ever renders progress mid-operation, the immediate-mode loop will fight Status. v1 keeps long ops fully synchronous inside `OnEnter()` to avoid this; any future "live progress" feature requires reworking §4.4.
- **Background-thread event delivery** from `MetaDataManager`: §4.4's `lock` + `ManualResetEventSlim` pattern handles this for v1, but if any new screen ever needs to react to events outside `OnEnter()`, we'd need a thread-safe event queue.

## 10. Visual quality safeguards

During implementation, spawn subagent reviews at two checkpoints:

1. After widgets are implemented and unit-tested — ask a `code-reviewer` subagent to audit the widget code for over-engineering and the rendered terminal output (captured via snapshot) for visual simplicity and clarity.
2. After all screens compose end-to-end — ask an `Explore` subagent to walk through the full launch → integrity-check → revision-list → switch-project flow against this spec and flag any deviation.
