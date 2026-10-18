# Contributing

Preserve original behaviour rather than redesigning the game. Cite native
executable and SEG:OFFSET evidence when changing reconstructed logic; retain
integer widths, wrap, call order, aliases, RNG order and observable quirks.
Use readable names, real typed structures and canonical data owners. Never
introduce a generic DOS-memory registry to avoid recovering a known record.

Game logic belongs in Original; SDL implements the lowest practical verified
OS/hardware boundary. Support automation must stay optional and visibly separate
from native gameplay. Tests are regression evidence, not authority over ASM.

Parsers must be bounded, unknown formats rejected and uncertain semantics
labelled. Editors write verified, non-overwriting working copies outside the
original installation. An asset parsing successfully is not gameplay acceptance.

Never contribute original binaries/data, saves, extracted models/images/audio,
raw emulator memory, third-party reference pictures or local captures. Selected
documentation screenshots need explicit provenance and NOTICE treatment.

Run the documented build and asset-independent tests. Private native comparison
is optional and must not silently use somebody else's installation or publish
it. Do not claim a complete campaign or pixel/waveform equivalence from a smoke
test. Ask before taking interactive control of a contributor's shared computer.
