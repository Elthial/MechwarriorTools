using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace MechwarriorTooling.Rendering;

public sealed class RasterImage
{
    public int Width { get; }
    public int Height { get; }
    public uint[] Pixels { get; }
    public RasterImage(int width, int height, uint background = 0xFF101923)
    {
        if (width < 1 || height < 1 || width > 4096 || height > 4096) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width; Height = height; Pixels = new uint[checked(width * height)]; Array.Fill(Pixels, background);
    }
    public void Set(int x, int y, uint colour) { if ((uint)x < Width && (uint)y < Height) Pixels[y * Width + x] = colour; }
    public void Line(int x0, int y0, int x1, int y1, uint colour)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1, dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1, error = dx + dy;
        for (int budget = 0; budget < 32768; budget++)
        {
            Set(x0, y0, colour);
            if (x0 == x1 && y0 == y1) return;
            int twice = 2 * error;
            if (twice >= dy) { error += dy; x0 += sx; }
            if (twice <= dx) { error += dx; y0 += sy; }
        }
    }
    public void WritePng(Stream output)
    {
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, Width); BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), Height);
        header[8] = 8; header[9] = 6;
        Chunk(output, "IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
        {
            byte[] row = new byte[Width * 4 + 1];
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    uint pixel = Pixels[y * Width + x]; int at = x * 4 + 1;
                    row[at] = (byte)(pixel >> 16); row[at + 1] = (byte)(pixel >> 8); row[at + 2] = (byte)pixel; row[at + 3] = (byte)(pixel >> 24);
                }
                zlib.Write(row);
            }
        }
        Chunk(output, "IDAT", compressed.ToArray()); Chunk(output, "IEND", []);
    }
    internal static void Chunk(Stream stream, string name, byte[] payload)
    {
        Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(number, payload.Length); stream.Write(number);
        byte[] tag = Encoding.ASCII.GetBytes(name); stream.Write(tag); stream.Write(payload);
        uint crc = 0xFFFFFFFF;
        foreach (byte value in tag.Concat(payload))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xEDB88320u : 0);
        }
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); stream.Write(number);
    }
}
