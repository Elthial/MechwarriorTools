namespace MechwarriorTooling.Core;

public readonly record struct NativeVector(short X, short Y, short Z, int FileOffset);
public sealed record ModelPolygon(byte Flags, byte Fill, byte Line, byte AlternateFill,
    byte AlternateLine, byte NormalIndex, byte[] Indices, int FileOffset);
public sealed record ModelPrimitive(byte Threshold, byte Kind, byte[] Payload, ModelPolygon[] Polygons);
public sealed record ModelVariant(ModelPrimitive[] Primitives);
public sealed record ModelEntry(byte ReferenceIndex, NativeVector[] Vectors, ModelVariant[] Variants);
public sealed record ModelResource(int Id, byte Flags, byte ScaleShift, short BaseScale,
    short[] Reference, ModelEntry[] Entries);

/// <summary>Retains the complete decoded allocation; edits never rebuild or discard unknown data.</summary>
public sealed class ModelBank
{
    public byte[] Decoded { get; }
    public ModelResource[] Resources { get; }
    private ModelBank(byte[] decoded, ModelResource[] resources) { Decoded = decoded; Resources = resources; }
    public static ModelBank Load(string path) => Parse(NativeArchive.Decode(BinaryData.ReadFile(path)));

    public static (int Start, int Base)[] Directory(byte[] decoded)
    {
        var data = new BinaryData(decoded, "Resource directory");
        var entries = new List<(int Start, int Base)>();
        for (int offset = 0; offset < decoded.Length; offset += 4)
        {
            uint pair = data.U32(offset);
            if (pair == 0)
            {
                if (entries.Any(entry => entry.Start < offset + 4)) throw new InvalidDataException("Resource overlaps directory.");
                return [.. entries];
            }
            if (entries.Count >= 8192) throw new InvalidDataException("Too many resources.");
            int paragraph = (int)(pair >> 16) * 16;
            int start = paragraph + (int)(pair & 0xFFFF);
            data.Require(start, 1);
            if (entries.Count != 0 && start <= entries[^1].Start) throw new InvalidDataException("Resource directory is not strictly increasing.");
            entries.Add((start, paragraph));
        }
        throw new InvalidDataException("Missing directory terminator.");
    }

    public static ModelBank Parse(byte[] decoded)
    {
        var data = new BinaryData(decoded, "Model bank");
        var directory = Directory(decoded);
        var resources = new List<ModelResource>();
        int budget = 100_000;
        for (int id = 0; id < directory.Length; id++)
        {
            var (start, paragraph) = directory[id];
            int end = id + 1 < directory.Length ? directory[id + 1].Start : decoded.Length;
            void Require(int offset, int size)
            {
                if (size < 0 || offset < start || offset > end - size) throw new InvalidDataException($"Resource {id}: table outside resource bounds.");
                if (--budget < 0) throw new InvalidDataException("Model traversal exceeds safety limit.");
            }
            int Near(int at) => paragraph + data.U16(at);
            Require(start, 14);
            int entryCount = Math.Max(0, (int)data.I16(start + 10));
            int entryTable = Near(start + 12);
            if (entryCount != 0) Require(entryTable, entryCount * 8);
            var entries = new List<ModelEntry>();
            for (int entryIndex = 0; entryIndex < entryCount; entryIndex++)
            {
                int entry = entryTable + entryIndex * 8;
                Require(entry, 8);
                int vectorCount = data.U8(entry + 1), vectorsAt = Near(entry + 2);
                Require(vectorsAt, vectorCount * 6);
                NativeVector[] vectors = new NativeVector[vectorCount];
                for (int vector = 0; vector < vectorCount; vector++)
                {
                    int at = vectorsAt + vector * 6;
                    vectors[vector] = new(data.I16(at), data.I16(at + 2), data.I16(at + 4), at);
                }
                if (data.U8(entry) >= vectorCount) throw new InvalidDataException("Reference vector exceeds entry vector count.");
                int variantCount = data.U16(entry + 4), variantTable = Near(entry + 6);
                if (variantCount != 0) Require(variantTable, variantCount * 4);
                var variants = new List<ModelVariant>();
                for (int variant = 0; variant < variantCount; variant++)
                {
                    int row = variantTable + variant * 4;
                    Require(row, 4);
                    int primitiveCount = Math.Max(0, (int)data.I16(row)), primitiveTable = Near(row + 2);
                    if (primitiveCount != 0) Require(primitiveTable, primitiveCount * 8);
                    var primitives = new List<ModelPrimitive>();
                    for (int primitive = 0; primitive < primitiveCount; primitive++)
                    {
                        int at = primitiveTable + primitive * 8;
                        Require(at, 8);
                        byte kind = data.U8(at + 1);
                        if (kind > 3) throw new InvalidDataException($"Unknown primitive {kind}.");
                        var polygons = new List<ModelPolygon>();
                        if (kind == 0)
                        {
                            int polygonCount = Math.Max(0, (int)data.I16(at + 2)), polygonTable = Near(at + 4);
                            if (polygonCount != 0) Require(polygonTable, polygonCount * 8);
                            for (int polygon = 0; polygon < polygonCount; polygon++)
                            {
                                int face = polygonTable + polygon * 8, cursor = Near(face + 6);
                                Require(face, 8);
                                var indices = new List<byte>();
                                for (;;)
                                {
                                    Require(cursor, 1);
                                    byte index = data.U8(cursor++);
                                    if (index == 255) break;
                                    if (index >= vectorCount) throw new InvalidDataException("Polygon vertex exceeds vector count.");
                                    if (indices.Count >= 4096) throw new InvalidDataException("Polygon index list exceeds safety limit.");
                                    indices.Add(index);
                                }
                                byte flags = data.U8(face), normal = data.U8(face + 5);
                                if ((flags & 3) is 1 or 3 && normal >= vectorCount) throw new InvalidDataException("Polygon normal exceeds vector count.");
                                polygons.Add(new(flags, data.U8(face + 1), data.U8(face + 2), data.U8(face + 3), data.U8(face + 4), normal, [.. indices], face));
                            }
                        }
                        if (kind is 1 or 3 && data.U8(at + (kind == 1 ? 4 : 6)) >= vectorCount)
                            throw new InvalidDataException("Primitive reference exceeds vector count.");
                        primitives.Add(new(data.U8(at), kind, decoded.AsSpan(at + 2, 6).ToArray(), [.. polygons]));
                    }
                    variants.Add(new([.. primitives]));
                }
                entries.Add(new(data.U8(entry), vectors, [.. variants]));
            }
            resources.Add(new(id, data.U8(start), data.U8(start + 1), data.I16(start + 2),
                [data.I16(start + 4), data.I16(start + 6), data.I16(start + 8)], [.. entries]));
        }
        return new ModelBank((byte[])decoded.Clone(), [.. resources]);
    }

    public static void ValidateSurfaceBank(byte[] decoded)
    {
        var directory = Directory(decoded);
        var data = new BinaryData(decoded, "Surface bank");
        for (int id = 0; id < directory.Length; id++)
        {
            var (start, paragraph) = directory[id];
            int end = id + 1 < directory.Length ? directory[id + 1].Start : decoded.Length;
            void Require(int at, int bytes)
            {
                if (at < start || bytes < 0 || at > end - bytes) throw new InvalidDataException($"Surface {id}: invalid reference.");
            }
            Require(start, 10);
            int count = data.U8(start + 7), patches = paragraph + data.U16(start + 8);
            if (count > 0) Require(patches, count * 16);
            for (int patch = 0; patch < count; patch++)
            {
                int at = patches + patch * 16;
                Require(paragraph + data.U16(at + 10), data.U8(at + 9) * 8);
                Require(paragraph + data.U16(at + 14), 8);
            }
        }
    }
}
