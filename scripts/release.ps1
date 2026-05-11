#requires -Version 5.0
<#
.SYNOPSIS
  Package and upload a DeployAssistant release to the team network share.

.DESCRIPTION
  Publishes the WPF GUI (self-contained single-file) and/or the .NET
  Framework CLI as separate zips, and drops each into a versioned subfolder
  under either:

    개발 (dev, default)  — for testing, no announcement
    배포 (production)    — only with -Official, after the dev drop is verified

  Layout produced on Y:

    Y:\...\DeployAssistant\<개발|배포>\<GuiVersion>\
        DeployAssistant_v<GuiVersion>_<date>_<sha>.zip
        (extracted alongside if -Extract)

    Y:\...\DeployAssistant\<개발|배포>\cli\<CliVersion>\
        DeployAssistant-CLI_v<CliVersion>_<date>_<sha>.zip
        (extracted alongside if -Extract)

  GUI and CLI versions are independent — pass either, both, or neither
  flag depending on what's shipping.

.PARAMETER GuiVersion
  Semver string for the GUI (e.g. "3.7.0"). Omit to skip the GUI build.

.PARAMETER CliVersion
  Semver string for the CLI (e.g. "1.1.0"). Omit to skip the CLI build.

.PARAMETER Official
  Promote to the 배포 (production) channel. Without this switch, drops
  land in 개발 (dev) and are not considered an announced release.

.PARAMETER Extract
  Also extract each zip alongside it on Y:. Default is zip-only, matching
  most of the share's existing entries.

.PARAMETER IncludeFrameworkDependentGui
  Bundle the framework-dependent GUI flavor next to the single-file build
  (under a `framework-dependent\` subfolder inside the GUI zip). Default
  is single-file self-contained only.

.PARAMETER DryRun
  Stage and zip locally, but skip the Y: drive copy.

.PARAMETER SkipBuild
  Skip the `dotnet build` sanity check (still runs the per-project
  publishes). Use only if a clean Release build was just confirmed in
  this shell.

.PARAMETER Destination
  Override the Y: drive parent. Default is the team share. The
  channel subfolder (개발 or 배포) is appended automatically based on
  -Official.

.EXAMPLE
  # Dev drop, CLI only
  ./scripts/release.ps1 -CliVersion 1.1.0

.EXAMPLE
  # Official production release of both, independent versions
  ./scripts/release.ps1 -GuiVersion 3.7.0 -CliVersion 1.1.0 -Official

.EXAMPLE
  # GUI only, with extracted folder alongside the zip
  ./scripts/release.ps1 -GuiVersion 3.7.0 -Official -Extract

.NOTES
  Routine documented at: docs/release-process.md
  Skill form:            .claude/skills/release-deployassistant/SKILL.md
#>

[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$GuiVersion,

    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$CliVersion,

    [switch]$Official,
    [switch]$Extract,
    [switch]$IncludeFrameworkDependentGui,
    [switch]$DryRun,
    [switch]$SkipBuild,
    [string]$Destination = 'Y:\21 Dev(SW)\02_Applications\02_Utility\DeployAssistant'
)

$ErrorActionPreference = 'Stop'

if (-not $GuiVersion -and -not $CliVersion) {
    throw "Pass at least one of -GuiVersion or -CliVersion."
}

$repoRoot    = (Resolve-Path "$PSScriptRoot\..").Path
$publishRoot = Join-Path $repoRoot 'publish'

# ---------------------------------------------------------------- preflight

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet SDK not found on PATH. Install .NET 8 SDK from https://aka.ms/dotnet/download."
}

$gitSha = (& git -C $repoRoot rev-parse --short HEAD).Trim()
if (-not $gitSha) { throw "Could not determine git short SHA. Is this a git repo?" }

$date    = Get-Date -Format 'yyyyMMdd'
$channel = if ($Official) { '배포' } else { '개발' }

# Bail out early if any target version folder already exists.
# Versions are immutable — bump the patch and retry.
if (-not $DryRun) {
    if ($GuiVersion) {
        $guiTarget = Join-Path $Destination (Join-Path $channel $GuiVersion)
        if (Test-Path $guiTarget) {
            throw "GUI version folder already exists: $guiTarget`n`nVersions are immutable. Bump the patch version and retry."
        }
    }
    if ($CliVersion) {
        $cliTarget = Join-Path $Destination (Join-Path $channel (Join-Path 'cli' $CliVersion))
        if (Test-Path $cliTarget) {
            throw "CLI version folder already exists: $cliTarget`n`nVersions are immutable. Bump the patch version and retry."
        }
    }
}

# Kill any local processes that hold a file lock on the publish output
Get-Process -Name "DeployAssistant","deployassistant" -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue

# ---------------------------------------------------------------- build

if (-not $SkipBuild) {
    Write-Host "Building DeployAssistant.sln (Release)..." -ForegroundColor Cyan
    & dotnet build (Join-Path $repoRoot 'DeployAssistant.sln') -c Release --no-incremental
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }
}

$pubGuiSc = Join-Path $publishRoot 'DeployAssistant-sc'
$pubGuiFd = Join-Path $publishRoot 'DeployAssistant-fd'
$pubCli   = Join-Path $publishRoot 'DeployAssistant.CLI'

if ($GuiVersion) {
    foreach ($p in @($pubGuiSc, $pubGuiFd)) { if (Test-Path $p) { Remove-Item $p -Recurse -Force } }

    Write-Host "Publishing WPF GUI (self-contained, single-file, win-x64)..." -ForegroundColor Cyan
    & dotnet publish (Join-Path $repoRoot 'DeployAssistant\DeployAssistant.csproj') `
        -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -o $pubGuiSc
    if ($LASTEXITCODE -ne 0) { throw "GUI self-contained publish failed." }

    if ($IncludeFrameworkDependentGui) {
        Write-Host "Publishing WPF GUI (framework-dependent, win-x64)..." -ForegroundColor Cyan
        & dotnet publish (Join-Path $repoRoot 'DeployAssistant\DeployAssistant.csproj') `
            -c Release -r win-x64 --self-contained false `
            -p:PublishSingleFile=false `
            -o $pubGuiFd
        if ($LASTEXITCODE -ne 0) { throw "GUI framework-dependent publish failed." }
    }
}

if ($CliVersion) {
    if (Test-Path $pubCli) { Remove-Item $pubCli -Recurse -Force }

    Write-Host "Publishing CLI (net472 framework-dependent)..." -ForegroundColor Cyan
    & dotnet publish (Join-Path $repoRoot 'DeployAssistant.CLI\DeployAssistant.CLI.csproj') `
        -c Release `
        -o $pubCli
    if ($LASTEXITCODE -ne 0) { throw "CLI publish failed." }
}

# ---------------------------------------------------------------- stage + zip

function New-ReleaseZip {
    param(
        [Parameter(Mandatory)] [string]$Kind,           # 'GUI' or 'CLI'
        [Parameter(Mandatory)] [string]$SourceDir,      # publish folder to pull files from
        [Parameter(Mandatory)] [string]$Version,
        [Parameter(Mandatory)] [string]$ZipFileName,
        [hashtable]$ExtraDirs = @{}                     # rel-subdir -> sourceDir, optional extras
    )

    $stagingRoot = Join-Path $env:TEMP "da_release_$([guid]::NewGuid().ToString('N').Substring(0,8))"
    New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null

    # Copy main payload (non-pdb) into the staging root, preserving structure
    Get-ChildItem $SourceDir -Recurse -File -Exclude '*.pdb' | ForEach-Object {
        $rel = $_.FullName.Substring($SourceDir.Length).TrimStart('\','/')
        $target = Join-Path $stagingRoot $rel
        $targetDir = Split-Path $target -Parent
        if ($targetDir -and -not (Test-Path $targetDir)) {
            New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
        }
        Copy-Item $_.FullName -Destination $target -Force
    }

    foreach ($subdir in $ExtraDirs.Keys) {
        $extraSrc = $ExtraDirs[$subdir]
        $extraDst = Join-Path $stagingRoot $subdir
        New-Item -ItemType Directory -Path $extraDst -Force | Out-Null
        Get-ChildItem $extraSrc -File -Exclude '*.pdb' | Copy-Item -Destination $extraDst -Force
    }

    # README
    $readme = if ($Kind -eq 'GUI') {
@"
DeployAssistant GUI v$Version  ($date, commit $gitSha)

Double-click DeployAssistant.exe to launch.

Self-contained: no .NET runtime install required.

Documentation
-------------
Notion: https://www.notion.so/clevision/Deploy-Assistant-DA-Manual-6c6358be215d470580f7351d25e0ba01
GitHub: https://github.com/ildooPark/DeployAssistant
"@
    } else {
@"
DeployAssistant CLI v$Version  ($date, commit $gitSha)

From PowerShell or cmd:
    deployassistant.exe --help

Requires .NET Framework 4.7.2 (default on Windows 10 1803+ and Windows 11).

Pass --yes to auto-confirm all prompts for non-interactive / scripted use.

Documentation
-------------
Notion: https://www.notion.so/clevision/35d398fd58f2811aa2affb10dc6edd6f
GitHub: https://github.com/ildooPark/DeployAssistant
"@
    }
    $readme | Out-File -FilePath (Join-Path $stagingRoot 'README.txt') -Encoding utf8

    # Zip the staging root's contents directly (no extra DeployAssistant\ wrapper)
    $zipPath = Join-Path $env:TEMP $ZipFileName
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path "$stagingRoot\*" -DestinationPath $zipPath -CompressionLevel Optimal

    [PSCustomObject]@{
        Kind        = $Kind
        Version     = $Version
        ZipPath     = $zipPath
        StagingRoot = $stagingRoot
    }
}

$builtZips = @()

if ($GuiVersion) {
    $extras = @{}
    if ($IncludeFrameworkDependentGui) { $extras['framework-dependent'] = $pubGuiFd }
    $builtZips += New-ReleaseZip -Kind 'GUI' -SourceDir $pubGuiSc -Version $GuiVersion `
        -ZipFileName "DeployAssistant_v${GuiVersion}_${date}_${gitSha}.zip" `
        -ExtraDirs $extras
}

if ($CliVersion) {
    $builtZips += New-ReleaseZip -Kind 'CLI' -SourceDir $pubCli -Version $CliVersion `
        -ZipFileName "DeployAssistant-CLI_v${CliVersion}_${date}_${gitSha}.zip"
}

Write-Host ""
Write-Host "Channel:  $channel ($(if ($Official) { 'production' } else { 'dev' }))" -ForegroundColor $(if ($Official) { 'Yellow' } else { 'Gray' })
Write-Host "Sha:      $gitSha"
Write-Host ""
foreach ($z in $builtZips) {
    $size = (Get-Item $z.ZipPath).Length
    Write-Host ("Packaged: {0} v{1}  ({2:N0} bytes)" -f $z.Kind, $z.Version, $size) -ForegroundColor Green
    Write-Host ("          {0}" -f $z.ZipPath)
}

# ---------------------------------------------------------------- upload

if ($DryRun) {
    Write-Host ""
    Write-Host "[DryRun] Skipped Y: drive copy. Zips remain in `$env:TEMP." -ForegroundColor Yellow
    foreach ($z in $builtZips) {
        Remove-Item $z.StagingRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    return
}

if (-not (Test-Path $Destination)) {
    throw "Destination not reachable: $Destination. Connect to the network share and retry, or use -DryRun."
}

foreach ($z in $builtZips) {
    $versionDir = if ($z.Kind -eq 'GUI') {
        Join-Path $Destination (Join-Path $channel $z.Version)
    } else {
        Join-Path $Destination (Join-Path $channel (Join-Path 'cli' $z.Version))
    }
    New-Item -ItemType Directory -Path $versionDir -Force | Out-Null

    $zipName = Split-Path $z.ZipPath -Leaf
    Copy-Item $z.ZipPath -Destination $versionDir -Force
    Write-Host "Uploaded: $versionDir\$zipName" -ForegroundColor Green

    if ($Extract) {
        Expand-Archive -Path (Join-Path $versionDir $zipName) -DestinationPath $versionDir -Force
        Write-Host "Extracted into: $versionDir\" -ForegroundColor Green
    }

    Remove-Item $z.StagingRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "--- $channel\ contents (top 2 levels) ---" -ForegroundColor Cyan
$channelDir = Join-Path $Destination $channel
if (Test-Path $channelDir) {
    Get-ChildItem $channelDir -Recurse -Depth 1 | Sort-Object FullName | Format-Table FullName, LastWriteTime
}

Write-Host ""
Write-Host "Done. Next steps:" -ForegroundColor Green
if ($CliVersion) {
    $cliVer = Join-Path $Destination (Join-Path $channel (Join-Path 'cli' $CliVersion))
    Write-Host "  Smoke-test the CLI:"
    Write-Host "    Expand-Archive `"$cliVer\DeployAssistant-CLI_v${CliVersion}_${date}_${gitSha}.zip`" `"$env:TEMP\cli-smoke`" -Force"
    Write-Host "    & `"$env:TEMP\cli-smoke\deployassistant.exe`" --help"
}
Write-Host "  Update Notion release notes (append toggle under existing '### 🆙 Updates'):"
if ($GuiVersion) { Write-Host "    GUI v${GuiVersion}:  https://www.notion.so/clevision/Deploy-Assistant-DA-Manual-6c6358be215d470580f7351d25e0ba01" }
if ($CliVersion) { Write-Host "    CLI v${CliVersion}:  https://www.notion.so/clevision/35d398fd58f2811aa2affb10dc6edd6f" }
