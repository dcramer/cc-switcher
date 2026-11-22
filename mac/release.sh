#!/bin/bash
# Builds the Mac app and attaches it to a GitHub release as cc-switcher-mac.zip.
#
#   mac/release.sh            the latest release
#   mac/release.sh v0.1.12    a specific one
#
# Releases are made by CI (Windows) on every push to main; run this afterwards from a checkout of
# the same commit. Needs Xcode or the Command Line Tools, and `gh` signed in with write access.
set -euo pipefail

cd "$(dirname "$0")/.."
REPO="dcramer/cc-switcher"

tag="${1:-$(gh release view --repo "$REPO" --json tagName --jq .tagName)}"
target="$(gh release view "$tag" --repo "$REPO" --json targetCommitish --jq .targetCommitish)"

# The zip should be built from the commit the release was made from.
if [ -n "$(git status --porcelain)" ]; then
	echo "The checkout has uncommitted changes; commit or stash them first." >&2
	exit 1
fi
git fetch -q origin
if [ "$(git rev-parse HEAD)" != "$(git rev-parse "$target^{commit}" 2>/dev/null)" ]; then
	echo "$tag was released from $target, but this checkout is at $(git rev-parse --short HEAD)." >&2
	echo "Run: git checkout $target" >&2
	exit 1
fi

VERSION="${tag#v}" mac/build.sh
rm -f mac/dist/cc-switcher-mac.zip
# ditto keeps the bundle's signature and symlinks intact, which zip doesn't.
ditto -c -k --keepParent "mac/dist/CC Switcher.app" mac/dist/cc-switcher-mac.zip
gh release upload "$tag" mac/dist/cc-switcher-mac.zip --repo "$REPO" --clobber
echo "Attached cc-switcher-mac.zip to $tag"
