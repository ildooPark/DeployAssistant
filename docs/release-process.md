# DeployAssistant Release Process

This document describes the manual release routine for DeployAssistant — how to cut a versioned build, drop it on the team's network share, and publish a release note in Notion. The same routine is encoded as a skill at `.claude/skills/release-deployassistant/SKILL.md` for AI-assisted execution.

> **Trigger phrases that invoke this routine:** "cut a release", "ship a release", "drop a build to Y:", "publish release notes", "release vX.Y.Z".

---

## Outputs

A release produces three things, in order:

1. **A versioned `.zip`** containing the WPF GUI (self-contained single-file) and the .NET Framework CLI, dropped at:
   `Y:\21 Dev(SW)\02_Applications\02_Utility\DeployAssistant\v<X.Y.Z>_<YYYYMMDD>\`
2. **The same files extracted next to the zip** (`DeployAssistant\` subfolder) so users can run the GUI by double-clicking or use the CLI from PowerShell without unzipping.
3. **A release note appended to the Notion page** for DeployAssistant. (TODO: page ID — see "Notion page" section below.)

The GitHub Actions `release.yml` workflow already publishes source + framework-dependent GUI + self-contained GUI + CLI zips to a GitHub Release on every master push; this routine is the **internal-distribution** layer that drops the same payload on the team share and writes a human release note.

---

## File manifest (what goes in the zip)

The zip contains a single top-level `DeployAssistant\` folder with this layout:

```
DeployAssistant\
├── GUI\
│   └── DeployAssistant.exe          # self-contained single-file (no runtime needed on target)
├── CLI\
│   ├── deployassistant.exe
│   ├── DeployAssistant.Core.dll
│   ├── Spectre.Console.dll
│   └── <other framework-dependent DLLs>
└── README.txt                        # one-page how-to for end users
```

| Component | Source path after `dotnet publish` |
|---|---|
| GUI (self-contained single-file) | `publish\DeployAssistant-sc\DeployAssistant.exe` |
| CLI (net472 framework-dependent, multi-file) | `publish\DeployAssistant.CLI\` (entire folder) |

`.pdb` files are intentionally excluded — strip the noise from the user-facing drop.

**Runtime requirements on the recipient:**
- **GUI** — none. Self-contained single-file bundles the .NET 8 Desktop Runtime.
- **CLI** — .NET Framework 4.7.2 (shipped with Windows 10 1803+ and all Windows 11 builds, so this is effectively zero-config on any current corporate workstation).

---

## Versioning convention

Semver: `vMAJOR.MINOR.PATCH`.

- **MAJOR** — breaking changes to persisted-file formats (`ProjectMetaData.bin`, `DeployAssistant.ignore`, `DeployAssistant.deploy`), CLI flag renames/removals, or .NET target-framework changes.
- **MINOR** — additive features (new TUI flows, new commands, new manager events, new GUI windows).
- **PATCH** — bugfixes, doc-only changes, internal refactors that don't change user-visible behavior.

Folder name on Y: drive: `v<X.Y.Z>_<YYYYMMDD>` — sortable both by version and date.
Zip filename: `DeployAssistant_v<X.Y.Z>_<YYYYMMDD>_<shortSha>.zip`.

The first formal release on this drive is **v1.0.0** (target date TBD).

---

## Prerequisites

- Git working tree clean, on `master` (or a branch about to be merged).
- .NET 8 SDK on PATH (`dotnet --list-sdks` shows `8.x`).
- .NET Framework 4.8 Developer Pack installed (for CLI's net472 target — see `CLAUDE.md`).
- Network access to `Y:\21 Dev(SW)\02_Applications\02_Utility\`.
- Notion MCP available (for the release-note step).
- No `DeployAssistant.exe` or `deployassistant.exe` processes running locally (they hold file locks on the build output).

---

## Step-by-step

### 1. Sync and verify

```powershell
git checkout master
git pull --ff-only
```

Verify the working tree is clean: `git status` shows nothing other than the well-known untracked items (`publish*/`, `.serena/project.yml`).

### 2. Kill stale processes (lock-free build)

```powershell
Get-Process -Name "DeployAssistant","deployassistant" -ErrorAction SilentlyContinue | Stop-Process -Force
```

### 3. Clean rebuild

```powershell
dotnet build DeployAssistant.sln -c Release --no-incremental
```

Verify the tail output reports **0 errors**. Warnings about obsolete V1 types (`ProjectFile`, `ProjectData`, `ChangedFile`) are expected and tracked under issue #23 — they do not block a release. Any **new** warning that isn't about V1 obsolescence stops the release until investigated.

### 4. Run tests (sanity check)

```powershell
dotnet test DeployAssistant.Tests/DeployAssistant.Tests.csproj -c Release --no-build
```

All tests must pass. Capture the count for the release note.

### 5. Determine the version

Look at the diff against the previous release tag (or the previous `v*` folder on Y: drive). Pick MAJOR / MINOR / PATCH per the rules above. State the chosen version explicitly before packaging — don't guess.

```powershell
# Inspect what's changed since the previous release folder
git log --oneline (previous-tag-or-sha)..HEAD
```

### 6. Package and upload

The packaged routine lives at `scripts/release.ps1` — it publishes the GUI (self-contained) and CLI (framework-dependent), assembles the file layout, zips it, drops it on Y: drive in a versioned subfolder, and also extracts the zip alongside for users who want to run from the share directly.

```powershell
./scripts/release.ps1 -Version 1.0.0
```

Optional flags:
- `-DryRun` — stage and zip locally, skip the Y: drive copy.
- `-NoExtract` — skip the unzipped folder on Y: drive (zip-only drop).
- `-IncludeFrameworkDependentGui` — also stage the framework-dependent GUI flavor (smaller download, requires .NET 8 Desktop Runtime on target). Default is self-contained only.

### 7. Verify the drop

```powershell
Get-ChildItem "Y:\21 Dev(SW)\02_Applications\02_Utility\DeployAssistant\" |
    Sort-Object Name | Format-Table Name, LastWriteTime
```

The new `v<X.Y.Z>_<YYYYMMDD>\` folder should be present, sorted alongside any prior releases.

Smoke-test on the share:

```powershell
& "Y:\21 Dev(SW)\02_Applications\02_Utility\DeployAssistant\v<X.Y.Z>_<YYYYMMDD>\DeployAssistant\CLI\deployassistant.exe" --help
```

Exit code must be 0 with "DeployAssistant" in the output. (This mirrors the `cli-smoke-test.yml` CI assertion.)

### 8. Write the release note in Notion

Append a new section to the DeployAssistant release-notes Notion page (do **not** replace prior release notes). Required sections:

- **Header** — version, build date, build sha, drop location, GitHub link
- **What's in the box** — file manifest with sizes
- **Highlights** — bullet list of user-visible changes since the last release
- **Quick start** — minimal usage instructions for both GUI and CLI
- **Known issues / out of scope**
- **Verification** — the `git checkout` + build commands the recipient can run to reproduce
- **Changelog** — list of merged PRs since the prior release with their numbers and titles

Use Notion-flavored Markdown (tables, headings, bullets, links). The release-note skill at `.claude/skills/release-deployassistant/SKILL.md` has the exact template.

### 9. (Optional) Mirror to GitHub Release

The CI `release.yml` workflow auto-creates a GitHub Release on every master push with all four zips already attached. If a specific commit is being formalised as a versioned release (rather than the rolling auto-release), tag it manually:

```powershell
git tag v<X.Y.Z>
git push origin v<X.Y.Z>
```

The CI workflow does not re-run on tag pushes (it's pinned to `push: branches: master`), so the GitHub Release artifacts for the matching commit remain the canonical record.

---

## Rollback

If a drop turns out broken:

1. Move the bad version folder out of `Y:\...\DeployAssistant\` (don't delete — keep for forensics). A `_recalled\` sibling folder works well.
2. Bump the patch version (e.g. `v1.0.0` → `v1.0.1`) and run the routine again from step 1.
3. Edit the Notion page to add a "RECALLED" note above the broken release section, with a one-line reason.

Never overwrite an existing version folder. Versions are immutable.

---

## Notion page

The release-note Notion page lives at: **`<TODO: confirm or create page; insert page ID here>`**

Until the page is created, run the release routine with `-DryRun` first to validate the local zip, then create the page and update both this doc and the skill with the real ID before the first production release.

---

## See also

- `.claude/skills/release-deployassistant/SKILL.md` — same routine in skill form for AI-assisted execution.
- `scripts/release.ps1` — the packaging + upload script invoked in step 6.
- `.github/workflows/release.yml` — the CI workflow that auto-publishes source + binary zips to GitHub Releases on every master push.
- `.github/workflows/cli-smoke-test.yml` — the CI workflow that validates the CLI publish on every PR.
- `CLAUDE.md` — repo overview, build commands, "when work is done" checklist.
