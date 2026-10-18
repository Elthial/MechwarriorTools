# Documentation

## Start here

- [User guide](USER_GUIDE.md): open an installation, find assets, edit and save working copies.
- [Troubleshooting](TROUBLESHOOTING.md): missing assets, unusual colours, rejected imports and failed saves.
- [Current capabilities and limits](IMPLEMENTATION_STATUS.md).

## Make or edit assets

- [Models and Blender](MODEL_AUTHORING.md): export parts/poses, edit geometry and materials, rebuild a bank or append a new resource.
- [Maps and buildings](MAP_AUTHORING.md): GRD/WLD tiles, scenarios, height and placement edits, and using new terrain geometry.
- [PNG, sound, music and exact CLI recipes](ASSET_INTERCHANGE.md).
- [Formats and resource relationships](FORMATS.md): what each file contains, what depends on it and which writers exist.
- [MWPCM specification](MWPCM_FORMAT.md): sampled-audio file layout and loop conventions.

## Understand the assets

- [Model catalogue and animations](ASSET_CATALOGUE_AND_MECH_ANIMATIONS.md).
- [Landscape colours](NATIVE_MAP_LANDSCAPE_COLOURS.md) and [EGA/model/sound preview behaviour](EGA_COLOURS_OBJECTIVE_AND_TONE_PROFILE.md).
- [Screenshots](SCREENSHOTS.md) and [reviewed game revisions](SUPPORTED_VERSIONS.md).

## Looking for game-code explanations?

Campaign worlds, contracts, pilots, Mech condition, weapon rules and their C17
methods are documented in the
[SDL-port gameplay guide](https://github.com/Elthial/Mechwarrior-sdl-port/blob/main/docs/gameplay/README.md).
The tools document asset formats and editing workflows; the port documents
the game that consumes those assets. Detailed reconstruction research stays
in the reverse-engineering repository.

Examples assume a legally owned installation. Keep originals read-only, authoring
files in a separate workspace and gameplay experiments in a disposable copy.
Neither extracted assets nor private installations belong in public source control.
