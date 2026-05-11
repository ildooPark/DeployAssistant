#requires -Version 5.0
<#
.SYNOPSIS
  Package and upload a DeployAssistant release to the team network share.

.DESCRIPTION
  Publishes the WPF GUI (self-contained single-file) and the .NET Framework
  CLI, stages them into a versioned zip, drops it on Y:\ along with an
  extracted copy, and prints the paths so the operator can paste them into
  the Notion release note.

  Run from the repo root or anywhere; the script resolves the repo root
  relative to its own location.

.PARAMETER Version
  Semver string without the 'v' prefix, e.g. "1.0.0".

.PARAMETER DryRun
  Stage and zip locally, but skip the Y: drive copy.

.PARAMETER NoExtract
  Skip extracting the zip alongside it on Y: drive (zip-only drop).

.PARAMETER IncludeFrameworkDependentGui
  Also stage the framework-dependent GUI flavor (smaller download, requires
  .NET 8 Desktop Runtime on the target machine). Default is self-contained
  single-file only.

.PARAMETER Destination
  Override the Y: drive location. Default is the team share.

.PARAMETER SkipBuild
  Skip the dotnet build sanity check (assumes the caller already ran
  `dotnet build -c Release` and confirmed clean output). Still runs the
  per-project `dotnet publish` calls.

.EXAMPLE
  ./scripts/release.ps1 -Version 1.0.0

.EXAMPLE
  ./scripts/release.ps1 -Version 1.0.1 -DryRun

.EXAMPLE
  ./scripts/release.ps1 -Version 1.1.0 -IncludeFrameworkDependentGui

.NOTES
  Routine documented at: docs/release-process.md
  Skill form:            .claude/skills/release-deployassistant/SKILL.md
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [switch]$DryRun,
    [switch]$NoExtract,
    [switch]$IncludeFrameworkDependentGui,
    [switch]$SkipBuild,
    [string]$Destination = 'Y:\21 Dev(SW)\02_Applications\02_Utility\DeployAssistant'
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$publishRoot = Join-Path $repoRoot 'publish'

# ---------------------------------------------------------------- preflight

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet SDK not found on PATH. Install .NET 8 SDK from https://aka.ms/dotnet/download."
}

$gitSha = (& git -C $repoRoot rev-parse --short HEAD).Trim()
if (-not $gitSha) { throw "Could not determine git short SHA. Is this a git repo?" }

$date       = Get-Date -Format 'yyyyMMdd'
$versionTag = "v$Version"
$folderName = "${versionTag}_${date}"
$zipName    = "DeployAssistant_${versionTag}_${date}_${gitSha}.zip"

# Bail out early if the target version already exists on Y:.  Versions are
# immutable — bump the patch and retry, or move the existing folder aside.
if (-not $DryRun -and (Test-Path $Destination)) {
    $versionDir = Join-Path $Destination $folderName
    if (Test-Path $versionDir) {
        throw @"
Version folder already exists: $versionDir

Versions are immutable. Bump the patch version (or move the existing folder
out of the way) and retry.
"@
    }
}

# Kill any local processes that would hold a file lock on the publish output
Get-Process -Name "DeployAssistant","deployassistant" -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue

# ---------------------------------------------------------------- build

if (-not $SkipBuild) {
    Write-Host "Building DeployAssistant.sln (Release)..." -ForegroundColor Cyan
    & dotnet build (Join-Path $repoRoot 'DeployAssistant.sln') -c Release --no-incremental
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }
}

# Reset publish output directories so stale artifacts can't leak in
$pubGuiSc  = Join-Path $publishRoot 'DeployAssistant-sc'
$pubGuiFd  = Join-Path $publishRoot 'DeployAssistant-fd'
$pubCli    = Join-Path $publishRoot 'DeployAssistant.CLI'

foreach ($p in @($pubGuiSc, $pubGuiFd, $pubCli)) {
    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}

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

Write-Host "Publishing CLI (net472 framework-dependent)..." -ForegroundColor Cyan
& dotnet publish (Join-Path $repoRoot 'DeployAssistant.CLI\DeployAssistant.CLI.csproj') `
    -c Release `
    -o $pubCli
if ($LASTEXITCODE -ne 0) { throw "CLI publish failed." }

# ---------------------------------------------------------------- stage

$stagingRoot = Join-Path $env:TEMP "da_release_$([guid]::NewGuid().ToString('N').Substring(0,8))"
$stagingDir  = Join-Path $stagingRoot 'DeployAssistant'
New-Item -ItemType Directory -Path (Join-Path $stagingDir 'GUI') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $stagingDir 'CLI') -Force | Out-Null

# GUI: pick the single-file self-contained exe (and the framework-dependent
# folder if requested).  .pdb files excluded from the user-facing drop.
$guiScExe = Join-Path $pubGuiSc 'DeployAssistant.exe'
if (-not (Test-Path $guiScExe)) {
    throw "Expected self-contained GUI exe not found at: $guiScExe"
}
Copy-Item $guiScExe -Destination (Join-Path $stagingDir 'GUI') -Force

if ($IncludeFrameworkDependentGui) {
    $guiFdDest = Join-Path $stagingDir 'GUI\framework-dependent'
    New-Item -ItemType Directory -Path $guiFdDest -Force | Out-Null
    Get-ChildItem $pubGuiFd -File -Exclude '*.pdb' | Copy-Item -Destination $guiFdDest -Force
}

# CLI: full publish folder minus .pdb files
Get-ChildItem $pubCli -File -Exclude '*.pdb' | Copy-Item -Destination (Join-Path $stagingDir 'CLI') -Force

# README for end users
$readme = @"
DeployAssistant $versionTag  ($date, commit $gitSha)

GUI (recommended for most users)
--------------------------------
Double-click  GUI\DeployAssistant.exe

Self-contained: no .NET runtime install required.


CLI (for scripting / non-interactive use)
-----------------------------------------
From PowerShell or cmd:
    CLI\deployassistant.exe --help

Requires .NET Framework 4.7.2 (already installed on Windows 10 1803+ and Windows 11).


Documentation
-------------
GitHub: https://github.com/ildooPark/DeployAssistant
Release notes: see the Notion release-notes page for this version.
"@
$readme | Out-File -FilePath (Join-Path $stagingDir 'README.txt') -Encoding utf8

# ---------------------------------------------------------------- zip

$zipPath = Join-Path $env:TEMP $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$stagingDir" -DestinationPath $zipPath -CompressionLevel Optimal

$zipInfo = Get-Item $zipPath
Write-Host ""
Write-Host "Packaged: $zipPath" -ForegroundColor Green
Write-Host ("Size:     {0:N0} bytes" -f $zipInfo.Length)
Write-Host "Version:  $versionTag"
Write-Host "Sha:      $gitSha"
Write-Host ""
Write-Host "--- Staged layout ---" -ForegroundColor Cyan
Get-ChildItem $stagingDir -Recurse | Where-Object { -not $_.PSIsContainer } |
    Select-Object @{n='Name';e={ $_.FullName.Substring($stagingDir.Length + 1) }}, Length |
    Format-Table

# ---------------------------------------------------------------- upload

if ($DryRun) {
    Write-Host ""
    Write-Host "[DryRun] Skipped Y: drive copy. Zip is at $zipPath." -ForegroundColor Yellow
    Remove-Item $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
    return
}

if (-not (Test-Path $Destination)) {
    throw "Destination not reachable: $Destination. Connect to the network share and retry, or use -DryRun."
}

$versionDir = Join-Path $Destination $folderName
New-Item -ItemType Directory -Path $versionDir -Force | Out-Null

Copy-Item $zipPath -Destination $versionDir -Force
Write-Host "Uploaded: $versionDir\$zipName" -ForegroundColor Green

if (-not $NoExtract) {
    # Extract directly into the version folder so the layout is:
    #   v1.0.0_20260511\DeployAssistant\GUI\DeployAssistant.exe
    Expand-Archive -Path (Join-Path $versionDir $zipName) -DestinationPath $versionDir -Force
    Write-Host "Extracted: $versionDir\DeployAssistant\" -ForegroundColor Green
}

Write-Host ""
Write-Host "--- Y:\DeployAssistant top-level layout ---" -ForegroundColor Cyan
Get-ChildItem $Destination | Sort-Object Name | Format-Table Name, LastWriteTime

# Cleanup local staging (keep the temp zip until the user confirms upload)
Remove-Item $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Done. Next steps:" -ForegroundColor Green
Write-Host "  1. Smoke-test the CLI on the share:"
Write-Host "       & `"$versionDir\DeployAssistant\CLI\deployassistant.exe`" --help"
Write-Host "  2. Open the Notion release-notes page and append a new section."
Write-Host "     Template: .claude/skills/release-deployassistant/SKILL.md"
