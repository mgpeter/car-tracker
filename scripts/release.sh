#!/usr/bin/env bash
#
# Bump the root VERSION file, and optionally build both Cambelt images locally for a smoke test.
#
# This script no longer publishes anything. CI is the only thing that can write to Docker Hub, and a release
# is a git tag - see DEC-021. It used to end in `docker push --all-tags`, which pushes EVERY local tag of the
# repository; once `:latest` and `:stable` mean "the blessed release", a dev-machine run of that would have
# moved the release channel to an unreviewed working-tree build.
#
# So the flow is now three deliberate steps:
#
#   1. bump          this script, then commit VERSION with the feature
#   2. push main     CI publishes :edge and :<sha>
#   3. push a tag    `git tag -a v<version>` publishes :<version>, :latest and :stable, by retagging the
#                    digest from step 2 rather than rebuilding it
#
# The bumped VERSION is NOT committed - stage it into the feature commit yourself.
#
#   ./scripts/release.sh --minor            # bump minor and stop
#   ./scripts/release.sh --patch --build    # bump, and build both images locally as :dev
#   ./scripts/release.sh --major --dry-run  # print the bump and exit
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
VERSION_FILE="$REPO_ROOT/VERSION"
REGISTRY_USER="${DOCKERHUB_USER:-mgpeter}"
SOURCE_URL="https://github.com/mgpeter/car-tracker"

# "short-name:dockerfile" — the image is $REGISTRY_USER/$short-name.
IMAGES=(
  "cartracker-webapi:deploy/Dockerfile.webapi"
  "cartracker-gateway:deploy/Dockerfile.gateway"
)

bump=""
bumpcount=0
build=0
dry=0
for arg in "$@"; do
  case "$arg" in
    --patch) bump="patch"; bumpcount=$((bumpcount + 1)) ;;
    --minor) bump="minor"; bumpcount=$((bumpcount + 1)) ;;
    --major) bump="major"; bumpcount=$((bumpcount + 1)) ;;
    --build) build=1 ;;
    --dry-run) dry=1 ;;
    *) echo "Unknown argument: $arg" >&2; exit 1 ;;
  esac
done

if [[ "$bumpcount" -ne 1 ]]; then
  echo "Specify exactly one of --patch, --minor, --major." >&2
  exit 1
fi

current="$(tr -d ' \t\r\n' < "$VERSION_FILE")"
if [[ ! "$current" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "VERSION does not contain a valid semver: '$current'" >&2
  exit 1
fi
IFS='.' read -r maj min pat <<< "$current"
case "$bump" in
  major) maj=$((maj + 1)); min=0; pat=0 ;;
  minor) min=$((min + 1)); pat=0 ;;
  patch) pat=$((pat + 1)) ;;
esac
new="$maj.$min.$pat"

echo "Version: $current -> $new"
if [[ "$dry" -eq 1 ]]; then
  echo "Dry run - nothing written or built."
  exit 0
fi

printf '%s\n' "$new" > "$VERSION_FILE"

if [[ "$build" -eq 1 ]]; then
  cd "$REPO_ROOT"
  # The same OCI labels CI applies, so a local image is not metadata-free. The revision is HEAD, which on a
  # dirty tree names a commit that does not describe what was built - that is the cost of building from a
  # working tree, and it is why CI's labels are the ones that count.
  revision="$(git rev-parse HEAD)"
  created="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

  for entry in "${IMAGES[@]}"; do
    image="$REGISTRY_USER/${entry%%:*}"
    dockerfile="${entry#*:}"
    # :dev, never :latest / :stable / :<version> - those are channel names and a local build must not be able
    # to occupy one, even by accident.
    echo "Building $image:dev..."
    docker build -f "$dockerfile" -t "$image:dev" \
      --label "org.opencontainers.image.version=$new" \
      --label "org.opencontainers.image.revision=$revision" \
      --label "org.opencontainers.image.source=$SOURCE_URL" \
      --label "org.opencontainers.image.created=$created" .
  done
fi

echo
echo "Done: $new"
echo
echo "  1. Stage VERSION into the feature commit and push:"
echo "       git add VERSION && git commit -m \"<subject>\" && git push"
echo "     CI publishes :edge and :<sha>."
echo
echo "  2. Once it has proven itself, release it:"
echo "       git tag -a v$new -m \"$new\" && git push origin v$new"
echo "     Release publishes :$new, :latest and :stable from that same digest."
