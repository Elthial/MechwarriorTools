namespace MechwarriorTooling.Core;

public sealed record WorldPlacement(ushort ResourceId, int PositionA, int PositionB, int Altitude, int FileOffset);
public sealed class TerrainTile
{
    public const int Columns = 47, Rows = 87, ByteCount = Columns * Rows;
    public byte[] Heights { get; }
    private TerrainTile(byte[] bytes) => Heights = (byte[])bytes.Clone();
    public static TerrainTile Parse(byte[] bytes)
    {
        if (bytes.Length != ByteCount) throw new InvalidDataException($"GRD needs {ByteCount} height bytes, got {bytes.Length}.");
        return new(bytes);
    }
    public static WorldPlacement[] ParsePlacements(byte[] bytes)
    {
        if (bytes.Length % 14 != 0 || bytes.Length > NativeArchive.MaximumBytes) throw new InvalidDataException("WLD contains a partial or oversized placement table.");
        var input = new BinaryData(bytes, "WLD");
        return Enumerable.Range(0, bytes.Length / 14).Select(index =>
        {
            int at = index * 14;
            return new WorldPlacement(input.U16(at), input.I32(at + 2), input.I32(at + 6), input.I32(at + 10), at);
        }).ToArray();
    }
}

public sealed record ScenarioSelection(int Id, byte[] Tiles, byte[] Formations, byte[] HeadingFlags);
public static class ScenarioData
{
    public static ScenarioSelection[] Parse(byte[] bytes)
    {
        if (bytes.Length != 0xE40) throw new InvalidDataException("snario.dat needs 0x0E40 bytes.");
        return Enumerable.Range(0, 60).Select(id =>
        {
            int at = id * 16;
            byte[] tiles = bytes.AsSpan(at, 4).ToArray();
            if (tiles.Any(tile => tile > 15)) throw new InvalidDataException("Scenario tile exceeds 0..15.");
            return new ScenarioSelection(id, tiles, bytes.AsSpan(at + 4, 10).ToArray(), bytes.AsSpan(at + 14, 2).ToArray());
        }).ToArray();
    }
}

public sealed record AssetFile(string Name, long Bytes, string Category);
public sealed class GameInstallation
{
    public string Root { get; }
    public AssetFile[] Files { get; }
    public GameInstallation(string directory)
    {
        Root = Path.GetFullPath(directory);
        if (!Directory.Exists(Root)) throw new DirectoryNotFoundException(Root);
        Files = Directory.EnumerateFiles(Root).Select(path => new FileInfo(path)).OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(file => new AssetFile(file.Name, file.Length, Category(file.Extension))).ToArray();
    }
    public string FilePath(string name)
    {
        var file = Files.SingleOrDefault(file => file.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return file == null ? throw new FileNotFoundException($"Installation is missing {name}.") : Path.Combine(Root, file.Name);
    }
    private static string Category(string extension) => extension.ToLowerInvariant() switch
    {
        ".tbl" => "Models", ".gi" => "Collision", ".grd" or ".wld" => "Maps",
        ".snd" or ".mus" => "Sounds", ".pal" => "Palette", ".exe" => "Executable", _ => "Other"
    };
}
