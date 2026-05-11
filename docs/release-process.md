# DeployAssistant Release Process

This document describes the manual release routine for DeployAssistant — how to cut a versioned build, drop it on the team's network share, and append a short release-note toggle in Notion. The same routine is encoded as a skill at `.claude/skills/release-deployassistant/SKILL.md` for AI-assisted execution.

> **Trigger phrases that invoke this routine:**
> - **Dev drop (default):** "cut a release", "ship a release", "drop a build to Y:", "release vX.Y.Z", "publish release notes"
> - **Official release (배포 channel):** "make it official", "official release", "promote to 배포", "make it an official release"

---

## Two channels: 개발 vs 배포

| Channel | Trigger | Purpose |
|---|---|---|
| `개발` (dev) | Default — no flag | Testing drops. Built every time the routine runs without `-Official`. Team members can pull from here to validate before announcement. |
| `배포` (production) | `./scripts/release.ps1 ... -Official` | Announced release. Only triggered when the operator explicitly says "make it official." Drops here are linked from the Notion release notes. |

The script never writes to `배포` without the explicit `-Official` switch.

---

## Outputs

A release produces, **per component**, in order:

1. **A versioned `.zip`** dropped at:
   - GUI: `Y:\21 Dev(SW)\02_Applications\02_Utility\DeployAssistant\<channel>\<GuiVersion>\` — matches the historical `3.4 / 3.5 / 3.6 / 3.6.1` layout (GUI sits directly under the channel folder; no `gui/` subfolder).
   - CLI: `Y:\21 Dev(SW)\02_Applications\02_Utility\DeployAssistant\<channel>\cli\<CliVersion>\` — under the `cli/` subfolder that already exists on the share.
2. **Extraction is opt-in** via `-Extract`. Default is zip-only, which matches the share's existing entries.
3. **A short release-note toggle** appended to the appropriate Notion page (see "Notion pages" below).

GUI and CLI versions track independently (e.g. GUI v3.7.0 ships alongside CLI v1.1.0). Pass either, both, or neither flag depending on what's shipping.

The GitHub Actions `release.yml` workflow already publishes source + binary zips to a GitHub Release on every master push; this routine is the **internal-distribution** layer that drops the same payload on the team share and writes the human note.

---

## File manifest (what goes in each zip)

### GUI zip — `DeployAssistant_v<X.Y.Z>_<YYYYMMDD>_<sha>.zip`

Contents (files land at the zip root — extract one folder deep):

```
DeployAssistant.exe              # self-contained single-file (no runtime install needed)
framework-dependent\             # only with -IncludeFrameworkDependentGui
    DeployAssistant.exe
    <DLLs>
README.txt
```

- **Runtime requirement on the target:** none for the single-file build (bundled .NET 8 Desktop Runtime). The optional framework-dependent flavor needs the .NET 8 Desktop Runtime installed.
- Source: `dotnet publish DeployAssistant\DeployAssistant.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`

### CLI zip — `DeployAssistant-CLI_v<X.Y.Z>_<YYYYMMDD>_<sha>.zip`

```
deployassistant.exe
DeployAssistant.Core.dll
Spectre.Console.dll
<other framework-dependent DLLs>
README.txt
```

- **Runtime requirement on the target:** .NET Framework 4.7.2 (shipped with Windows 10 1803+ and all Windows 11 — zero-config on any current corporate workstation).
- Source: `dotnet publish DeployAssistant.CLI\DeployAssistant.CLI.csproj -c Release`

`.pdb` files are intentionally excluded from both — strip the noise from the user-facing drop.

---

## Notion pages

Two pages — one per component. Each has an existing `### 🆙 Updates` section. **Do not create a new section** — append a new heading-level toggle inside the existing `🆙 Updates` section, newest first.

| Component | Page title | Page ID | URL |
|---|---|---|---|
| GUI | Deploy Assistant(DA) Manual | `6c6358be215d470580f7351d25e0ba01` | <https://www.notion.so/clevision/Deploy-Assistant-DA-Manual-6c6358be215d470580f7351d25e0ba01> |
| CLI | Deploy Assistant CLI(DA-CLI) Manual | `35d398fd58f2811aa2affb10dc6edd6f` | <https://www.notion.so/clevision/35d398fd58f2811aa2affb10dc6edd6f> |

### Release-note format — short, concise, one toggle per version

Append directly under the existing `### 🆙 Updates` heading. Match the page's existing convention: heading-level toggle (`{toggle="true"}` suffix), tab-indented body.

```markdown
### v<X.Y.Z> — <YYYY-MM-DD> {toggle="true"}
	- <one-line highlight 1>
	- <one-line highlight 2>
	- <one-line highlight 3>
	**Build:** `<shortSha>` · **Drop:** `Y:\...\배포\<X.Y.Z>\` (GUI) or `Y:\...\배포\cli\<X.Y.Z>\` (CLI)
```

**Watch for `**`/inline-code collisions** — `**`.ignore` parity**` (bold with inline code inside) gets rendered as `**`.ignore`**** parity**` by Notion's markdown processor and looks broken. Either drop the bold around inline-code spans or fix the rendering with a follow-up replacement after the first append.

Aim for **3–6 bullets max**. If something needs more explanation, link out to the GitHub PR; don't bloat the toggle. The toggle is collapsed by default — users scan the version list, click only what they care about.

The GUI page lists GUI-facing highlights only; the CLI page lists CLI-facing highlights only. Refactors that touch both → mentioned briefly on both pages, one line each.

---

## Versioning convention

GUI and CLI track independent semver — they ship from the same commit and the same `DeployAssistant.Core`, but their release histories diverged before the formal-release routine existed (GUI was at v3.6.1 on the share; CLI was just starting at v1.0.x in `개발\cli\`).

Semver: `MAJOR.MINOR.PATCH`. Folder names on the share use the bare version (e.g. `3.7.0`, `1.1.0`), no `v` prefix.

- **MAJOR** — breaking changes to persisted file formats (`ProjectMetaData.bin`, `DeployAssistant.ignore`, `DeployAssistant.deploy`), CLI flag renames/removals, or .NET target-framework changes.
- **MINOR** — additive features (new TUI flows, new commands, new manager events, new GUI windows).
- **PATCH** — bugfixes, doc-only changes, internal refactors that don't change user-visible behavior.

Folder name: `v<X.Y.Z>_<YYYYMMDD>` — sortable by both version and date.
Zip filename: `DeployAssistant-<GUI|CLI>_v<X.Y.Z>_<YYYYMMDD>_<shortSha>.zip`.

GUI and CLI share the same semver — they ship from the same commit and the same `DeployAssistant.Core`. A version bump applies to both, even if only one side has user-visible changes.

The first formal release on this drive is **v1.0.0** (target date TBD).

---

## Prerequisites

- Git working tree clean, on `master` (or a branch about to be merged).
- .NET 8 SDK on PATH (`dotnet --list-sdks` shows `8.x`).
- .NET Framework 4.8 Developer Pack installed (for CLI's net472 target — see `CLAUDE.md`).
- Network access to `Y:\21 Dev(SW)\02_Applications\02_Utility\DeployAssistant\`.
- Notion MCP available (for the release-note step).
- No `DeployAssistant.exe` or `deployassistant.exe` processes running locally (they hold file locks on the build output — the script also force-kills them defensively).

---

## Step-by-step

### 1. Sync and verify

```powershell
git checkout master
git pull --ff-only
```

`git status` should show nothing beyond the well-known untracked items (`publish*/`, `.serena/project.yml`).

### 2. Clean rebuild

```powershell
dotnet build DeployAssistant.sln -c Release --no-incremental
```

Tail output must report **0 errors**. Warnings about obsolete V1 types (`ProjectFile`, `ProjectData`, `ChangedFile`, `ProjectMetaData`, `ProjectSimilarity`) are expected — they're tracked under issue #23 and do not block a release. Any **new** warning that is not about V1 obsolescence stops the release until investigated.

### 3. Run tests

```powershell
dotnet test DeployAssistant.Tests/DeployAssistant.Tests.csproj -c Release --no-build
```

All must pass. Capture the count for the release note.

### 4. Determine the version

Look at the diff against the previous release tag (or the previous `v*` folder on Y: drive `배포\`). Pick MAJOR / MINOR / PATCH per the rules above. State the chosen version explicitly before packaging.

```powershell
git log --oneline <previous-release-sha>..HEAD
```

### 5. Dev drop (개발) — first

GUI and CLI track independent semver. Pass either or both. At least one is required.

```powershell
# Both components together:
./scripts/release.ps1 -GuiVersion <X.Y.Z> -CliVersion <X.Y.Z>

# Just one side:
./scripts/release.ps1 -CliVersion <X.Y.Z>
```

This builds, packages, and drops into `Y:\...\DeployAssistant\개발\<GuiVersion>\` (GUI sits directly under the channel folder — matches the historical `3.4`, `3.5`, `3.6.1` layout) and `Y:\...\DeployAssistant\개발\cli\<CliVersion>\`. **No `-Official` flag**. The drop is for validation only — no announcement.

Other useful flags:
- `-DryRun` — skip the Y: copy entirely (local zip only).
- `-Extract` — also extract each zip alongside it on Y:. Default is zip-only (matches the historical share entries).
- `-IncludeFrameworkDependentGui` — bundle the framework-dependent GUI flavor inside a `framework-dependent\` subfolder of the GUI zip.
- `-SkipBuild` — skip the `dotnet build` sanity check.

### 6. Smoke-test the dev drop

```powershell
# CLI: extract the zip locally and check --help (mirrors cli-smoke-test.yml in CI)
Expand-Archive "Y:\21 Dev(SW)\02_Applications\02_Utility\DeployAssistant\개발\cli\<CliVersion>\DeployAssistant-CLI_v<CliVersion>_*.zip" "$env:TEMP\cli-smoke" -Force
& "$env:TEMP\cli-smoke\deployassistant.exe" --help

# GUI: extract the zip and double-click DeployAssistant.exe
```

Exit code 0 from `--help` with "DeployAssistant" in output. GUI launches without missing-runtime errors.

If anything looks wrong, **stop**. Don't promote a broken drop. Fix in the repo, bump the patch, re-run step 5.

### 7. Official release (배포) — only when validated

```powershell
./scripts/release.ps1 -GuiVersion <X.Y.Z> -CliVersion <X.Y.Z> -Official
```

Same outputs, but written to `배포\<GuiVersion>\` and `배포\cli\<CliVersion>\`. The 배포 channel is the announced production channel — once a version is there, it's considered shipped.

### 8. Verify the drop

```powershell
Get-ChildItem "Y:\21 Dev(SW)\02_Applications\02_Utility\DeployAssistant\배포" -Recurse -Depth 1 |
    Sort-Object FullName | Format-Table FullName, LastWriteTime
```

### 9. Append release-note toggles in Notion

Two pages, both end with a `### 🆙 Updates` section.

For each component shipped, append (do **not** replace prior content):

```markdown
### v<X.Y.Z> — <YYYY-MM-DD> {toggle="true"}
	- <highlight 1>
	- <highlight 2>
	- <highlight 3>
	**Build:** `<shortSha>` · **Drop:** `Y:\...\배포\<gui|cli>\v<X.Y.Z>_<YYYYMMDD>\`
```

Newest version at the top of the section's children. 3–6 bullets max. The full skill template is at `.claude/skills/release-deployassistant/SKILL.md`.

---

## Rollback

If a 배포 drop turns out broken:

1. Move the bad version folder to a `_recalled\` sibling under `배포\<gui|cli>\` (don't delete — keep for forensics).
2. Bump the patch version (e.g. `v1.0.0` → `v1.0.1`) and run the routine again from step 1.
3. Edit the Notion toggle for the broken version — prefix the version title with `(RECALLED)` and add a one-line reason at the top of its body.

Never overwrite an existing version folder. Versions are immutable in both 개발 and 배포.

---

## See also

- `.claude/skills/release-deployassistant/SKILL.md` — same routine in skill form for AI-assisted execution.
- `scripts/release.ps1` — the packaging + upload script invoked in steps 5 and 7.
- `.github/workflows/release.yml` — the CI workflow that auto-publishes source + binary zips to GitHub Releases on every master push.
- `.github/workflows/cli-smoke-test.yml` — the CI workflow that validates the CLI publish on every PR.
- `CLAUDE.md` — repo overview, build commands, "when work is done" checklist.
