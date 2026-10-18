using System.Buffers.Binary;

namespace MechwarriorTooling.Core;

/// <summary>Dynamix methods 00/01/02; method 02 uses width-byte packets, not GIF LZW.</summary>
public static class NativeArchive
{
    public const int MaximumBytes = 32 * 1024 * 1024;
    public static byte[] Decode(byte[] archive)
    {
        var input = new BinaryData(archive, "Native archive");
        input.Require(0, 5);
        uint length = input.U32(1);
        if (archive.Length > MaximumBytes || length > MaximumBytes) throw new InvalidDataException("Archive exceeds safety limit.");
        int expected = (int)length;
        if (archive[0] == 0)
        {
            if (archive.Length != expected + 5) throw new InvalidDataException("Raw archive length mismatch.");
            return archive[5..];
        }
        if (archive[0] == 1)
        {
            byte[] result = new byte[expected];
            int target = 0;
            for (int cursor = 5; cursor < archive.Length;)
            {
                byte control = input.U8(cursor++);
                int count = control & 0x7F;
                if (count > expected - target) throw new InvalidDataException("RLE output exceeds declared size.");
                if ((control & 0x80) != 0) result.AsSpan(target, count).Fill(input.U8(cursor++));
                else { input.Require(cursor, count); archive.AsSpan(cursor, count).CopyTo(result.AsSpan(target)); cursor += count; }
                target += count;
            }
            if (target != expected) throw new InvalidDataException("RLE output shorter than declared size.");
            return result;
        }
        if (archive[0] != 2) throw new InvalidDataException($"Unsupported archive method {archive[0]}.");
        if (expected == 0) return [];
        var reader = new PacketReader(archive);
        byte[]?[] dictionary = new byte[4096][];
        for (int value = 0; value < 256; value++) dictionary[value] = [(byte)value];
        int width = 9, maximum = 0x1FF, next = 0x101;
        int previous = reader.Read(width, false);
        if (previous >= 256) throw new InvalidDataException("LZW initial code is not a literal.");
        byte first = (byte)previous;
        byte[] output = new byte[expected];
        output[0] = first;
        int written = 1;
        while (written < expected)
        {
            bool refill = false;
            if (next > maximum)
            {
                if (width >= 12) throw new InvalidDataException("Invalid LZW width growth.");
                width++;
                maximum = width == 12 ? 4096 : (1 << width) - 1;
                refill = true;
            }
            int code = reader.Read(width, refill);
            byte[] oldPhrase = dictionary[previous] ?? throw new InvalidDataException("Missing LZW previous phrase.");
            if (code == 256)
            {
                Array.Clear(dictionary, 256, 3840);
                next = 256; width = 9; maximum = 0x1FF;
                code = reader.Read(width, true);
                if (code >= 256) throw new InvalidDataException("LZW clear requires a literal.");
            }
            int incoming = code;
            byte[] phrase;
            if (code == next)
            {
                var prior = dictionary[previous] ?? throw new InvalidDataException("Invalid LZW repeat.");
                phrase = [.. prior, first];
            }
            else if (code < next && dictionary[code] is { } known) phrase = known;
            else throw new InvalidDataException("Invalid LZW dictionary code.");
            if (phrase.Length > expected - written) throw new InvalidDataException("LZW phrase exceeds declared size.");
            phrase.CopyTo(output, written); written += phrase.Length; first = phrase[0];
            if (next < 4096) dictionary[next++] = [.. oldPhrase, first];
            previous = incoming;
        }
        return output;
    }

    /// <summary>Native method 00 preserves decoded offsets and unknown bytes without recompression.</summary>
    public static byte[] StoreRaw(byte[] decoded)
    {
        if (decoded.Length > MaximumBytes - 5) throw new InvalidDataException("Archive exceeds safety limit.");
        byte[] output = new byte[decoded.Length + 5];
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(1), (uint)decoded.Length);
        decoded.CopyTo(output, 5);
        return output;
    }

    private sealed class PacketReader(byte[] data)
    {
        private int position = 5, packetStart, packetBytes, bit;
        public int Read(int width, bool refill)
        {
            if (refill || bit >= packetBytes * 8 - (width - 1))
            {
                packetStart = position; packetBytes = Math.Min(width, data.Length - position);
                position += packetBytes; bit = 0;
            }
            if (bit + width > packetBytes * 8) throw new InvalidDataException("Truncated LZW packet.");
            int value = 0;
            for (int index = 0; index < width; index++)
                value |= ((data[packetStart + (bit + index) / 8] >> ((bit + index) % 8)) & 1) << index;
            bit += width;
            return value;
        }
    }
}
