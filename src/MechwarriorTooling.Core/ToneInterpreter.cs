namespace MechwarriorTooling.Core;

public sealed record ToneTick(int Tick, ushort Period, ushort Level, ushort Phase, ushort PhaseLimit,
    ushort ModulationOffset, ushort Cursor, ushort DelayTicks, bool Active);

/// <summary>2134:0248 channel initialization, 0196 commands and 0101 IRQ advance.</summary>
internal sealed class ToneInterpreter
{
    private readonly BinaryData data;
    private readonly SoundEvent sound;
    private readonly DosToneMemoryProfile memory;
    private ushort cursor, returnCursor, delay = 1, basePeriod, periodStep, outputPeriod, level, levelStep;
    private ushort phase, phaseStep, phaseLimit, modulationOffset, repeatCount;
    private short modulationScale;
    private ushort levelSequenceOffset = 0, levelSequencePosition, levelSequenceTicks;
    private readonly ushort[] unidentifiedFields = new ushort[8];
    public bool Active { get; private set; } = true;
    public ToneInterpreter(byte[] bytes, SoundEvent sound, DosToneMemoryProfile memory)
    {
        data = new BinaryData(bytes, "IBM tone event"); this.sound = sound; this.memory = memory;
        cursor = checked((ushort)(sound.Start + 8));
    }
    private void ParseCommands()
    {
        int position = cursor;
        for (int budget = 0; budget < 65536; budget++)
        {
            if (position < sound.Start + 8 || position >= sound.End) throw new InvalidDataException("Tone command cursor escaped event.");
            int field = data.U8(position++) & 0x7E;
            if (field == 0x7C) { Active = false; cursor = (ushort)position; return; }
            if (field == 0x78) { position = returnCursor; continue; }
            if (position > sound.End - 2) throw new InvalidDataException("Truncated tone operand.");
            ushort value = data.U16(position); position += 2;
            if (field == 0x7A) { returnCursor = (ushort)position; position = (position + value) & 0xFFFF; continue; }
            if (field == 0x7E)
            {
                if (repeatCount != 0 && --repeatCount == 0) continue;
                position = (position + value) & 0xFFFF; continue;
            }
            if (field == 0x2C) throw new NotSupportedException("Authored level-sequence directories require a separate recovered event-allocation binding.");
            if (field >= 0x2E)
            {
                (modulationOffset, phaseLimit) = memory.Descriptor(value);
                continue;
            }
            switch (field)
            {
                case 0x00: cursor = value; break;
                case 0x02: delay = value; break;
                case 0x04: basePeriod = value; break;
                case 0x06: periodStep = value; break;
                case 0x08: outputPeriod = value; break;
                case 0x0A: level = value; break;
                case 0x0C: levelStep = value; break;
                case 0x0E: case 0x10: case 0x12: case 0x14: unidentifiedFields[(field - 0x0E) / 2] = value; break;
                case 0x16: levelSequencePosition = value; break;
                case 0x18: levelSequenceTicks = value; break;
                case 0x1A: phase = value; break;
                case 0x1C: phaseStep = value; break;
                case 0x1E: modulationScale = unchecked((short)value); break;
                case 0x20: repeatCount = value; break;
                case 0x22: case 0x24: case 0x26: case 0x28: unidentifiedFields[4 + (field - 0x22) / 2] = value; break;
                case 0x2A: phaseLimit = value; break;
            }
            if (field != 0x02) continue;
            if (value == 0) Active = false;
            cursor = (ushort)position; return;
        }
        throw new InvalidDataException("Tone command budget exceeded.");
    }
    public ToneTick Advance(int tick)
    {
        delay = unchecked((ushort)(delay - 1));
        if (delay == 0 && cursor != 0) ParseCommands();
        level = unchecked((ushort)(level + levelStep));
        basePeriod = unchecked((ushort)(basePeriod + periodStep));
        ushort nextPhase = unchecked((ushort)(phase + phaseStep)), modulationHigh = 0;
        if (nextPhase != 0)
        {
            if (nextPhase >= phaseLimit) nextPhase = unchecked((ushort)(nextPhase - phaseLimit));
            phase = nextPhase;
            // Native SAR4 is signed, followed by WORD-wrapped addressing and
            // signed IMUL's high word. There is one subtraction, not modulo.
            ushort address = unchecked((ushort)((unchecked((short)nextPhase) >> 4) + modulationOffset));
            short sample = unchecked((short)(memory.ReadByte(address) << 8));
            modulationHigh = unchecked((ushort)((sample * modulationScale) >> 16));
        }
        outputPeriod = unchecked((ushort)(basePeriod + modulationHigh));
        if (levelSequenceTicks != 0 && --levelSequenceTicks == 0)
        {
            bool yielded = false;
            for (int budget = 0; budget < 65536; budget++)
            {
                ushort at = unchecked((ushort)(levelSequenceOffset + levelSequencePosition));
                ushort value = data.U16(at), ticks = data.U16(unchecked((ushort)(at + 2)));
                levelSequencePosition = unchecked((ushort)(levelSequencePosition + 4));
                if (ticks == ushort.MaxValue) { level = value; if (level == 0) levelStep = 0; continue; }
                levelStep = value; levelSequenceTicks = ticks; yielded = true; break;
            }
            if (!yielded) throw new InvalidDataException("Tone envelope budget exceeded.");
        }
        return new(tick, outputPeriod, level, phase, phaseLimit, modulationOffset, cursor, delay, Active);
    }
}
