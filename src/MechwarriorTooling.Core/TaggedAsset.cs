using System.Text;

namespace MechwarriorTooling.Core;

public sealed record AssetChunk(string Tag, byte[] Bytes);

/// <summary>Dynamix four-character tagged files. Unknown chunks survive replacement.</summary>
public sealed class TaggedAsset(string tag, AssetChunk[] chunks)
{
    public string Tag { get; } = tag;
    public AssetChunk[] Chunks { get; } = chunks;
    public byte[] Chunk(string name) => Chunks.SingleOrDefault(chunk => chunk.Tag == name)?.Bytes
        ?? throw new InvalidDataException($"Missing {name} chunk.");
    public static TaggedAsset Parse(byte[] bytes)
    {
        var data = new BinaryData(bytes, "Tagged asset"); data.Require(0, 8);
        if ((data.U32(4) & 0x7FFFFFFF) != bytes.Length - 8) throw new InvalidDataException("Invalid tagged root length.");
        var chunks = new List<AssetChunk>();
        for (int offset = 8; offset < bytes.Length;)
        {
            data.Require(offset, 8); uint size = data.U32(offset + 4);
            if (size > NativeArchive.MaximumBytes) throw new InvalidDataException("Oversized tagged chunk.");
            data.Require(offset + 8, (int)size);
            string name = Encoding.ASCII.GetString(bytes, offset, 4);
            if (chunks.Any(chunk => chunk.Tag == name)) throw new InvalidDataException("Ambiguous duplicate chunk.");
            chunks.Add(new(name, bytes.AsSpan(offset + 8, (int)size).ToArray())); offset += 8 + (int)size;
        }
        return new(Encoding.ASCII.GetString(bytes, 0, 4), [.. chunks]);
    }
    public byte[] Replace(string name, byte[] replacement)
        => new TaggedAsset(Tag, Chunks.Select(chunk => chunk.Tag == name ? new AssetChunk(name, replacement) : chunk).ToArray()).Write();
    public byte[] Write()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes(Tag)); writer.Write(0u);
        foreach (var chunk in Chunks)
        { writer.Write(Encoding.ASCII.GetBytes(chunk.Tag)); writer.Write(chunk.Bytes.Length); writer.Write(chunk.Bytes); }
        if (stream.Length > NativeArchive.MaximumBytes) throw new InvalidDataException("Tagged output exceeds limit.");
        stream.Position = 4; writer.Write((uint)(stream.Length - 8) | 0x80000000u); return stream.ToArray();
    }
}
