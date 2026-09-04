#requires -Version 7
# Fetches the patched libmpv build (zhongfly/mpv-winbuild - carries the
# d3d11-composition / display-swapchain patches used by WinUI3 frontends).
# Extracts libmpv-2.dll into apps/windows/src/Assets/libmpv/x64/
#
# Supply-chain policy (ARCHITECTURE.md §11.4): pin a release tag via
#   ./fetch-mpv.ps1 -Version "<tag>"
# for reproducible builds. Every download records the SHA-256 of the archive
# and the extracted DLL into libmpv-2.dll.sha256; on later runs the local DLL
# is re-verified against that record (corruption / silent replacement guard).
#
# Param:
#   -Version "latest" | "<release tag>"   (pin a tag for reproducible builds)
Param([string]$Version = "latest")
$ErrorActionPreference = "Stop"

$destRoot = Join-Path $PSScriptRoot "..\apps\windows\src\Assets\libmpv\x64"
New-Item -ItemType Directory -Path $destRoot -Force | Out-Null
$dllPath = Join-Path $destRoot "libmpv-2.dll"
$hashPath = Join-Path $destRoot "libmpv-2.dll.sha256"

function Get-Sha256([string]$File) {
    (Get-FileHash -Algorithm SHA256 $File).Hash.ToLowerInvariant()
}

# Integrity check of an existing install — skip the download only when the
# recorded hash still matches.
if ((Test-Path $dllPath) -and (Test-Path $hashPath)) {
    $expected = (Get-Content $hashPath | Select-Object -First 1).Trim().Split(' ')[0]
    $actual = Get-Sha256 $dllPath
    if ($actual -eq $expected) {
        Write-Host "OK: libmpv-2.dll already present and hash-verified ($($actual.Substring(0,16))…)"
        exit 0
    }
    Write-Warning "libmpv-2.dll hash mismatch — re-downloading."
}

$sevenZip = @(
    "C:\Program Files\7-Zip\7z.exe",
    "C:\Program Files (x86)\7-Zip\7z.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($Version -eq "latest") {
    Write-Warning "Fetching 'latest' mpv build — pin a -Version tag for reproducible builds."
    $release = Invoke-RestMethod "https://api.github.com/repos/zhongfly/mpv-winbuild/releases/latest" -TimeoutSec 30
} else {
    $release = Invoke-RestMethod "https://api.github.com/repos/zhongfly/mpv-winbuild/releases/tags/$Version" -TimeoutSec 30
}
Write-Host "Release: $($release.tag_name)"

# x86_64 dev build — the only mpv-winbuild asset carrying libmpv-2.dll
# (d3d11 composition + display-swapchain patches). Non-v3 for broad CPU support.
$asset = $release.assets | Where-Object {
    $_.name -match "^mpv-dev-x86_64-" -and $_.name -notmatch "lgpl|v3|debug" -and $_.name -match "\.7z$"
} | Sort-Object name | Select-Object -Last 1

if (-not $asset) {
    throw "No x86_64 7z asset found in $($release.tag_name)"
}
Write-Host "Asset: $($asset.name) ($([math]::Round($asset.size/1MB,1)) MB)"

$tmp = Join-Path $env:TEMP "mpv-winbuild-$(Get-Random)"
New-Item -ItemType Directory -Path $tmp -Force | Out-Null
$archive = Join-Path $tmp $asset.name

Write-Host "Downloading..."
Invoke-WebRequest $asset.browser_download_url -OutFile $archive -TimeoutSec 1800

Write-Host "Extracting (7z)…"
if ($sevenZip) {
    & $sevenZip x $archive "-o$tmp" -y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "7z extraction failed" }
} else {
    throw "7-Zip not found - install 7zip.7zip (winget) and rerun"
}

$dll = Get-ChildItem -Path $tmp -Recurse -Filter "libmpv-2.dll" | Select-Object -First 1
if (-not $dll) {
    throw "libmpv-2.dll not found in archive"
}
Copy-Item $dll.FullName (Join-Path $destRoot "libmpv-2.dll") -Force
$archiveHash = Get-Sha256 $archive
$dllHash = Get-Sha256 $dllPath
Set-Content -Path $hashPath -Value "$dllHash  libmpv-2.dll`n$archiveHash  $($asset.name)"
Write-Host "OK: $dllPath ($([math]::Round((Get-Item $dllPath).Length/1MB,1)) MB)"
Write-Host "SHA-256: $dllHash"
Write-Host "Note: mpv builds are GPL-licensed - license/source links required on distribution."
