# Gridiron Bracket Box

Avalonia (C#, .NET 10) desktop app that replaces paper NFL playoff brackets.
Targets: Windows 10 single-file .exe and Arch Linux AppImage.

## Layout
- `src/GridironBracketBox.Core` — bracket logic, no UI. Bye rules live ONLY in `ByeRules.cs`.
- `src/GridironBracketBox` — Avalonia UI (MVVM, CommunityToolkit.Mvvm).
- `tests/GridironBracketBox.Tests` — xUnit tests; run `dotnet test`.

## Build
- Run: `dotnet run --project src/GridironBracketBox`
- Test: `dotnet test`
- Windows release: `.\build-windows.ps1` → `dist\windows\GridironBracketBox.exe` (self-contained single file).
  Packaging settings live in the app .csproj (PropertyGroup conditioned on RuntimeIdentifier).
- Linux AppImage: on Windows `.\make-linux-kit.ps1` → `dist\GridironBracketBox-linux-kit.tar.gz`
  (source + linux-x64 binary); on Linux `bash build-appimage.sh` → `dist/GridironBracketBox-x86_64.AppImage`.
  Step-by-step for the user: LINUX.md. Linux files must stay LF (.gitattributes).

## Rules
- **DECISIONS.txt:** every time you make a design choice or change, append a dated entry
  (`YYYY-MM-DD — what — why`) to `DECISIONS.txt` in the project root. Never rewrite old entries.
- Matchups are always computed by re-seeding (highest remaining seed vs lowest); never hard-code brackets.
- The user is new to Claude Code: explain steps as you go, and pause after each stage.
