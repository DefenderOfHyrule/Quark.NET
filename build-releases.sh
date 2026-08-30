#!/bin/bash

set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

if [ -n "${QUARK_VERSION:-}" ]; then
    VERSION="$QUARK_VERSION"
else
    VERSION=$(sed -n 's/.*CurrentVersion = new(\([0-9]*\), *\([0-9]*\), *\([0-9]*\)).*/\1.\2.\3/p' \
        MainWindow.axaml.cs | head -1)
    VERSION="${VERSION:-0.0.0}"
fi
echo "Building Quark.NET $VERSION"

PROJECT="Quark.NET.csproj"
OUT="$SCRIPT_DIR/releases"
rm -rf "$OUT" && mkdir -p "$OUT"

echo "Cleaning obj/bin to avoid stale Avalonia XAML compiler cache..."
rm -rf "$SCRIPT_DIR/obj" "$SCRIPT_DIR/bin"

IS_WINDOWS=0; IS_LINUX=0; IS_MAC=0
case "$(uname -s 2>/dev/null || echo Windows)" in
    MINGW*|MSYS*|CYGWIN*|Windows) IS_WINDOWS=1 ;;
    Linux)                          IS_LINUX=1   ;;
    Darwin)                         IS_MAC=1     ;;
esac

WDI_DLL="$SCRIPT_DIR/libs/windows/quark-wdi.dll"
WDI_DLL_ARM64="$SCRIPT_DIR/libs/windows/quark-wdi-arm64.dll"
LIBUSB_DLL_ARM64="$SCRIPT_DIR/libs/windows/libusb-quark-arm64.dll"

if [ "$IS_WINDOWS" = "1" ]; then
    if [ "${SKIP_WDI:-0}" = "1" ] && [ -f "$WDI_DLL" ]; then
        echo ""
        echo "- Skipping quark-wdi.dll build (SKIP_WDI=1, dll exists)"
    else
        echo ""
        echo "- Building quark-wdi.dll (libusbK driver installer)..."
        if [ ! -f "$SCRIPT_DIR/build_wdi.sh" ]; then
            echo "  [ERROR] build_wdi.sh not found at repo root."
            echo "          Cannot build quark-wdi.dll."
            exit 1
        fi
        bash "$SCRIPT_DIR/build_wdi.sh" x64
        if [ ! -f "$WDI_DLL" ]; then
            echo "  [ERROR] build_wdi.sh completed but $WDI_DLL was not produced."
            exit 1
        fi
        echo "✓  quark-wdi.dll"
    fi

    if [ "${SKIP_WDI_ARM64:-0}" = "1" ] && [ -f "$WDI_DLL_ARM64" ]; then
        echo ""
        echo "- Skipping quark-wdi-arm64.dll build (SKIP_WDI_ARM64=1, dll exists)"
    else
        echo ""
        echo "- Building quark-wdi-arm64.dll (WinUSB driver installer)..."
        if [ ! -f "$SCRIPT_DIR/build_wdi.sh" ]; then
            echo "  [ERROR] build_wdi.sh not found at repo root."
            echo "          Cannot build quark-wdi-arm64.dll."
            exit 1
        fi
        if bash "$SCRIPT_DIR/build_wdi.sh" arm64; then
            if [ ! -f "$WDI_DLL_ARM64" ]; then
                echo "  [ERROR] build_wdi.sh arm64 completed but $WDI_DLL_ARM64 was not produced."
                exit 1
            fi
            echo "✓  quark-wdi-arm64.dll"
        else
            echo "  [WARN] build_wdi.sh arm64 failed (likely missing the aarch64-w64-mingw32"
            echo "         cross toolchain). Skipping the win-arm64 target for this build."
            SKIP_WIN_ARM64=1
        fi
    fi
elif [ ! -f "$WDI_DLL" ]; then
    echo ""
    echo "[ERROR] $WDI_DLL not found."
    echo "        This host can't build it (build_wdi.sh needs MSYS2/MinGW on"
    echo "        Windows), but the win-x64 target still needs it to exist."
    echo "        Build quark-wdi.dll on Windows via build_wdi.sh, then copy"
    echo "        it to libs/windows/quark-wdi.dll on this machine before"
    echo "        running this script again."
    exit 1
fi

if [ "$IS_WINDOWS" != "1" ] && [ ! -f "$WDI_DLL_ARM64" ]; then
    echo ""
    echo "[WARN] $WDI_DLL_ARM64 not found and this host can't build it."
    echo "       Skipping the win-arm64 target for this build. Build it on"
    echo "       Windows via 'build_wdi.sh arm64', then copy it to"
    echo "       libs/windows/quark-wdi-arm64.dll before running this script"
    echo "       again if you want a win-arm64 release."
    SKIP_WIN_ARM64=1
fi

if [ ! -f "$LIBUSB_DLL_ARM64" ]; then
    echo ""
    echo "[WARN] $LIBUSB_DLL_ARM64 not found."
    echo "       Build it with 'compile-libusb-windows.sh arm64' (needs the"
    echo "       aarch64-w64-mingw32 cross toolchain) and place it at"
    echo "       libs/windows/libusb-quark-arm64.dll. Skipping the win-arm64"
    echo "       target for this build."
    SKIP_WIN_ARM64=1
fi

SKIP_WIN_ARM64="${SKIP_WIN_ARM64:-0}"

publish() {
    local rid="$1" label="$2"
    echo ""
    echo "- Publishing $label ($rid)..."
    dotnet publish "$PROJECT" \
        -c Release \
        -r "$rid" \
        --self-contained true \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:PublishTrimmed=false \
        -p:ApplicationVersion="$VERSION" \
        -o "$OUT/$rid"
}

lipo_bin() {
    local out="$1" in_a="$2" in_b="$3" tool=""
    if command -v lipo >/dev/null 2>&1; then
        tool="lipo"
    elif command -v llvm-lipo >/dev/null 2>&1; then
        tool="llvm-lipo"
    else
        tool="$(compgen -c llvm-lipo- 2>/dev/null | sort -V | tail -1)"
    fi

    if [ -z "$tool" ]; then
        echo "  [ERROR] No lipo/llvm-lipo(-N) found."
        exit 1
    fi
    "$tool" -create -output "$out" "$in_a" "$in_b"
}

make_zip() {
    local src="$1" dest="$2"
    if [ "$IS_WINDOWS" = "1" ]; then
        if command -v 7z >/dev/null 2>&1; then
            7z a -tzip "$dest" "$src" >/dev/null
        else
            powershell -NoProfile -Command \
                "Compress-Archive -Path '$src' -DestinationPath '$dest' -Force"
        fi
    else
        if [ -d "$src" ]; then
            local parent dir
            parent="$(dirname "$src")"
            dir="$(basename "$src")"
            (cd "$parent" && zip -qr "$dest" "$dir")
        else
            local parent file
            parent="$(dirname "$src")"
            file="$(basename "$src")"
            (cd "$parent" && zip -q "$dest" "$file")
        fi
    fi
}

app_bundle() {
    local rid="$1" exe_name="$2"
    local APP="$OUT/$rid/Quark.app"
    mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
    cp "$OUT/$rid/$exe_name" "$APP/Contents/MacOS/Quark"
    chmod +x "$APP/Contents/MacOS/Quark"

    local DYLIB_SRC="$SCRIPT_DIR/libs/macos/libusb-quark.dylib"
    if [ -f "$DYLIB_SRC" ]; then
        cp "$DYLIB_SRC" "$APP/Contents/MacOS/libusb-quark.dylib"
        echo "  bundled libusb"
    else
        echo "  [WARN] $DYLIB_SRC not found."
    fi

    cat > "$APP/Contents/Info.plist" << PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleExecutable</key>         <string>Quark</string>
    <key>CFBundleIdentifier</key>         <string>io.github.defenderofhyrule.quark</string>
    <key>CFBundleName</key>               <string>Quark</string>
    <key>CFBundlePackageType</key>        <string>APPL</string>
    <key>CFBundleShortVersionString</key> <string>$VERSION</string>
    <key>LSMinimumSystemVersion</key>     <string>12.0</string>
    <key>NSHighResolutionCapable</key>    <true/>
    <key>LSArchitecturePriority</key>
    <array><string>arm64</string><string>x86_64</string></array>
    <key>CFBundleIconFile</key>           <string>AppIcon</string>
</dict>
</plist>
PLIST

    if [ -f "Assets/icon.icns" ]; then
        cp "Assets/icon.icns" "$APP/Contents/Resources/AppIcon.icns"
    fi
}

sign_and_notarize() {
    local app="$1"

    if [ "$IS_MAC" = "1" ]; then
        if [ -z "${APPLE_SIGN_ID:-}" ]; then
            echo "  (skipping signing: APPLE_SIGN_ID not set)"
            return
        fi
        echo "  signing with codesign ($APPLE_SIGN_ID)..."
        codesign --force --deep --timestamp --options runtime \
            --entitlements "$SCRIPT_DIR/Quark-macos.entitlements" \
            --sign "$APPLE_SIGN_ID" "$app"
        codesign --verify --deep --strict --verbose=2 "$app"
        echo "  ✓ signed"

        if [ -n "${APPLE_NOTARY_KEY:-}" ]; then
            if [ -z "${APPLE_NOTARY_PROFILE:-}" ]; then
                echo "  [ERROR] APPLE_NOTARY_KEY is set but APPLE_NOTARY_PROFILE is not."
                echo "          Run 'xcrun notarytool store-credentials' once to create a profile."
                exit 1
            fi
            echo "  notarizing via notarytool..."
            local tmpzip
            tmpzip="$(mktemp -t quark-notarize-XXXXXX).zip"
            ditto -c -k --keepParent "$app" "$tmpzip"
            xcrun notarytool submit "$tmpzip" --keychain-profile "$APPLE_NOTARY_PROFILE" --wait
            rm -f "$tmpzip"
            xcrun stapler staple "$app"
            echo "  ✓ notarized + stapled"
        fi
    else
        if [ -z "${APPLE_P12:-}" ]; then
            echo "  (skipping signing: APPLE_P12 not set)"
            return
        fi
        if ! command -v rcodesign >/dev/null 2>&1; then
            echo "  [ERROR] APPLE_P12 is set but rcodesign is not installed/on PATH."
            echo "          Install with: cargo install apple-codesign"
            exit 1
        fi
        if [ -z "${APPLE_P12_PW:-}" ]; then
            echo "  [ERROR] APPLE_P12 is set but APPLE_P12_PW is not."
            exit 1
        fi
        echo "  signing with rcodesign..."
        rcodesign sign --p12-file "$APPLE_P12" --p12-password-file "$APPLE_P12_PW" \
            --code-signature-flags runtime \
            --entitlements-xml-path "$SCRIPT_DIR/Quark-macos.entitlements" \
            "$app"
        echo "  ✓ signed"

        if [ -n "${APPLE_NOTARY_KEY:-}" ]; then
            echo "  notarizing via rcodesign..."
            rcodesign notary-submit --api-key-file "$APPLE_NOTARY_KEY" --staple "$app"
            echo "  ✓ notarized + stapled"
        fi
    fi
}

publish win-x64 "Windows x64"
mv "$OUT/win-x64/Quark.NET.exe" "$OUT/Quark-win-x64.exe"
echo "✓  Quark-win-x64.exe"

if [ "$SKIP_WIN_ARM64" = "1" ]; then
    echo ""
    echo "- Skipping Windows ARM64 (missing quark-wdi-arm64.dll and/or libusb-quark-arm64.dll)"
else
    publish win-arm64 "Windows ARM64"
    mv "$OUT/win-arm64/Quark.NET.exe" "$OUT/Quark-win-arm64.exe"
    echo "✓  Quark-win-arm64.exe"
fi

publish linux-x64 "Linux x64"
mv "$OUT/linux-x64/Quark.NET" "$OUT/Quark-linux-x64"
chmod +x "$OUT/Quark-linux-x64"
echo "✓  Quark-linux-x64"

publish linux-arm64 "Linux ARM64"
mv "$OUT/linux-arm64/Quark.NET" "$OUT/Quark-linux-arm64"
chmod +x "$OUT/Quark-linux-arm64"
echo "✓  Quark-linux-arm64"

publish osx-x64   "macOS x64 (slice)"
publish osx-arm64 "macOS ARM64 (slice)"

echo ""
echo "- Merging into a universal (arm64 + x64) macOS binary..."
mkdir -p "$OUT/osx"

lipo_bin "$OUT/osx/Quark.NET" \
    "$OUT/osx-x64/Quark.NET" "$OUT/osx-arm64/Quark.NET"
chmod +x "$OUT/osx/Quark.NET"
echo "  ✓ universal executable"

app_bundle osx Quark.NET
sign_and_notarize "$OUT/osx/Quark.app"
make_zip "$OUT/osx/Quark.app" "$OUT/Quark-osx.zip"
echo "✓  Quark-osx.zip"

echo ""
echo "════════════════════════════════"
echo " Quark.NET $VERSION - done"
echo "════════════════════════════════"
ls -lh "$OUT"/Quark-* 2>/dev/null || true
