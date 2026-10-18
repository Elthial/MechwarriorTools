using System.Text;

namespace MechwarriorTooling.Core;

/// <summary>2A36:21BF/222D palette load/select and 0071 polygon-pattern selection.</summary>
public sealed class EgaPalette
{
    private readonly ushort[] primary = new ushort[32], secondary = new ushort[32];
    private readonly byte[] hardware = new byte[16];
    public string Name { get; }
    public static EgaPalette Initial { get; } = new("EGA initial palette");
    private EgaPalette(string name)
    {
        Name = name;
        for (int colour = 0; colour < 16; colour++)
        {
            primary[colour] = secondary[colour] = primary[colour + 16] = secondary[colour + 16] = (ushort)(colour * 0x0101);
            hardware[colour] = (byte)colour;
        }
    }
    public static EgaPalette Load(string path) => Parse(BinaryData.ReadFile(path), Path.GetFileName(path));
    public static EgaPalette Parse(byte[] bytes, string name = "PAL")
    {
        var file = new BinaryData(bytes, name); file.Require(0, 8);
        if (Encoding.ASCII.GetString(bytes, 0, 4) != "PAL:" || (file.U32(4) & 0x7FFFFFFF) != bytes.Length - 8)
            throw new InvalidDataException($"{name}: invalid PAL root.");
        byte[]? ega = null;
        for (int at = 8; at < bytes.Length;)
        {
            file.Require(at, 8); uint length = file.U32(at + 4);
            if (length > NativeArchive.MaximumBytes) throw new InvalidDataException("Palette chunk exceeds bounds.");
            file.Require(at + 8, (int)length);
            if (Encoding.ASCII.GetString(bytes, at, 4) == "EGA:")
            {
                if (ega != null || length != 128) throw new InvalidDataException("EGA palette needs one 128-byte chunk.");
                ega = bytes.AsSpan(at + 8, 128).ToArray();
            }
            at += 8 + (int)length;
        }
        return FromEgaChunk(ega ?? throw new InvalidDataException("Missing EGA palette chunk."), name);
    }
    public static EgaPalette FromEgaChunk(byte[] encoded, string name = "EGA chunk")
    {
        if (encoded.Length != 128) throw new InvalidDataException("EGA palette needs 128 bytes.");
        var palette = new EgaPalette(name);
        ushort Pattern(int at) => (ushort)((encoded[at] & 15) | ((encoded[at + 1] >> 4) << 8));
        for (int colour = 0; colour < 16; colour++)
        {
            // SelectPalette publishes identity slots 0..15, imports 16..31 and
            // programs hardware from the selected bank's low colour nibble.
            palette.primary[colour] = palette.secondary[colour] = (ushort)colour;
            palette.hardware[colour] = (byte)(Pattern(colour * 2) & 15);
            palette.primary[colour + 16] = Pattern((colour + 16) * 2);
            palette.secondary[colour + 16] = Pattern(64 + (colour + 16) * 2);
        }
        return palette;
    }
    private void Require(byte colour)
    {
        if (colour >= 32) throw new NotSupportedException($"Palette field 0x{colour:X2} selects an unloaded native palette bank; it must not be truncated to four bits.");
    }
    public byte PolygonPixel(byte colour, int x, int y)
    {
        Require(colour);
        ushort first = primary[colour], second = secondary[colour];
        ushort pattern = (y & 1) != 0 && first != second ? second : first;
        // Native single-phase path paints the low byte when both patterns match.
        int pixel = first != second && (x & 1) == 0 ? pattern >> 8 : pattern & 255;
        return hardware[pixel];
    }
    public byte LinePixel(byte colour)
    {
        Require(colour);
        return hardware[primary[colour] & 15]; // 2A36:0608 uses primary low byte only.
    }
}
