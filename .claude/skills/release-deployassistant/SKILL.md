---
name: release-deployassistant
description: Use when the user asks to cut a release, ship a build, drop a build to Y:, publish release notes, or any phrasing involving "release vX.Y.Z" / "release X.Y.Z" / "make it official" / "promote to 배포" of DeployAssistant. Builds the WPF GUI (self-contained single-file) and the CLI as separate zips, uploads to the 개발 (dev) channel by default or 배포 (production) on -Official, and appends a short version toggle to the appropriate Notion page.
---

# Release DeployAssistant

This skill executes the formal release routine documented in `docs/release-process.md`. It produces, per component (GUI and CLI), a versioned zip + extracted folder on the team's Y: drive, and a short toggle in the component's Notion page.

## Two channels

| Channel | Trigger | Path on Y: |
|---|---|---|
| `개발` (dev) | Default — no flag | `Y:\21 Dev(SW)\02_Applications\02_Utility\DeployAssistant\개발\<gui\|cli>\v<X.Y.Z>_<YYYYMMDD>\` |
| `배포` (production) | `-Official` switch | `Y:\21 Dev(SW)\02_Applications\02_Utility\DeployAssistant\배포\<gui\|cli>\v<X.Y.Z>_<YYYYMMDD>\` |

**Never write to 배포 without the user explicitly saying "official release" / "make it official" / "promote to 배포" / similar.** Default is always 개발 for validation drops.

## When this fires

**For a dev drop (개발 channel — default):**
- "cut a release", "ship a release", "release vX.Y.Z"
- "drop a build to Y:", "drop the latest build", "package the release"
- "publish release notes", "write the release note for vX.Y.Z"

**For an official release (배포 channel — requires `-Official`):**
- "make it official", "make it an official release", "official release of vX.Y.Z"
- "promote to 배포", "promote v1.0.0 to production"
- "push v1.0.1 to 배포"

If the user says only "build", that does NOT fire this skill — they want a local build, not a release. Run `dotnet build DeployAssistant.sln -c Release` and stop.

## Required prep

Before the first action of this skill, confirm with the user:

1. **The version numbers** — GUI and CLI track independently. Ask the user explicitly: "GUI version?" and "CLI version?" Each is optional, but at least one is required. Check the share before proposing — `Get-ChildItem Y:\...\DeployAssistant\배포` for the latest GUI version, `Get-ChildItem Y:\...\DeployAssistant\배포\cli` for the latest CLI version. The two histories diverge (GUI was at 3.6.1, CLI started at 1.0.x — don't assume a single shared version applies to both).
2. **The channel** — dev (개발, default) or official (배포). If a previous dev drop with the same version already exists, suggest promoting it with `-Official` rather than rebuilding.
3. **That the working tree is clean and on `master`** (or about-to-merge).
4. **Network connectivity to Y: drive**.

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

Output must report **0 errors**. Warnings about obsolete V1 types (`ProjectFile`, `ProjectData`, `ChangedFile`, `ProjectMetaData`, `ProjectSimilarity`) are expected (tracked under issue #23) and do not block. Any **new** warning that is not about V1 obsolescence stops the release until investigated.

### 3. Run tests

```powershell
dotnet test DeployAssistant.Tests/DeployAssistant.Tests.csproj -c Release --no-build
```

All must pass. Capture the count (e.g. "285/285") for the release note.

### 4. Package + upload

**Dev drop (default — both components):**

```powershell
./scripts/release.ps1 -GuiVersion <X.Y.Z> -CliVersion <X.Y.Z>
```

**Official release (only after user said "make it official"):**

```powershell
./scripts/release.ps1 -GuiVersion <X.Y.Z> -CliVersion <X.Y.Z> -Official
```

**Single-component release (e.g. CLI-only patch):**

```powershell
./scripts/release.ps1 -CliVersion 1.1.1 -Official
```

The script publishes the GUI self-contained single-file and/or the CLI net472, packages each into its own zip, and drops them at:
- GUI: `Y:\...\DeployAssistant\<channel>\<GuiVersion>\` (e.g. `배포\3.7.0\`)
- CLI: `Y:\...\DeployAssistant\<channel>\cli\<CliVersion>\` (e.g. `배포\cli\1.1.0\`)

Re-running with the same version + channel fails (versions are immutable) — this is intentional.

Useful flags:
- `-DryRun` — skip the Y: drive copy (local zip only). Use for the very first run on a fresh checkout to validate the script before touching the share.
- `-Extract` — also extract each zip alongside it on Y:. Default is zip-only (matches the share's existing entries).
- `-IncludeFrameworkDependentGui` — bundle the framework-dependent GUI flavor inside a `framework-dependent\` subfolder of the GUI zip.
- `-SkipBuild` — skip the `dotnet build` sanity check (still runs the per-project publishes).

Capture the script's final output (zip paths, drop paths, sha) — you'll paste them into the Notion toggles.

### 5. Smoke-test the drop

```powershell
# CLI: extract the published zip and check --help
Expand-Archive "Y:\21 Dev(SW)\02_Applications\02_Utility\DeployAssistant\<channel>\cli\<CliVersion>\DeployAssistant-CLI_v<CliVersion>_*.zip" "$env:TEMP\cli-smoke" -Force
& "$env:TEMP\cli-smoke\deployassistant.exe" --help
```

Exit code must be 0 with "DeployAssistant" in the output. If the user can drive a GUI machine, ask them to extract the GUI zip from `<channel>\<GuiVersion>\` and double-click `DeployAssistant.exe` to confirm it launches.

### 6. Append the Notion release-note toggles

Two pages — append to each that ships in this release:

| Component | Page ID | URL |
|---|---|---|
| GUI | `6c6358be215d470580f7351d25e0ba01` | <https://www.notion.so/clevision/Deploy-Assistant-DA-Manual-6c6358be215d470580f7351d25e0ba01> |
| CLI | `35d398fd58f2811aa2affb10dc6edd6f` | <https://www.notion.so/clevision/35d398fd58f2811aa2affb10dc6edd6f> |

Use `notion-update-page` with `command: update_content`. **Do NOT use `replace_content`** — it would wipe prior release notes.

**Find this exact line in the existing page content:**

```
### 🆙 Updates 
```

…and **append the new toggle right below** the section header (newest at the top of that section's children). The page uses heading-level toggles via the `{toggle="true"}` suffix — body is tab-indented under the heading.

### Release-note template (short and concise — aim for 3–6 bullets)

```markdown
### v<X.Y.Z> — <YYYY-MM-DD> {toggle="true"}
	- <user-visible highlight 1 — one line>
	- <user-visible highlight 2 — one line>
	- <user-visible highlight 3 — one line>
	**Build:** `<shortSha>` · **Drop:** `Y:\21 Dev(SW)\02_Applications\02_Utility\DeployAssistant\<배포|개발>\<X.Y.Z>\` (GUI) or `Y:\...\<배포|개발>\cli\<X.Y.Z>\` (CLI)
```

**Notion gotchas (observed during v3.7.0/v1.1.0 ship):**
- Italic placeholder text (`*foo*`) inside a page is stored as a structured block, not a flat string — `update_content` may fail to match it via plain text search. Target nearby plain text instead, or use `replace_content` for whole-page rewrites.
- Bold-around-inline-code (` **`.ignore`** `) renders correctly in Notion's UI but `update_content` echoes it back as `**`.ignore`****` — looks broken on re-fetch and on subsequent edits. Two ways out: avoid the pattern (drop the bold around inline-code spans), or apply a follow-up `update_content` that replaces the mangled string with the clean form.
- Newly created pages may inherit a child database from the data source's `default_page_template`. If you see a `validation_error` about "would delete N child page(s)/database(s)" on an unrelated update, that's the cause — pass `allow_deleting_content: true` if the inherited content is unwanted (verify what would be deleted first).

- The GUI page lists GUI-facing highlights only.
- The CLI page lists CLI-facing highlights only.
- Refactors that touch both (e.g. a Core change) get one brief line on each page.
- If something needs more than one line of explanation, link to the PR — don't bloat the toggle.

### 7. Report back

Print to the user:

- Channel (개발 / 배포)
- Version + sha
- Test count (passed / total)
- Drop locations on Y: (one line per component shipped)
- Notion page URL(s) updated
- Zip sizes

Don't go further. The GitHub Actions `release.yml` workflow already publishes the same artifacts to a GitHub Release on every master push, so no separate GitHub Release step is needed unless the user explicitly asks for a tag.

## Stopping conditions

Stop and ask the user before continuing if any of:

- Build emits a **new** warning (not the V1-obsolescence ones tracked in issue #23)
- Any test fails
- Y: drive is unreachable
- The target version folder already exists in the requested channel (prior drop with the same version in the same channel — bump patch or move aside)
- The Notion page returns an error or its `### 🆙 Updates` section can't be located
- Working tree has uncommitted changes that look like real work (not just `publish*/`, `.serena/project.yml`, or other known-untracked artifacts)
- The user has asked for `-Official` but the corresponding dev drop wasn't smoke-tested yet — confirm before promoting

## What this skill does NOT do

- Bump version numbers automatically — always ask the user.
- Promote to 배포 implicitly — the `-Official` switch must be set explicitly, after the user says so.
- Push git tags or create GitHub releases (the `release.yml` workflow does the GitHub Release automatically on master push).
- Deploy to anywhere except the Y: drive paths defined above.
- Modify the source tree (the routine is read-only against the repo — `scripts/release.ps1` only writes to `publish/`, `$env:TEMP`, and Y:).
- Write to anywhere on Notion except the two pages listed above, and only by appending under their existing `### 🆙 Updates` sections.
