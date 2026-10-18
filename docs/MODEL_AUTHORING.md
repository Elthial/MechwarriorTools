# Power user: models and Blender

Start with an unchanged export/import round trip before making a large model.
That establishes your scale, axes, material names and destination resource.
These commands are run from the repository root after a Release build:

```powershell
$cli = './src/MechwarriorTooling.Cli/bin/Release/net10.0/MechwarriorTooling.Cli.dll'
$game = 'D:/Games/MechWarrior'
$work = Join-Path $PWD 'workspaces/model-lab'
New-Item -ItemType Directory -Force -Path $work | Out-Null
```

All outputs below need new filenames. Read originals from `$game`; write outside it.

## Understand the selection

A **bank** contains numbered **resources**. A resource contains entries and their
alternative/detail drawing records. It is not necessarily a whole model or one
animation frame. The chassis banks contain body-part shapes; executable-authored
tables assemble those into logical poses. Shapes may be reused by several sequences.

Use the catalogue before choosing a resource:

```powershell
dotnet $cli models list --bank "$game/locpck.tbl" --game-dir $game
dotnet $cli models inspect --bank "$game/locpck.tbl" --resource 14 --game-dir $game
```

The eight banks are LOCPCK, JENPCK, PHAPCK, SHAPCK, RIFPCK, HAMPCK, MARPCK
and BMAPCK. TERPCK holds terrain; OTHPCK holds miscellaneous/effect geometry,
including the objective building. See [the animation catalogue](ASSET_CATALOGUE_AND_MECH_ANIMATIONS.md).

## Export the right thing

```powershell
# One part shape for replacement:
dotnet $cli models export-obj --bank "$game/locpck.tbl" --resource 14 --output "$work/arm.obj"
# A complete pose for reference:
dotnet $cli mechs export-obj --game-dir $game --mech Jenner --sequence 1 --frame 0 --output "$work/jenner-pose.obj"
# Every polygon shape in a bank:
dotnet $cli models export-obj --bank "$game/jenpck.tbl" --all --output "$work/jenner-bank.obj"
```

CLI export writes OBJ, MTL and `.obj.mw.json` reference metadata. Keep them
together. The desktop's individual-resource export writes OBJ/MTL, without that
CLI sidecar. Import reads the OBJ's geometry and material **names**, not the
sidecar as an automatic gameplay patch.

OBJ objects are named like `mw_r0014_e000_v00_d00`: resource, entry, variant and
detail record. Preserve those names for selecting resources from multi-resource
exports. A whole-bank export overlaps alternate part shapes at their authored
origins; isolate a resource rather than mistaking that overlap for a broken Mech.

An assembled pose is convenient reference geometry, not a skinned rig or Blender
action. There is no single-command import of an edited whole pose into all of a
chassis's animation frames. Point/ellipse primitives are not exported as invented
spheres; OBJ contains polygon/line geometry.

## Edit in Blender

1. Import Wavefront OBJ. The files store native planar axes and altitude as X/Y/Z,
   with Z up. Use matching axis conversion on import/export; verify it with the
   unchanged round trip before relying on a Blender preset.
2. Preserve scale and origin. Exported coordinates already include resource scale;
   they are not specified in metres. Apply object transforms before exporting.
3. Keep object/resource names and native material names. Export only intended
   meshes. Cameras, lights, rig constraints and object animations do not become
   native game behaviour.
4. Use simple flat geometry. Keep faces planar, preferably triangles or quads;
   concave or non-planar n-gons need deliberate splitting. Check face winding.
5. Export OBJ and rebuild a **new** bank. Reopen it in the workshop and compare
   multiple poses and palettes before testing a copied game installation.

Materials such as `mw_p07_l00_e07_o00_f81` encode primary colour fields, EGA
surface/outline fields and polygon flags. Each two-digit value is hexadecimal.
For EGA work, `e` selects the surface and `o` the outline. Preserve `p`, `l` and
`f` unless intentionally changing their native roles. Changing MTL `Kd` alone
changes Blender's preview, not the game's material. Textures, UVs, PBR and smooth
shading are not native replacements for those fields.

## Replace a resource

```powershell
dotnet $cli models import-obj --bank "$game/locpck.tbl" --resource 14 --input "$work/arm-edited.obj" --output "$work/locpck-v1.tbl"
dotnet $cli models inspect --bank "$work/locpck-v1.tbl" --resource 14 --game-dir $game
dotnet $cli models render --bank "$work/locpck-v1.tbl" --resource 14 --palette "$game/tropic.pal" --output "$work/arm-v1.png"
```

For a complete exported polygon bank, `--all` replaces its polygon-only resources.
Keep every expected resource's tagged objects; this is not a partial-selection
merge. Mixed non-mesh resources are retained by this mode. Importing one resource
can deliberately replace it with polygon geometry.

Topology import rebuilds native references and emits an uncompressed native bank.
It is not byte-identical pointer/compression restoration. Ordinary face normals
are recalculated. Imported groups become separate entries with one variant/detail
record each; an exported multi-variant graph is not automatically rebuilt as the
same graph. Extreme shape changes also need reference/scale and collision review.

## Create a new resource

Make a fresh low-poly mesh with an ordinary object name, such as `WorkshopBuilding`,
and assign native-encoded materials. Do not leave Blender's generic `Material`
name on faces. A new untagged OBJ is assigned to the resource specified by the CLI.

The append ID is the bank's current resource count—not an arbitrary unused number.
The shipped TERPCK has 30 resources; verify the count before using this example:

```powershell
dotnet $cli models import-obj --bank "$game/terpck.tbl" --resource 30 --input "$work/new-building.obj" --output "$work/terpck-expanded.tbl"
dotnet $cli models inspect --bank "$work/terpck-expanded.tbl" --resource 30 --game-dir $game
```

For a minimal authoring/import test, save this as `new-building.obj` in `$work`.
It is an original simple box, not extracted game geometry:

```obj
o WorkshopBuilding
usemtl mw_p07_l00_e07_o00_f81
v -100 -100 0
v 100 -100 0
v 100 100 0
v -100 100 0
v -100 -100 200
v 100 -100 200
v 100 100 200
v -100 100 200
f 4 3 2 1
f 5 6 7 8
f 1 2 6 5
f 2 3 7 6
f 3 4 8 7
f 4 1 5 8
```

This creates displayable geometry, not mission placement, collision data or a
new playable chassis. [Map authoring](MAP_AUTHORING.md) explains the next boundary.

## Limits to design around

- At most 48 entries per resource and 20 vertices per face.
- At most 255 vectors per entry, **including** reference vectors and generated normals.
  An apparently modest vertex count can exceed this after face normals are added.
- Coordinates must fit signed 16-bit values after undoing resource scale; import
  rounds to native coordinates. Very small faces can collapse during that step.
- Colour fields accepted by OBJ import are 0–31; these are palette selections,
  not arbitrary RGB colours.
- A resource must fit its native allocation. Simplify/split oversized models.
- Picture primitives needing relocation are unsupported by the bank rebuilder.

A replacement BattleMech must keep all body-part/animation slots the game expects.
A ninth chassis additionally needs gameplay definitions, mounts, animation tables,
collision, cockpit and campaign/combat integration. OBJ import does not create them.
