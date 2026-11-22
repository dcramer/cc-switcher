#!/bin/bash
# Builds "CC Switcher.app" into mac/dist. With --install, also copies it to ~/Applications.
# Requires Xcode or the Command Line Tools (swiftc).
set -euo pipefail

cd "$(dirname "$0")"
# CI passes the release version; local builds are 0.0.0.
VERSION="${VERSION:-0.0.0}"
APP="dist/CC Switcher.app"
MIN_MACOS="13.0"

rm -rf dist
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" dist/obj

SDK="$(xcrun --sdk macosx --show-sdk-path)"
for arch in arm64 x86_64; do
	xcrun --sdk macosx swiftc -O -swift-version 5 \
		-sdk "$SDK" -target "$arch-apple-macos$MIN_MACOS" \
		-o "dist/obj/cc-switcher-$arch" Sources/*.swift
done
lipo -create -output "$APP/Contents/MacOS/CC Switcher" dist/obj/cc-switcher-arm64 dist/obj/cc-switcher-x86_64
rm -rf dist/obj

cat >"$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleExecutable</key><string>CC Switcher</string>
	<key>CFBundleIdentifier</key><string>com.cc-switcher.app</string>
	<key>CFBundleName</key><string>CC Switcher</string>
	<key>CFBundleDisplayName</key><string>CC Switcher</string>
	<key>CFBundlePackageType</key><string>APPL</string>
	<key>CFBundleIconFile</key><string>AppIcon</string>
	<key>CFBundleShortVersionString</key><string>$VERSION</string>
	<key>CFBundleVersion</key><string>$VERSION</string>
	<key>LSMinimumSystemVersion</key><string>$MIN_MACOS</string>
	<key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
	<key>NSHighResolutionCapable</key><true/>
	<key>NSHumanReadableCopyright</key><string>Unofficial; not affiliated with Anthropic. MIT license.</string>
	<key>NSAppleEventsUsageDescription</key>
	<string>CC Switcher sends sign-in links to the Claude profile that asked for them, and brings a profile's window forward.</string>
	<key>CFBundleURLTypes</key>
	<array>
		<dict>
			<key>CFBundleURLName</key><string>Claude link</string>
			<key>CFBundleURLSchemes</key><array><string>claude</string></array>
		</dict>
	</array>
</dict>
</plist>
EOF

# App icon from docs/icon.png.
ICONSET="dist/AppIcon.iconset"
mkdir -p "$ICONSET"
for s in 16 32 128 256 512; do
	sips -z $s $s ../docs/icon.png --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
	sips -z $((s * 2)) $((s * 2)) ../docs/icon.png --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"
rm -rf "$ICONSET"

# Ad-hoc signature: enough to run locally; not notarized.
codesign --force --sign - "$APP"
echo "Built $APP"

if [ "${1:-}" = "--install" ]; then
	DEST="$HOME/Applications/CC Switcher.app"
	mkdir -p "$HOME/Applications"
	pkill -x "CC Switcher" 2>/dev/null || true
	rm -rf "$DEST"
	cp -R "$APP" "$DEST"
	/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister -f "$DEST"
	echo "Installed $DEST"

	# `cc-switcher` on the command line, if ~/.local/bin exists.
	if [ -d "$HOME/.local/bin" ]; then
		printf '#!/bin/sh\nexec "%s/Contents/MacOS/CC Switcher" "$@"\n' "$DEST" >"$HOME/.local/bin/cc-switcher"
		chmod +x "$HOME/.local/bin/cc-switcher"
		echo "Installed $HOME/.local/bin/cc-switcher"
	fi
fi
