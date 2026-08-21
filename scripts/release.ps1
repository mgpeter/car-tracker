#Requires -Version 5
<#
.SYNOPSIS
  Bump the version, build both CarTracker images, tag latest + <version>, and push to Docker Hub.

.DESCRIPTION
  Mirrors the glance-dashboard release convention, extended to CarTracker's two images (webapi + gateway).
  The root VERSION file is the single source of truth for image tags. The bumped VERSION is NOT committed —
  commit it yourself after a successful release. Requires an ambient `docker login` to Docker Hub.

.EXAMPLE
  ./scripts/release.ps1 -Minor            # bump minor, build, push latest + <version>
  ./scripts/release.ps1 -Patch -NoPush    # bump + build + tag locally, do not push
  ./scripts/release.ps1 -Major -DryRun    # print the bump and exit
#>
[CmdletBinding()]
param(
    [switch]$Patch,
    [switch]$Minor,
    [switch]$Major,
    [switch]$NoPush,
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
if ($DryRun) { Write-Host 'Dry run - nothing written, built or pushed.' -ForegroundColor Yellow; return }

# Write LF-only, no BOM: this file is read by CI into an image tag.
[System.IO.File]::WriteAllText($VersionFile, "$new`n")

Push-Location $RepoRoot
try {
    foreach ($img in $Images) {
        Write-Host "Building $($img.Name)..." -ForegroundColor Cyan
        # --pull: the Dockerfiles still float aspnet:10.0 and node:24-alpine. Docker does not re-check a
        # floating tag it has cached, so without this a release can ship against a months-old runtime base.
        # The SDK stage is pinned to an exact patch instead, because a stale SDK breaks the build outright.
        docker build --pull -f $img.Dockerfile -t "$($img.Name):latest" -t "$($img.Name):$new" .
        if ($LASTEXITCODE -ne 0) { throw "docker build failed for $($img.Name)" }
    }

    # The public site's gateway, which cannot share the NAS one: the SPA's Auth0 application is substituted
    # into the JS bundle by Vite at build time, so it is fixed before the image exists. Same repository, a
    # `-cambelt` tag suffix, so `latest` goes on meaning what it always has and the NAS is untouched.
    #
    # Skipped unless the identifiers are in the environment, and the polarity is deliberate: building it
    # without them would produce an image tagged `-cambelt` that silently carries the NAS Auth0 application,
    # whose only symptom is a login loop on cambelt.app some days later.
    $cambeltClientId = $env:CAMBELT_AUTH0_CLIENT_ID
    $cambeltAudience = $env:CAMBELT_AUTH0_AUDIENCE
    if ($cambeltClientId -and $cambeltAudience) {
        $gw = "$RegistryUser/cartracker-gateway"
        $cambeltDomain = if ($env:CAMBELT_AUTH0_DOMAIN) { $env:CAMBELT_AUTH0_DOMAIN } else { 'usualexpat.uk.auth0.com' }

        Write-Host "Building $gw (cambelt.app)..." -ForegroundColor Cyan
        docker build --pull -f 'deploy/Dockerfile.gateway' `
            --build-arg "VITE_AUTH0_DOMAIN=$cambeltDomain" `
            --build-arg "VITE_AUTH0_CLIENT_ID=$cambeltClientId" `
            --build-arg "VITE_AUTH0_AUDIENCE=$cambeltAudience" `
            -t "${gw}:latest-cambelt" -t "${gw}:$new-cambelt" .
        if ($LASTEXITCODE -ne 0) { throw "docker build failed for $gw (cambelt.app)" }

        # A silently-ignored build argument looks exactly like a successful build, so check rather than trust.
        $probe = docker run --rm --entrypoint sh "${gw}:$new-cambelt" -c 'grep -rhoE "VITE_AUTH0_AUDIENCE:.[^,]*" /app/wwwroot/assets/*.js | head -1'
        if ($probe -notmatch [regex]::Escape($cambeltAudience)) {
            throw "The cambelt.app image does not carry audience '$cambeltAudience' - the build argument did not reach the bundle. Bundle says: $probe"
        }
        Write-Host "  audience compiled in: $probe" -ForegroundColor Green
    }
    else {
        Write-Host 'CAMBELT_AUTH0_CLIENT_ID / CAMBELT_AUTH0_AUDIENCE not set - skipping the cambelt.app gateway image.' -ForegroundColor Yellow
    }

    if ($NoPush) {
        Write-Host 'Built and tagged locally (-NoPush); skipping push.' -ForegroundColor Yellow
    }
    else {
        foreach ($img in $Images) {
            Write-Host "Pushing $($img.Name)..." -ForegroundColor Cyan
            docker push --all-tags $img.Name
            if ($LASTEXITCODE -ne 0) { throw "docker push failed for $($img.Name)" }
        }
    }
}
finally { Pop-Location }

Write-Host ''
Write-Host "Done: $new. Commit the bumped VERSION:" -ForegroundColor Green
Write-Host "  git add VERSION; git commit -m `"Bump VERSION to $new`"" -ForegroundColor Green
