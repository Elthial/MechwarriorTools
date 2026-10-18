# Power user: formats and resource relationships

This is an authoring reference, not a decompiler journal. File structures below
describe the supported readers/writers. Changing a file successfully is distinct
from proving that every game consumer supports your new content.

## What can I change?

| Content | Native storage | Supported authoring route |
| --- | --- | --- |
| 3D model geometry | PCK.TBL banks | OBJ/MTL export; polygon replacement, topology rebuild and resource append. |
| Collision | GI banks | Inspected/validated by relevant private checks; no authoring generator. |
| Tile heights | GRD | Existing-cell edits in desktop/CLI; no heightmap-image importer. |
| World objects | WLD | Existing-placement edits; no row insertion/deletion command. |
| Scenario composition | SNARIO.DAT | Inspection and assembled preview; no scenario/formation writer. |
| Cockpit/combat pictures | Tagged BMP/SCR | Indexed PNG export and selected-image replacement. |
| Campaign pictures | MW_*PICS.BIN / MW_PICS.BIN | Indexed PNG export and selected-picture replacement. |
| Landscape palette | PAL | Palette loading/selection; no palette-file authoring UI. |
| Speaker effects | SND | Inspect, audition and render events to WAV/MWPCM; no WAV-to-SND compiler. |
| Songs | MUS | Validate/export/import MIDI format 0; no MIDI-to-WAV synthesizer. |
| Sampled audio | MWPCM | WAV import/export and loop metadata; not consumed by the original SND loader. |

Mech equipment, chassis availability and animation assembly also depend on
executable-authored definitions. None of these imports patches those definitions.

## Native archives

Many compressed resources start with a method byte followed by a little-endian
32-bit decoded length. The reader supports method 0 (stored), 1 (run-length) and
2 (the game's packeted LZW). This is not interchangeable with GIF's LZW stream.
The common safety bound is 32 MiB, not a target budget for new gameplay assets.

Native model and combat-picture writers use stored method 0 where applicable.
An edited file can therefore be larger than the compressed original even when
its content is similar. Different compressed bytes are not automatically corruption;
reopen the output and compare decoded content. Campaign picture packing is a
separate contract rather than this common archive used everywhere.

## Model bank hierarchy

```text
PCK.TBL native archive
  decoded resource directory (numbered resources, terminated)
    resource: scale/reference information and entries
      entry: vectors, a reference vector, alternative drawing variants
        variant: drawing/detail records
          polygon record: flags, colour fields, normal and vertex references
          or non-polygon primitive
```

Directory entries locate resources using paragraph-relative addressing. Internal
references stay within their native resource allocation. Use the writer to rebuild
these relationships; inserting raw bytes into the middle of a bank will invalidate
references. Resource IDs are directory positions and are observed by animations
and placements; renumbering them is not a harmless file tidy-up.

Vectors include more than visible vertices: normals and reference points also
consume entries. Native coordinates are signed 16-bit values with resource scale.
The OBJ writer applies the scale; import reverses it and rounds coordinates.
Its bounds are listed in [model authoring](MODEL_AUTHORING.md#limits-to-design-around).

OBJ imports rebuild geometry rather than preserving the original allocation graph.
For a small existing-topology correction, the built-in vertex/colour editor keeps
the decoded layout and unknown data, but does not recalculate authored normals.
For changed topology, use OBJ and review regenerated normals and remaining native
reference/scale/collision assumptions.

## GRD: stored tile heights

The supported grid is exactly **47 columns × 87 rows**, one unsigned byte per
cell: 4,089 bytes. Cells are row-major, so a cell's position is `row * 47 + column`.
Values are stored simulation quantities, not a declared metres scale.
GRD is not a PNG, rendered minimap or collection of model vertices.

## WLD: placed world objects

WLD is a sequence of **14-byte little-endian records**, with no partial final
record accepted:

| Field | Stored type | Authoring meaning |
| --- | --- | --- |
| Resource ID | unsigned 16-bit | Terrain-bank resource to display. |
| Position A | signed 32-bit | First planar world coordinate (`--a`). |
| Position B | signed 32-bit | Second planar world coordinate (`--b`). |
| Altitude | signed 32-bit | Vertical position, separate from grid height. |

The tile's GRD and WLD are companions, not one combined file. Geometry and collision
are further dependencies. The CLI validates an edited ID against the bank supplied
to the edit command; it does not create corresponding collision data or a contract
objective. See [map authoring](MAP_AUTHORING.md).

## Scenario selectors

The supported SNARIO.DAT is 3,648 bytes. The exposed catalogue contains 60
scenario-selection entries describing four tile selectors, formation selectors
and heading flags. Do not treat this catalogue as a complete mission authoring
specification or discard the rest of the file. Selection, forces, contract goals
and Mech definitions have additional game-side relationships.

## Pictures and palettes

Combat `.bmp` and `.scr` are tagged game resources, **not Windows BMP files**.
BMP banks provide image dimensions and packed pixels; SCR holds a full 320×200
screen. Use `graphics inspect` to identify images and `graphics export-png` to
export native pixels. Model/map preview PNGs are a different output.

Campaign whole-bank and indexed-directory picture files use different layouts.
Some banks carry their own palette; indexed campaign/combat artwork needs the
appropriate external palette. Renamed MW_PICS copies need `--format indexed`
because filename-based detection no longer identifies them.

For PNG reimport retain canvas dimensions, native palette colours and transparency
rules. EGA polygon colour selections and PAL display mappings are distinct from
arbitrary RGB materials. The model preview supports native patterned EGA fills;
MTL gives Blender only a solid approximation.

## Audio and music

SND events sequence tone/envelope behaviour. WAV export records a bounded rendering
of that sequence; it does not encode the sequence again. Looping effects can be
truncated at the requested duration. The supported finite playback profile is
reported by the tool instead of silently inventing missing instrument data.

Reviewed MUS songs are Standard MIDI format 0. Imports require a single-track,
PPQN-timed MIDI file; other MIDI formats/timing modes are rejected. Preserve
game-specific events when editing a song and test the intended playback backend.

MWPCM stores signed 16-bit mono/stereo samples, rate and optional loop frames.
It is a project-defined interchange format. Read [its specification](MWPCM_FORMAT.md)
and [audio recipes](ASSET_INTERCHANGE.md#sounds-wav-and-new-native-mwpcm) before
assuming a sampled clip can replace an original event.

## Where to inspect the implementation

Core readers/writers are in `src/MechwarriorTooling.Core`: `NativeArchive`,
`ModelBank`, `NativeModelWriter`, `TerrainTile`, `ScenarioData`, `BitmapAsset`,
`CampaignPictures`, `EgaPalette`, `SoundBank` and `PcmClip`. OBJ/PNG interchange
is in `src/MechwarriorTooling.Rendering`. These are the current format contracts;
unsupported fields and features should not be guessed from an extension alone.
