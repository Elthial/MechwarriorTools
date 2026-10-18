using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using MechwarriorTooling.Core;

namespace MechwarriorTooling.Rendering;

public sealed record PngPixels(RasterImage Image, byte[]? Indices, uint[]? Palette);

/// <summary>Lossless indexed export and strict PNG import; supports Aseprite RGB/RGBA/indexed non-interlaced PNGs.</summary>
public static class PngInterchange
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    public static uint[] Colours(EgaPalette palette) => Enumerable.Range(0, 16).Select(index => SoftwareRenderer.Ega[palette.LinePixel((byte)index)]).ToArray();
    public static byte[] Export(IndexedPicture picture, uint[] palette, bool transparentZero)
    {
        if (palette.Length != 16 || picture.Width < 1 || picture.Height < 1 || picture.Pixels.Length != checked(picture.Width * picture.Height)
            || picture.Pixels.Any(index => index > 15)) throw new InvalidDataException("Invalid 16-colour picture.");
        using var output = new MemoryStream(); output.Write(Signature);
        byte[] header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian(header, picture.Width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), picture.Height);
        header[8] = 8; header[9] = 3; RasterImage.Chunk(output, "IHDR", header);
        byte[] colours = palette.SelectMany(pixel => new[] { (byte)(pixel >> 16), (byte)(pixel >> 8), (byte)pixel }).ToArray();
        RasterImage.Chunk(output, "PLTE", colours); if (transparentZero) RasterImage.Chunk(output, "tRNS", [0]);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
            for (int row = 0; row < picture.Height; row++) { zlib.WriteByte(0); zlib.Write(picture.Pixels, row * picture.Width, picture.Width); }
        RasterImage.Chunk(output, "IDAT", compressed.ToArray()); RasterImage.Chunk(output, "IEND", []); return output.ToArray();
    }
    public static IndexedPicture Import(byte[] png, int id, uint[] palette, bool transparentZero)
    {
        var decoded = Decode(png); byte[] pixels = new byte[decoded.Image.Pixels.Length];
        for (int pixel = 0; pixel < pixels.Length; pixel++)
        {
            uint colour = decoded.Image.Pixels[pixel]; uint alpha = colour >> 24;
            if (alpha == 0 && transparentZero) { pixels[pixel] = 0; continue; }
            if (alpha != 255) throw new InvalidDataException("Native artwork needs opaque pixels or fully transparent index zero; partial alpha is unsupported.");
            int match = -1;
            if (decoded.Indices != null && decoded.Indices[pixel] < palette.Length && palette[decoded.Indices[pixel]] == colour)
                match = decoded.Indices[pixel];
            else match = Array.FindIndex(palette, candidate => candidate == colour);
            if (match < 0 || (transparentZero && match == 0)) throw new InvalidDataException($"Pixel {pixel} is outside the native palette (or opaque transparent-zero). Use the exported indexed palette in Aseprite; no silent colour quantization.");
            pixels[pixel] = (byte)match;
        }
        return new(id, decoded.Image.Width, decoded.Image.Height, pixels);
    }
    public static PngPixels Decode(byte[] png)
    {
        if (png.Length > NativeArchive.MaximumBytes || png.Length < 33 || !png.AsSpan(0, 8).SequenceEqual(Signature)) throw new InvalidDataException("Invalid/oversized PNG.");
        int width = 0, height = 0, depth = 0, type = -1; byte[]? paletteBytes = null, transparency = null; bool end = false, idat = false;
        using var compressed = new MemoryStream();
        for (int at = 8; at < png.Length;)
        {
            if (at > png.Length - 12) throw new InvalidDataException("Truncated PNG chunk.");
            uint count = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at));
            if (count > NativeArchive.MaximumBytes || count > png.Length - at - 12) throw new InvalidDataException("Invalid PNG chunk size.");
            int length = (int)count; string tag = Encoding.ASCII.GetString(png, at + 4, 4);
            uint crc = 0xFFFFFFFF;
            foreach (byte value in png.AsSpan(at + 4, length + 4))
            { crc ^= value; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0); }
            if (~crc != BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length))) throw new InvalidDataException("PNG CRC mismatch.");
            var bytes = png.AsSpan(at + 8, length);
            if (type < 0 && tag != "IHDR") throw new InvalidDataException("PNG must start with IHDR.");
            switch (tag)
            {
                case "IHDR":
                    if (type >= 0 || length != 13) throw new InvalidDataException("Invalid/duplicate PNG header.");
                    width = BinaryPrimitives.ReadInt32BigEndian(bytes); height = BinaryPrimitives.ReadInt32BigEndian(bytes[4..]); depth = bytes[8]; type = bytes[9];
                    if (width is < 1 or > 4096 || height is < 1 or > 4096 || bytes[10] != 0 || bytes[11] != 0 || bytes[12] != 0
                        || (type == 3 ? depth is not (1 or 2 or 4 or 8) : depth != 8 || type is not (0 or 2 or 4 or 6)))
                        throw new NotSupportedException("Use non-interlaced 8-bit RGB/RGBA/grey or 1/2/4/8-bit indexed PNG."); break;
                case "PLTE":
                    if (idat || paletteBytes != null || length == 0 || length > 768 || length % 3 != 0) throw new InvalidDataException("Invalid PNG palette.");
                    paletteBytes = bytes.ToArray(); break;
                case "tRNS": if (idat || transparency != null) throw new InvalidDataException("Invalid PNG transparency."); transparency = bytes.ToArray(); break;
                case "IDAT": compressed.Write(bytes); idat = true; break;
                case "IEND": if (length != 0 || at + 12 != png.Length) throw new InvalidDataException("PNG end/trailing bytes."); end = true; break;
                default: if ((png[at + 4] & 32) == 0) throw new NotSupportedException($"Unsupported critical PNG chunk {tag}."); break;
            }
            at += length + 12;
        }
        if (!end || !idat || (type == 3 && paletteBytes == null)) throw new InvalidDataException("Incomplete PNG.");
        int components = type switch { 2 => 3, 4 => 2, 6 => 4, _ => 1 };
        int stride = checked((width * components * depth + 7) / 8), filterBytes = Math.Max(1, components * depth / 8);
        int decodedLength = checked((stride + 1) * height);
        if (decodedLength > NativeArchive.MaximumBytes) throw new InvalidDataException("Decoded PNG exceeds bounds.");
        byte[] rows = new byte[decodedLength]; compressed.Position = 0;
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress))
        { zlib.ReadExactly(rows); if (zlib.ReadByte() != -1) throw new InvalidDataException("PNG has extra decompressed scanlines."); }
        byte[] scanlines = new byte[stride * height];
        for (int row = 0; row < height; row++)
            for (int column = 0; column < stride; column++)
            {
                int left = column >= filterBytes ? scanlines[row * stride + column - filterBytes] : 0;
                int above = row > 0 ? scanlines[(row - 1) * stride + column] : 0;
                int upperLeft = row > 0 && column >= filterBytes ? scanlines[(row - 1) * stride + column - filterBytes] : 0;
                int Paeth()
                {
                    int p = left + above - upperLeft, a = Math.Abs(p - left), b = Math.Abs(p - above), c = Math.Abs(p - upperLeft);
                    return a <= b && a <= c ? left : b <= c ? above : upperLeft;
                }
                int predictor = rows[row * (stride + 1)] switch { 0 => 0, 1 => left, 2 => above, 3 => (left + above) / 2, 4 => Paeth(), _ => throw new InvalidDataException("Unknown PNG filter.") };
                scanlines[row * stride + column] = unchecked((byte)(rows[row * (stride + 1) + 1 + column] + predictor));
            }
        uint[]? colours = paletteBytes == null ? null : Enumerable.Range(0, paletteBytes.Length / 3).Select(index =>
            ((uint)(transparency != null && index < transparency.Length ? transparency[index] : 255) << 24)
            | ((uint)paletteBytes[index * 3] << 16) | ((uint)paletteBytes[index * 3 + 1] << 8) | paletteBytes[index * 3 + 2]).ToArray();
        byte[]? indices = type == 3 ? new byte[width * height] : null; var image = new RasterImage(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int at = y * stride + x * components; uint pixel;
                if (type == 3)
                {
                    int index = (scanlines[y * stride + x * depth / 8] >> (8 - depth - (x * depth & 7))) & ((1 << depth) - 1);
                    if (index >= colours!.Length) throw new InvalidDataException("PNG palette index out of range.");
                    indices![y * width + x] = (byte)index; pixel = colours[index];
                }
                else
                {
                    byte red = scanlines[at], green = type is 0 or 4 ? red : scanlines[at + 1], blue = type is 0 or 4 ? red : scanlines[at + 2];
                    byte alpha = type == 6 ? scanlines[at + 3] : type == 4 ? scanlines[at + 1] : (byte)255;
                    if (transparency != null && type == 0)
                    { if (transparency.Length != 2) throw new InvalidDataException("Invalid grey tRNS."); if (red == BinaryPrimitives.ReadUInt16BigEndian(transparency)) alpha = 0; }
                    if (transparency != null && type == 2)
                    { if (transparency.Length != 6) throw new InvalidDataException("Invalid RGB tRNS."); if (red == BinaryPrimitives.ReadUInt16BigEndian(transparency) && green == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(2)) && blue == BinaryPrimitives.ReadUInt16BigEndian(transparency.AsSpan(4))) alpha = 0; }
                    pixel = ((uint)alpha << 24) | ((uint)red << 16) | ((uint)green << 8) | blue;
                }
                image.Set(x, y, pixel);
            }
        return new(image, indices, colours);
    }
}
