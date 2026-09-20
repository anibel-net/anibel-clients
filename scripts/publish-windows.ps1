#requires -Version 7
Param(
    [string]$ArchiveDirectory = (Join-Path $PSScriptRoot '..\artifacts\share'),
    [string]$Version,
    [string]$UpdateRepository = '',
    [switch]$Installer,
    [string]$ReleaseDirectory = (Join-Path $PSScriptRoot '..\artifacts\releases')
)
$ErrorActionPreference = 'Stop'
if (-not $Version) {
    [xml]$versions = Get-Content (Join-Path $PSScriptRoot '../apps/windows/version.props') -Raw
    $Version = $versions.Project.PropertyGroup.Version
}
if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-beta\.(0|[1-9][0-9]*))?$') {
    throw 'Use a version such as 0.2.0 or 0.3.0-beta.1.'
}
$channel = if ($Version.Contains('-')) { 'win-x64-preview' } else { 'win-x64' }
if ($Installer -and $UpdateRepository -notmatch '^https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/?$') {
    throw 'Installer builds require -UpdateRepository https://github.com/owner/public-repository.'
}
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repository 'apps\windows\src\Anibel.App.csproj'
foreach ($required in @('target/release/anibel_core.dll', 'apps/windows/src/Assets/ffmpeg/x64/ffmpeg.exe', 'apps/windows/src/Assets/ffmpeg/x64/ffprobe.exe', 'apps/windows/src/Assets/libass/x64/libass-9.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $repository $required))) { throw "Missing release dependency: $required" }
}
# A fresh folder is essential: publish does not remove old, unused runtime DLLs.
$staging = Join-Path $repository ('artifacts\windows-publish-' + [Guid]::NewGuid().ToString('N'))
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $visualStudio) { throw 'Visual Studio C++ x64 build tools are required.' }
$crtVersions = Get-ChildItem (Join-Path $visualStudio 'VC\Redist\MSVC') -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } |
    Sort-Object { [version]$_.Name } -Descending
$crtDirectory = $crtVersions | ForEach-Object {
    Get-ChildItem (Join-Path $_.FullName 'x64') -Directory -Filter 'Microsoft.VC*.CRT' -ErrorAction SilentlyContinue
} | Where-Object { Test-Path (Join-Path $_.FullName 'vcruntime140.dll') } |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $crtDirectory) { throw 'VC++ x64 runtime files were not found.' }

dotnet publish $project -c Release -r win-x64 -p:Platform=x64 `
    -p:OptimizeDistribution=true -p:EnablePlaybackSmoke=false `
    "-p:Version=$Version" "-p:UpdateRepository=$UpdateRepository" "-p:UpdateChannel=$channel" `
    -p:IntermediateOutputPath=obj/distribution/ --self-contained true `
    -p:WindowsAppSDKSelfContained=true -o $staging
if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
Copy-Item (Join-Path $crtDirectory '*.dll') $staging
Copy-Item (Join-Path $repository 'LICENSE') (Join-Path $staging 'LICENSE.txt')
@'
Anibel.Net for Windows x64

Extract the whole archive into a folder, then open Anibel.Net.exe.
Keep all DLLs, Assets, and licenses next to the app.
The .NET and Windows App SDK runtimes are included.
The WebView2 runtime is needed for embedded web players.
'@ | Set-Content (Join-Path $staging 'READ-ME-FIRST.txt') -Encoding utf8

New-Item -ItemType Directory -Force $ArchiveDirectory | Out-Null
$archive = Join-Path ([IO.Path]::GetFullPath($ArchiveDirectory)) 'Anibel-Windows-x64.zip'
$stream = [IO.File]::Open($archive, [IO.FileMode]::Create)
try {
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem $staging -File -Recurse) {
            $name = [IO.Path]::GetRelativePath($staging, $file.FullName).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $file.FullName, $name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $zip.Dispose() }
} finally { $stream.Dispose() }
$hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content ($archive + '.sha256') ($hash + '  ' + [IO.Path]::GetFileName($archive)) -Encoding ascii
Write-Host ('Archive: {0} ({1:N2} MiB)' -f $archive, ((Get-Item $archive).Length / 1MB))
Write-Host ('Unpacked build: ' + $staging)

if ($Installer) {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Could not restore the pinned Velopack tool.' }
    dotnet tool run vpk pack --packId Anibel.Net --packVersion $Version --packDir $staging `
        --mainExe Anibel.Net.exe --packTitle Anibel.Net --packAuthors Anibel.Net `
        --icon (Join-Path $repository 'apps/windows/src/Assets/AppIcon.ico') `
        --channel $channel --outputDir $ReleaseDirectory --noPortable
    if ($LASTEXITCODE -ne 0) { throw 'Installer packaging failed.' }
}
