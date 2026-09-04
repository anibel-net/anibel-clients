#requires -Version 7
# Builds anibel_core.dll (release) into the workspace target dir.
# The WinUI csproj copies it next to the exe automatically (CopyAnibelCore).
Param(
    [switch]$Clean
)
$ErrorActionPreference = "Stop"

if ($Clean) { cargo clean -p anibel-core }

cargo build --release -p anibel-core

$dll = Join-Path $PSScriptRoot "..\target\release\anibel_core.dll"
if (-not (Test-Path $dll)) {
    throw "anibel_core.dll not found at $dll"
}
Write-Host "OK: $dll"
