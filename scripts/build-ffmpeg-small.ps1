#requires -Version 7
param([string]$MsysRoot = 'C:\msys64')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$bash = Join-Path $MsysRoot 'usr/bin/bash.exe'
if (-not (Test-Path -LiteralPath $bash)) {
    throw 'Install MSYS2 and its UCRT64 gcc, pkgconf, libxml2, zlib, nasm packages, plus make.'
}
Push-Location $repoRoot
try {
    & $bash scripts/build-ffmpeg-small.sh
    if ($LASTEXITCODE -ne 0) { throw 'Minimal FFmpeg build failed.' }
    $runtime = Join-Path $repoRoot 'artifacts/ffmpeg-small/runtime'
    $destination = Join-Path $repoRoot 'apps/windows/src/Assets/ffmpeg/x64'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -Path "$runtime/*" -Destination $destination -Recurse -Force
    $bytes = (Get-ChildItem -LiteralPath $destination -File -Recurse | Measure-Object Length -Sum).Sum
    Write-Host ('FFmpeg runtime ready: {0:N2} MiB' -f ($bytes / 1MB))
} finally {
    Pop-Location
}
