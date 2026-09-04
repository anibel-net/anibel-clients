#requires -Version 7
# Anibel Clients — one-time env bootstrap (Windows).
$ErrorActionPreference = "Stop"

Write-Host "== 1. .NET 10 SDK ==" -NoNewline; Write-Host ""
$dotnetSdks = (dotnet --list-sdks) 2>$null
if ($dotnetSdks -match "^10\.") {
    Write-Host "   found: $($dotnetSdks | Select-Object -First 1)"
} else {
    Write-Host "   installing Microsoft.DotNet.SDK.10 ..."
    winget install --id Microsoft.DotNet.SDK.10 --silent --accept-source-agreements --accept-package-agreements --disable-interactivity
}

Write-Host "== 2. Rust toolchain (stable + msvc target) =="
rustup default stable 2>$null
rustup target add x86_64-pc-windows-msvc

Write-Host "== 3. WinUI templates =="
dotnet new install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates

Write-Host "== 4. Verify build chain =="
cargo build -p anibel-core
dotnet build apps/windows/src/Anibel.App.csproj -c Debug -p:Platform=x64

Write-Host "== done =="
Write-Host "Run:  cargo test -p anibel-core"
Write-Host "Run:  dotnet run --project apps/windows/src/Anibel.App.csproj -c Debug -p:Platform=x64"
