param(
    [string]$GameDirectory = $(if ($env:MECHWARRIOR_ASSET_DIRECTORY) { $env:MECHWARRIOR_ASSET_DIRECTORY } else { Join-Path $PSScriptRoot 'assets/original-game' }),
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$Build
)
$ErrorActionPreference = 'Stop'
$desktop = Join-Path $PSScriptRoot "src/MechwarriorTooling/bin/$Configuration/net10.0-windows/MechwarriorTooling.dll"
if ($Build -or !(Test-Path -LiteralPath $desktop)) {
    & (Join-Path $PSScriptRoot 'Build.ps1') -Configuration $Configuration
}
if (Test-Path -LiteralPath $GameDirectory) {
    & dotnet $desktop --game-dir (Resolve-Path -LiteralPath $GameDirectory).Path
} else {
    & dotnet $desktop
}
if ($LASTEXITCODE -ne 0) { throw "MechwarriorTooling exited with code $LASTEXITCODE." }
