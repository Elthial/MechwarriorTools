using System.Numerics;
using MechwarriorTooling.Core;

namespace MechwarriorTooling.Rendering;

public sealed record SceneFace(Vector3[] Vertices, byte Colour, byte LineColour, bool Filled = true, bool BasePlane = false);
public sealed record ScenePoint(Vector3 Position, float Radius, byte Colour);
public sealed class Scene
{
    public List<SceneFace> Faces { get; } = [];
    public List<ScenePoint> Points { get; } = [];
    public byte? BackgroundColour { get; set; }
    public (int Columns, int Rows)? TerrainGrid { get; private set; }
    public void AddResource(ModelResource resource, Vector3 origin = default)
    {
        // Authored Mech parts share the same object origin (0810:0817).
        // Resource-local vectors use the same power-of-two scale as world/surface queries.
        if (resource.ScaleShift > 16) throw new InvalidDataException("Resource scale exceeds preview bounds.");
        float scale = MathF.ScaleB(1, resource.ScaleShift);
        foreach (var entry in resource.Entries)
        {
            if (entry.Variants.Length == 0) continue;
            foreach (var primitive in entry.Variants[0].Primitives.Take(1))
            {
                foreach (var polygon in primitive.Polygons)
                {
                    Vector3[] vertices = polygon.Indices.Select(index =>
                    {
                        var vertex = entry.Vectors[index];
                        return new Vector3(vertex.X, vertex.Y, vertex.Z) * scale + origin;
                    }).ToArray();
                    // 22B6:6501 selects +3/+4 with graphicsColourMode==0.
                    // Despite the old fill/line labels, 2A36:0071 fills with
                    // drawLineColour (+4); 0608 outlines with drawFillColour (+3).
                    Faces.Add(new(vertices, polygon.AlternateLine, polygon.AlternateFill, (polygon.Flags & 0x80) != 0));
                }
                if (primitive.Kind == 1)
                {
                    var payload = new BinaryData(primitive.Payload, "Ellipse");
                    var vector = entry.Vectors[payload.U8(2)];
                    Points.Add(new(new Vector3(vector.X, vector.Y, vector.Z) * scale + origin,
                        Math.Max(1, payload.I16(0) * scale), (byte)(payload.U8(4) & 0x1F)));
                }
            }
        }
    }
    public static Scene FromBank(ModelBank bank, IEnumerable<int> resourceIds)
    {
        var scene = new Scene();
        foreach (int id in resourceIds)
        {
            if ((uint)id >= bank.Resources.Length) throw new InvalidDataException($"Part resource {id} is outside the bank.");
            scene.AddResource(bank.Resources[id]);
        }
        return scene;
    }
    public static Scene Terrain(byte[] heights, int columns, int rows, LandscapeColours landscape)
    {
        if (heights.Length != columns * rows) throw new InvalidDataException("Terrain dimensions mismatch.");
        var scene = new Scene { BackgroundColour = landscape.Ground, TerrainGrid = (columns, rows) };
        // 0810:5565 world/grid mapping. GRD is collision data; hills/rivers
        // already have authored geometry. The inspection base is a flat plane
        // spanning ALL cells, not an invented GRD visual height mesh.
        Vector3 Corner(int column, int row) => new((row - 87) * 512, (47 - column) * 512, 0);
        scene.Faces.Add(new([Corner(0, 0), Corner(columns, 0), Corner(columns, rows), Corner(0, rows)],
            landscape.Ground, landscape.Ground, BasePlane: true));
        return scene;
    }
}

public static class PreviewClock
{
    public const double RevolutionSeconds = 10;
    public static double Angle(double activeSeconds) => (activeSeconds % RevolutionSeconds) / RevolutionSeconds * Math.Tau;
    public static int Cycle(double activeSeconds, int sequenceCount) => sequenceCount == 0 ? 0 : (int)(activeSeconds / RevolutionSeconds % sequenceCount);
}
