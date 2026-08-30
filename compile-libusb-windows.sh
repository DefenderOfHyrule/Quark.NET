#!/bin/bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

LIBUSB_TAG="v1.0.30"
BUILD_DIR="/tmp/libusb-build-windows"
OUT_DIR="$SCRIPT_DIR/libs/windows"
TARGET="${1:-both}"

for tool in autoreconf make; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        echo "[ERROR] $tool not found on PATH."
        exit 1
    fi
done

rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR" "$OUT_DIR"

echo "Cloning libusb $LIBUSB_TAG..."
git clone --depth 1 --branch "$LIBUSB_TAG" https://github.com/libusb/libusb.git "$BUILD_DIR/src"

echo "Running autoreconf..."
cd "$BUILD_DIR/src"
autoreconf --install --force

build_arch() {
    local name="$1" triple="$2" out_name="$3"
    local prefix="$BUILD_DIR/out-$name"
    local build="$BUILD_DIR/build-$name"

    if ! command -v "${triple}-gcc" >/dev/null 2>&1; then
        echo "[ERROR] ${triple}-gcc not found. Install the MinGW-w64 toolchain for $name."
        exit 1
    fi

    echo ""
    echo "Building $name ($triple)..."
    mkdir -p "$build"
    cd "$build"

    "$BUILD_DIR/src/configure" \
        --host="$triple" \
        --prefix="$prefix" \
        --disable-dependency-tracking

    mkdir -p libusb/.deps

    make -j1
    make install

    cp "$prefix/bin/libusb-1.0.dll" "$OUT_DIR/$out_name"
    echo "✓  $out_name"
}

case "$TARGET" in
    x64)
        build_arch x64   x86_64-w64-mingw32  libusb-quark.dll
        ;;
    arm64)
        build_arch arm64 aarch64-w64-mingw32 libusb-quark-arm64.dll
        ;;
    both)
        build_arch x64   x86_64-w64-mingw32  libusb-quark.dll
        build_arch arm64 aarch64-w64-mingw32 libusb-quark-arm64.dll
        ;;
    *)
        echo "[ERROR] Unknown target '$TARGET'. Use 'x64', 'arm64', or 'both'."
        exit 1
        ;;
esac

echo ""
echo "Done. Output in $OUT_DIR"
