param(
    [Parameter(Mandatory)][string]$GameDirectory,
    [string]$CliPath = (Join-Path $PSScriptRoot '../src/MechwarriorTooling.Cli/bin/Release/net10.0/MechwarriorTooling.Cli.dll')
)
$ErrorActionPreference = 'Stop'
$game = (Resolve-Path -LiteralPath $GameDirectory).Path
$cli = (Resolve-Path -LiteralPath $CliPath).Path
$output = Join-Path $PSScriptRoot ('../artifacts/interchange-cli-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
function Invoke-Tool {
    & dotnet $cli @args | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Interchange CLI failed: $args" }
}
function Assert-Same([string]$First, [string]$Second) {
    if ((Get-FileHash -LiteralPath $First).Hash -ne (Get-FileHash -LiteralPath $Second).Hash) { throw 'Byte-exact interchange comparison failed.' }
}
Invoke-Tool models export-obj --bank (Join-Path $game 'locpck.tbl') --resource 0 --output (Join-Path $output 'locust.obj')
Invoke-Tool models import-obj --bank (Join-Path $game 'locpck.tbl') --resource 0 --input (Join-Path $output 'locust.obj') --output (Join-Path $output 'locust-edited.tbl')
Invoke-Tool models inspect --bank (Join-Path $output 'locust-edited.tbl') --resource 0
Invoke-Tool mechs export-obj --game-dir $game --mech Jenner --sequence 1 --frame 0 --output (Join-Path $output 'jenner-pose.obj')
Invoke-Tool models export-obj --bank (Join-Path $game 'locpck.tbl') --all --output (Join-Path $output 'locust-bank.obj')
Invoke-Tool models import-obj --bank (Join-Path $game 'locpck.tbl') --all --input (Join-Path $output 'locust-bank.obj') --output (Join-Path $output 'locust-bank-edited.tbl')
Invoke-Tool graphics export-png --file (Join-Path $game 'prompt.bmp') --image 0 --output (Join-Path $output 'prompt.png')
Invoke-Tool graphics import-png --file (Join-Path $game 'prompt.bmp') --image 0 --input (Join-Path $output 'prompt.png') --output (Join-Path $output 'prompt-edited.bmp')
Invoke-Tool graphics export-png --file (Join-Path $output 'prompt-edited.bmp') --image 0 --palette (Join-Path $game 'tropic.pal') --output (Join-Path $output 'prompt-again.png')
Assert-Same (Join-Path $output 'prompt.png') (Join-Path $output 'prompt-again.png')
Invoke-Tool graphics export-png --file (Join-Path $game 'MW_CPICS.BIN') --all --output (Join-Path $output 'campaign')
Invoke-Tool graphics import-png --file (Join-Path $game 'MW_CPICS.BIN') --image 1 --input (Join-Path $output 'campaign/image-001.png') --output (Join-Path $output 'campaign-edited.bin')
Invoke-Tool graphics inspect --file (Join-Path $output 'campaign-edited.bin') --format campaign
Invoke-Tool graphics export-png --file (Join-Path $game 'MW_PICS.BIN') --all --output (Join-Path $output 'indexed')
Invoke-Tool sounds export-wav --bank (Join-Path $game 'btechsnd.snd') --event 8 --output (Join-Path $output 'shot.wav')
Invoke-Tool sounds import-wav --input (Join-Path $output 'shot.wav') --output (Join-Path $output 'shot.mwpcm') --loop-start 0 --loop-end 100
Invoke-Tool sounds inspect-pcm --input (Join-Path $output 'shot.mwpcm')
Invoke-Tool sounds export-wav --input (Join-Path $output 'shot.mwpcm') --output (Join-Path $output 'shot-edited.wav')
Invoke-Tool sounds import-wav --input (Join-Path $output 'shot-edited.wav') --output (Join-Path $output 'shot-again.mwpcm')
Assert-Same (Join-Path $output 'shot.mwpcm') (Join-Path $output 'shot-again.mwpcm')
Invoke-Tool sounds export-pcm --bank (Join-Path $game 'btechsnd.snd') --event 8 --output (Join-Path $output 'native-event.mwpcm')
Invoke-Tool music export-midi --input (Join-Path $game 'mech.mus') --output (Join-Path $output 'mech.mid')
Invoke-Tool music import-midi --input (Join-Path $output 'mech.mid') --output (Join-Path $output 'mech-copy.mus')
Assert-Same (Join-Path $game 'mech.mus') (Join-Path $output 'mech-copy.mus')
& dotnet $cli sounds inspect-pcm --input (Join-Path $output 'prompt.png') 2>$null
if ($LASTEXITCODE -eq 0) { throw 'Invalid PCM input unexpectedly accepted.' }
Write-Host "PASS: CLI OBJ/MTL, assembled pose, whole-bank OBJ, indexed PNG, campaign banks, WAV/MWPCM loops and MIDI round trips. $output"
