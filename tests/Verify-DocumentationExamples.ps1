param(
    [Parameter(Mandatory)][string]$GameDirectory,
    [string]$CliPath = (Join-Path $PSScriptRoot '../src/MechwarriorTooling.Cli/bin/Release/net10.0/MechwarriorTooling.Cli.dll')
)
$ErrorActionPreference = 'Stop'
$game = (Resolve-Path -LiteralPath $GameDirectory).Path
$cli = (Resolve-Path -LiteralPath $CliPath).Path
$output = Join-Path $PSScriptRoot ('../artifacts/documentation-examples-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output | Out-Null
function Invoke-Tool {
    $result = & dotnet $cli @args
    if ($LASTEXITCODE -ne 0) { throw "Documented CLI example failed: $args" }
    return $result
}
$originals = @('terpck.tbl','tile0.grd','tile0.wld')
$hashes = @{}
foreach ($name in $originals) { $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $game $name)).Hash }

# Exercise the fresh OBJ printed in the guide, not a different test mesh.
$guide = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../docs/MODEL_AUTHORING.md'))
$match = [regex]::Match($guide, '(?s)```obj\r?\n(.*?)\r?\n```')
if (!$match.Success) { throw 'The documented new-resource OBJ example is missing.' }
$mesh = Join-Path $output 'new-building.obj'
[IO.File]::WriteAllText($mesh, $match.Groups[1].Value, [Text.UTF8Encoding]::new($false))
$summary = (Invoke-Tool models inspect --bank (Join-Path $game 'terpck.tbl') --resource 0 --game-dir $game) | ConvertFrom-Json
$append = [int]$summary.resources
if ($append -ne 30) { throw 'This example targets the reviewed 30-resource terrain bank.' }
$bank = Join-Path $output 'terpck-expanded.tbl'
$result = (Invoke-Tool models import-obj --bank (Join-Path $game 'terpck.tbl') --resource $append --input $mesh --output $bank) | ConvertFrom-Json
if ($result.resources -ne $append + 1) { throw 'Appended resource count did not increase.' }
$rebuilt = (Invoke-Tool models inspect --bank $bank --resource $append --game-dir $game) | ConvertFrom-Json
if ($rebuilt.resource.Entries.Count -ne 1) { throw 'New building did not reopen as one entry.' }
Invoke-Tool models render --bank $bank --resource $append --palette (Join-Path $game 'tropic.pal') --output (Join-Path $output 'new-building.png') | Out-Null

$grid = Join-Path $output 'tile0-height-v1.grd'
$world = Join-Path $output 'tile0-objects-v1.wld'
Invoke-Tool maps edit-height --grid (Join-Path $game 'tile0.grd') --column 10 --row 10 --height 12 --output $grid | Out-Null
if ([IO.File]::ReadAllBytes($grid)[10 * 47 + 10] -ne 12) { throw 'Height edit did not update the documented cell.' }
$tile = (Invoke-Tool maps inspect --grid (Join-Path $game 'tile0.grd') --world (Join-Path $game 'tile0.wld')) | ConvertFrom-Json
if (!$tile.placements.Count) { throw 'The example tile needs at least one placement.' }
$placement = $tile.placements[0]
Invoke-Tool maps edit-placement --world (Join-Path $game 'tile0.wld') --object 0 --resource 2 --a $placement.PositionA --b $placement.PositionB --altitude $placement.Altitude --terrain-bank (Join-Path $game 'terpck.tbl') --output $world | Out-Null
$edited = (Invoke-Tool maps inspect --grid $grid --world $world) | ConvertFrom-Json
if ($edited.placements[0].ResourceId -ne 2 -or $edited.placements[0].Altitude -ne $placement.Altitude) { throw 'Placement edit changed unexpected data.' }
Invoke-Tool maps render --grid $grid --world $world --game-dir $game --view 3d --palette (Join-Path $game 'tropic.pal') --output (Join-Path $output 'tile0-edited.png') | Out-Null
Invoke-Tool maps render --grid (Join-Path $game 'tile0.grd') --game-dir $game --view briefing --output (Join-Path $output 'tile0-top.png') | Out-Null
Invoke-Tool maps render --grid (Join-Path $game 'tile0.grd') --game-dir $game --view 3d --palette (Join-Path $game 'arctic.pal') --output (Join-Path $output 'tile0-arctic.png') | Out-Null
Invoke-Tool scenarios inspect --file (Join-Path $game 'snario.dat') | Out-Null
foreach ($name in $originals) {
    if ((Get-FileHash -LiteralPath (Join-Path $game $name)).Hash -ne $hashes[$name]) { throw "Original file changed: $name" }
}
Write-Host "PASS: documented fresh-resource OBJ, native append/inspect/render, map height/placement/preview and scenario commands. $output"
Write-Host 'This does not test an external Blender session, edited collision data or native gameplay acceptance.'
