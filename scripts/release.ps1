#Requires -Version 5
<#
.SYNOPSIS
  Bump the root VERSION file, and optionally build both Cambelt images locally for a smoke test.

.DESCRIPTION
  This script no longer publishes anything. CI is the only thing that can write to Docker Hub, and a release
  is a git tag - see DEC-021. It used to end in `docker push --all-tags`, which pushes EVERY local tag of the
  repository; once `:latest` and `:stable` mean "the blessed release", a dev-machine run of that would have
  moved the release channel to an unreviewed working-tree build.

  So the flow is now three deliberate steps:

    1. bump          this script, then commit VERSION with the feature
    2. push main     CI publishes :edge and :<sha>
    3. push a tag    `git tag -a v<version>` publishes :<version>, :latest and :stable, by retagging the
                     digest from step 2 rather than rebuilding it

  The bumped VERSION is NOT committed - stage it into the feature commit yourself.

.EXAMPLE
  ./scripts/release.ps1 -Minor             # bump minor and stop
  ./scripts/release.ps1 -Patch -Build      # bump, and build both images locally as :dev
  ./scripts/release.ps1 -Major -DryRun     # print the bump and exit
#>
[CmdletBinding()]
param(
    [switch]$Patch,
    [switch]$Minor,
    [switch]$Major,
    # Build both images locally, tagged :dev. Never :latest, :stable or :<version> - those are channel names
    # and a local build must not be able to occupy one, even by accident.
    [switch]$Build,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$RepoRoot     = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$VersionFile  = Join-Path $RepoRoot 'VERSION'
$RegistryUser = if ($env:DOCKERHUB_USER) { $env:DOCKERHUB_USER } else { 'mgpeter' }
$Images = @(
    @{ Name = "$RegistryUser/cartracker-webapi";  Dockerfile = 'deploy/Dockerfile.webapi'  }
    @{ Name = "$RegistryUser/cartracker-gateway"; Dockerfile = 'deploy/Dockerfile.gateway' }
)

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
if ($DryRun) { Write-Host 'Dry run - nothing written or built.' -ForegroundColor Yellow; return }

# Write LF-only, no BOM: this file is read by MSBuild into <Version> and by CI into image tags and labels.
[System.IO.File]::WriteAllText($VersionFile, "$new`n")

if ($Build) {
    Push-Location $RepoRoot
    try {
        # The same OCI labels CI applies, so a local image is not metadata-free. The revision is HEAD, which
        # on a dirty tree names a commit that does not describe what was built - that is the cost of building
        # from a working tree, and it is why CI's labels are the ones that count.
        $revision = (git rev-parse HEAD).Trim()
        $created  = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        $source   = 'https://github.com/mgpeter/car-tracker'

        foreach ($img in $Images) {
            Write-Host "Building $($img.Name):dev..." -ForegroundColor Cyan
            # --pull: the Dockerfiles still float aspnet:10.0 and node:24-alpine. Docker does not re-check a
            # floating tag it has cached, so without this a build can run against a months-old runtime base.
            # The SDK stage is pinned to an exact patch instead, because a stale SDK breaks the build outright.
            docker build --pull -f $img.Dockerfile -t "$($img.Name):dev" `
                --label "org.opencontainers.image.version=$new" `
                --label "org.opencontainers.image.revision=$revision" `
                --label "org.opencontainers.image.source=$source" `
                --label "org.opencontainers.image.created=$created" .
            if ($LASTEXITCODE -ne 0) { throw "docker build failed for $($img.Name)" }
        }
    }
    finally { Pop-Location }
}

Write-Host ''
Write-Host "Done: $new" -ForegroundColor Green
Write-Host ''
Write-Host '  1. Stage VERSION into the feature commit and push:' -ForegroundColor Green
Write-Host "       git add VERSION; git commit -m `"<subject>`"; git push" -ForegroundColor Green
Write-Host '     CI publishes :edge and :<sha>.' -ForegroundColor DarkGray
Write-Host ''
Write-Host '  2. Once it has proven itself, release it:' -ForegroundColor Green
Write-Host "       git tag -a v$new -m `"$new`"; git push origin v$new" -ForegroundColor Green
Write-Host "     Release publishes :$new, :latest and :stable from that same digest." -ForegroundColor DarkGray
