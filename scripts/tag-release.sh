#!/usr/bin/env bash
#
# Tag the current commit as the release named by the VERSION file, and push that tag to origin.
#
# The macOS and Linux port of tag-release.ps1; the two make the same checks and take the same options. The
# reasoning behind each check is written up in tag-release.ps1 and not repeated here.
#
# A release is a git tag (DEC-021). Pushing `v<version>` runs .github/workflows/release.yml, which promotes the
# images CI already built from this commit to :<version>, :latest and :stable by retagging the digest. Nothing
# is rebuilt. Every guard in release.yml fires after the tag is published, so the same questions are asked here
# first:
#
#   1. VERSION on disk matches VERSION at HEAD   (release.yml guard 1)
#   2. the tag does not exist, locally or on origin   (guard 4: a published version tag never moves)
#   3. the working tree is clean
#   4. HEAD is on origin/main   (guards 2 and 3)
#
# It pushes one tag, not `--tags`, unless --all-tags is given.
#
#   ./scripts/tag-release.sh --dry-run   # print the checks and the plan, then exit
#   ./scripts/tag-release.sh             # check, show the plan, ask, tag and push
#   ./scripts/tag-release.sh --yes       # same without the prompt
#   ./scripts/tag-release.sh --force     # allow a dirty tree or a commit off origin/main (hotfix branches)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
VERSION_FILE="$REPO_ROOT/VERSION"
REMOTE="origin"

dry=0
yes=0
alltags=0
force=0
for arg in "$@"; do
  case "$arg" in
    --dry-run) dry=1 ;;
    --yes|-y) yes=1 ;;
    --all-tags) alltags=1 ;;
    --force) force=1 ;;
    -h|--help) sed -n '2,25p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "Unknown argument: $arg" >&2; exit 1 ;;
  esac
done

if [[ -t 1 ]]; then
  RED=$'\033[31m'; YELLOW=$'\033[33m'; GREEN=$'\033[32m'; CYAN=$'\033[36m'; DIM=$'\033[2m'; RESET=$'\033[0m'
else
  RED=""; YELLOW=""; GREEN=""; CYAN=""; DIM=""; RESET=""
fi

fail() {
  echo
  echo "${RED}REFUSED: $1${RESET}"
  if [[ -n "${2:-}" ]]; then
    echo
    echo "${YELLOW}$2${RESET}"
  fi
  exit 1
}

ok() { echo "${DIM}  [ok] $1${RESET}"; }
warn() { echo "${YELLOW}  [!!] $1${RESET}"; }

cd "$REPO_ROOT"

# --- The version, and the tag it names ---------------------------------------------------------------------
[[ -f "$VERSION_FILE" ]] || fail "No VERSION file at $VERSION_FILE."

version="$(tr -d ' \t\r\n' < "$VERSION_FILE")"
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  fail "VERSION does not contain a valid semver: '$version'." \
    "Fix the file, or run ./scripts/release.sh to bump it properly."
fi

tag="v$version"
sha="$(git rev-parse HEAD)"
short="${sha:0:12}"
branch="$(git rev-parse --abbrev-ref HEAD)"

echo
echo "${CYAN}Releasing $tag${RESET}"
echo "  commit  $short  on $branch"
echo

# --- Check 1: VERSION on disk is VERSION at HEAD -----------------------------------------------------------
committed="$(git show HEAD:VERSION | tr -d ' \t\r\n')"
if [[ "$committed" != "$version" ]]; then
  fail "VERSION on disk says $version, but VERSION at HEAD says $committed." \
"The image built from this commit reports $committed on /api/meta, so tagging it $tag would ship an image
reporting one number under a tag claiming another.

Commit the bump into the feature it describes, then tag that commit:
    git add VERSION
    git commit --amend --no-edit      (or into the feature commit, per CLAUDE.md)
    git push"
fi
ok "VERSION on disk matches VERSION at HEAD"

# --- Check 2: the tag does not already exist, locally or on origin -----------------------------------------
if [[ -n "$(git tag --list "$tag")" ]]; then
  fail "$tag already exists locally." \
"A published version tag never moves: release.yml refuses to repoint one.

Release a new version instead:  ./scripts/release.sh --patch"
fi

if ! remote_tag="$(git ls-remote --tags "$REMOTE" "refs/tags/$tag" 2>&1)"; then
  fail "Could not reach $REMOTE to check whether $tag already exists." "$remote_tag"
fi
if [[ -n "$remote_tag" ]]; then
  fail "$tag already exists on $REMOTE." \
"It was pushed from somewhere else. If that release failed part-way, re-run the workflow rather than
re-pushing the tag:  gh workflow run release.yml -f tag=$tag"
fi
ok "$tag is unused, locally and on $REMOTE"

# --- Check 3: the working tree is clean --------------------------------------------------------------------
dirty="$(git status --porcelain)"
if [[ -n "$dirty" ]]; then
  count="$(printf '%s\n' "$dirty" | wc -l | tr -d ' ')"
  if [[ "$force" -eq 1 ]]; then
    warn "working tree has $count uncommitted change(s) - releasing anyway (--force)"
  else
    fail "The working tree has $count uncommitted change(s)." \
"A tag names a commit, so none of that is in the release - and the image being promoted was built by CI
from $short, which never saw your working tree.

Commit or stash first, or pass --force if the changes are irrelevant to what ships.

$dirty"
  fi
else
  ok "working tree is clean"
fi

# --- Check 4: HEAD is on origin/main -----------------------------------------------------------------------
git fetch --quiet --no-tags "$REMOTE" main
if git merge-base --is-ancestor HEAD FETCH_HEAD; then
  ok "HEAD is on $REMOTE/main"
elif [[ "$force" -eq 1 ]]; then
  warn "HEAD is not on $REMOTE/main - releasing anyway (--force)"
else
  fail "HEAD is not an ancestor of $REMOTE/main." \
"release.yml refuses on both counts:

  - a commit that never landed on main is not a release (guard 2);
  - a commit origin has never seen was never built, so there is no image to promote and the workflow fails
    with a registry 404 that reads like a credentials problem (guard 3).

Push first:  git push $REMOTE $branch"
fi

# --- What this will do -------------------------------------------------------------------------------------
subject="$(git log -1 --pretty=%s)"
if [[ "$alltags" -eq 1 ]]; then
  push_args=(push "$REMOTE" --tags)
else
  push_args=(push "$REMOTE" "$tag")
fi

echo
echo "${CYAN}This will:${RESET}"
echo "  git tag -a $tag -m \"$version\"   on $short  $subject"
echo "  git ${push_args[*]}"
echo
echo "${DIM}  -> release.yml promotes cartracker-webapi and cartracker-gateway to${RESET}"
echo "${DIM}     :$version, :latest and :stable, from the digest CI already built.${RESET}"
echo "${DIM}     Any deployment following the stable channel takes it on its next Watchtower pass.${RESET}"

if [[ "$alltags" -eq 1 ]]; then
  echo
  echo "${YELLOW}  --all-tags: EVERY local tag goes, not just this one.${RESET}"
fi

if [[ "$dry" -eq 1 ]]; then
  echo
  echo "${YELLOW}Dry run - no tag written, nothing pushed.${RESET}"
  exit 0
fi

if [[ "$yes" -ne 1 ]]; then
  echo
  read -r -p "Tag and push $tag? [y/N] " answer
  case "$answer" in
    y|Y|yes|YES|Yes) ;;
    *) echo "${YELLOW}Nothing done.${RESET}"; exit 0 ;;
  esac
fi

echo
git tag -a "$tag" -m "$version"
echo "${GREEN}Tagged $tag${RESET}"

if ! push_out="$(git "${push_args[@]}" 2>&1)"; then
  # The tag exists locally and the push did not happen; leaving it would make the next run refuse on check 2
  # for a release that never went out.
  git tag -d "$tag" >/dev/null
  fail "Push failed - the local tag has been removed so you can retry cleanly." "$push_out"
fi

echo "${GREEN}Pushed to $REMOTE${RESET}"
echo
echo "${DIM}Watch it:  gh run watch --workflow=release.yml${RESET}"
echo "${DIM}Or:        https://github.com/mgpeter/car-tracker/actions/workflows/release.yml${RESET}"
