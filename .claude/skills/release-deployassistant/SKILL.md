---
name: release-deployassistant
description: Use when the user asks to cut a release, ship a build, drop a build to Y:, publish release notes, or any phrasing involving "release vX.Y.Z" / "release X.Y.Z" of DeployAssistant. Builds the WPF GUI (self-contained single-file) and the CLI, packages them, uploads to the team network share, and writes a release note in Notion.
---

# Release DeployAssistant

This skill executes the formal release routine documented in `docs/release-process.md`. It produces three artifacts: a versioned zip, an extracted folder on the team's Y: drive, and a release note appended to a fixed Notion page.

## When this fires

- "cut a release", "ship a release", "release vX.Y.Z"
- "drop a build to Y:", "drop the latest build", "package the release"
- "publish release notes", "write the release note for vX.Y.Z"
- "push v1.0.1 to the team share"

If the user says only "build", that does NOT fire this skill — they want a local build, not a release. Run `dotnet build DeployAssistant.sln -c Release` and stop.

## Required prep

Before the first action of this skill, confirm with the user:

1. **The version number** (semver MAJOR.MINOR.PATCH). If they didn't say, propose one based on the diff vs the previous release on Y: and ask them to confirm. Don't proceed without an explicit version.
2. **That the working tree is clean and on `master`** (or about-to-merge).
3. **Network connectivity to Y: drive**.

If any of these fail, stop and report back — don't half-execute.

## Steps

### 1. Sync, kill stale processes

```powershell
git checkout master
git pull --ff-only
Get-Process -Name "DeployAssistant","deployassistant" -ErrorAction SilentlyContinue | Stop-Process -Force
```

### 2. Clean rebuild

```powershell
dotnet build DeployAssistant.sln -c Release --no-incremental
```

Output must report **0 errors**. Warnings about obsolete V1 types (`ProjectFile`, `ProjectData`, `ChangedFile`, `ProjectMetaData`, `ProjectSimilarity`) are expected and tracked under issue #23 — they do not block a release. Any **new** warning that is not about V1 obsolescence stops the release until investigated.

### 3. Run tests

```powershell
dotnet test DeployAssistant.Tests/DeployAssistant.Tests.csproj -c Release --no-build
```

All must pass. Capture the count (e.g. "285/285") for the release note.

### 4. Package + upload

```powershell
./scripts/release.ps1 -Version <X.Y.Z>
```

The script handles: publishing the GUI self-contained single-file, publishing the CLI net472, assembling the staging layout (`DeployAssistant\GUI\` + `DeployAssistant\CLI\` + `README.txt`), zipping, and copying to the Y: drive in a versioned subfolder. Re-running with the same version fails (versions are immutable) — this is intentional.

Optional flags:
- `-DryRun` — skip the Y: drive copy (local zip only). Use this to validate before the first real release.
- `-NoExtract` — zip-only drop, no extracted folder on the share.
- `-IncludeFrameworkDependentGui` — also stage the framework-dependent GUI flavor under `DeployAssistant\GUI\framework-dependent\`. Default is single-file self-contained only.
- `-SkipBuild` — skip the `dotnet build` sanity check (still runs the per-project publishes). Use only if you've already confirmed a clean build in this same shell.

Capture the script's final output (zip path + sha + versioned folder path) — you'll paste it into the Notion release note.

### 5. Verify the drop

```powershell
& "Y:\21 Dev(SW)\02_Applications\02_Utility\DeployAssistant\v<X.Y.Z>_<YYYYMMDD>\DeployAssistant\CLI\deployassistant.exe" --help
```

Exit code must be 0 with "DeployAssistant" in the output (mirrors the `cli-smoke-test.yml` CI assertion).

### 6. Write the Notion release note

**Target page:** `<TODO: page ID — confirm with team or create the page before the first production release>`

Use `notion-update-page` with `command: update_content` to **append** a new section at the end of the page. Do NOT use `replace_content` — that would wipe prior release notes. Find the last `---` divider, then add new content beneath it.

If the page is currently blank (first release ever, like v1.0.0), use `replace_content` to fill it from scratch.

### Release-note template

```markdown
# DeployAssistant v<X.Y.Z> Release Notes

**Release date:** <YYYY-MM-DD>
**Build:** `v<X.Y.Z>_<YYYYMMDD>_<shortSha>`
**Drop location:** `Y:\21 Dev(SW)\02_Applications\02_Utility\DeployAssistant\v<X.Y.Z>_<YYYYMMDD>\`
**Source:** [GitHub: ildooPark/DeployAssistant](https://github.com/ildooPark/DeployAssistant) — master @ `<shortSha>`

---

## What's in the box

| Component | Path inside the drop | Size | Purpose |
|---|---|---|---|
| WPF GUI (self-contained) | `DeployAssistant\GUI\DeployAssistant.exe` | <MB> | Double-click to launch. No .NET runtime install required. |
| CLI (net472 fwk-dep) | `DeployAssistant\CLI\deployassistant.exe` + DLLs | <MB> | Scripting / non-interactive use. Requires .NET Framework 4.7.2 (default on Win10 1803+ / Win11). |
| README | `DeployAssistant\README.txt` | <KB> | One-page how-to for end users. |

**Runtime requirements:**
- GUI — none (bundled .NET 8 Desktop Runtime).
- CLI — .NET Framework 4.7.2 (ships with current Windows).

---

## Highlights

<bullets — user-visible changes since the previous release>

---

## Quick start

**GUI:**
1. Browse to `Y:\...\DeployAssistant\v<X.Y.Z>_<YYYYMMDD>\DeployAssistant\GUI\`
2. Double-click `DeployAssistant.exe`
3. (First run) Configure your destination project path

**CLI:**
```powershell
& "Y:\...\DeployAssistant\v<X.Y.Z>_<YYYYMMDD>\DeployAssistant\CLI\deployassistant.exe" --help
```

---

## Known issues / out of scope

<bullets — leave blank if none>

---

## Verification

```powershell
git checkout <shortSha>
dotnet build DeployAssistant.sln -c Release
dotnet test  DeployAssistant.Tests/DeployAssistant.Tests.csproj -c Release --no-build
# Expected: 0 errors, <N>/<N> tests pass
```

---

## Changelog

<list of merged PRs since the prior release with their numbers and titles. Use `gh pr list --state merged --base master --limit 30 --json number,title,mergedAt` to fetch.>
```

### 7. Report back

Print to the user:

- Drop location on Y:
- Notion page URL
- Build SHA
- Test count (passed / total)
- Zip size

Don't go further. The GitHub Actions `release.yml` workflow already publishes the same artifacts to a GitHub Release on every master push, so no separate GitHub Release step is needed unless the user explicitly asks for a tag.

## Stopping conditions

Stop and ask the user before continuing if any of:

- Build emits a **new** warning (not the V1-obsolescence ones tracked in issue #23)
- Any test fails
- Y: drive is unreachable
- The target version folder already exists (prior release with the same version)
- The Notion page returns an error
- Working tree has uncommitted changes that look like real work (not just `publish*/`, `.serena/project.yml`, or other known-untracked artifacts)

## What this skill does NOT do

- Bump version numbers automatically — always ask the user.
- Push git tags or create GitHub releases (the `release.yml` workflow does the GitHub Release automatically on master push).
- Deploy to anywhere except Y: drive.
- Modify the source tree (the routine is read-only against the repo — `scripts/release.ps1` only writes to `publish/`, `$env:TEMP`, and Y:).
- Send notifications anywhere except the Notion page.
