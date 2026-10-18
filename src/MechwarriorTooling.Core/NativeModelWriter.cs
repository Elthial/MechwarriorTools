using System.Buffers.Binary;

namespace MechwarriorTooling.Core;

/// <summary>Relocatable paragraph-relative bank writer; supports replacing or appending complete resources.</summary>
public static class NativeModelWriter
{
    public static byte[] Replace(ModelBank bank, int resource, ModelResource replacement)
    {
        if (resource < 0 || resource > bank.Resources.Length) throw new ArgumentOutOfRangeException(nameof(resource));
        var resources = bank.Resources.ToList();
        if (resource == resources.Count) resources.Add(replacement); else resources[resource] = replacement;
        return Write(resources);
    }
    public static byte[] Write(IReadOnlyList<ModelResource> resources)
    {
        if (resources.Count is < 1 or > 8192) throw new InvalidDataException("Invalid resource count.");
        using var output = new MemoryStream(); output.SetLength((resources.Count + 1) * 4);
        for (int id = 0; id < resources.Count; id++)
        {
            output.Position = output.Length;
            while ((output.Position & 15) != 0) output.WriteByte(0);
            int paragraph = checked((int)output.Position / 16);
            if (paragraph > ushort.MaxValue) throw new InvalidDataException("Bank exceeds DOS paragraph-address range.");
            byte[] body = Resource(resources[id]); output.Write(body);
            long end = output.Position; output.Position = id * 4;
            using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, true)) writer.Write((uint)paragraph << 16);
            output.Position = end;
        }
        byte[] decoded = output.ToArray(); ModelBank.Parse(decoded); return decoded;
    }
    private static byte[] Resource(ModelResource resource)
    {
        using var stream = new MemoryStream();
        int Allocate(int count)
        {
            int at = checked((int)stream.Length);
            if (count < 0 || at + count > ushort.MaxValue) throw new InvalidDataException("Resource exceeds its 16-bit near-pointer allocation.");
            stream.SetLength(at + count); return at;
        }
        void Word(int at, int value) => BinaryPrimitives.WriteUInt16LittleEndian(stream.GetBuffer().AsSpan(at), unchecked((ushort)value));
        void Byte(int at, byte value) => stream.GetBuffer()[at] = value;
        int header = Allocate(14), entries = Allocate(checked(resource.Entries.Length * 8));
        Byte(header, resource.Flags); Byte(1, resource.ScaleShift); Word(2, resource.BaseScale);
        for (int axis = 0; axis < 3; axis++) Word(4 + axis * 2, resource.Reference[axis]);
        Word(10, resource.Entries.Length); Word(12, entries);
        for (int index = 0; index < resource.Entries.Length; index++)
        {
            var entry = resource.Entries[index]; int at = entries + index * 8;
            if (entry.Vectors.Length is < 1 or > 255 || entry.ReferenceIndex >= entry.Vectors.Length)
                throw new InvalidDataException("Native entries require 1..255 vectors including normals/reference points.");
            Byte(at, entry.ReferenceIndex); Byte(at + 1, (byte)entry.Vectors.Length);
            int vectors = Allocate(entry.Vectors.Length * 6); Word(at + 2, vectors);
            for (int vector = 0; vector < entry.Vectors.Length; vector++)
            {
                var value = entry.Vectors[vector]; Word(vectors + vector * 6, value.X); Word(vectors + vector * 6 + 2, value.Y); Word(vectors + vector * 6 + 4, value.Z);
            }
            int variants = Allocate(entry.Variants.Length * 4); Word(at + 4, entry.Variants.Length); Word(at + 6, variants);
            for (int variantIndex = 0; variantIndex < entry.Variants.Length; variantIndex++)
            {
                var variant = entry.Variants[variantIndex]; int row = variants + variantIndex * 4;
                int records = Allocate(variant.Primitives.Length * 8); Word(row, variant.Primitives.Length); Word(row + 2, records);
                for (int recordIndex = 0; recordIndex < variant.Primitives.Length; recordIndex++)
                {
                    var record = variant.Primitives[recordIndex]; int recordAt = records + recordIndex * 8;
                    Byte(recordAt, record.Threshold); Byte(recordAt + 1, record.Kind);
                    if (record.Kind is 2 or 3) throw new NotSupportedException("Picture primitives need reference relocation before rebuilding this bank.");
                    if (record.Kind != 0)
                    {
                        if (record.Payload.Length != 6) throw new InvalidDataException("Invalid primitive payload.");
                        record.Payload.CopyTo(stream.GetBuffer(), recordAt + 2); continue;
                    }
                    int faces = Allocate(record.Polygons.Length * 8); Word(recordAt + 2, record.Polygons.Length); Word(recordAt + 4, faces);
                    // Preserve the unused final primitive word as well as all authored flags.
                    Byte(recordAt + 6, record.Payload[4]); Byte(recordAt + 7, record.Payload[5]);
                    for (int faceIndex = 0; faceIndex < record.Polygons.Length; faceIndex++)
                    {
                        var face = record.Polygons[faceIndex]; int faceAt = faces + faceIndex * 8;
                        Byte(faceAt, face.Flags); Byte(faceAt + 1, face.Fill); Byte(faceAt + 2, face.Line);
                        Byte(faceAt + 3, face.AlternateFill); Byte(faceAt + 4, face.AlternateLine); Byte(faceAt + 5, face.NormalIndex);
                        int indices = Allocate(face.Indices.Length + 1); Word(faceAt + 6, indices);
                        face.Indices.CopyTo(stream.GetBuffer(), indices); Byte(indices + face.Indices.Length, 255);
                    }
                }
            }
        }
        return stream.ToArray();
    }
}
