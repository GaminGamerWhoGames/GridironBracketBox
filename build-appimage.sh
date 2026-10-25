#!/usr/bin/env bash
# Builds the Linux AppImage:  dist/GridironBracketBox-x86_64.AppImage
# Usage (on Linux, from the project root):   bash build-appimage.sh
#
# - If the .NET 10 SDK is installed (sudo pacman -S dotnet-sdk), the app is tested and compiled here.
# - Otherwise it uses the Linux program already compiled on Windows (dist/linux-x64/GridironBracketBox,
#   which is included in the Linux kit made by make-linux-kit.ps1).
# Needs internet the first time (downloads appimagetool into build/tools/).
set -euo pipefail
cd "$(dirname "$(readlink -f "$0")")"

APP=GridironBracketBox
BIN_DIR=dist/linux-x64
OUT=dist/$APP-x86_64.AppImage
WORK=build/appimage
TOOL=build/tools/appimagetool-x86_64.AppImage
TOOL_URL=https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage

step() { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }

if command -v dotnet >/dev/null 2>&1; then
    step "1/4  .NET SDK found: running tests"
    dotnet test GridironBracketBox.slnx -c Release
    step "2/4  Compiling the linux-x64 single-file program"
    rm -rf "$BIN_DIR"
    dotnet publish src/GridironBracketBox/GridironBracketBox.csproj -c Release -r linux-x64 -o "$BIN_DIR"
    rm -f "$BIN_DIR"/*.pdb   # debug symbols aren't needed to run
elif [[ -f "$BIN_DIR/$APP" ]]; then
    step "1/4  No .NET SDK: using the program compiled on Windows ($BIN_DIR/$APP)"
    step "2/4  (compile skipped)"
else
    echo "Error: no .NET SDK and no prebuilt $BIN_DIR/$APP." >&2
    echo "Either install the SDK (sudo pacman -S dotnet-sdk) or use the Linux kit made on Windows." >&2
    exit 1
fi

step "3/4  Assembling the AppDir (the folder that becomes the AppImage)"
APPDIR=$WORK/AppDir
rm -rf "$WORK"
mkdir -p "$APPDIR/usr/bin"
install -m 755 "$BIN_DIR/$APP"                              "$APPDIR/usr/bin/$APP"
install -m 755 packaging/linux/AppRun                       "$APPDIR/AppRun"
install -m 644 packaging/linux/gridiron-bracket-box.desktop "$APPDIR/gridiron-bracket-box.desktop"
install -m 644 src/GridironBracketBox/Assets/app.png        "$APPDIR/gridiron-bracket-box.png"
ln -s gridiron-bracket-box.png "$APPDIR/.DirIcon"

step "4/4  Packing the AppImage"
if [[ ! -x "$TOOL" ]]; then
    mkdir -p "$(dirname "$TOOL")"
    curl -fL --progress-bar -o "$TOOL" "$TOOL_URL"
    chmod +x "$TOOL"
fi
mkdir -p dist
# APPIMAGE_EXTRACT_AND_RUN lets appimagetool itself run even if FUSE isn't installed.
ARCH=x86_64 APPIMAGE_EXTRACT_AND_RUN=1 "$TOOL" --no-appstream "$APPDIR" "$OUT"

printf '\n\033[1;32mDone: %s (%s)\033[0m\n' "$OUT" "$(du -h "$OUT" | cut -f1)"
echo "Run it:   ./$OUT"
echo "If it complains about FUSE:   sudo pacman -S fuse2   (or run it with --appimage-extract-and-run)"
