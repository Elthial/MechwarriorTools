using System.Text;

namespace MechwarriorTooling.Core;

public sealed record SoundEvent(int Id, int Start, int End, ushort Type, string Flow, string[] Commands)
{
    // Recovered call contexts, not assertions that an event belongs exclusively to that caller.
    public string Name => Id switch
    {
        0x06 => "Collision / movement rejection (usage)", 0x07 => "Player damage (usage)",
        0x08 => "Player shot (usage)", 0x0A => "Player critical loss (usage)",
        0x0B => "Player footsteps (usage)", 0x0D => "Jump launch (usage)", _ => $"Event {Id:X2}"
    };
}
public sealed record SoundPreview(byte[] Wave, double Seconds, bool Truncated, bool AuthoredLoop, ToneTick[] Trace, string MemoryProfile);

/// <summary>Tagged IBM bank and bounded tone-bytecode inspection. Missing ITM memory is never invented.</summary>
public sealed class SoundBank
{
    private readonly byte[] data;
    private readonly bool hasInstrumentChunk;
    public SoundEvent[] Events { get; }
    public const int SampleRate = 44100;
    public const double TickRate = 1193182.0 / (65535 / 13); // startup scheduler period 0x0D, callback interval 1
    public SoundBank(byte[] bytes)
    {
        var file = new BinaryData(bytes, "SND bank");
        file.Require(0, 8);
        if (Encoding.ASCII.GetString(bytes, 0, 4) != "SND:" || (file.U32(4) & 0x7FFFFFFF) != bytes.Length - 8)
            throw new InvalidDataException("Invalid SND root/length.");
        var chunks = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        for (int at = 8; at < bytes.Length;)
        {
            file.Require(at, 8); string tag = Encoding.ASCII.GetString(bytes, at, 4);
            uint size = file.U32(at + 4);
            if (size > NativeArchive.MaximumBytes) throw new InvalidDataException("Sound chunk exceeds limit.");
            file.Require(at + 8, (int)size);
            if (!chunks.TryAdd(tag, bytes.AsSpan(at + 8, (int)size).ToArray())) throw new InvalidDataException("Duplicate sound chunk.");
            at += 8 + (int)size;
        }
        hasInstrumentChunk = chunks.ContainsKey("ITM:");
        if (!chunks.TryGetValue("INF:", out var information) || !chunks.TryGetValue("IBM:", out var archive))
            throw new InvalidDataException("Sound bank does not contain an INF/IBM preview backend.");
        int count = new BinaryData(information, "INF").U16(2);
        if (count is < 1 or > 4096) throw new InvalidDataException("Sound event count exceeds preview bounds.");
        data = NativeArchive.Decode(archive);
        var decoded = new BinaryData(data, "IBM directory");
        decoded.Require(0, count * 4);
        Events = Enumerable.Range(0, count).Select(id =>
        {
            uint pair = decoded.U32(id * 4);
            if ((pair >> 16) != 0) throw new NotSupportedException("Paragraph-based sound events are not yet supported by this preview interpreter.");
            int start = (int)(pair & 65535) + (int)(pair >> 16) * 16;
            decoded.Require(start, 8);
            int length = decoded.U16(start + 2), end = checked(start + length);
            if (start < count * 4 || length < 8) throw new InvalidDataException("Invalid tone event length.");
            decoded.Require(start, length);
            return Inspect(id, start, end, decoded.U16(start));
        }).ToArray();
    }
    private SoundEvent Inspect(int id, int start, int end, ushort type)
    {
        int cursor = start + 8, repeat = 0, returnAt = 0;
        var visited = new HashSet<(int, int, int)>(); var commands = new HashSet<string>();
        string flow = "inspection limit";
        for (int budget = 0; budget < 65536; budget++)
        {
            if (!visited.Add((cursor, repeat, returnAt))) { flow = "loop"; break; }
            if (cursor < start + 8 || cursor >= end) throw new InvalidDataException("Tone cursor escapes event.");
            int field = data[cursor++] & 0x7E;
            commands.Add($"0x{field:X2}");
            if (field == 0x7C) { flow = "stop"; break; }
            if (field == 0x78) { cursor = returnAt; continue; }
            if (cursor > end - 2) throw new InvalidDataException("Tone operand truncated.");
            int operand = data[cursor] | data[cursor + 1] << 8; cursor += 2;
            if (field == 0x7A) { returnAt = cursor; cursor = (cursor + operand) & 65535; }
            else if (field == 0x7E)
            {
                if (repeat != 0 && --repeat == 0) continue;
                cursor = (cursor + operand) & 65535;
            }
            else if (field == 0x20) repeat = operand;
            else if (field == 2 && operand == 0) { flow = "stop"; break; }
            else if (field == 0x2C) { flow = "level-sequence (not previewable yet)"; break; }
        }
        return new(id, start, end, type, flow, commands.Order().ToArray());
    }
    public SoundPreview Render(int id, double seconds = 10, DosToneMemoryProfile? memory = null)
    {
        if ((uint)id >= Events.Length || !double.IsFinite(seconds) || seconds <= 0 || seconds > 30) throw new ArgumentOutOfRangeException(nameof(id));
        var sound = Events[id];
        if (sound.Type != 0) throw new NotSupportedException("Only IBM tone events are currently previewable.");
        if (hasInstrumentChunk) throw new NotSupportedException("An authored ITM chunk needs its own binding; the missing-ITM DOS profile must not replace it.");
        memory ??= DosToneMemoryProfile.Combat;
        var interpreter = new ToneInterpreter(data, sound, memory);
        int sample = 0, limit = (int)(seconds * SampleRate);
        double phase = 0; var trace = new List<ToneTick>();
        using var pcm = new MemoryStream(); using var writer = new BinaryWriter(pcm, Encoding.UTF8, true);
        for (int tick = 0; sample < limit && interpreter.Active; tick++)
        {
            // 2134:0000 selects the active channel before advancing it; a stop
            // still emits that tick's inherited period/level, then deselects next IRQ.
            ToneTick state = interpreter.Advance(tick); trace.Add(state);
            int endSample = Math.Min(limit, (int)Math.Floor((tick + 1) * SampleRate / TickRate));
            double frequency = 1193182.0 / (state.Period == 0 ? 65536 : state.Period);
            for (; sample < endSample; sample++)
            {
                phase = (phase + frequency / SampleRate) % 1;
                writer.Write((short)(state.Level == 0 ? 0 : phase < 0.5 ? 6000 : -6000));
            }
        }
        using var wave = new MemoryStream(); using var header = new BinaryWriter(wave, Encoding.UTF8, true);
        header.Write("RIFF"u8); header.Write((int)pcm.Length + 36); header.Write("WAVEfmt "u8); header.Write(16);
        header.Write((short)1); header.Write((short)1); header.Write(SampleRate); header.Write(SampleRate * 2);
        header.Write((short)2); header.Write((short)16); header.Write("data"u8); header.Write((int)pcm.Length);
        pcm.Position = 0; pcm.CopyTo(wave);
        return new(wave.ToArray(), (double)sample / SampleRate, interpreter.Active, sound.Flow == "loop", [.. trace], memory.Name);
    }
}
