# Builds the Windows release: one self-contained .exe in dist\windows\
# Usage (from the project root):   .\build-windows.ps1
# Runs on Windows with the .NET 10 SDK installed. The resulting .exe needs nothing installed.

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

Write-Host "1/2  Running tests..." -ForegroundColor Cyan
dotnet test GridironBracketBox.slnx -c Release
if ($LASTEXITCODE -ne 0) { throw "Tests failed - not packaging." }

Write-Host "2/2  Publishing win-x64 single-file exe..." -ForegroundColor Cyan
$out = Join-Path $PSScriptRoot 'dist\windows'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish src/GridironBracketBox/GridironBracketBox.csproj -c Release -r win-x64 -o $out
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

# Debug-symbol files (.pdb) from the graphics libraries aren't needed to run the app.
Remove-Item (Join-Path $out '*.pdb') -ErrorAction SilentlyContinue

$exe = Get-Item (Join-Path $out 'GridironBracketBox.exe')
Write-Host ("Done: {0}  ({1:N1} MB)" -f $exe.FullName, ($exe.Length / 1MB)) -ForegroundColor Green
