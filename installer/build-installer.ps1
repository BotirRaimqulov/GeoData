# Build and package GeoData Pro installer
# Run from the repo root: .\installer\build-installer.ps1
# Requires: .NET 9 SDK, Inno Setup 6 (iscc on PATH or detected below)

param(
    [string]$Configuration = "Release",
    [string]$Runtime       = "win-x64",
    [string]$PublishDir    = "publish\win-x64"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path $PSScriptRoot -Parent

Write-Host "=== GeoData Pro Installer Build ===" -ForegroundColor Cyan

# 1. dotnet publish
$proj = Join-Path $RepoRoot "src\GeoDataPro.App\GeoDataPro.App.csproj"
$out  = Join-Path $RepoRoot $PublishDir

Write-Host "`n[1/2] Publishing $proj ..." -ForegroundColor Yellow
dotnet publish $proj `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -o $out `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "      Output: $out" -ForegroundColor Green

# 2. Inno Setup compile
$iss = Join-Path $PSScriptRoot "GeoDataPro.iss"

# Try common install locations if iscc not on PATH
$iscc = "iscc"
if (-not (Get-Command iscc -ErrorAction SilentlyContinue)) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\iscc.exe",
        "${env:ProgramFiles}\Inno Setup 6\iscc.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) { $iscc = $c; break }
    }
}

Write-Host "`n[2/2] Compiling installer with Inno Setup ..." -ForegroundColor Yellow
& $iscc $iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed" }

$setupExe = Join-Path $PSScriptRoot "Output\GeoDataPro-1.1.0-Setup.exe"
Write-Host "`nDone! Installer: $setupExe" -ForegroundColor Green
