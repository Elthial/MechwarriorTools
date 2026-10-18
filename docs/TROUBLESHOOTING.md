# Troubleshooting and authoring checks

| Problem | Check or next step |
| --- | --- |
| Installation is missing a file | Select the complete extracted game directory, not the SDL port's build folder. Inventory it with the CLI. |
| Unknown executable revision | Compare the supported hashes. Chassis/animation definitions need a reviewed BTECH.EXE; do not substitute an unpacked research copy. Separate model/map inspection has different dependencies. |
| Working copy has unexpected colours | Pass the original palette explicitly. Palette fallback beside an isolated edited bank may differ from the installation. |
| A surface looks checkered | Check native EGA fill patterns and landscape palette before recolouring it. A solid Blender material is only an approximation. |
| Whole-bank OBJ looks like overlapping junk | It contains alternative body-part shapes at authored origins. Isolate a resource, or export an assembled Mech pose for reference. |
| OBJ has no matching tagged resource | Keep `mw_r…` object names when round-tripping a bank. Export only your fresh untagged mesh when deliberately appending a new resource. |
| Material name rejected | Preserve/assign a native name such as `mw_p07_l00_e07_o00_f81`; changing MTL RGB or using Blender's default `Material` is not native material authoring. |
| Mesh disappears or faces look reversed | Check scale, origin, applied transforms, winding and normals. Compare an unchanged export/import first. |
| Too many vectors or entries | Normals/reference points count too. Simplify faces or split geometry within the documented resource/entry limits. |
| Face collapses on import | Native coordinate rounding removed its area. Increase relevant dimensions or simplify it; keep within coordinate limits. |
| Built-in vertex edit rejected as a normal alias | That vector is also used as a normal. The small-edit path refuses the alias; consider a reviewed OBJ topology rebuild instead. |
| Save refused despite accepting overwrite | Outputs intentionally require a new name outside the original installation. Create the destination directory and use a versioned filename. |
| Repeating an export fails immediately | OBJ export has multiple companion files and bulk PNG export creates a directory. Choose a fresh output prefix/directory, including sidecar names. |
| WLD will not reopen in the desktop | Put its same-stem GRD alongside it. Save grid and placements separately. |
| New terrain ID fails map rendering | The renderer loads TERPCK from `--game-dir`. Use the copied installation containing the edited bank, not the original bank with fewer resources. |
| Height edits do not reshape a visible hill | GRD is not the visible terrain mesh. Edit the placed model separately; the preview deliberately avoids inventing a heightfield renderer. |
| Imported PNG is rejected | Keep dimensions and palette colours, avoid partial alpha and interlace, and use the correct bitmap/campaign/indexed format. A preview screenshot is not a source-art export. |
| WAV imports but no new sound plays in game | WAV→MWPCM is an authoring conversion. The original SND bank cannot consume it; gameplay event bindings/backend support are separate work. |
| A song is rejected | Export single-track MIDI format 0 with PPQN timing. Renaming a format-1 file is insufficient. |

## A reliable edit loop

1. Inspect the untouched asset and record its bank/resource/image/placement selection.
2. Export and reimport once without editing. Reopen and compare the result.
3. Make one small change and use a new output name.
4. Inspect the saved native file, not just your Blender/Aseprite source.
5. Test a disposable game installation through the relevant gameplay conditions.

For a Mech, check every affected pose and body part. For a map, check movement,
collision, targeting and impacts. For art, check palette/transparency and restored
UI regions. For music, test the actual intended backend. Do not interpret a valid
parser result as full in-game acceptance.

## Reporting an issue

Include tool build/version information, exact CLI command or page/actions, asset
filename and resource/image/placement ID, landscape palette, error text, expected
result and whether it also happens on the untouched source. Share your own minimal
reproducer if possible; do not upload the game installation, extracted original
media or private saves. A screenshot can illustrate an issue but is not a substitute
for specifying which asset was selected.
