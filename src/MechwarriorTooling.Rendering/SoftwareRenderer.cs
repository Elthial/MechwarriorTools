using System.Numerics;
using MechwarriorTooling.Core;

namespace MechwarriorTooling.Rendering;

/// <summary>Deterministic inspection renderer using EGA indices. Not yet native pixel-conformant.</summary>
public static class SoftwareRenderer
{
    public const uint SoftWhite = 0xFFE0E0E0; // Optional display preference, never a native palette replacement.
    public static readonly uint[] Ega = [0xFF000000, 0xFF0000AA, 0xFF00AA00, 0xFF00AAAA,
        0xFFAA0000, 0xFFAA00AA, 0xFFAA5500, 0xFFAAAAAA, 0xFF555555, 0xFF5555FF,
        0xFF55FF55, 0xFF55FFFF, 0xFFFF5555, 0xFFFF55FF, 0xFFFFFF55, 0xFFFFFFFF];
    private sealed record Projected(Vector3[] Vertices, byte Colour, byte Line, bool Filled, float Depth, bool BasePlane);
    public static RasterImage Render(Scene scene, int width, int height, double yaw = 0.4, double tilt = 0.2,
        double zoom = 1, bool wireframe = false, bool axes = false, EgaPalette? palette = null, bool mapProjection = false, bool softenWhite = false)
    {
        palette ??= EgaPalette.Initial;
        uint Pixel(byte hardwareColour) => softenWhite && hardwareColour == 15 ? SoftWhite : Ega[hardwareColour];
        var image = new RasterImage(width, height);
        if (mapProjection && scene.BackgroundColour is byte background)
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++) image.Set(x, y, Pixel(palette.PolygonPixel(background, x, y)));
        var all = scene.Faces.SelectMany(face => face.Vertices).Concat(scene.Points.SelectMany(point => new[] { point.Position - new Vector3(point.Radius), point.Position + new Vector3(point.Radius) })).ToArray();
        if (all.Length == 0) return image;
        Vector3 minimum = all.Aggregate(Vector3.Min), maximum = all.Aggregate(Vector3.Max), centre = (minimum + maximum) / 2;
        float radius = Math.Max(1, (maximum - minimum).Length() / 2);
        float scale = (float)(Math.Min(width, height) * 0.42 / radius * Math.Clamp(zoom, 0.1, 8));
        float sine = (float)Math.Sin(yaw), cosine = (float)Math.Cos(yaw), pitchSin = (float)Math.Sin(tilt), pitchCos = (float)Math.Cos(tilt);
        Vector3 Transform(Vector3 point)
        {
            if (mapProjection && scene.TerrainGrid is { } grid)
                return new((point.X / 512 + 87) * width / grid.Rows, (47 - point.Y / 512) * height / grid.Columns, -point.Z);
            point -= centre;
            float x = point.X * cosine - point.Y * sine, depth = point.X * sine + point.Y * cosine;
            return new(width / 2f + x * scale, height / 2f - (point.Z * pitchCos - depth * pitchSin) * scale,
                depth * pitchCos + point.Z * pitchSin);
        }
        var faces = scene.Faces.Where(face => face.Vertices.Length > 0).Select(face =>
        {
            var vertices = face.Vertices.Select(Transform).ToArray();
            return new Projected(vertices, face.Colour, face.LineColour, face.Filled, vertices.Average(vertex => vertex.Z), face.BasePlane);
        }).OrderByDescending(face => face.BasePlane).ThenByDescending(face => face.Depth);
        foreach (var face in faces)
        {
            var vertices = face.Vertices;
            if (!wireframe && face.Filled && vertices.Length >= 3)
            {
                int firstRow = Math.Max(0, (int)MathF.Ceiling(vertices.Min(vertex => vertex.Y) - 0.5f));
                int lastRow = Math.Min(height - 1, (int)MathF.Floor(vertices.Max(vertex => vertex.Y) - 0.5f));
                var intersections = new List<float>(vertices.Length);
                for (int y = firstRow; y <= lastRow; y++)
                {
                    intersections.Clear();
                    for (int index = 0; index < vertices.Length; index++)
                    {
                        Vector3 a = vertices[index], b = vertices[(index + 1) % vertices.Length];
                        float scan = y + 0.5f;
                        if ((a.Y <= scan && b.Y > scan) || (b.Y <= scan && a.Y > scan)) intersections.Add(a.X + (scan - a.Y) * (b.X - a.X) / (b.Y - a.Y));
                    }
                    intersections.Sort();
                    for (int pair = 0; pair + 1 < intersections.Count; pair += 2)
                        for (int x = Math.Max(0, (int)MathF.Ceiling(intersections[pair] - 0.5f)); x <= Math.Min(width - 1, (int)MathF.Floor(intersections[pair + 1] - 0.5f)); x++)
                            image.Set(x, y, Pixel(palette.PolygonPixel(face.Colour, x, y)));
                }
            }
            if (wireframe || !face.Filled || vertices.Length < 3 || face.Colour != face.Line)
                for (int index = 0; index < vertices.Length; index++)
                {
                    var a = vertices[index]; var b = vertices[(index + 1) % vertices.Length];
                    image.Line((int)a.X, (int)a.Y, (int)b.X, (int)b.Y, Pixel(wireframe ? (byte)11 : palette.LinePixel(face.Line)));
                }
        }
        // Orthographic point/ellipse inspection; native perspective clamping
        // and inter-primitive ordering remain a separate fidelity gate.
        foreach (var point in scene.Points)
        {
            var centrePoint = Transform(point.Position); int radiusPixels = Math.Clamp((int)(point.Radius * scale), 1, 256);
            for (int y = -radiusPixels; y <= radiusPixels; y++)
            {
                int halfWidth = (int)Math.Sqrt((long)radiusPixels * radiusPixels - y * y);
                for (int x = -halfWidth; x <= halfWidth; x++)
                    if (!wireframe || Math.Abs(x) == halfWidth)
                    {
                        int pixelX = (int)centrePoint.X + x, pixelY = (int)centrePoint.Y + y;
                        image.Set(pixelX, pixelY, Pixel(palette.PolygonPixel(point.Colour, pixelX, pixelY)));
                    }
            }
        }
        if (axes)
        {
            var origin = Transform(centre);
            Vector3[] directions = [Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ];
            int[] colours = [12, 10, 9];
            for (int axis = 0; axis < 3; axis++)
            {
                var end = Transform(centre + directions[axis] * radius / 2);
                image.Line((int)origin.X, (int)origin.Y, (int)end.X, (int)end.Y, Pixel((byte)colours[axis]));
            }
        }
        return image;
    }
    // Native briefing 0810:4176 draws world resources over the map camera's
    // landscape ground, not invented GRD height colours. A increases right;
    // B increases up, matching row/column world mapping. Projection remains inspection-only.
    public static RasterImage Briefing(Scene scene, int width, int height, EgaPalette palette, bool softenWhite = false)
        => Render(scene, width, height, yaw: 0, tilt: -Math.PI / 2, palette: palette, mapProjection: true, softenWhite: softenWhite);
}
