#!/usr/bin/env bash
#
# Bump the version, build both CarTracker images, tag latest + <version>, and push to Docker Hub.
# Mirrors the glance-dashboard release convention, extended to CarTracker's two images (webapi + gateway).
# The root VERSION file is the single source of truth. The bumped VERSION is NOT committed — commit it
# yourself after a successful release. Requires an ambient `docker login`.
#
#   ./scripts/release.sh --minor            # bump minor, build, push latest + <version>
#   ./scripts/release.sh --patch --no-push  # bump + build + tag locally, do not push
#   ./scripts/release.sh --major --dry-run  # print the bump and exit
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
VERSION_FILE="$REPO_ROOT/VERSION"
REGISTRY_USER="${DOCKERHUB_USER:-mgpeter}"

# "short-name:dockerfile" — the image is $REGISTRY_USER/$short-name.
IMAGES=(
  "cartracker-webapi:deploy/Dockerfile.webapi"
  "cartracker-gateway:deploy/Dockerfile.gateway"
)

bump=""
bumpcount=0
push=1
dry=0
for arg in "$@"; do
  case "$arg" in
    --patch) bump="patch"; bumpcount=$((bumpcount + 1)) ;;
    --minor) bump="minor"; bumpcount=$((bumpcount + 1)) ;;
    --major) bump="major"; bumpcount=$((bumpcount + 1)) ;;
    --no-push) push=0 ;;
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
  echo "Dry run - nothing written, built or pushed."
  exit 0
fi

printf '%s\n' "$new" > "$VERSION_FILE"

cd "$REPO_ROOT"
for entry in "${IMAGES[@]}"; do
  image="$REGISTRY_USER/${entry%%:*}"
  dockerfile="${entry#*:}"
  echo "Building $image..."
  docker build -f "$dockerfile" -t "$image:latest" -t "$image:$new" .
done

if [[ "$push" -eq 1 ]]; then
  for entry in "${IMAGES[@]}"; do
    image="$REGISTRY_USER/${entry%%:*}"
    echo "Pushing $image..."
    docker push --all-tags "$image"
  done
else
  echo "Built and tagged locally (--no-push); skipping push."
fi

echo
echo "Done: $new. Commit the bumped VERSION:"
echo "  git add VERSION && git commit -m \"Bump VERSION to $new\""
