# Hash-Failure Metadata Fallback for Integrity Check

**Status:** Design — pending user review before implementation
**Date:** 2026-05-11
**Builds on:** PR #30 (integrity-check resilience), GUI v3.7.1 / CLI v1.1.1
**Author:** Brainstormed with Claude Code on `master` @ commit a09c230

---

## Context

PR #30 made integrity check resilient to per-file hash failures: a locked, permission-denied, or transiently-blocked file no longer aborts the entire scan. Instead, the offending file's hash comes back as `""` and the scan continues, with a warning appended to the integrity log.

That's a partial fix. The remaining gap: a file whose hash returned `""` provides **no signal** about whether it has actually changed. Today, an "empty hash" entry in the intersect loop is silently dropped via `projectFilesConcurrent.TryRemove(fileRelPath, out _)` (FileManager.cs:226), so the file disappears from the change set entirely. The user gets a log warning but no verdict — Unchanged or Modified? They have to inspect the file themselves.

Worse, the existing pre-checkout integrity gate (PR #24) treats an empty change set as "nothing to worry about" and proceeds with checkout. If the unscanned file was actually modified, checkout silently overwrites it from backup — **silent data loss**.

## Goals

1. **Verdict on every intersected file.** Every file in `intersectFiles` produces either an `Unchanged` (no change record) or `Modified` outcome — never silently dropped.
2. **Work with locked / partially-accessible files.** When MD5 can't be computed, use filesystem and PE metadata to reach a verdict that's *good enough* for the checkout workflow to remain safe.
3. **Transparent logging.** The user sees in the integrity-check log which files were verified by metadata only, so they can spot-check if something looks off.
4. **No schema changes.** Snapshot already stores `DataSize` and `BuildVersion` — both are usable as fallback comparison inputs without bumping the metadata format.

## Non-goals

- **Not a content-integrity guarantee.** Metadata can match while content differs (same-size byte-flip, etc.). The fallback is best-effort, not cryptographic. We document this and the user accepts it as the trade-off for keeping checkout functional under file locks.
- **Not exponential / sophisticated retry policy.** The retry is fixed-delay (default 200 ms × 3 retries = 600 ms worst case per file). Exponential backoff, jitter, or per-error-code policy is overkill for the workload — locks here are typically AV/installer/deploy-swap transients that clear quickly or persist indefinitely; nothing in between benefits from exponential backoff.
- **Not a third "Unverified" data state.** Considered and rejected — adding a state breaks the checkout integrity gate. Verdict must be binary (Modified or Unchanged).
- **Not extended to `addedFiles` loop.** Added files have no snapshot entry to compare against; an empty hash there is recorded with an empty hash + log warning (already done in PR #30). No fallback applies.
- **Not PE COFF linker timestamp.** Considered as a "build fingerprint" — rejected because reading the COFF header requires opening the file stream, which has the same lock vulnerability as MD5. When MD5 fails, the linker timestamp usually fails too. Filesystem metadata + `FileVersionInfo` survive locks via different Win32 API paths.

## Algorithm

For each `fileRelPath` in the intersect set (files present in both snapshot and disk-scan):

```
hash = TryHashMD5WithRetry(file, maxRetries = 3, retryDelayMs = 200)
//      attempts CreateFile up to (1 + maxRetries) times,
//      sleeps retryDelayMs between attempts,
//      returns "" if all attempts fail

if hash succeeds (non-empty string):
    if hash == snapshot.DataHash:
        verdict = Unchanged   // no change record emitted
    else:
        verdict = Modified    // emit a Modified ChangedFile, using fresh hash
    end

else:  // all retries exhausted: locked, permission-denied, persistent IO
    currentSize    = FileInfo.Length                                      // directory-level metadata, no file open
    currentVersion = FileVersionInfo.GetVersionInfo(fullPath).FileVersion // uses Win32 GetFileVersionInfo, survives FileShare.Read locks
    if currentSize == snapshot.DataSize && currentVersion == snapshot.BuildVersion:
        verdict = Unchanged
        log: "Note: <relpath> verified by metadata only — hash unavailable after <N> retries"
    else:
        verdict = Modified
        log: "Modified: <relpath> — metadata differs from snapshot (hash unavailable after <N> retries)"
    end
end
```

### Retry policy

The retry loop sits *inside* `HashTool.GetFileMD5CheckSum` (both string and instance overloads), encapsulated at the source of the failure rather than at every call site. When `CreateFile` fails, the method sleeps `retryDelayMs` and tries again, up to `maxRetries` more attempts. If all attempts fail, it returns `""` (string overload) or leaves `DataHash` empty (instance overload) — the existing fallback signal the call-site loops already understand.

| Parameter | Default | Rationale |
|---|---|---|
| `maxRetries` | **3** (so up to 4 total `CreateFile` attempts including the initial one) | Targets transient AV / installer / brief-deploy locks. Three retries catches almost all real-world transients without making the scan painful for genuinely-stuck files. |
| `retryDelayMs` | **200 ms** | AV scans typically clear in 100–1000 ms. 200 ms strikes a balance between recovery odds and total wait time. Worst case per file: 3 × 200 ms = 600 ms. |

For a typical project with hundreds of files, retries run inside the existing `Parallel.ForEach`, so they're concurrent — total elapsed scan time grows by roughly `(locked_files_count / parallelism) × 600 ms` in the worst case. On an 8-core machine, 100 simultaneously-locked files add ~12 seconds.

Both parameters are arguments to the retried hash methods so tests can override them. Setting `maxRetries = 0` skips retry entirely (useful for fast unit-test runs that want to exercise the metadata fallback immediately); `maxRetries = 1` exercises the retry path with minimal delay.

The method signatures become (default values preserve backwards compatibility for all existing call sites that don't pass the new args):

```csharp
public string GetFileMD5CheckSum(string projectPath, string srcFileRelPath, int maxRetries = 3, int retryDelayMs = 200);
public void GetFileMD5CheckSum(ProjectFile file, int maxRetries = 3, int retryDelayMs = 200);
```

If even the metadata reads fail (extreme rare double-failure: file genuinely vanished mid-scan, or low-level disk error), the helper's internal try/catch returns `match = true` to err on the side of `Unchanged`. This keeps the checkout integrity gate functional in the face of double-failure — the alternative (treating it as Modified) would falsely block checkout on a transient anomaly. The double-failure log line records the exception so a careful user can spot it.

## Affected code locations

### MainProjectIntegrityCheck (FileManager.cs)

The intersect-loop's async-task block (~lines 240–286) compares `projectFile.DataHash != intersectedFile.DataHash`. Today, when `intersectedFile.DataHash` is empty (hash failed), the file was already removed from `projectFilesConcurrent` in the prior Parallel.ForEach, so it never reaches this block.

**Change:** in the Parallel.ForEach (~lines 222–227), instead of removing empty-hash files from the dict, mark them as "needs metadata verdict" so they flow through to the async-task block. The async-task block then branches:
- If `intersectedFile.DataHash` is non-empty → existing hash comparison path
- If empty → new metadata comparison helper, route to either Unchanged-skip or emit a Modified change

### ProjectIntegrityCheck (FileManager.cs)

The intersect loop (~lines 424–440) currently does `_hashTool.GetFileMD5CheckSum(...)` and compares directly. PR #30 added an early `continue` on empty hash, dropping the file from the diff.

**Change:** replace the `continue` with the metadata comparison fallback. If metadata says Unchanged → continue (skip the file, no change record). If metadata says Modified → fall through to the existing backup-lookup logic that emits a Restore change.

### New private helper

```csharp
private bool VerifyByMetadata(string projectPath, string relPath, ProjectFile snapshotEntry, StringBuilder integrityLog)
{
    try
    {
        var info = new FileInfo(Path.Combine(projectPath, relPath));
        long currentSize = info.Length;
        string currentVersion = FileVersionInfo.GetVersionInfo(info.FullName).FileVersion ?? "";

        bool matches = currentSize == snapshotEntry.DataSize
                    && currentVersion == snapshotEntry.BuildVersion;

        integrityLog.AppendLine(matches
            ? $"Note: {relPath} verified by metadata only — hash unavailable"
            : $"Modified: {relPath} — metadata differs from snapshot (hash unavailable)");

        return matches;  // true = treat as Unchanged, false = treat as Modified
    }
    catch (Exception ex)
    {
        integrityLog.AppendLine($"Warning: {relPath} — metadata read also failed ({ex.GetType().Name}: {ex.Message}); excluding from integrity verdict");
        return true;  // err on the side of "Unchanged" to keep the checkout gate functional
    }
}
```

Lives in `FileManager` as `private` since both call sites are there. Takes the integrity log as a parameter so logging is thread-safe-by-construction (caller manages the log; for the parallel block, caller uses the existing `ConcurrentBag<string> hashFailureLog`).

For the parallel-block call site specifically, the helper needs a thread-safe log sink. Two approaches:
- Pass `ConcurrentBag<string>` instead of `StringBuilder` (two overloads, or one generic interface)
- Build the message string and return it alongside the bool, let the caller decide where to append

**Decision:** return `(bool match, string logMessage)` from the helper. Caller appends to the appropriate sink. Cleaner.

```csharp
private (bool match, string logMessage) VerifyByMetadata(string projectPath, string relPath, ProjectFile snapshotEntry)
```

## Logging behavior

| Outcome | Log line emitted | Visible to GUI/CLI |
|---|---|---|
| Hash succeeded, content unchanged | (none — silent success) | n/a |
| Hash succeeded, content modified | `"File <name> on <relpath> has been modified"` (existing) | ✓ via `IntegrityCheckEventHandler` |
| Hash failed, metadata matched | `"Note: <relpath> verified by metadata only — hash unavailable"` | ✓ |
| Hash failed, metadata differed | `"Modified: <relpath> — metadata differs from snapshot (hash unavailable)"` | ✓ |
| Hash + metadata both failed | `"Warning: <relpath> — metadata read also failed ...; excluding from integrity verdict"` | ✓ |

The log argument of `IntegrityCheckEventHandler` (already wired to both GUI's integrity-check window and CLI's TUI integrity view) carries all these. No new event signatures, no new wiring.

## Pre-checkout integrity gate implications

PR #24 added a gate that prompts the user when integrity check finds any `IntegrityChecked` changes. With this fallback:

- Files verified by metadata only → no `Modified` change record → no prompt → checkout proceeds normally.
- Files where metadata differs → `Modified` change record (with a slightly different log message) → existing dirty-prompt flow handles them.

The gate logic itself does not need changes. The new "metadata-only verified" log lines appear in the integrity report shown alongside the prompt (if any other changes also exist), so a careful user can spot a file they expected to be modified that came back as "verified by metadata."

## Testing strategy

All five tests land in `DeployAssistant.Tests/Utils/IntegrityCheckRobustnessTests.cs` (the first four are unit tests against the `VerifyByMetadata` helper, the fifth is a focused integration test against the full integrity flow):

1. **`Fallback_HashFails_MetadataMatches_NoModifiedChange`** — set up an intersect file whose MD5 fails (use `_hashTool` with a locked path), where size+BuildVersion in snapshot match the on-disk file. Assert no `Modified` change record for that file; assert log contains "verified by metadata only". Test uses `maxRetries = 0` to skip the retry delay.
2. **`Fallback_HashFails_SizeDiffers_ProducesModifiedChange`** — same setup but disk file has a different size than snapshot. Assert `Modified` change record present; assert log contains "metadata differs". `maxRetries = 0`.
3. **`Fallback_HashFails_VersionDiffers_ProducesModifiedChange`** — same setup but BuildVersion differs. Same assertion as #2. `maxRetries = 0`.
4. **`Fallback_HashAndMetadataBothFail_LoggedNotCrashed`** — both fail (delete the file between scan and verify). Assert no exception propagated; assert log records the double-failure. `maxRetries = 0`.
5. **`Retry_LockReleasedAfterTwoAttempts_HashEventuallySucceeds`** — start a background task that holds a file with `FileShare.None` for ~250 ms, then releases. Call `GetFileMD5CheckSum` with `maxRetries = 3, retryDelayMs = 100`. Assert that the returned hash is non-empty (one of the retries succeeded) AND that the call took at least 100 ms (proving retry actually engaged).
6. **`Retry_PersistentLock_AllAttemptsFail_ReturnsEmpty`** — hold the file for the entire duration of the call. With `maxRetries = 2, retryDelayMs = 50`, assert (a) returned hash is `""`, (b) elapsed time is at least 100 ms (= 2 × 50 ms — retries actually happened, not short-circuited).
7. **`Retry_MaxRetriesZero_SkipsRetryEntirely`** — fast-path test: lock the file, call with `maxRetries = 0`, assert hash is `""` and elapsed time is essentially zero (no sleep happened).
8. **`MainProjectIntegrityCheck_LockedFile_OnlyMetadataMatchesSnapshot_NoModifiedChange`** — integration test: actually hold a real file open with `FileShare.None`, run integrity check (using default `maxRetries = 3`), assert the file doesn't appear in `_preStagedFilesDict` as Modified.

Tests 1–4 are unit tests against `VerifyByMetadata`. Tests 5–7 are unit tests against `HashTool.GetFileMD5CheckSum` retry behavior. Test 8 is an integration test against the full integrity check flow.

## Risk assessment

| Risk | Mitigation |
|---|---|
| Same-size + same-version content swap (genuine modification) escapes detection | Documented as a known limitation. Probability is very low for typical workflows; users who care can manually re-check by re-running integrity once the file is unlocked. |
| `FileVersionInfo.GetVersionInfo` fails on non-PE files (returns version "0.0.0.0" or empty) | Already handled — DA's existing code uses `?? ""` on the version property. Snapshot also stores `""` for non-PE files. So same-empty-string matches and the comparison degrades gracefully to size-only for non-PE files. |
| Race condition between file scan and `FileInfo.Length` read | Already handled by the helper's outer try/catch. Vanishingly rare. |
| New log messages confuse users who don't read the log | The messages are self-explanatory and only appear when MD5 actually fails. In the common case (no failures), the log looks unchanged. |
| Performance impact of always trying metadata after hash failure | Negligible. `FileInfo.Length` is a single Win32 `GetFileAttributesEx` call (~µs). `FileVersionInfo.GetVersionInfo` is a `GetFileVersionInfoSize` + `GetFileVersionInfo` (~ms). Only invoked on the rare hash-failure path. |
| Retry adds latency to integrity scan | Worst case: `(locked_files_count / parallelism) × maxRetries × retryDelayMs`. With defaults (3 × 200 ms = 600 ms) on an 8-core box, 100 simultaneously-locked files add ~12 s to a scan. Acceptable trade-off — locked files are rare in steady state; the alternative is silently degraded confidence on every transient lock. |
| Retry could mask a permanently-locked file (e.g. left behind by a crashed installer) | After all retries exhaust, falls through to the metadata path — same as no-retry behavior. Logging mentions the retry count so user knows it wasn't a flash failure. |

## Open questions

None — all design questions resolved during brainstorming. Ready for implementation plan.

## References

- PR #29: `.ignore` git-style untrack — fixes scope filtering on both sides of diff
- PR #30: integrity-check resilience — per-file failures no longer abort scan, outer catch fires event
- Audit prompt: subagent report dated 2026-05-11, identifying partial-resilience verdict and 6 priority fixes
- Brainstorm conversation: 2026-05-11 (this session)
