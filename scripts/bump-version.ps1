#Requires -Version 5
<#
.SYNOPSIS
  Bump the root VERSION file. Nothing else.

.DESCRIPTION
  VERSION is the single source of truth for the assembly version (Directory.Build.props reads it into
  <Version>, so GET /api/meta and an export's schemaVersion report it) and for the image tags CI applies. Every
  feature commit bumps it, staged into the feature commit itself.

  This script does not build, push, tag or commit. CI publishes :edge and :<sha> on a push; a release is a git
  tag, which retags the digest CI already built (DEC-021). `release.ps1` sits beside this one and adds an
  optional local -Build for a throwaway :dev smoke test.

.EXAMPLE
  ./scripts/bump-version.ps1 -Minor            # 0.25.0 -> 0.26.0
  ./scripts/bump-version.ps1 -Patch -DryRun    # print the bump and exit
#>
[CmdletBinding()]
param(
    [switch]$Patch,
    [switch]$Minor,
    [switch]$Major,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$RepoRoot    = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$VersionFile = Join-Path $RepoRoot 'VERSION'

$bumps = @($Patch, $Minor, $Major).Where({ $_ })
if ($bumps.Count -ne 1) { throw 'Specify exactly one of -Patch, -Minor, -Major.' }

$current = (Get-Content $VersionFile -Raw).Trim()
if ($current -notmatch '^\d+\.\d+\.\d+$') { throw "VERSION does not contain a valid semver: '$current'" }
$maj, $min, $pat = $current.Split('.') | ForEach-Object { [int]$_ }

if     ($Major) { $maj++; $min = 0; $pat = 0 }
elseif ($Minor) { $min++; $pat = 0 }
else            { $pat++ }
$new = "$maj.$min.$pat"

Write-Host "Version: $current -> $new" -ForegroundColor Cyan
if ($DryRun) { Write-Host 'Dry run - nothing written.' -ForegroundColor Yellow; return }

# LF-only, no BOM. MSBuild reads this into <Version> and CI reads it into image tags and labels; a BOM makes
# the first character of the version something no semver parser expects.
[System.IO.File]::WriteAllText($VersionFile, "$new`n")

Write-Host ''
Write-Host "  Stage VERSION into the feature commit, not a follow-up one:" -ForegroundColor Green
Write-Host "    git add VERSION" -ForegroundColor Green
