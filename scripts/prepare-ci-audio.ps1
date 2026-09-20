#requires -Version 7
# Test infrastructure only. Never install this driver on a developer or user PC.
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'This script is only for disposable GitHub-hosted Windows runners.'
}
Start-Service AudioEndpointBuilder
Start-Service Audiosrv
$directory = Join-Path $env:RUNNER_TEMP 'anibel-ci-audio'
New-Item -ItemType Directory -Force $directory | Out-Null
$archive = Join-Path $directory 'Scream4.0.zip'
Invoke-WebRequest 'https://github.com/duncanthrax/scream/releases/download/4.0/Scream4.0.zip' -OutFile $archive
if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne 'FA33E25F9A46C61E4E0CD83362C51C3D2A45C6FE4091AAD7507E240E40F1A520') {
    throw 'CI audio driver checksum mismatch.'
}
Expand-Archive $archive (Join-Path $directory 'scream')
$install = Join-Path $directory 'scream/Install'
$certificate = (Get-AuthenticodeSignature (Join-Path $install 'driver/x64/Scream.sys')).SignerCertificate
if (-not $certificate) { throw 'CI audio driver has no signer certificate.' }
$store = [System.Security.Cryptography.X509Certificates.X509Store]::new('TrustedPublisher', 'LocalMachine')
try { $store.Open('ReadWrite'); $store.Add($certificate) } finally { $store.Dispose() }
& (Join-Path $install 'helpers/devcon-x64.exe') install (Join-Path $install 'driver/x64/Scream.inf') '*Scream'
if ($LASTEXITCODE -ne 0) { throw "CI audio driver installation failed: $LASTEXITCODE" }
$device = Get-CimInstance Win32_SoundDevice | Where-Object { $_.Name -like '*Scream*' -and $_.Status -eq 'OK' }
if (-not $device) { throw 'CI audio device is not ready.' }
$device | Select-Object Name, Status
