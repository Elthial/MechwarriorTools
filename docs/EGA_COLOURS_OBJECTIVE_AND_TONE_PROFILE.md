# Preview colours, objective building and sound effects

## Native EGA appearance

The preview uses the model's EGA polygon colours and the chosen landscape
palette. It does not substitute other colour fields or assume that an arbitrary
colour reduced to sixteen entries is the right EGA result. Native patterned
fills can create checkerboard colours on BattleMech parts and terrain.

Use matching palettes when comparing a chassis across standing, walking and
jump poses. A surprising colour is worth investigating, but recolouring the
mesh from memory is not evidence of a renderer correction.

## Objective building

The Models page's objective-building shortcut selects the yellow/black special
target in OTHPCK. Its identification comes from the game's objective use, not
merely resemblance to a reference picture. Other buildings and unidentified
resources remain separate entries.

## Sound preview

The workshop interprets the game's sound events for preview and WAV export.
Some modulation depends on initial instrument/envelope state. The supported
profile reuses the finite, reviewed state already used by the C17 port; the
tool does not invent an instrument bank when data is absent.

All fifteen shipped IBM events pass the supported private playback checks.
Unsupported bindings still fail explicitly. That result is not qualification
of every historic sound adapter or proof of waveform-identical DOS playback.
