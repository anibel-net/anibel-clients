#requires -Version 7
Param(
    [string]$ArchiveDirectory = (Join-Path $PSScriptRoot '..\artifacts\share')
)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repository 'apps\windows\src\Anibel.App.csproj'
# A fresh folder is essential: publish does not remove old, unused runtime DLLs.
$staging = Join-Path $repository ('artifacts\windows-publish-' + [Guid]::NewGuid().ToString('N'))
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $visualStudio) { throw 'Visual Studio C++ x64 build tools are required.' }
$crt = Get-ChildItem (Join-Path $visualStudio 'VC\Redist\MSVC') -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+$' } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$crtDirectory = Join-Path $crt.FullName 'x64\Microsoft.VC143.CRT'
if (-not (Test-Path $crtDirectory)) { throw 'VC++ x64 runtime files were not found.' }

dotnet publish $project -c Release -r win-x64 -p:Platform=x64 `
    -p:OptimizeDistribution=true -p:EnablePlaybackSmoke=false `
    -p:IntermediateOutputPath=obj/distribution/ --self-contained true `
    -p:WindowsAppSDKSelfContained=true -o $staging
if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
Copy-Item (Join-Path $crtDirectory '*.dll') $staging
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
