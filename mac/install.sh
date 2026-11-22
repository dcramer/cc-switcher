#!/bin/bash
# Installs or updates CC Switcher for macOS from the latest release:
#
#   curl -fsSL https://raw.githubusercontent.com/dcramer/cc-switcher/main/mac/install.sh | bash
#
# Puts "CC Switcher.app" in ~/Applications and a `cc-switcher` command in ~/.local/bin.
# Profiles and sign-ins in ~/.cc-switcher are left as they are.
set -euo pipefail

REPO="dcramer/cc-switcher"
APPS="$HOME/Applications"
APP="$APPS/CC Switcher.app"
BIN="$HOME/.local/bin"

fail() { echo "CC Switcher: $*" >&2; exit 1; }

[ "$(uname -s)" = "Darwin" ] || fail "this installer is for macOS; on Windows use the setup.exe from the releases page."
major="$(sw_vers -productVersion | cut -d. -f1)"
[ "$major" -ge 13 ] || fail "needs macOS 13 or later (this Mac has $(sw_vers -productVersion))."

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# The Mac app is added to releases after CI makes them, so the newest release may not have it
# yet: take the newest one that does. Releases are listed newest first.
URL="${CC_SWITCHER_DOWNLOAD_URL:-}"
if [ -z "$URL" ]; then
	URL="$(curl -fsSL "https://api.github.com/repos/$REPO/releases?per_page=30" \
		| grep -o '"browser_download_url": *"[^"]*/cc-switcher-mac\.zip"' \
		| head -n 1 | sed -E 's/.*"(https[^"]*)"$/\1/')" || true
	[ -n "$URL" ] || fail "couldn't find a release with the Mac app at https://github.com/$REPO/releases"
fi

echo "Downloading $(basename "$(dirname "$URL")")…"
curl -fsSL "$URL" -o "$tmp/cc-switcher-mac.zip" || fail "couldn't download $URL"
ditto -x -k "$tmp/cc-switcher-mac.zip" "$tmp/unpacked" || fail "the download isn't a valid zip."
[ -d "$tmp/unpacked/CC Switcher.app" ] || fail "the download doesn't contain CC Switcher.app."

# Close a running copy so it can be replaced. Claude itself keeps running.
if pgrep -x "CC Switcher" >/dev/null; then
	osascript -e 'quit app id "com.cc-switcher.app"' >/dev/null 2>&1 || true
	sleep 1
	pkill -x "CC Switcher" 2>/dev/null || true
fi

mkdir -p "$APPS"
rm -rf "$APP"
ditto "$tmp/unpacked/CC Switcher.app" "$APP"
/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$APP"
echo "Installed $APP"

mkdir -p "$BIN"
printf '#!/bin/sh\nexec "%s/Contents/MacOS/CC Switcher" "$@"\n' "$APP" >"$BIN/cc-switcher"
chmod +x "$BIN/cc-switcher"
case ":$PATH:" in
*":$BIN:"*) echo "Installed the cc-switcher command" ;;
*) echo "Installed the cc-switcher command in $BIN (add it to your PATH to use it)" ;;
esac

open "$APP"
echo "Done. CC Switcher is open; it's also in Spotlight and ~/Applications."
