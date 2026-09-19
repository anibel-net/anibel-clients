#requires -Version 7
param(
    [Parameter(Mandatory)][string]$AppExe,
    [string]$FixtureDirectory = 'artifacts/native-playback-test'
)
$ErrorActionPreference = 'Stop'
$application = (Resolve-Path -LiteralPath $AppExe).Path
$fixtures = (Resolve-Path -LiteralPath $FixtureDirectory).Path
$server = Start-Process python -ArgumentList @('-m', 'http.server', '18765', '--bind', '127.0.0.1', '--directory', "`"$fixtures`"") -WindowStyle Hidden -PassThru
$test = $null
try {
    $test = Start-Process $application -ArgumentList @('--native-playback-smoke', "`"$fixtures`"") -WindowStyle Hidden -PassThru
    if (-not $test.WaitForExit(240000)) { throw 'Native playback smoke timed out.' }
    $result = Get-Content (Join-Path $fixtures 'result.txt') -Raw
    Write-Output $result
    if ($test.ExitCode -ne 0 -or $result -notmatch 'ALL PASSED') { throw 'Native playback smoke failed.' }
}
finally {
    if ($test -and -not $test.HasExited) { Stop-Process -Id $test.Id }
    if (-not $server.HasExited) { Stop-Process -Id $server.Id }
}
