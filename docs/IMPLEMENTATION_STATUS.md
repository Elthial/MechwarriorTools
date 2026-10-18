# Current workshop capabilities

MechwarriorTooling is a working .NET 10 asset workshop, not a planned-only
scaffold. Windows WPF provides the desktop UI; Core, Rendering and CLI are
separate projects. A legally obtained game installation remains required.

## Available now

- Browse eight assembled BattleMechs, rotate/zoom and select animations/frames.
- Browse individual model resources, including recovered body-part membership
  and the labelled yellow/black objective building.
- View battlefield tiles/scenarios from above and in an orbitable scene.
- Edit supported model geometry/colour fields, map heights and object placements.
- Preview supported sound events with normal playback controls.
- Import/export Blender OBJ/MTL, supported indexed PNG artwork, WAV/MWPCM and
  MUS/MIDI through the desktop interchange window or CLI.
- Save validated, non-overwriting working copies, with undo/redo for supported
  editor operations. Original installations remain read-only.

## Limits that still matter

The renderer is an orthographic inspection view, not the game's complete
perspective renderer. The ten-second turntable and inspection animation cadence
are viewer behaviour, not combat timing. SFX support is partial; the viewer is
not a projectile combat simulator. Scenario assembly is browsable, but full
scenario authoring is not implemented.

Appending model resources does not register a ninth playable Mech. Imported
MWPCM does not enable sampled-audio playback in the C17 game. Successful
parsing and a correct-looking preview do not certify an edited mission asset.

## Verification

The standalone Windows Release build passed with no warnings/errors. Synthetic
verification passed 52 checks; optional private-installation verification passed
80 checks. Detached UI checks passed without desktop control or sound playback.
These are separate from native perspective equivalence and physical listening.

[Interchange instructions](ASSET_INTERCHANGE.md) and the
[documentation index](README.md) describe the supported workflows.
