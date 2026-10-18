# Asset interchange: Blender, Aseprite, WAV and MIDI

This is the authoring/extraction workflow. It does **not** require completing
the native renderer. The desktop's **Asset interchange…** button opens four
tabs: Blender/OBJ, indexed PNG artwork, sampled audio, and music/MIDI.
The CLI provides the same formats plus assembled-Mech and whole-bank exports.
Originals stay read-only. Always write new files outside the installation.
Builds, exports and extracted copyrighted material belong in ignored artifacts
or a separate working directory, not source control.

## CLI setup

From the repository root, after `./Build.ps1`:

```powershell
$cli = './src/MechwarriorTooling.Cli/bin/Release/net10.0/MechwarriorTooling.Cli.dll'
$game = 'D:/Games/MechWarrior' # Your legally owned installation.
$work = Join-Path $PWD 'workspaces/asset-lab'
New-Item -ItemType Directory -Force -Path $work | Out-Null
dotnet $cli help
```

Commands below never overwrite existing files. Use a new output name each
iteration. Decimal and `0x` resource IDs are accepted. Core/CLI are portable
.NET 10; the desktop is Windows-only. Paths in these examples are illustrative.

## 3D: OBJ/MTL for Blender

Export an individual resource, every resource in a bank, or an assembled
six-part Mech pose:

```powershell
dotnet $cli models list --bank "$game/locpck.tbl" --game-dir $game
dotnet $cli models export-obj --bank "$game/locpck.tbl" --resource 14 --output "$work/locust-arm.obj"
dotnet $cli models export-obj --bank "$game/jenpck.tbl" --all --output "$work/jenner-all-shapes.obj"
dotnet $cli mechs export-obj --game-dir $game --mech Jenner --sequence 1 --frame 0 --output "$work/jenner-walk-0.obj"
dotnet $cli models export-obj --bank "$game/othpck.tbl" --resource 14 --output "$work/objective-building.obj"
```

Each CLI export creates `.obj`, `.mtl`, and `.obj.mw.json`. The desktop's
individual-resource export writes OBJ/MTL only. The JSON carries source
fingerprint, resource data and, when the reviewed executable is available,
body-part/animation usage. It is reference metadata, not an automatically applied
gameplay/chassis patch. OBJ contains **all polygon variants/detail records**,
not just the first one used by the preview renderer. Point/ellipse primitives
are not polygon meshes and are not converted to invented spheres.

Objects are named `mw_r0014_e000_v00_d00`: resource, entry, variant, detail record.
For a whole Mech bank these are alternative part shapes at their authored
origins, not objects laid out in an artificial grid. They overlap: isolate
resource objects in Blender, or use an assembled pose export for initial inspection.
Animations are exported as authored frame geometry, **not** a skinned skeleton
or Blender action. Native animation maps remain in the executable.

### Blender procedure

1. Import Wavefront OBJ with Z as Up and Y as Forward so native A/B/C remains
   Blender X/Y/Z. Keep the same axes when exporting back. A file unit is a
   scaled native world-coordinate unit, not a claim that one unit is a metre.
2. Edit geometry, apply object transforms, and preserve resource/object names
   for multi-resource round trips. Export the intended meshes, not cameras/lights.
3. Use flat faces and native palette materials. Do not bake a texture or expect
   Blender lighting to become game shading. Textures/UVs are not in this format.
4. Export OBJ and import into a **new native bank** using the CLI below. Keep
   n-gons small; triangles/quads are the safest authoring choice.

Material names encode native fields, for example `mw_p07_l00_e07_o00_f81`:
primary +1, primary +2, EGA surface +4, EGA outline +3, polygon flags.
The MTL RGB is merely a solid Blender preview of the EGA material; it cannot
express native screen-space dithering. Changing only MTL `Kd` does not change
the game material: preserve or deliberately rename the encoded material.
Supported imported colour indices are 0–31. New objects default to that example
material if none is selected. Face winding controls recalculated Q14 normals.

```powershell
# Replace the selected resource; topology/vertex counts may change.
dotnet $cli models import-obj --bank "$game/locpck.tbl" --resource 14 --input "$work/locust-arm-edited.obj" --output "$work/locpck-edited.tbl"

# Rebuild all polygon-only resources from the complete exported bank.
dotnet $cli models import-obj --bank "$game/jenpck.tbl" --all --input "$work/jenner-all-shapes-edited.obj" --output "$work/jenpck-edited.tbl"

# Append a newly authored, untagged Blender mesh to TERPCK's existing 30 resources.
dotnet $cli models import-obj --bank "$game/terpck.tbl" --resource 30 --input "$work/new-building.obj" --output "$work/terpck-expanded.tbl"
```

Import reconstructs near references and the paragraph-relative directory.
It emits native archive method 00, recalculates ordinary face normals and
preserves explicitly authored normals for original degenerate faces. Existing
entry-order reference vectors and resource headers are retained where matched;
new entries get a geometric reference centre. Independent imported objects no
longer claim a shared vector allocation. It is a topology replacement, not
byte-identical restoration of the original pointer graph or compression stream.

Limits: 48 entries per resource, 255 vectors per entry **including reference
vectors and normals**, at most 20 vertices per face, signed 16-bit coordinates
after undoing the resource scale, and a 16-bit near allocation per resource.
Split an oversized mesh into objects, simplify it, and keep faces small.
Imported groups become independent entries with one polygon variant/detail
record each; the exporter exposes all variants, but import does not reconstruct
an original multi-variant graph from those objects. Mixed non-mesh resources are
retained during `--all` imports; import a single resource to intentionally replace
such a resource with mesh geometry. Native bitmap/picture primitive relocation
(kinds 2/3, absent from these installed banks) is explicitly unsupported.

### Adding Mechs versus adding geometry

These commands enable extraction, editing, replacement and appendable geometry.
They do **not** expand the game's hardcoded eight chassis, fixed roster/schema,
animation availability tables, collision data or resource-ID consumers.
For a replacement Mech, retain the expected resource slots and supply its part
shapes for every referenced animation frame. For a ninth chassis, the game-side
tables/loaders still need explicit expansion. Bigger replacement meshes also
need their native resource bounds/scale and collision representation reviewed;
an OBJ import is not a promise of a validated new playable Mech.

## 2D: indexed PNG / Aseprite

Supported original artwork:

- All combat tagged `.bmp` banks (`BMP:INF:/BIN:`), including cockpit art,
  Mech inspection pictures, HUD glyphs and prompt images.
- Combat `.scr` screens (`SCR:BIN:`), 320×200 pixels.
- All campaign `MW_*PICS.BIN` whole-bank files and per-record `MW_PICS.BIN`.

These `.bmp` files are **not Windows BMPs**. Commands exchange their actual
native pixels, not the window screenshot/export button's rendered previews.

```powershell
dotnet $cli graphics inspect --file "$game/prompt.bmp"
dotnet $cli graphics export-png --file "$game/prompt.bmp" --image 0 --output "$work/prompt.png"
dotnet $cli graphics export-png --file "$game/STATUS.SCR" --image 0 --output "$work/status.png"
dotnet $cli graphics export-png --file "$game/MW_CPICS.BIN" --all --output "$work/campaign-art"
dotnet $cli graphics export-png --file "$game/MW_PICS.BIN" --all --output "$work/indexed-campaign-art"

# Edit the exported PNG in Aseprite, retaining its indexed palette and canvas.
dotnet $cli graphics import-png --file "$game/prompt.bmp" --image 0 --input "$work/prompt-edited.png" --output "$work/prompt-edited.bmp"
dotnet $cli graphics import-png --file "$game/MW_CPICS.BIN" --image 1 --input "$work/campaign-art/image-001.png" --output "$work/mw_cpics-edited.bin"
```

PNG export is indexed, with sixteen native colours. Combat BMP index zero
is transparent; SCR is opaque. Campaign PNGs default to opaque; add
`--transparent-zero` to **both** export/import when editing a zero-masked sprite.
Whole campaign banks supply their own six-bit RGB palette. Combat artwork and
indexed campaign records use `--palette FILE.pal` if specified, otherwise sibling
`tropic.pal`, or initial EGA colours when that file is absent.
Always supply the same palette when re-exporting a working copy elsewhere.
For a renamed copy of `MW_PICS.BIN`, use `--format indexed`; for other campaign
copies use `--format campaign`. `--format bitmap` selects combat BMP/SCR.

Aseprite: keep the exported palette; use nearest-neighbour/pencil tools, disable
antialiasing, and retain the canvas dimensions. Save PNG for import; `.aseprite`
project files remain your editable source, not a native input format. PNG import
accepts indexed 1/2/4/8-bit and 8-bit RGB/RGBA/greyscale, all standard PNG row
filters, but not interlace. Out-of-palette pixels, partial alpha and changed
dimensions are rejected instead of silently degrading the artwork.

Combat writes preserve other tagged chunks and replace BIN with method 00.
Campaign writes preserve record padding and other pictures, using the native
nibble RLE; indexed replacements append a record and update its directory slot.
Fonts and PAL-file authoring are not included in this PNG image workflow.

## Sounds: WAV and new native MWPCM

```powershell
# Extract an original speaker event into either external WAV or sampled MWPCM.
dotnet $cli sounds export-wav --bank "$game/btechsnd.snd" --event 8 --seconds 10 --output "$work/shot.wav"
dotnet $cli sounds export-pcm --bank "$game/btechsnd.snd" --event 8 --seconds 10 --output "$work/shot.mwpcm"

# Import new/edited sampled audio without PC-speaker pitch approximation.
dotnet $cli sounds import-wav --input "$work/new-laser.wav" --output "$work/new-laser.mwpcm"
dotnet $cli sounds import-wav --input "$work/walking.wav" --output "$work/walking.mwpcm" --loop-start 1000 --loop-end 12000
dotnet $cli sounds inspect-pcm --input "$work/walking.mwpcm"
dotnet $cli sounds export-wav --input "$work/walking.mwpcm" --output "$work/walking-edit.wav"
```

MWPCM stores signed 16-bit little-endian mono/stereo PCM at 8–192 kHz, with no
resampling or pitch conversion. WAV PCM 8/16/24/32-bit and float32 input is
accepted. 16-bit samples round-trip exactly; higher precision is rounded/clamped
to S16. Source channels/rate are retained. WAV `smpl` forward infinite loops
round-trip as sample-frame positions; explicit CLI loop options override them.
No loop is `0/0`; a loop is `[start,end)` within the frame count. Speaker event
loops are rendered to the requested bounded window and report truncation; their
true loop points are not guessed from that recording.

The desktop interchange tab can open WAV/MWPCM, play/pause/stop, edit loop fields
and save either format. Playback auditions the whole clip, not an implementation
of sample-range loop transport. Loop points are preserved in saved files.

**Runtime boundary:** MWPCM is now a native authoring/tooling asset format, not
an original DOS SND bank or an already connected runtime override. The game still
uses its original sound scheduler. Loading MWPCM for gameplay, event-to-file
bindings, mixing and loop execution must be connected at the port's audio
boundary separately. No SND file is replaced with incompatible sampled bytes.
See [the exact MWPCM v1 specification](MWPCM_FORMAT.md).

## Music: MIDI

```powershell
dotnet $cli music export-midi --input "$game/mech.mus" --output "$work/intro.mid"
dotnet $cli music import-midi --input "$work/intro-edited.mid" --output "$work/mech-edited.mus"
```

The five installed songs already have `MThd`/`MTrk` format-0 MIDI containers;
conversion preserves bytes. Native songs contain MIDI real-time Start events
as well as channel/meta/SysEx events. Validation retains them and running status.
For new music, export a **single-track MIDI format 0 with PPQN timing** from the
music editor; format 1/2 and SMPTE division are rejected. This does not synthesize
MIDI to WAV or guarantee a new song's instrument/controllers work on every original
backend. Preserve game-specific events when editing an extracted song.

## Check the workflows

`Build.ps1 -GameDirectory PATH -VerifyUi` runs synthetic/private verification and
detached UI checks. `tests/Verify-InterchangeCli.ps1 -GameDirectory PATH` exercises
CLI disk exports/reimports; `tests/Verify-DocumentationExamples.ps1 -GameDirectory PATH`
checks the documented fresh-resource and map-edit examples. Outputs stay ignored.

These checks validate formats, not an actual Blender/Aseprite session or in-game
acceptance of a newly authored asset. Test replacements in a copied installation.
See [models](MODEL_AUTHORING.md), [maps](MAP_AUTHORING.md),
[formats](FORMATS.md) and [troubleshooting](TROUBLESHOOTING.md).
