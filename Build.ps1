param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [string]$GameDirectory,
    [switch]$VerifyUi,
    [switch]$VerifyNativeAudio
)
$ErrorActionPreference = 'Stop'
$previousResolver = $env:MSBuildEnableWorkloadResolver
try {
    $env:MSBuildEnableWorkloadResolver = 'false'
    & dotnet build (Join-Path $PSScriptRoot 'MechwarriorTooling.slnx') --configuration $Configuration --nologo -m:1
    if ($LASTEXITCODE -ne 0) { throw 'Tooling build failed.' }
    $verification = Join-Path $PSScriptRoot "tests/MechwarriorTooling.Verification/bin/$Configuration/net10.0/MechwarriorTooling.Verification.dll"
    $verificationArguments = @($verification)
    if ($GameDirectory) {
        $verificationArguments += @('--game-dir', (Resolve-Path -LiteralPath $GameDirectory).Path)
        $referenceReport = Join-Path $PSScriptRoot 'tests/reference/MODEL_BANK_STRUCTURE.json'
        if (Test-Path -LiteralPath $referenceReport) { $verificationArguments += @('--reference-report', $referenceReport) }
    }
    & dotnet @verificationArguments
    if ($LASTEXITCODE -ne 0) { throw 'Tooling verification failed.' }
    if ($VerifyNativeAudio) {
        if (!$GameDirectory) { throw '-VerifyNativeAudio requires -GameDirectory.' }
        & python (Join-Path $PSScriptRoot 'tests/Verify-NativeToneParity.py') --game-dir (Resolve-Path -LiteralPath $GameDirectory).Path --configuration $Configuration
        if ($LASTEXITCODE -ne 0) { throw 'Native-instruction audio parity failed.' }
    }
    if ($VerifyUi) {
        if (!$GameDirectory) { throw '-VerifyUi requires -GameDirectory.' }
        $desktop = Join-Path $PSScriptRoot "src/MechwarriorTooling/bin/$Configuration/net10.0-windows/MechwarriorTooling.dll"
        $captureDirectory = Join-Path $PSScriptRoot ('artifacts/ui-' + [guid]::NewGuid().ToString('N'))
        & dotnet $desktop --game-dir (Resolve-Path -LiteralPath $GameDirectory).Path --verify-ui $captureDirectory
        if ($LASTEXITCODE -ne 0) { throw 'Off-screen UI verification failed. See the MechwarriorTooling-ui-error.txt file in the temporary directory.' }
        Write-Host "Off-screen UI checks and screenshots: $captureDirectory"
    }
}
finally { $env:MSBuildEnableWorkloadResolver = $previousResolver }
