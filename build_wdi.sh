#!/bin/bash

set -e

TARGET_ARCH="${1:-x64}"

LIBUSBK_MSYS=/c/libusbK-3.1.0.0-bin/bin

LIBWDI_SRC=libs/libwdi

REPO="$(cd "$(dirname "$0")" && pwd)"
LIBWDI_ABS="$REPO/$LIBWDI_SRC"
WDI_C="$REPO/wdi/quark_wdi.c"
OUT_DIR="$REPO/libs/windows"

SDK_COPY_MSYS=/c/quark-wdi-sdk
SDK_COPY_WIN='C:\quark-wdi-sdk'
SDK_COPY_WIN_CESC='C:\\\\quark-wdi-sdk'
LIBUSBK_DIR_CESC='C:\\quark-wdi-sdk'

touch_in_order() {
    for f in "$@"; do
        touch "$f"
        sleep 1
    done
}

case "$TARGET_ARCH" in
    x64)
        PATCHED_DIR="$REPO/build/libwdi-patched-x64"
        OUT_DLL="$OUT_DIR/quark-wdi.dll"
        HOST_TRIPLE="x86_64-w64-mingw32"
        DRIVER_TYPE_DEFINE="WDI_LIBUSBK"
        DRIVER_NAME_DEFINE="libusbK"
        DRIVER_CAT_DEFINE="libusbK.cat"
        NEEDS_LIBUSBK_SDK=1
        NEEDS_WDK_STUB=0
        TOOLCHAIN_CANDIDATES=("/mingw64/bin")
        ;;
    arm64)
        PATCHED_DIR="$REPO/build/libwdi-patched-arm64"
        OUT_DLL="$OUT_DIR/quark-wdi-arm64.dll"
        HOST_TRIPLE="aarch64-w64-mingw32"
        DRIVER_TYPE_DEFINE="WDI_WINUSB"
        DRIVER_NAME_DEFINE="WinUSB"
        DRIVER_CAT_DEFINE="winusb.cat"
        NEEDS_LIBUSBK_SDK=0
        NEEDS_WDK_STUB=1
        TOOLCHAIN_CANDIDATES=("/c/llvm-mingw/bin" "/c/llvm-mingw-x86_64/bin")
        ;;
    *)
        echo "[ERROR] Unknown target '$TARGET_ARCH'. Use 'x64' or 'arm64'."
        exit 1
        ;;
esac

for dir in "${TOOLCHAIN_CANDIDATES[@]}"; do
    if [ -x "$dir/${HOST_TRIPLE}-gcc" ] || [ -x "$dir/${HOST_TRIPLE}-gcc.exe" ]; then
        export PATH="$dir:$PATH"
        echo "Using toolchain at $dir for $HOST_TRIPLE"
        break
    fi
done

echo ""
echo "============================================================"
echo " Quark.NET - building $(basename "$OUT_DLL")"
echo "============================================================"
echo " Target:     $TARGET_ARCH ($HOST_TRIPLE)"
echo " Driver:     $DRIVER_TYPE_DEFINE"
echo " libwdi src: $LIBWDI_ABS"
echo " Output:     $OUT_DLL"
echo "============================================================"
echo ""

if [ ! -f "$LIBWDI_ABS/configure.ac" ]; then
    echo "[ERROR] libwdi source not found at $LIBWDI_ABS"
    echo "        Grab it from https://github.com/pbatard/libwdi and place it there"
    echo "        (OmniRCM already vendors a copy under its own libs/libwdi you can reuse)."
    exit 1
fi

if ! command -v "${HOST_TRIPLE}-gcc" >/dev/null 2>&1; then
    echo "[ERROR] ${HOST_TRIPLE}-gcc not found on PATH."
    if [ "$TARGET_ARCH" = "arm64" ]; then
        echo "        Use the standalone llvm-mingw toolchain, which"
        echo "        ships x64-native binaries that can cross-compile for"
        echo "        aarch64-w64-mingw32:"
        echo "          1. Download llvm-mingw-<date>-ucrt-x86_64.zip from"
        echo "             https://github.com/mstorsjo/llvm-mingw/releases"
        echo "          2. Extract it anywhere, e.g. C:\\llvm-mingw"
        echo "          3. Add its bin\\ dir to PATH for this shell, e.g.:"
        echo "                 export PATH=\"/c/llvm-mingw/bin:\$PATH\""
        echo "        No ARM64 hardware needed, this cross-compiles fine"
        echo "        from an ordinary x64 machine."
    else
        echo "        Install the MinGW-w64 x86_64 toolchain, e.g.:"
        echo "            pacman -S mingw-w64-x86_64-gcc"
    fi
    exit 1
fi

echo "Using compiler: $(command -v "${HOST_TRIPLE}-gcc")"
GCC_VERSION_LINE="$("${HOST_TRIPLE}-gcc" --version 2>&1 | head -1)"
echo "  ($GCC_VERSION_LINE)"
if [ "$TARGET_ARCH" = "x64" ] && echo "$GCC_VERSION_LINE" | grep -qi "clang"; then
    echo ""
    echo "[WARNING] This is Clang, not GNU GCC, but the x64/libusbK build's"
    echo "          configure.ac was only ever patched to work around"
    echo "          Clang/LLD's missing --add-stdcall-alias support for the"
    echo "          arm64/WinUSB build, expect the same 'unknown argument:"
    echo "          --add-stdcall-alias' failure here. This usually means"
    echo "          llvm-mingw's bin directory is earlier on PATH than"
    echo "          MSYS2's own /mingw64/bin. Check with:"
    echo "              echo \$PATH"
    echo "          and put /mingw64/bin first, or open a fresh MINGW64"
    echo "          shell rather than reusing one that's had llvm-mingw"
    echo "          added to PATH."
    echo ""
fi

if [ ! -f "$WDI_C" ]; then
    echo "[ERROR] quark_wdi.c not found at $WDI_C"
    exit 1
fi

if [ "$NEEDS_LIBUSBK_SDK" = "1" ]; then
    if [ ! -d "$LIBUSBK_MSYS" ]; then
        echo "[ERROR] libusbK SDK/bin not found at $LIBUSBK_MSYS"
        echo "        Download the libusbK binary SDK (e.g. from"
        echo "        https://github.com/mcuee/libusbk/releases or the libusb-win32"
        echo "        successor project) and extract it so that this path exists,"
        echo "        or edit LIBUSBK_MSYS above to point at wherever you extracted it."
        echo "        (If you already set this up for OmniRCM, you can reuse the same SDK.)"
        exit 1
    fi

    echo "[1/5] Copying SDK to $SDK_COPY_WIN..."
    rm -rf "$SDK_COPY_MSYS"
    cp -r "$LIBUSBK_MSYS" "$SDK_COPY_MSYS"

    for arch in amd64 x86; do
        src="$SDK_COPY_MSYS/sys/$arch/WdfCoInstaller01009.dll"
        dst="$SDK_COPY_MSYS/sys/$arch/WdfCoInstaller1009.dll"
        [ -f "$src" ] && [ ! -f "$dst" ] && cp "$src" "$dst"
    done
    echo "    done."
elif [ "$NEEDS_WDK_STUB" = "1" ]; then
    echo "[1/5] Creating placeholder WDK redist files (WinUSB coinstallers are"
    echo "      unused on ARM64, see comment in this script for why)..."
    rm -rf "$SDK_COPY_MSYS"
    mkdir -p "$SDK_COPY_MSYS/redist/winusb/x86" "$SDK_COPY_MSYS/redist/winusb/amd64"
    mkdir -p "$SDK_COPY_MSYS/redist/wdf/x86" "$SDK_COPY_MSYS/redist/wdf/amd64"
    for arch in x86 amd64; do
        printf 'stub' > "$SDK_COPY_MSYS/redist/winusb/$arch/winusbcoinstaller2.dll"
        printf 'stub' > "$SDK_COPY_MSYS/redist/wdf/$arch/WdfCoInstaller01009.dll"
    done
    echo "    done."
else
    echo "[1/5] Skipping libusbK SDK copy (not needed for WinUSB build)."
fi

echo ""
echo "[2/5] Copying and patching libwdi source..."
rm -rf "$PATCHED_DIR"
mkdir -p "$(dirname "$PATCHED_DIR")"
cp -r "$LIBWDI_ABS" "$PATCHED_DIR"

if [ "$NEEDS_WDK_STUB" = "1" ]; then
    python3 - "$PATCHED_DIR/configure.ac" << 'PYEOF'
import sys
path = sys.argv[1]
with open(path) as f:
    lines = f.readlines()

start_marker = "# AC_CHECK_FILES only works when not cross compiling\n"
end_marker = "\tif test \"x$LIBUSB0_DIR\" != \"x\"; then\n"

start = next(i for i, l in enumerate(lines) if l == start_marker)
end = next(i for i, l in enumerate(lines) if l == end_marker and i > start)

replacement = [
    'if test "x$WDK_DIR" != "x"; then\n',
    '\tCOINSTALLER_DIR="winusb"\n',
    '\tX64_DIR="amd64"\n',
    '\tAC_DEFINE_UNQUOTED([COINSTALLER_DIR], ["${COINSTALLER_DIR}"], [CoInstaller subdirectory for WinUSB redist files ("winusb" or "wdf")])\n',
    '\tAC_SUBST([COINSTALLER_DIR])\n',
    '\tAC_DEFINE_UNQUOTED([X64_DIR], ["${X64_DIR}"], [64bit subdirectory for WinUSB redist files ("x64" or "amd64")])\n',
    '\tAC_SUBST([X64_DIR])\n',
    '\tAC_DEFINE_UNQUOTED([WDK_DIR], ["${WDK_DIR_CESC}"], [embed WinUSB driver files from the following WDK location])\n',
    'fi\n',
    '\n',
]

lines[start:end] = replacement

for i, l in enumerate(lines):
    if l == "fi\n" and i > start and lines[i + 1] == "\n" and lines[i + 2] == "# Message logging\n":
        del lines[i]
        break
else:
    raise SystemExit("could not find the trailing 'fi' before '# Message logging', configure.ac may have changed upstream")

with open(path, 'w') as f:
    f.writelines(lines)

print(f"Replaced lines {start+1}-{end} ({end-start} lines) with {len(replacement)} lines, and removed the stray trailing fi")
PYEOF

    perl -i -pe '
        s/AM_LDFLAGS="-Wl,--add-stdcall-alias"/AM_LDFLAGS=""/;
        s/-Wno-stringop-truncation//;
    ' "$PATCHED_DIR/configure.ac"

    python3 - "$PATCHED_DIR/libwdi/Makefile.am" << 'PYEOF'
import sys
path = sys.argv[1]
with open(path) as f:
    lines = f.readlines()

start = next(i for i, l in enumerate(lines) if l == "if OPT_M64\n")
end = next(i for i, l in enumerate(lines) if l == "endif\n" and i > start)
del lines[start:end + 1]

with open(path, 'w') as f:
    f.writelines(lines)

print(f"Removed lines {start+1}-{end+1} (the 'if OPT_M64...endif' installer_x64 block)")
PYEOF

    perl -i -pe 's/^(\t\{ 0, INSTALLER_PATH_64 "\\\\installer_x64\.exe", "\."\ \},)$/\/\/ $1  -- skipped, not built for this target, not used by quark_wdi.c/' \
        "$PATCHED_DIR/libwdi/embedder_files.h"
else
    perl -i -0pe '
        s/\tif test "x\$WDK_DIR" != "x"; then.*?^\tfi\n//ms;
        s/\tif test "x\$LIBUSB0_DIR" != "x"; then.*?^\tfi\n//ms;
    ' "$PATCHED_DIR/configure.ac"
fi

echo "    done."

echo ""
echo "[3/5] Bootstrapping patched libwdi..."
cd "$PATCHED_DIR"
./bootstrap.sh

echo ""
echo "[4/5] Configuring libwdi..."
if [ "$NEEDS_LIBUSBK_SDK" = "1" ]; then
    ./configure \
        --host="$HOST_TRIPLE" \
        --disable-32bit \
        --with-wdfver=1009 \
        --with-libusbk="$LIBUSBK_DIR_CESC"

    grep 'LIBUSBK_DIR' config.h
elif [ "$NEEDS_WDK_STUB" = "1" ]; then
    export WDK_DIR_CESC="$SDK_COPY_WIN_CESC"
    ./configure \
        --host="$HOST_TRIPLE" \
        --disable-32bit \
        --with-wdfver=1009 \
        --with-wdkdir="$SDK_COPY_WIN"

    grep 'WDK_DIR\|X64_DIR\|COINSTALLER_DIR' config.h
else
    ./configure \
        --host="$HOST_TRIPLE" \
        --disable-32bit \
        --with-wdfver=1009
fi

echo ""
echo "    Building libwdi..."
make -j"$(nproc)"

echo ""
echo "[5/5] Compiling quark_wdi.c -> $(basename "$OUT_DLL")..."
mkdir -p "$OUT_DIR"

"${HOST_TRIPLE}-gcc" \
    -shared \
    -o "$OUT_DLL" \
    "$WDI_C" \
    -DQUARK_WDI_DRIVER_TYPE="$DRIVER_TYPE_DEFINE" \
    "-DQUARK_WDI_DRIVER_NAME=\"$DRIVER_NAME_DEFINE\"" \
    "-DQUARK_WDI_CAT_NAME=\"$DRIVER_CAT_DEFINE\"" \
    -I"$PATCHED_DIR/libwdi" \
    -L"$PATCHED_DIR/libwdi/.libs" \
    "$PATCHED_DIR/libwdi/.libs/libwdi.a" \
    -lsetupapi -lole32 -lshell32 -ladvapi32 -lcfgmgr32 \
    -Wl,--subsystem,windows \
    -static-libgcc \
    -Wl,-Bstatic -lpthread -Wl,-Bdynamic

[ "$NEEDS_LIBUSBK_SDK" = "1" -o "$NEEDS_WDK_STUB" = "1" ] && rm -rf "$SDK_COPY_MSYS"

echo ""
echo "============================================================"
echo " Done!  $OUT_DLL is ready."
echo "============================================================"

