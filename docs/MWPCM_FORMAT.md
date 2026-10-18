# MWPCM sampled audio, version 1

Extension `.mwpcm`; magic ASCII `MWPC`. This is a new MechWarrior port/tooling
format, not a newly discovered Dynamix format. It holds PCM directly; no speaker
bytecode, synthesized envelope, codec dependency or invented MIDI instrument.

All integers are little-endian. Header size is exactly 32 bytes:

| Offset | Type | Meaning |
| --- | --- | --- |
| 0 | char[4] | `MWPC` |
| 4 | u16 | Version = 1 |
| 6 | u16 | Channels: 1 mono or 2 stereo |
| 8 | u32 | Sample rate, 8000–192000 Hz |
| 12 | u32 | Sample-frame count, positive |
| 16 | u32 | Loop start frame, inclusive |
| 20 | u32 | Loop end frame, exclusive |
| 24 | u32 | Payload byte count = frames × channels × 2 |
| 28 | u32 | Reserved flags = 0; unknown values rejected |
| 32 | i16[] | Interleaved PCM samples, signed little-endian |

Stereo order is left/right per frame. A frame contains one sample per channel;
loop positions are **frames**, not bytes or individual interleaved samples.
No loop is start=0/end=0. Otherwise `0 <= start < end <= frames`.
Whole-file length must equal `32 + payloadBytes`; truncated and trailing bytes
are rejected. The tooling's 32 MiB asset bound applies; the current WAV bridge
also reserves room for WAV headers. Samples use the full -32768..32767 range.

S16 WAV import/export preserves samples, sample rate and channels without
resampling. Other supported WAV sample encodings are converted to S16 by
rounding/clamping. WAV forward infinite `smpl` loops are converted between
its inclusive end position and MWPCM's exclusive end position.

Version 1 deliberately has no compression, spatial-position metadata, event ID,
gain override or instrument description. Those are playback/event-binding
decisions, not properties guessed from waveform bytes. The original game's
SND parser does not consume this format. The tooling reads/writes/auditions it;
gameplay playback integration remains an explicit future port task.
