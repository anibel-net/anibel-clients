#requires -Version 7
# Builds (and optionally runs) the WinUI 3 app. Release builds pull
# target\release\anibel_core.dll automatically.
Param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$Run
)
$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "..\apps\windows\src\Anibel.App.csproj"
dotnet build $project -c $Configuration -p:Platform=x64

if ($Run) {
    dotnet run --project $project -c $Configuration -p:Platform=x64
}
