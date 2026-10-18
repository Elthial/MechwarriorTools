# Workshop development direction

The asset viewer/editor and CLI are implemented. This is the current direction,
not the original pre-implementation proposal.

Priorities are reliable extraction and working-copy authoring: model geometry,
animation/frame inspection, battlefield objects, 2D artwork and audio interchange.
Keep desktop and CLI workflows aligned so assets can be inspected interactively
and processed repeatably without taking control of the user's desktop.

Do not expand the game roster or claim a complete renderer merely because a
resource can be appended or displayed. New playable Mechs need coordinated
gameplay definitions and resources; edited assets need testing in a copied game.

Preserve read-only original installations, bounded parsers, non-overwriting
validated saves and explicit fidelity limits. Unsupported material remains
labelled rather than replaced with invented data.

See [current capabilities](docs/IMPLEMENTATION_STATUS.md),
[asset workflows](docs/ASSET_INTERCHANGE.md) and the
[SDL-port gameplay guide](https://github.com/Elthial/Mechwarrior-sdl-port/blob/main/docs/gameplay/README.md).
