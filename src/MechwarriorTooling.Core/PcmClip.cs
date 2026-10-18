using System.Buffers.Binary;

namespace MechwarriorTooling.Core;

/// <summary>MWPC v1: uncompressed signed 16-bit little-endian interleaved PCM. Not a DOS SND bank.</summary>
public sealed record PcmClip(int SampleRate, int Channels, short[] Samples, int LoopStart = 0, int LoopEnd = 0)
{
    public int Frames => Samples.Length / Channels;
    public double Seconds => (double)Frames / SampleRate;
    public bool Looping => LoopEnd > LoopStart;
    public void Validate()
    {
        if (Channels is < 1 or > 2 || SampleRate is < 8000 or > 192000 || Samples.Length == 0 || Samples.Length % Channels != 0
            || Samples.Length > (NativeArchive.MaximumBytes - 44) / 2) throw new InvalidDataException("PCM requires bounded mono/stereo S16 audio at 8..192 kHz.");
        if (LoopStart < 0 || LoopEnd < 0 || LoopEnd > Frames || (LoopEnd == 0 ? LoopStart != 0 : LoopStart >= LoopEnd))
            throw new InvalidDataException("PCM loop is [start,end) in sample frames, or 0/0 for no loop.");
    }
    public byte[] WriteNative()
    {
        Validate(); using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write("MWPC"u8); writer.Write((ushort)1); writer.Write((ushort)Channels); writer.Write(SampleRate);
        writer.Write(Frames); writer.Write(LoopStart); writer.Write(LoopEnd); writer.Write(Samples.Length * 2); writer.Write(0);
        foreach (short sample in Samples) writer.Write(sample); return stream.ToArray();
    }
    public static PcmClip ReadNative(byte[] bytes)
    {
        var data = new BinaryData(bytes, "MWPCM"); data.Require(0, 32);
        if (!bytes.AsSpan(0, 4).SequenceEqual("MWPC"u8) || data.U16(4) != 1 || data.U32(28) != 0) throw new InvalidDataException("Unsupported MWPCM magic/version/reserved flags.");
        int channels = data.U16(6), rate = data.I32(8), frames = data.I32(12), length = data.I32(24);
        if (channels is < 1 or > 2 || frames < 1 || (long)frames * channels * 2 != length || length != bytes.Length - 32 || bytes.Length > NativeArchive.MaximumBytes)
            throw new InvalidDataException("Invalid MWPCM sample extent.");
        short[] samples = new short[length / 2];
        for (int sample = 0; sample < samples.Length; sample++) samples[sample] = data.I16(32 + sample * 2);
        var clip = new PcmClip(rate, channels, samples, data.I32(16), data.I32(20)); clip.Validate(); return clip;
    }
    public byte[] WriteWave()
    {
        Validate(); using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(36 + Samples.Length * 2 + (Looping ? 68 : 0)); writer.Write("WAVEfmt "u8);
        writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)Channels); writer.Write(SampleRate);
        writer.Write(SampleRate * Channels * 2); writer.Write((ushort)(Channels * 2)); writer.Write((ushort)16);
        if (Looping)
        {
            writer.Write("smpl"u8); writer.Write(60);
            writer.Write(0); writer.Write(0); writer.Write((uint)(1_000_000_000L / SampleRate)); writer.Write(60); writer.Write(0);
            writer.Write(0); writer.Write(0); writer.Write(1); writer.Write(0);
            writer.Write(0); writer.Write(0); writer.Write(LoopStart); writer.Write(LoopEnd - 1); writer.Write(0); writer.Write(0);
        }
        writer.Write("data"u8); writer.Write(Samples.Length * 2); foreach (short sample in Samples) writer.Write(sample); return stream.ToArray();
    }
    public static PcmClip ReadWave(byte[] bytes)
    {
        var data = new BinaryData(bytes, "WAV"); data.Require(0, 12);
        if (!bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8) || data.U32(4) != bytes.Length - 8)
            throw new InvalidDataException("Expected a complete RIFF WAVE file.");
        int format = 0, channels = 0, rate = 0, bits = 0, block = 0, loopStart = 0, loopEnd = 0; byte[]? payload = null;
        for (int at = 12; at < bytes.Length;)
        {
            data.Require(at, 8); uint size = data.U32(at + 4);
            if (size > NativeArchive.MaximumBytes) throw new InvalidDataException("Oversized WAV chunk.");
            int count = (int)size; data.Require(at + 8, count); var tag = bytes.AsSpan(at, 4); int start = at + 8;
            if (tag.SequenceEqual("fmt "u8))
            {
                if (format != 0 || count < 16) throw new InvalidDataException("Invalid/duplicate WAV format.");
                format = data.U16(start); channels = data.U16(start + 2); rate = data.I32(start + 4); block = data.U16(start + 12); bits = data.U16(start + 14);
            }
            else if (tag.SequenceEqual("data"u8))
            { if (payload != null) throw new InvalidDataException("Multiple WAV data chunks unsupported."); payload = bytes.AsSpan(start, count).ToArray(); }
            else if (tag.SequenceEqual("smpl"u8))
            {
                if (count < 36 || data.U32(start + 28) > 1) throw new InvalidDataException("Only zero/one forward WAV loop is supported.");
                if (data.U32(start + 28) == 1)
                {
                    if (count < 60 || data.U32(start + 40) != 0 || data.U32(start + 52) != 0 || data.U32(start + 56) != 0)
                        throw new InvalidDataException("Only infinite forward, integral-frame WAV loops are supported.");
                    loopStart = checked((int)data.U32(start + 44)); loopEnd = checked((int)data.U32(start + 48) + 1);
                }
            }
            at = checked(start + count + (count & 1)); if (at > bytes.Length) throw new InvalidDataException("Missing WAV chunk padding.");
        }
        if (payload == null || channels is < 1 or > 2 || rate is < 8000 or > 192000 || format is not (1 or 3)
            || (format == 1 ? bits is not (8 or 16 or 24 or 32) : bits != 32) || block != channels * (bits / 8) || payload.Length % block != 0)
            throw new InvalidDataException("Use PCM 8/16/24/32-bit or float32 mono/stereo WAV, 8..192 kHz.");
        short[] samples = new short[payload.Length / (bits / 8)];
        for (int sample = 0; sample < samples.Length; sample++)
        {
            int offset = sample * (bits / 8); double value;
            if (format == 3)
            { value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset))); if (!double.IsFinite(value)) throw new InvalidDataException("Non-finite WAV sample."); }
            else value = bits switch
            {
                8 => (payload[offset] - 128) / 128.0,
                16 => BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(offset)) / 32768.0,
                24 => ((payload[offset] | payload[offset + 1] << 8 | payload[offset + 2] << 16) << 8 >> 8) / 8388608.0,
                _ => BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(offset)) / 2147483648.0
            };
            samples[sample] = (short)Math.Clamp(Math.Round(value * 32768), short.MinValue, short.MaxValue);
        }
        var clip = new PcmClip(rate, channels, samples, loopStart, loopEnd); clip.Validate(); return clip;
    }
}
