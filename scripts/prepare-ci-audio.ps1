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
$archive = Join-Path $directory 'Scream3.6.zip'
Invoke-WebRequest 'https://github.com/duncanthrax/scream/releases/download/3.6/Scream3.6.zip' -OutFile $archive
if ((Get-FileHash $archive -Algorithm SHA256).Hash -ne '25EA5E778B4E6995A98D448B9B5F6D321F681663F1AEEEC69D8E63183D008B19') {
    throw 'CI audio driver checksum mismatch.'
}
Expand-Archive $archive (Join-Path $directory 'scream')
$install = Join-Path $directory 'scream/Install'
$signature = Get-AuthenticodeSignature (Join-Path $install 'driver/Scream.sys')
if ($signature.Status -ne 'Valid') { throw 'CI audio driver signature is invalid.' }
$certificate = $signature.SignerCertificate
if (-not $certificate) { throw 'CI audio driver has no signer certificate.' }
$store = [System.Security.Cryptography.X509Certificates.X509Store]::new('TrustedPublisher', 'LocalMachine')
try { $store.Open('ReadWrite'); $store.Add($certificate) } finally { $store.Dispose() }
& (Join-Path $install 'helpers/devcon.exe') install (Join-Path $install 'driver/Scream.inf') '*Scream'
if ($LASTEXITCODE -ne 0) { throw "CI audio driver installation failed: $LASTEXITCODE" }
$device = Get-CimInstance Win32_SoundDevice | Where-Object { $_.Name -like '*Scream*' -and $_.Status -eq 'OK' }
if (-not $device) { throw 'CI audio device is not ready.' }
$device | Select-Object Name, Status
