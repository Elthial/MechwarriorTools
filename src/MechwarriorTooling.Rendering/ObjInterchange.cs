using System.Globalization;
using System.Numerics;
using System.Text;
using MechwarriorTooling.Core;

namespace MechwarriorTooling.Rendering;

/// <summary>Blender-compatible OBJ/MTL. Coordinates stay game A/B/C, Z-up, with applied resource scale.</summary>
public static class ObjInterchange
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    public static string SelectResource(string text, int id)
    {
        bool tagged = text.Split('\n').Any(line => line.TrimStart().StartsWith("o mw_r", StringComparison.Ordinal));
        if (!tagged) return text; // Fresh Blender mesh: the CLI explicitly selects its destination resource.
        bool selected = false, found = false; var output = new StringBuilder();
        foreach (string line in text.Split('\n'))
        {
            string value = line.Trim();
            if (value.StartsWith("o ", StringComparison.Ordinal))
            {
                selected = value.StartsWith($"o mw_r{id:D4}_", StringComparison.Ordinal); found |= selected;
            }
            // Retain all vertex declarations in their original positions: positive AND negative indices remain valid.
            if (value.StartsWith("v ", StringComparison.Ordinal) || value.StartsWith("vn ", StringComparison.Ordinal) || value.StartsWith("usemtl ", StringComparison.Ordinal) || selected) output.AppendLine(line);
        }
        if (!found) throw new InvalidDataException($"OBJ has no mw_r{id:D4}_ object. Preserve resource names for bank/Mech round trips.");
        return output.ToString();
    }
    public static (string Obj, string Mtl) Export(ModelBank bank, IEnumerable<int> resources, string materialFile, EgaPalette palette)
    {
        var obj = new StringBuilder("# MechwarriorTooling: native A/B/C, Z-up; flat faces. Preserve mw_* material names.\n");
        obj.AppendLine("mtllib " + materialFile);
        var materials = new Dictionary<string, byte>(); int vertexBase = 1, normalIndex = 0;
        foreach (int id in resources)
        {
            if ((uint)id >= bank.Resources.Length) throw new ArgumentOutOfRangeException(nameof(resources));
            var resource = bank.Resources[id]; float scale = MathF.ScaleB(1, resource.ScaleShift);
            for (int entryIndex = 0; entryIndex < resource.Entries.Length; entryIndex++)
            {
                var entry = resource.Entries[entryIndex];
                for (int variant = 0; variant < entry.Variants.Length; variant++)
                    for (int record = 0; record < entry.Variants[variant].Primitives.Length; record++)
                    {
                        var primitive = entry.Variants[variant].Primitives[record];
                        if (primitive.Kind != 0) continue; // Point/picture records are not meshes; preserved in native source, never invented as spheres.
                        obj.AppendLine($"o mw_r{id:D4}_e{entryIndex:D3}_v{variant:D2}_d{record:D2}");
                        foreach (var vertex in entry.Vectors)
                            obj.AppendLine(FormattableString.Invariant($"v {vertex.X * scale:R} {vertex.Y * scale:R} {vertex.Z * scale:R}"));
                        foreach (var polygon in primitive.Polygons)
                        {
                            string material = $"mw_p{polygon.Fill:X2}_l{polygon.Line:X2}_e{polygon.AlternateLine:X2}_o{polygon.AlternateFill:X2}_f{polygon.Flags:X2}";
                            materials[material] = polygon.AlternateLine; obj.AppendLine("usemtl " + material);
                            if (polygon.Indices.Length >= 3)
                            {
                                var normal = entry.Vectors[polygon.NormalIndex < entry.Vectors.Length ? polygon.NormalIndex : 0];
                                obj.AppendLine(FormattableString.Invariant($"vn {normal.X / 16384.0:R} {normal.Y / 16384.0:R} {normal.Z / 16384.0:R}")); normalIndex++;
                                obj.AppendLine("f " + string.Join(" ", polygon.Indices.Select(index => $"{index + vertexBase}//{normalIndex}")));
                            }
                            else if (polygon.Indices.Length == 2) obj.AppendLine("l " + string.Join(" ", polygon.Indices.Select(index => index + vertexBase)));
                        }
                        vertexBase += entry.Vectors.Length;
                    }
            }
        }
        var mtl = new StringBuilder("# Native colour fields encoded in material names; RGB is only a Blender preview.\n");
        foreach (var material in materials)
        {
            uint rgb = SoftwareRenderer.Ega[palette.PolygonPixel(material.Value, 0, 0)];
            mtl.AppendLine("newmtl " + material.Key);
            mtl.AppendLine(FormattableString.Invariant($"Kd {((rgb >> 16) & 255) / 255.0:R} {((rgb >> 8) & 255) / 255.0:R} {(rgb & 255) / 255.0:R}"));
            mtl.AppendLine("Ka 0 0 0\nKs 0 0 0\nillum 1\n");
        }
        return (obj.ToString(), mtl.ToString());
    }
    public static ModelResource Import(string text, ModelResource? original = null)
    {
        if (text.Length > NativeArchive.MaximumBytes) throw new InvalidDataException("OBJ exceeds safety limit.");
        if (original?.ScaleShift > 16) throw new NotSupportedException("Unreviewed resource coordinate scale.");
        var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var objects = new List<List<(int[] Indices, string Material, Vector3? Normal)>>();
        List<(int[] Indices, string Material, Vector3? Normal)> current = []; objects.Add(current); string material = "mw_p07_l00_e07_o00_f81";
        foreach (string raw in text.Split('\n'))
        {
            string[] tokens = raw.Split('#')[0].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;
            switch (tokens[0])
            {
                case "v":
                    if (tokens.Length != 4) throw new InvalidDataException("OBJ needs three-component vertices; apply transforms before export.");
                    var vector = new Vector3(float.Parse(tokens[1], Invariant), float.Parse(tokens[2], Invariant), float.Parse(tokens[3], Invariant));
                    if (!float.IsFinite(vector.X) || !float.IsFinite(vector.Y) || !float.IsFinite(vector.Z)) throw new InvalidDataException("Non-finite OBJ coordinate.");
                    vertices.Add(vector); if (vertices.Count > 1_000_000) throw new InvalidDataException("Too many OBJ vertices."); break;
                case "o": case "g": if (current.Count > 0) { current = []; objects.Add(current); } break;
                case "vn":
                    if (tokens.Length != 4) throw new InvalidDataException("Invalid OBJ normal.");
                    normals.Add(new(float.Parse(tokens[1], Invariant), float.Parse(tokens[2], Invariant), float.Parse(tokens[3], Invariant))); break;
                case "usemtl": if (tokens.Length != 2) throw new InvalidDataException("Invalid material selection."); material = tokens[1]; break;
                case "f": case "l":
                    if (tokens.Length < (tokens[0] == "f" ? 4 : 3)) throw new InvalidDataException("Empty/degenerate OBJ face.");
                    int[] indices = tokens.Skip(1).Select(token =>
                    {
                        int index = int.Parse(token.Split('/')[0], Invariant);
                        index = index < 0 ? vertices.Count + index : index - 1;
                        if ((uint)index >= vertices.Count) throw new InvalidDataException("OBJ index outside vertex list."); return index;
                    }).ToArray();
                    if (indices.Length > 20) throw new InvalidDataException("Face exceeds the native 20-vertex polygon workspace. Triangulate or split the face in Blender.");
                    Vector3? authoredNormal = null;
                    string[] first = tokens[1].Split('/');
                    if (first.Length >= 3 && first[2].Length > 0)
                    {
                        int normal = int.Parse(first[2], Invariant); normal = normal < 0 ? normals.Count + normal : normal - 1;
                        if ((uint)normal >= normals.Count) throw new InvalidDataException("OBJ normal index outside list."); authoredNormal = normals[normal];
                    }
                    current.Add((indices, material, authoredNormal)); if (current.Count > 4096) throw new InvalidDataException("Too many faces in one native entry."); break;
                case "p": throw new NotSupportedException("OBJ point import is unsupported; use a polygon mesh.");
            }
        }
        float scale = MathF.ScaleB(1, original?.ScaleShift ?? 0);
        var entries = new List<ModelEntry>();
        foreach (var faces in objects.Where(group => group.Count > 0))
        {
            var vectors = new List<NativeVector>(); var vertexMap = new Dictionary<int, byte>(); var polygons = new List<ModelPolygon>();
            byte Add(Vector3 value)
            {
                if (vectors.Count >= 255) throw new InvalidDataException("Entry exceeds 255 native vectors including face normals. Split the mesh into Blender objects.");
                short Quantize(float component)
                {
                    double rounded = Math.Round(component, MidpointRounding.AwayFromZero);
                    if (!double.IsFinite(rounded) || rounded < short.MinValue || rounded > short.MaxValue) throw new InvalidDataException("Vector exceeds native signed 16-bit range; scale the mesh down.");
                    return (short)rounded;
                }
                vectors.Add(new(Quantize(value.X), Quantize(value.Y), Quantize(value.Z), 0)); return (byte)(vectors.Count - 1);
            }
            // Native entry ordering has its own reference vector, not necessarily
            // an OBJ face vertex. Retain it for corresponding original entries.
            if (original != null && entries.Count < original.Entries.Length)
            {
                var old = original.Entries[entries.Count]; var reference = old.Vectors[old.ReferenceIndex];
                Add(new(reference.X, reference.Y, reference.Z));
            }
            else
            {
                var used = faces.SelectMany(face => face.Indices).Distinct().Select(index => vertices[index] / scale).ToArray();
                Add(used.Aggregate(Vector3.Zero, (sum, value) => sum + value) / used.Length);
            }
            foreach (var face in faces)
            {
                byte[] indices = face.Indices.Select(index =>
                {
                    if (!vertexMap.TryGetValue(index, out byte mapped)) { mapped = Add(vertices[index] / scale); vertexMap.Add(index, mapped); }
                    return mapped;
                }).ToArray();
                string[] fields = face.Material.Split('_');
                if (fields.Length != 6 || fields[0] != "mw" || fields.Skip(1).Any(field => field.Length != 3)
                    || fields[1][0] != 'p' || fields[2][0] != 'l' || fields[3][0] != 'e' || fields[4][0] != 'o' || fields[5][0] != 'f')
                    throw new InvalidDataException($"Material '{face.Material}' needs native fields: mw_p07_l00_e07_o00_f81. Preserve exported names or assign a native material.");
                byte[] values = fields.Skip(1).Select(field => byte.Parse(field[1..], NumberStyles.HexNumber, Invariant)).ToArray();
                if (values.Take(4).Any(value => value >= 32)) throw new InvalidDataException("Only loaded colour indices 0..31 are supported.");
                byte normal = 0;
                if (indices.Length >= 3)
                {
                    Vector3 Position(byte index) { var value = vectors[index]; return new(value.X, value.Y, value.Z); }
                    Vector3 sum = Vector3.Zero;
                    for (int index = 1; index < indices.Length - 1; index++) sum += Vector3.Cross(Position(indices[index]) - Position(indices[0]), Position(indices[index + 1]) - Position(indices[0]));
                    if (sum.LengthSquared() == 0 && face.Normal == null) throw new InvalidDataException("Face degenerates after native coordinate quantization without an authored normal.");
                    normal = Add((sum.LengthSquared() == 0 ? face.Normal!.Value : Vector3.Normalize(sum)) * 16384); // Preserve native authored degenerate faces; recalculate other Q14 normals.
                }
                else values[4] = (byte)(values[4] & ~0x80);
                polygons.Add(new(values[4], values[0], values[1], values[3], values[2], normal, indices, 0));
            }
            entries.Add(new(0, [.. vectors], [new([new(255, 0, new byte[6], [.. polygons])])]));
            if (entries.Count > 48) throw new InvalidDataException("Resource exceeds the native 48-entry ordering workspace. Merge/simplify Blender objects.");
        }
        if (entries.Count == 0) throw new InvalidDataException("OBJ contains no mesh faces or lines.");
        // Independent Blender objects own separate vectors; do not retain shared-geometry mode.
        return new(original?.Id ?? 0, (byte)((original?.Flags ?? 0) & ~0x80), original?.ScaleShift ?? 0,
            original?.BaseScale ?? 256, original?.Reference ?? new short[3], [.. entries]);
    }
}
