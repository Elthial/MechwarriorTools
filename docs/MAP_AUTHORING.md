# Power user: maps, placements and new buildings

Map editing is currently **tile editing**, not a complete mission/scenario editor.
Keep these inputs separate:

| Input | Purpose |
| --- | --- |
| GRD | Stored height/collision grid. |
| WLD | Ordered placements referencing terrain-bank resources, with position and altitude. |
| TERPCK.TBL | The visible terrain/building geometry used by those placements. |
| GI collision data | Separate collision information; not generated from an OBJ. |
| SNARIO.DAT | Scenario tile/formation selection; browsing is supported, authoring is not. |
| PAL and executable landscape definitions | Ground/sky/polygon appearance for the chosen landscape. |

A scenario assembles four tiles. Selecting it in the workshop does not create a
new contract or change enemy forces. The campaign star map is a different system.

## Inspect a tile

Use the common `$cli`, `$game` and `$work` setup from [interchange](ASSET_INTERCHANGE.md#cli-setup).

```powershell
dotnet $cli maps inspect --grid "$game/tile0.grd" --world "$game/tile0.wld"
dotnet $cli maps render --grid "$game/tile0.grd" --game-dir $game --view briefing --output "$work/tile0-top.png"
dotnet $cli maps render --grid "$game/tile0.grd" --game-dir $game --view 3d --palette "$game/arctic.pal" --output "$work/tile0-arctic.png"
dotnet $cli scenarios inspect --file "$game/snario.dat"
```

The desktop Maps page supports cell inspection, stored height edits and edits to
existing object placements. The left view stays fixed for picking; drag/wheel in
the right view to orbit/zoom. Scenario assembly is read-only.

## Change a height

```powershell
dotnet $cli maps edit-height --grid "$game/tile0.grd" --column 10 --row 10 --height 12 --output "$work/tile0-height-v1.grd"
```

Cells use columns 0–46 and rows 0–86; stored heights are 0–255. They are not
documented metres. The visible ground is a flat native-coloured plane with placed
terrain geometry, not a generated mesh of GRD heights. Editing a height does not
automatically reshape a hill model or make a matching Blender terrain mesh.

## Change an existing placement

Inspect first and reuse its position/altitude when changing only the shape:

```powershell
$tile = dotnet $cli maps inspect --grid "$game/tile0.grd" --world "$game/tile0.wld" | ConvertFrom-Json
$placement = $tile.placements[0] # Choose a tile with at least one placement.
dotnet $cli maps edit-placement --world "$game/tile0.wld" --object 0 --resource 2 --a $placement.PositionA --b $placement.PositionB --altitude $placement.Altitude --terrain-bank "$game/terpck.tbl" --output "$work/tile0-objects-v1.wld"
```

`--object` selects an existing WLD row; `--resource` selects geometry inside the
supplied terrain bank. `--a` and `--b` are the CLI's planar world-coordinate
arguments, not GRD column/row numbers. Altitude is separate. Start from an existing
placement rather than guessing a metres-to-world conversion or ground origin.

The editor/CLI does not insert or delete WLD rows. A new placement table or complete
scenario requires an external, format-aware authoring step and separate validation.
Do not describe changing one row as creating a whole new mission.

## Preview edited pairs

For CLI preview, point to the GRD and WLD explicitly; they need not share a stem:

```powershell
dotnet $cli maps render --grid "$work/tile0-height-v1.grd" --world "$work/tile0-objects-v1.wld" --game-dir $game --view 3d --palette "$game/tropic.pal" --output "$work/tile0-edited.png"
```

For **Open working copy** in the desktop, give a WLD its matching same-stem GRD
alongside it. Save grid and placement edits separately; saving one does not bundle
the other. Keep originals and intermediate copies clearly named.

## Put a new model on a tile

1. Follow [model authoring](MODEL_AUTHORING.md) to replace or append a TERPCK resource.
2. Reference that resource from an existing WLD row, passing the **edited bank** to
   `maps edit-placement --terrain-bank` so its ID can be checked.
3. Assemble a private copied installation containing the edited TERPCK and tile
   files under the expected filenames. Keep the unmodified installation intact.
4. Render against that copy using `maps render --game-dir` and the edited tile pair.
   The renderer loads TERPCK from this directory; `--terrain-bank` is an edit-command
   argument, not a map-render override.
5. Check native collision requirements and then test actual movement, visibility,
   targeting and weapon impacts in a copied game. An appended render resource may
   lack corresponding collision/consumer support even though the workshop shows it.

An ordinary WLD building is not automatically a contract objective. The game's
special objective building has separate mission handling; importing or placing
a visually similar mesh does not assign those rules to it.

## Not currently an import path

For a Blender reference scene, export individual TERPCK resources as OBJ and use
`maps inspect`'s placement coordinates to position them yourself. The tool does
not export a complete placed Blender scene, and importing those meshes again
does not read Blender object transforms back into WLD placements.

There is no Blender-scene-to-WLD/GRD importer, PNG-heightmap importer, automatic
collision-bank generator or scenario/formation writer. OBJ import creates model
bank geometry only. Use existing placement/height edits for the supported path;
do not rename an OBJ, image or modern map format to `.wld` or `.grd`.
