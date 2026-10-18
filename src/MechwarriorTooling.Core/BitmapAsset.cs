namespace MechwarriorTooling.Core;

public sealed record IndexedPicture(int Id, int Width, int Height, byte[] Pixels);

/// <summary>Combat BMP:INF:/BIN: and SCR:BIN:. Pixels are native indices, not rendered screenshots.</summary>
public sealed class BitmapAsset
{
    private readonly TaggedAsset tagged;
    public IndexedPicture[] Pictures { get; }
    public bool TransparentZero => tagged.Tag == "BMP:";
    private BitmapAsset(TaggedAsset tagged, IndexedPicture[] pictures) { this.tagged = tagged; Pictures = pictures; }
    public static BitmapAsset Parse(byte[] bytes)
    {
        var tagged = TaggedAsset.Parse(bytes);
        if (tagged.Tag is not ("BMP:" or "SCR:")) throw new InvalidDataException("Expected native BMP: or SCR: asset, not a Windows BMP.");
        byte[] packed = NativeArchive.Decode(tagged.Chunk("BIN:"));
        int[] widths, heights;
        if (tagged.Tag == "SCR:") { widths = [320]; heights = [200]; }
        else
        {
            var info = new BinaryData(tagged.Chunk("INF:"), "BMP dimensions"); int count = info.U16(0);
            if (count is < 1 or > 4096) throw new InvalidDataException("Invalid image count.");
            info.Require(0, 2 + count * 4);
            widths = Enumerable.Range(0, count).Select(index => (int)info.U16(2 + index * 2)).ToArray();
            heights = Enumerable.Range(0, count).Select(index => (int)info.U16(2 + count * 2 + index * 2)).ToArray();
        }
        var pictures = new List<IndexedPicture>(); int offset = 0;
        for (int id = 0; id < widths.Length; id++)
        {
            int width = widths[id], height = heights[id];
            if (width is < 1 or > 4096 || height is < 1 or > 4096 || width % 8 != 0)
                throw new InvalidDataException("Native combat images require width divisible by eight and bounded positive dimensions.");
            int count = checked(width * height);
            if (count / 2 > packed.Length - offset) throw new InvalidDataException("Bitmap pixel payload truncated.");
            byte[] pixels = new byte[count];
            for (int pixel = 0; pixel < count; pixel++) pixels[pixel] = (byte)((packed[offset + pixel / 2] >> ((pixel & 1) == 0 ? 4 : 0)) & 15);
            offset += count / 2; pictures.Add(new(id, width, height, pixels));
        }
        if (offset != packed.Length) throw new InvalidDataException("Unexpected trailing bitmap pixels; refusing a potentially destructive rewrite.");
        return new(tagged, [.. pictures]);
    }
    public byte[] Replace(int id, IndexedPicture picture)
    {
        if ((uint)id >= Pictures.Length) throw new ArgumentOutOfRangeException(nameof(id));
        var old = Pictures[id];
        if (picture.Width != old.Width || picture.Height != old.Height || picture.Pixels.Length != old.Pixels.Length)
            throw new InvalidDataException("Replacement must retain native dimensions; resize the artwork to the exported canvas.");
        if (picture.Pixels.Any(pixel => pixel > 15)) throw new InvalidDataException("Bitmap needs 16-colour indices.");
        using var packed = new MemoryStream();
        foreach (var item in Pictures)
        {
            var pixels = item.Id == id ? picture.Pixels : item.Pixels;
            for (int pixel = 0; pixel < pixels.Length; pixel += 2) packed.WriteByte((byte)((pixels[pixel] << 4) | pixels[pixel + 1]));
        }
        byte[] output = tagged.Replace("BIN:", NativeArchive.StoreRaw(packed.ToArray()));
        Parse(output); return output;
    }
}
