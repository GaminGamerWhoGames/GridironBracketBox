# Makes dist\GridironBracketBox-linux-kit.tar.gz for building the AppImage on Linux:
#   the source code (as of the last git commit) + the Linux program compiled here on Windows.
# Usage (from the project root):   .\make-linux-kit.ps1      then follow LINUX.md on the Linux machine.

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if (git status --porcelain) {
    Write-Warning "You have uncommitted changes; the kit's source is the LAST COMMIT. Commit first if you want them included."
}

Write-Host "1/3  Running tests..." -ForegroundColor Cyan
dotnet test GridironBracketBox.slnx -c Release
if ($LASTEXITCODE -ne 0) { throw "Tests failed - not packaging." }

Write-Host "2/3  Compiling the linux-x64 single-file program..." -ForegroundColor Cyan
$bin = Join-Path $PSScriptRoot 'dist\linux-x64'
if (Test-Path $bin) { Remove-Item $bin -Recurse -Force }
dotnet publish src/GridironBracketBox/GridironBracketBox.csproj -c Release -r linux-x64 -o $bin
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
Remove-Item (Join-Path $bin '*.pdb') -ErrorAction SilentlyContinue

Write-Host "3/3  Packing the kit..." -ForegroundColor Cyan
$staging = Join-Path $PSScriptRoot 'dist\kit-staging'
$root = Join-Path $staging 'GridironBracketBox'
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $root 'dist\linux-x64') | Out-Null

# Source = clean copy of the last commit (.gitattributes keeps the Linux scripts' LF line endings).
$srcTar = Join-Path $staging 'src.tar'
git archive --format=tar -o $srcTar HEAD
tar -xf $srcTar -C $root
Remove-Item $srcTar
Copy-Item (Join-Path $bin 'GridironBracketBox') (Join-Path $root 'dist\linux-x64\')

$kit = Join-Path $PSScriptRoot 'dist\GridironBracketBox-linux-kit.tar.gz'
tar -czf $kit -C $staging GridironBracketBox
Remove-Item $staging -Recurse -Force

Write-Host ("Done: {0}  ({1:N1} MB)" -f $kit, ((Get-Item $kit).Length / 1MB)) -ForegroundColor Green
Write-Host "Copy it to Linux and follow LINUX.md."
