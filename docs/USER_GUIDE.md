# User guide

MechwarriorTooling lets you inspect the original game's assets and prepare edited
working copies. It does not install the game, automatically mod a live installation
or turn an imported mesh into a fully registered BattleMech.

## First launch

On Windows, install the .NET 10 SDK and run these commands from the repository root:

```powershell
./Build.ps1
./Run-MechwarriorTooling.ps1 -GameDirectory 'D:/Games/MechWarrior'
```

Alternatively launch without a directory and choose **Open installation**. Select
the directory containing the game's files, not the port executable or an archive
that has not been extracted. Keep the complete installation available: geometry,
palettes and executable-authored Mech definitions are separate inputs.

The workshop is Windows-only. The CLI runs separately through .NET on other
platforms; [CLI setup](ASSET_INTERCHANGE.md#cli-setup) shows its invocation.

## Finding what you want

| I want to see… | Open |
| --- | --- |
| A complete BattleMech or its walk/jump poses | **Mechs**; select the chassis, sequence and frame. |
| One arm, leg, hill, missile or building shape | **Models**; choose a bank and resource. |
| The yellow/black objective building | **Models**, then the objective-building shortcut. |
| A battlefield tile or assembled scenario | **Maps**; choose the tile/scenario. |
| Explosion, smoke and other effect geometry | **SFX** or the miscellaneous bank on Models. |
| A sound effect and its waveform | **Sounds**; select an event and use playback controls. |
| Campaign/cockpit pictures, or import/export controls | **Asset interchange…**, then the appropriate tab. |

The lower panes describe the selected asset, supported edits, format information
and changes. Drag the preview to rotate and use the wheel to zoom. The second
Mech preview turns automatically; pause it when comparing a pose. Available
animations depend on the chassis: not every Mech has jump jets.

## What a preview does—and does not—show

Mechs are assembled from the game's part shapes. This is an orthographic asset
preview, not a running combat mission. The turntable and animation playback speed
are viewer settings, not the Mech's movement speed or game timing.

Choose a landscape palette when judging colours. Patterned EGA surfaces may look
checkered; Blender's solid material previews cannot show those patterns faithfully.
Maps rotate a ground plane and placed objects together. Arctic ground is white;
**Soften white** changes only its display/export appearance, not the native data.

Sounds audition supported events. Exporting an effect to WAV is different from
putting a new WAV into the game: the original sound bank is a sequenced format.

## Make your first safe edit

1. Create an authoring workspace outside the original installation. Keep your
   Blender/Aseprite/audio-editor projects there as well as exported files.
2. Open the original installation, select an asset and inspect its information.
3. Use the **Editor** pane for a supported vertex, colour, height or placement
   change. Use Undo/Redo while that document remains open.
4. Choose **Save copy** and a new output filename outside the installation.
   Existing files are not overwritten, even if a save dialog asks for confirmation.
5. Choose **Open working copy** to inspect the saved TBL, GRD or WLD after opening
   the installation. A WLD preview needs its matching GRD alongside it. A GRD can
   be inspected without a companion WLD, but will not show those placements.
6. Test gameplay only in a separate copy of the original installation. Keep the
   expected game filename when installing a replacement in that disposable copy.

Use a fresh filename for each revision. Undo is bounded and is not a substitute
for keeping your authoring sources. Inspect **Changes** before saving; opening a
different file is not a persistent multi-file editing project.

The vertex editor retains authored normals; it is intended for small corrections,
not automatic reshading of a redesigned mesh. The editor refuses vectors which
also own a face normal. Use the OBJ workflow when rebuilding topology/normals.

For topology changes or new geometry, use [the Blender workflow](MODEL_AUTHORING.md),
not the single-vertex editor. [The map guide](MAP_AUTHORING.md) explains which
files must stay together when terrain and placements change.

## Asset export versus a preview PNG

The page's preview **Export PNG** captures an inspection render. It is useful for
reference, but is not the editable native sprite or model file. For Aseprite use
the interchange window's **2D / PNG** export or `graphics export-png`. For Blender
use OBJ export, not a screenshot. See [interchange recipes](ASSET_INTERCHANGE.md).

## Before sharing a replacement

Confirm it reopens, looks right in relevant animations/palettes, and works in a
copied game through actual play. Parsing successfully does not establish collision,
damage behaviour or mission compatibility. Check NOTICE.md: a modified export
can still contain original copyrighted content. Do not share the installation,
private saves or extracted original asset packs.
