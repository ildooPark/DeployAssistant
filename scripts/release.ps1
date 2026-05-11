#requires -Version 5.0
<#
.SYNOPSIS
  Package and upload a DeployAssistant release to the team network share.

.DESCRIPTION
  Publishes the WPF GUI (self-contained single-file) and the .NET Framework
  CLI as two separate zips, drops them on the Y: drive in a versioned
  subfolder under either:

    개발 (dev, default)  — for testing, no announcement
    배포 (production)    — only with -Official, after the dev drop is verified

  Layout produced on Y::

    Y:\...\DeployAssistant\<개발|배포>\gui\v<X.Y.Z>_<date>\
        DeployAssistant-GUI_v<X.Y.Z>_<date>_<sha>.zip
        DeployAssistant\GUI\DeployAssistant.exe     (extracted)
        README.txt

    Y:\...\DeployAssistant\<개발|배포>\cli\v<X.Y.Z>_<date>\
        DeployAssistant-CLI_v<X.Y.Z>_<date>_<sha>.zip
        DeployAssistant\CLI\deployassistant.exe + DLLs   (extracted)
        README.txt

.PARAMETER Version
  Semver string without the 'v' prefix, e.g. "1.0.0".

.PARAMETER Official
  Promote to the 배포 (production) channel. Without this switch, the drop
  lands in 개발 (dev) and is not considered an announced release.

.PARAMETER Component
  Which component(s) to publish. "Both" (default) ships GUI + CLI.
  "Gui" or "Cli" ship just one — useful when only one side changed.

.PARAMETER DryRun
  Stage and zip locally, but skip the Y: drive copy.

.PARAMETER NoExtract
  Skip extracting each zip alongside it on Y: drive (zip-only drop).

.PARAMETER IncludeFrameworkDependentGui
  Also stage the framework-dependent GUI flavor under
  DeployAssistant\GUI\framework-dependent\. Default is self-contained
  single-file only (no .NET runtime required on target).

.PARAMETER SkipBuild
  Skip the `dotnet build` sanity check (still runs the per-project
  publishes). Use only if a clean Release build was just confirmed in
  this shell.

.PARAMETER Destination
  Override the Y: drive parent. Default is the team share. The
  channel subfolder (개발 or 배포) is appended automatically based on
  -Official.

.EXAMPLE
  # Dev drop for testing
  ./scripts/release.ps1 -Version 1.0.0

.EXAMPLE
  # Official production release (after dev drop was verified)
  ./scripts/release.ps1 -Version 1.0.0 -Official

.EXAMPLE
  # CLI-only patch release
  ./scripts/release.ps1 -Version 1.0.1 -Component Cli -Official

.NOTES
  Routine documented at: docs/release-process.md
  Skill form:            .claude/skills/release-deployassistant/SKILL.md
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [switch]$Official,

    [ValidateSet('Both', 'Gui', 'Cli')]
    [string]$Component = 'Both',

    [switch]$DryRun,
    [switch]$NoExtract,
    [switch]$IncludeFrameworkDependentGui,
    [switch]$SkipBuild,
    [string]$Destination = 'Y:\21 Dev(SW)\02_Applications\02_Utility\DeployAssistant'
)

$ErrorActionPreference = 'Stop'

$repoRoot    = (Resolve-Path "$PSScriptRoot\..").Path
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

# Channel — 개발 (dev) by default; 배포 (production) only with -Official
$channel = if ($Official) { '배포' } else { '개발' }

$wantGui = ($Component -in 'Both', 'Gui')
$wantCli = ($Component -in 'Both', 'Cli')

$guiDest = Join-Path $Destination (Join-Path $channel 'gui')
$cliDest = Join-Path $Destination (Join-Path $channel 'cli')

# Bail out early if any target version folder already exists.
# Versions are immutable — bump the patch and retry.
if (-not $DryRun) {
    if ($wantGui -and (Test-Path (Join-Path $guiDest $folderName))) {
        throw "GUI version folder already exists: $(Join-Path $guiDest $folderName)`n`nVersions are immutable. Bump the patch version and retry."
    }
    if ($wantCli -and (Test-Path (Join-Path $cliDest $folderName))) {
        throw "CLI version folder already exists: $(Join-Path $cliDest $folderName)`n`nVersions are immutable. Bump the patch version and retry."
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

if ($wantGui) {
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

if ($wantCli) {
    if (Test-Path $pubCli) { Remove-Item $pubCli -Recurse -Force }

    Write-Host "Publishing CLI (net472 framework-dependent)..." -ForegroundColor Cyan
    & dotnet publish (Join-Path $repoRoot 'DeployAssistant.CLI\DeployAssistant.CLI.csproj') `
        -c Release `
        -o $pubCli
    if ($LASTEXITCODE -ne 0) { throw "CLI publish failed." }
}

# ---------------------------------------------------------------- stage + zip helpers

function New-ReleaseZip {
    param(
        [Parameter(Mandatory)] [string]$Kind,             # 'GUI' or 'CLI'
        [Parameter(Mandatory)] [string]$StagingFilesDir,  # directory whose contents fill DeployAssistant\<Kind>\
        [Parameter(Mandatory)] [string]$ZipFileName
    )

    $stagingRoot = Join-Path $env:TEMP "da_release_$([guid]::NewGuid().ToString('N').Substring(0,8))"
    $payloadDir  = Join-Path $stagingRoot 'DeployAssistant'
    $kindDir     = Join-Path $payloadDir $Kind
    New-Item -ItemType Directory -Path $kindDir -Force | Out-Null

    # Copy non-pdb files (preserve subdirectory structure if any)
    Get-ChildItem $StagingFilesDir -Recurse -File -Exclude '*.pdb' | ForEach-Object {
        $rel = $_.FullName.Substring($StagingFilesDir.Length).TrimStart('\','/')
        $target = Join-Path $kindDir $rel
        $targetDir = Split-Path $target -Parent
        if ($targetDir -and -not (Test-Path $targetDir)) {
            New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
        }
        Copy-Item $_.FullName -Destination $target -Force
    }

    # README
    $kindLower = $Kind.ToLower()
    $readme = if ($Kind -eq 'GUI') {
@"
DeployAssistant GUI $versionTag  ($date, commit $gitSha)

Double-click  GUI\DeployAssistant.exe  to launch.

Self-contained: no .NET runtime install required.

Documentation
-------------
Notion: https://www.notion.so/clevision/Deploy-Assistant-DA-Manual-6c6358be215d470580f7351d25e0ba01
GitHub: https://github.com/ildooPark/DeployAssistant
"@
    } else {
@"
DeployAssistant CLI $versionTag  ($date, commit $gitSha)

From PowerShell or cmd:
    CLI\deployassistant.exe --help

Requires .NET Framework 4.7.2 (default on Windows 10 1803+ and Windows 11).

Pass --yes to auto-confirm all prompts for non-interactive / scripted use.

Documentation
-------------
Notion: https://www.notion.so/clevision/35d398fd58f2811aa2affb10dc6edd6f
GitHub: https://github.com/ildooPark/DeployAssistant
"@
    }
    $readme | Out-File -FilePath (Join-Path $payloadDir 'README.txt') -Encoding utf8

    # Zip it
    $zipPath = Join-Path $env:TEMP $ZipFileName
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path "$payloadDir" -DestinationPath $zipPath -CompressionLevel Optimal

    [PSCustomObject]@{
        Kind        = $Kind
        ZipPath     = $zipPath
        StagingRoot = $stagingRoot
        PayloadDir  = $payloadDir
    }
}

$builtZips = @()

if ($wantGui) {
    # The single-file GUI publish puts DeployAssistant.exe directly in $pubGuiSc.
    # If framework-dependent flavor was requested, include it as a subfolder.
    $guiStage = Join-Path $env:TEMP "da_gui_stage_$([guid]::NewGuid().ToString('N').Substring(0,8))"
    New-Item -ItemType Directory -Path $guiStage -Force | Out-Null
    $guiScExe = Join-Path $pubGuiSc 'DeployAssistant.exe'
    if (-not (Test-Path $guiScExe)) { throw "Expected self-contained GUI exe not found at: $guiScExe" }
    Copy-Item $guiScExe -Destination $guiStage -Force

    if ($IncludeFrameworkDependentGui) {
        $guiFdSub = Join-Path $guiStage 'framework-dependent'
        New-Item -ItemType Directory -Path $guiFdSub -Force | Out-Null
        Get-ChildItem $pubGuiFd -File -Exclude '*.pdb' | Copy-Item -Destination $guiFdSub -Force
    }

    $builtZips += New-ReleaseZip -Kind 'GUI' -StagingFilesDir $guiStage `
        -ZipFileName "DeployAssistant-GUI_${versionTag}_${date}_${gitSha}.zip"
    Remove-Item $guiStage -Recurse -Force -ErrorAction SilentlyContinue
}

if ($wantCli) {
    $builtZips += New-ReleaseZip -Kind 'CLI' -StagingFilesDir $pubCli `
        -ZipFileName "DeployAssistant-CLI_${versionTag}_${date}_${gitSha}.zip"
}

Write-Host ""
Write-Host "Channel:  $channel ($(if ($Official) { 'production' } else { 'dev' }))" -ForegroundColor $(if ($Official) { 'Yellow' } else { 'Gray' })
Write-Host "Version:  $versionTag"
Write-Host "Sha:      $gitSha"
Write-Host ""
foreach ($z in $builtZips) {
    $size = (Get-Item $z.ZipPath).Length
    Write-Host ("Packaged: {0}  ({1:N0} bytes)" -f $z.ZipPath, $size) -ForegroundColor Green
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
    $kindDest = if ($z.Kind -eq 'GUI') { $guiDest } else { $cliDest }
    $versionDir = Join-Path $kindDest $folderName
    New-Item -ItemType Directory -Path $versionDir -Force | Out-Null

    $zipName = Split-Path $z.ZipPath -Leaf
    Copy-Item $z.ZipPath -Destination $versionDir -Force
    Write-Host "Uploaded: $versionDir\$zipName" -ForegroundColor Green

    if (-not $NoExtract) {
        Expand-Archive -Path (Join-Path $versionDir $zipName) -DestinationPath $versionDir -Force
        Write-Host "Extracted: $versionDir\DeployAssistant\" -ForegroundColor Green
    }

    Remove-Item $z.StagingRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "--- $channel\ top-level layout ---" -ForegroundColor Cyan
$channelDir = Join-Path $Destination $channel
if (Test-Path $channelDir) {
    Get-ChildItem $channelDir -Recurse -Depth 1 | Sort-Object FullName | Format-Table FullName, LastWriteTime
}

Write-Host ""
Write-Host "Done. Next steps:" -ForegroundColor Green
if ($wantCli) {
    $cliVer = Join-Path $cliDest $folderName
    Write-Host "  Smoke-test the CLI:"
    Write-Host "    & `"$cliVer\DeployAssistant\CLI\deployassistant.exe`" --help"
}
Write-Host "  Update Notion release notes:"
if ($wantGui) { Write-Host "    GUI:  https://www.notion.so/clevision/Deploy-Assistant-DA-Manual-6c6358be215d470580f7351d25e0ba01" }
if ($wantCli) { Write-Host "    CLI:  https://www.notion.so/clevision/35d398fd58f2811aa2affb10dc6edd6f" }
Write-Host "  Template (per-version toggle under existing '### 🆙 Updates' section):"
Write-Host "    ### $versionTag — $(Get-Date -Format 'yyyy-MM-dd') {toggle=`"true`"}"
Write-Host "        - <highlight 1>"
Write-Host "        - <highlight 2>"
