namespace MechwarriorTooling.Core;

/// <summary>MW_*PICS whole-bank nibble RLE, or MW_PICS's uncompressed directory/per-record RLE.</summary>
public sealed class CampaignPictures
{
    private readonly byte[] source, decoded;
    private readonly bool indexed;
    private readonly int slots;
    public IndexedPicture[] Pictures { get; }
    public uint[]? Palette { get; }
    private static int Offset(BinaryData data, int at) => checked((int)(((uint)data.U16(at) << 16) | data.U16(at + 2)));
    public CampaignPictures(byte[] bytes, bool indexedRecords)
    {
        source = (byte[])bytes.Clone(); indexed = indexedRecords; decoded = indexed ? source : Expand(bytes);
        var data = new BinaryData(decoded, "Campaign pictures");
        int directory = indexed ? 600 : Offset(data, 0);
        if (directory is < 6 or > 600 || directory % 6 != 0) throw new InvalidDataException("Invalid campaign picture directory.");
        data.Require(0, directory); slots = directory / 6;
        if (!indexed)
        {
            int at = Offset(data, 0); if (data.U16(slots * 4) != 48) throw new InvalidDataException("Missing campaign RGB palette."); data.Require(at, 48);
            Palette = Enumerable.Range(0, 16).Select(index =>
            {
                uint Component(int component)
                {
                    byte value = data.U8(at + index * 3 + component); if (value > 63) throw new InvalidDataException("Invalid six-bit campaign RGB component.");
                    return (uint)(value * 255 / 63);
                }
                return 0xFF000000u | Component(0) << 16 | Component(1) << 8 | Component(2);
            }).ToArray();
        }
        var pictures = new List<IndexedPicture>();
        for (int id = indexed ? 0 : 1; id < slots; id++)
        {
            int start = Offset(data, id * 4), size = data.U16(slots * 4 + id * 2);
            if (start == 0 && size == 0) continue;
            if (start < directory || size == 0) throw new InvalidDataException("Invalid campaign record extent.");
            data.Require(start, size); byte[] record = decoded.AsSpan(start, size).ToArray();
            if (indexed) record = Expand(record);
            var item = new BinaryData(record, "Campaign picture"); int width = item.U16(0), height = item.U16(2);
            if (width is < 1 or > 4096 || height is < 1 or > 4096) throw new InvalidDataException("Invalid campaign picture dimensions.");
            int stride = (width + 1) / 2; item.Require(4, stride * height); byte[] pixels = new byte[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) pixels[y * width + x] = (byte)((record[4 + y * stride + x / 2] >> ((x & 1) == 0 ? 4 : 0)) & 15);
            pictures.Add(new(id, width, height, pixels));
        }
        Pictures = [.. pictures];
    }
    public byte[] Replace(int id, IndexedPicture replacement)
    {
        var old = Pictures.Single(picture => picture.Id == id);
        if (replacement.Width != old.Width || replacement.Height != old.Height || replacement.Pixels.Length != old.Pixels.Length || replacement.Pixels.Any(pixel => pixel > 15))
            throw new InvalidDataException("Campaign PNG must retain original dimensions and 16-colour indices.");
        var data = new BinaryData((byte[])decoded.Clone(), "Campaign rewrite"); int start = Offset(data, id * 4), size = data.U16(slots * 4 + id * 2);
        byte[] record = indexed ? Expand(data.Bytes.AsSpan(start, size).ToArray()) : data.Bytes.AsSpan(start, size).ToArray();
        int stride = (old.Width + 1) / 2;
        for (int y = 0; y < old.Height; y++)
            for (int x = 0; x < old.Width; x++)
            {
                int at = 4 + y * stride + x / 2, shift = (x & 1) == 0 ? 4 : 0;
                record[at] = (byte)((record[at] & ~(15 << shift)) | replacement.Pixels[y * old.Width + x] << shift);
            }
        byte[] output;
        if (indexed)
        {
            byte[] compressed = Compress(record);
            if (compressed.Length > ushort.MaxValue) throw new InvalidDataException("Compressed picture exceeds native record size.");
            int next = data.Bytes.Length; data.Write16(id * 4, unchecked((short)(next >> 16))); data.Write16(id * 4 + 2, unchecked((short)next));
            data.Write16(slots * 4 + id * 2, unchecked((short)compressed.Length)); output = [.. data.Bytes, .. compressed];
        }
        else
        {
            record.CopyTo(data.Bytes, start); output = Compress(data.Bytes);
            if (data.Bytes.Length > ushort.MaxValue || output.Length > ushort.MaxValue) throw new InvalidDataException("Whole campaign bank exceeds native 16-bit load range.");
        }
        new CampaignPictures(output, indexed); return output;
    }
    public static byte[] Expand(byte[] bytes)
    {
        var nibbles = new List<byte>(); int position = 0;
        byte Read() { byte value = (byte)((bytes[position / 2] >> ((position & 1) == 0 ? 0 : 4)) & 15); position++; return value; }
        while (position < bytes.Length * 2)
        {
            byte value = Read();
            if (value == 15)
            {
                if (position + 2 > bytes.Length * 2) break; // Filer's unused terminal nibble, never trusted for record extents.
                int count = Read() + 1; value = Read(); for (int repeat = 0; repeat < count; repeat++) nibbles.Add(value);
            }
            else nibbles.Add(value);
            if (nibbles.Count > NativeArchive.MaximumBytes * 2) throw new InvalidDataException("Campaign RLE expansion limit.");
        }
        byte[] output = new byte[(nibbles.Count + 1) / 2];
        for (int index = 0; index < nibbles.Count; index++) output[index / 2] |= (byte)(nibbles[index] << ((index & 1) == 0 ? 4 : 0));
        return output;
    }
    public static byte[] Compress(byte[] bytes)
    {
        var tokens = new List<byte>();
        byte Nibble(int index) => (byte)((bytes[index / 2] >> ((index & 1) == 0 ? 4 : 0)) & 15);
        for (int index = 0; index < bytes.Length * 2;)
        {
            byte value = Nibble(index); int count = 1;
            while (count < 16 && index + count < bytes.Length * 2 && Nibble(index + count) == value) count++;
            if (value == 15 || count >= 4) { tokens.Add(15); tokens.Add((byte)(count - 1)); tokens.Add(value); index += count; }
            else { tokens.Add(value); index++; }
        }
        if ((tokens.Count & 1) != 0) tokens.Add(0); // Structural lengths ignore any harmless final padding nibble.
        byte[] output = new byte[tokens.Count / 2];
        for (int index = 0; index < tokens.Count; index++) output[index / 2] |= (byte)(tokens[index] << ((index & 1) == 0 ? 0 : 4));
        return output;
    }
}
