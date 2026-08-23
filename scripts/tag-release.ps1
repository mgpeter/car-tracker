#Requires -Version 5
<#
.SYNOPSIS
  Tag the current commit as the release named by the VERSION file, and push that tag to origin.

.DESCRIPTION
  A release is a git tag (DEC-021). Pushing `v<version>` runs .github/workflows/release.yml, which promotes
  the images CI already built from this commit into `:<version>`, `:latest` and `:stable` by retagging the
  digest - nothing is rebuilt, so what ships is the artifact CI actually validated.

  This script does not decide the version. `release.ps1 -Minor` does that, and the bump is committed with the
  feature it describes. This one reads what is already there and blesses the commit it sits on.

  WHY IT CHECKS FOUR THINGS BEFORE PUSHING. release.yml has four guards of its own, and every one of them
  fires AFTER the tag exists - at which point the tag is already published, and the only ways out are
  `workflow_dispatch` or deleting and re-pushing a tag, which is precisely what that workflow's guard 4 exists
  to catch. So the same questions are asked here, where the answer costs nothing:

    1. VERSION on disk matches VERSION at HEAD    -> release.yml guard 1 (the tag must name its commit's
                                                     version, or the image reports one number under a tag
                                                     claiming another)
    2. the tag does not already exist             -> release.yml guard 4 (a published version tag never moves)
    3. HEAD is on origin/main                     -> release.yml guards 2 and 3 (blessing a commit that never
                                                     landed is not a release, and a commit origin has never
                                                     seen was never built)
    4. the working tree is clean                  -> not a workflow guard at all, and the cheapest of the four
                                                     to get wrong: a tag names a commit, so uncommitted work
                                                     is simply absent from the release you are about to bless

  IT PUSHES ONE TAG, NOT `--tags`. `git push --tags` pushes every local tag the repository has, which is the
  same shape of mistake `release.ps1`'s header records about `docker push --all-tags`: a command whose blast
  radius is "everything local" aimed at a channel that means "the blessed release". Any stale or experimental
  tag on the machine would go with it. Pass -AllTags if you genuinely want that.

.EXAMPLE
  ./scripts/tag-release.ps1 -DryRun    # print what it would tag and push, then exit
  ./scripts/tag-release.ps1            # check, show the plan, ask, tag and push
  ./scripts/tag-release.ps1 -Yes       # same without the prompt, for a non-interactive run
#>
[CmdletBinding()]
param(
    # Print the plan and every check, then exit without writing a tag.
    [switch]$DryRun,
    # Skip the confirmation prompt. The checks still run and still refuse.
    [switch]$Yes,
    # Push every local tag rather than just this release's. Rarely what anyone wants - see the note above.
    [switch]$AllTags,
    # Tag a commit that is not on origin/main, or with a dirty tree. For a hotfix branch being released
    # deliberately; it does NOT get past the version-mismatch or tag-exists checks, because release.yml
    # would refuse those anyway and failing here is cheaper.
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$RepoRoot    = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$VersionFile = Join-Path $RepoRoot 'VERSION'
$Remote      = 'origin'

function Fail([string]$message, [string]$fix) {
    Write-Host ''
    Write-Host "REFUSED: $message" -ForegroundColor Red
    if ($fix) {
        Write-Host ''
        Write-Host $fix -ForegroundColor Yellow
    }
    exit 1
}

function Invoke-Git {
    # Every git call goes through here so a non-zero exit is a thrown error rather than a silently empty
    # string that the next check then reads as "fine".
    #
    # NOT named `Git`. PowerShell resolves an alias, then a function, then a cmdlet, then an executable - so a
    # function called `Git` swallows every `git` call in the file, including its own, and the script dies with
    # "call depth overflow" rather than anything mentioning git.
    $output = & git @args 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($args -join ' ') failed:`n$output"
    }
    return ($output | Out-String).Trim()
}

Push-Location $RepoRoot
try {
    # ---------------------------------------------------------------------------------------------------
    # The version, and the tag it names
    # ---------------------------------------------------------------------------------------------------
    if (-not (Test-Path $VersionFile)) { Fail "No VERSION file at $VersionFile." '' }

    $version = (Get-Content $VersionFile -Raw).Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        Fail "VERSION does not contain a valid semver: '$version'." 'Fix the file, or run ./scripts/release.ps1 to bump it properly.'
    }

    $tag = "v$version"
    $sha = Invoke-Git rev-parse HEAD
    $branch = Invoke-Git rev-parse --abbrev-ref HEAD

    Write-Host ''
    Write-Host "Releasing $tag" -ForegroundColor Cyan
    Write-Host "  commit  $($sha.Substring(0,12))  on $branch"
    Write-Host ''

    # ---------------------------------------------------------------------------------------------------
    # Check 1 - VERSION on disk is VERSION at HEAD  (release.yml guard 1)
    #
    # .dockerignore excludes .git, so nothing inside either image build can read a tag: the number an image
    # reports comes from the VERSION file at the commit that built it. A bumped-but-uncommitted VERSION would
    # tag a commit whose image reports the PREVIOUS number - the 1.0.0-on-the-NAS failure in a new costume.
    # ---------------------------------------------------------------------------------------------------
    $committedVersion = (Invoke-Git show "HEAD:VERSION").Trim()
    if ($committedVersion -ne $version) {
        Fail "VERSION on disk says $version, but VERSION at HEAD says $committedVersion." @"
The image built from this commit reports $committedVersion on /api/meta and stamps it into every account
export, so tagging it $tag would ship an image reporting one number under a tag claiming another.

Commit the bump into the feature it describes, then tag that commit:
    git add VERSION
    git commit --amend --no-edit      (or into the feature commit, per CLAUDE.md)
    git push
"@
    }
    Write-Host '  [ok] VERSION on disk matches VERSION at HEAD' -ForegroundColor DarkGray

    # ---------------------------------------------------------------------------------------------------
    # Check 2 - the tag does not already exist, locally or on origin  (release.yml guard 4)
    #
    # A published version tag never moves. Git refuses a duplicate name locally, but that message says
    # nothing about why it matters, and it says nothing at all about a tag that exists only on origin.
    # ---------------------------------------------------------------------------------------------------
    $localTag = & git tag --list $tag
    if ($localTag) {
        Fail "$tag already exists locally." @"
A published version tag never moves: release.yml refuses to repoint one, because a version tag that meant a
different image yesterday is worse than no tag at all.

Release a new version instead:  ./scripts/release.ps1 -Patch
"@
    }

    $remoteTag = & git ls-remote --tags $Remote "refs/tags/$tag" 2>&1
    if ($LASTEXITCODE -ne 0) {
        Fail "Could not reach $Remote to check whether $tag already exists." "$remoteTag"
    }
    if ($remoteTag) {
        Fail "$tag already exists on $Remote." @"
It was pushed from somewhere else. If that release failed part-way, re-run the workflow rather than
re-pushing the tag:  gh workflow run release.yml -f tag=$tag
"@
    }
    Write-Host "  [ok] $tag is unused, locally and on $Remote" -ForegroundColor DarkGray

    # ---------------------------------------------------------------------------------------------------
    # Check 3 - the working tree is clean
    #
    # Not one of release.yml's guards, and the cheapest of the four to get wrong. A tag names a commit, so
    # anything uncommitted is simply absent from the release - and the release is built from the digest CI
    # produced for that commit, which never saw the working tree at all.
    # ---------------------------------------------------------------------------------------------------
    $dirty = Invoke-Git status --porcelain
    if ($dirty) {
        $count = ($dirty -split "`n").Count
        if ($Force) {
            Write-Host "  [!!] working tree has $count uncommitted change(s) - releasing anyway (-Force)" -ForegroundColor Yellow
        }
        else {
            Fail "The working tree has $count uncommitted change(s)." @"
A tag names a commit, so none of that is in the release you are about to bless - and the image being
promoted was built by CI from $($sha.Substring(0,12)), which never saw your working tree.

Commit or stash first, or pass -Force if you know the changes are irrelevant to what ships.

$dirty
"@
        }
    }
    else {
        Write-Host '  [ok] working tree is clean' -ForegroundColor DarkGray
    }

    # ---------------------------------------------------------------------------------------------------
    # Check 4 - HEAD is on origin/main  (release.yml guards 2 and 3)
    #
    # Guard 2 refuses a tag that is not an ancestor of main: blessing a commit that never landed is not a
    # release. Guard 3 refuses a commit with no `:<sha>` image, which is what an unpushed commit always is -
    # CI has never seen it, so it has never built it. That one surfaces on the workflow as a registry 404
    # that reads like a credentials problem, which is a bad half-hour to hand somebody.
    # ---------------------------------------------------------------------------------------------------
    Invoke-Git fetch --no-tags $Remote main | Out-Null
    $onMain = & git merge-base --is-ancestor HEAD FETCH_HEAD 2>&1
    $isAncestor = ($LASTEXITCODE -eq 0)

    if (-not $isAncestor) {
        if ($Force) {
            Write-Host "  [!!] HEAD is not on $Remote/main - releasing anyway (-Force)" -ForegroundColor Yellow
        }
        else {
            Fail "HEAD is not an ancestor of $Remote/main." @"
Two things follow from that, and release.yml refuses on both:

  - a commit that never landed on main is not a release (guard 2);
  - a commit origin has never seen was never built, so there is no image to promote and the workflow fails
    with a registry 404 that reads like a credentials problem (guard 3).

Push first:  git push $Remote $branch
"@
        }
    }
    else {
        Write-Host "  [ok] HEAD is on $Remote/main" -ForegroundColor DarkGray
    }

    # ---------------------------------------------------------------------------------------------------
    # What this will do
    # ---------------------------------------------------------------------------------------------------
    $subject = Invoke-Git log -1 --pretty=%s
    $pushArgs = if ($AllTags) { @('push', $Remote, '--tags') } else { @('push', $Remote, $tag) }

    Write-Host ''
    Write-Host 'This will:' -ForegroundColor Cyan
    Write-Host "  git tag -a $tag -m `"$version`"   on $($sha.Substring(0,12))  $subject"
    Write-Host "  git $($pushArgs -join ' ')"
    Write-Host ''
    Write-Host "  -> release.yml promotes cartracker-webapi and cartracker-gateway to" -ForegroundColor DarkGray
    Write-Host "     :$version, :latest and :stable, from the digest CI already built." -ForegroundColor DarkGray
    Write-Host "     Any deployment following the stable channel takes it on its next Watchtower pass." -ForegroundColor DarkGray

    if ($AllTags) {
        Write-Host ''
        Write-Host '  -AllTags: EVERY local tag goes, not just this one.' -ForegroundColor Yellow
    }

    if ($DryRun) {
        Write-Host ''
        Write-Host 'Dry run - no tag written, nothing pushed.' -ForegroundColor Yellow
        return
    }

    # Publishing is outward-facing and awkward to undo: a version tag never moves, so the recovery for a
    # wrong one is a new version rather than a correction. Worth one keystroke.
    if (-not $Yes) {
        Write-Host ''
        $answer = Read-Host "Tag and push $tag? [y/N]"
        if ($answer -notmatch '^(y|yes)$') {
            Write-Host 'Nothing done.' -ForegroundColor Yellow
            return
        }
    }

    Write-Host ''
    Invoke-Git tag -a $tag -m $version | Out-Null
    Write-Host "Tagged $tag" -ForegroundColor Green

    try {
        Invoke-Git @pushArgs | Out-Null
    }
    catch {
        # The tag exists locally now and the push did not happen, so leaving it behind would make the next
        # run refuse on check 2 for a release that never went out.
        & git tag -d $tag | Out-Null
        Fail "Push failed - the local tag has been removed so you can retry cleanly." "$_"
    }

    Write-Host "Pushed to $Remote" -ForegroundColor Green
    Write-Host ''
    Write-Host "Watch it:  gh run watch --workflow=release.yml" -ForegroundColor DarkGray
    Write-Host "Or:        https://github.com/mgpeter/car-tracker/actions/workflows/release.yml" -ForegroundColor DarkGray
}
finally {
    Pop-Location
}
