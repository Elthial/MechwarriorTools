using System.Globalization;
using System.Text.Json;
using MechwarriorTooling.Core;
using MechwarriorTooling.Rendering;

try
{
    if (args.Length == 0 || args[0] is "help" or "--help")
    {
        Console.WriteLine("""
            MechwarriorTooling (.NET 10)
              inventory --game-dir PATH
              mechs list --game-dir PATH
              mechs render --game-dir PATH --mech Jenner --sequence 1 --frame 0 --output FILE.png
              models inspect --bank PATH [--resource 0] [--game-dir PATH]
              models list --bank PATH [--game-dir PATH]
              models render --bank PATH --resource 0 --output FILE.png
              models edit-vertex --bank PATH --resource 0 --entry 0 --vertex 0 --x 0 --y 0 --z 0 --output COPY.tbl
              models edit-colour --bank PATH --resource 0 --entry 0 --polygon 0 --fill 7 --line 0 [--colour-mode ega|primary] --output COPY.tbl
              maps inspect --grid PATH [--world PATH]
              maps render --grid PATH --view briefing|3d --output FILE.png [--palette FILE.pal] [--game-dir PATH] [--soften-white]
              maps edit-height --grid PATH --column 0 --row 0 --height 10 --output COPY.grd
              maps edit-placement --world PATH --object 0 --resource 0 --a 0 --b 0 --altitude 0 --terrain-bank PATH --output COPY.wld
              scenarios inspect --file PATH
              sounds inspect --bank PATH
              sounds render --bank PATH --event 1 --seconds 10 --output FILE.wav [--trace FILE.json]
            Render commands accept --palette FILE.pal; otherwise use tropic.pal in the installation/bank directory.
            edit-colour defaults to EGA: --fill changes the native surface (+4), --line the outline (+3).
            JSON inspection is the default. Export never overwrites an existing file.
            Model previews are palette-indexed inspection renders, not native pixel-conformance claims.
            """);
        Console.WriteLine(InterchangeCommands.Help);
        return 0;
    }
    if (InterchangeCommands.Run(args) is int interchangeResult) return interchangeResult;
    string Option(string name, string? fallback = null)
    {
        int at = Array.IndexOf(args, "--" + name);
        if (at < 0) return fallback ?? throw new ArgumentException($"Missing --{name}.");
        if (at + 1 >= args.Length || args[at + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Missing --{name} value.");
        return args[at + 1];
    }
    int Number(string name, int fallback = 0)
    {
        string value = Option(name, fallback.ToString(CultureInfo.InvariantCulture));
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt32(value[2..], 16) : int.Parse(value, CultureInfo.InvariantCulture);
    }
    void Json(object value) => Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    EgaPalette Palette(string? resource = null)
    {
        string explicitPath = Option("palette", "");
        if (explicitPath.Length != 0) return EgaPalette.Load(explicitPath);
        string directory = resource == null ? Option("game-dir") : Option("game-dir", Path.GetDirectoryName(Path.GetFullPath(resource))!);
        return EgaPalette.Load(new GameInstallation(directory).FilePath("tropic.pal"));
    }
    void Png(RasterImage image)
    {
        using var stream = new FileStream(Option("output"), FileMode.CreateNew, FileAccess.Write);
        image.WritePng(stream);
    }
    string verb = args.Length > 1 ? args[1] : "";
    if (args[0] == "inventory")
    {
        var game = new GameInstallation(Option("game-dir")); Json(new { schemaVersion = 1, game.Root, game.Files });
    }
    else if (args[0] == "mechs")
    {
        var game = new GameInstallation(Option("game-dir")); var definitions = CombatDefinitions.Load(game.FilePath("BTECH.EXE"));
        if (verb == "list") Json(new { schemaVersion = 1, definitions.Mechs });
        else if (verb == "render")
        {
            var mech = definitions.Mechs.SingleOrDefault(mech => mech.Name.Equals(Option("mech"), StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException("Unknown Mech name.");
            var sequence = definitions.Sequences(mech.Index).Single(sequence => sequence.Id == Number("sequence"));
            var bank = ModelBank.Load(game.FilePath(mech.BankName));
            Png(SoftwareRenderer.Render(Scene.FromBank(bank, definitions.PartResources(mech.Index, sequence, Number("frame"))), 640, 480, palette: Palette()));
        }
        else throw new ArgumentException("Unknown mechs command.");
    }
    else if (args[0] == "models")
    {
        string path = Path.GetFullPath(Option("bank")); var bank = ModelBank.Load(path); int id = Number("resource");
        if ((uint)id >= bank.Resources.Length) throw new ArgumentException("Resource is outside the bank.");
        if (verb is "inspect" or "list")
        {
            string gameDirectory = Option("game-dir", Path.GetDirectoryName(path)!);
            var game = new GameInstallation(gameDirectory);
            string? executable = game.Files.FirstOrDefault(file => file.Name.Equals("BTECH.EXE", StringComparison.OrdinalIgnoreCase))?.Name;
            var definitions = executable == null ? null : CombatDefinitions.Load(game.FilePath(executable));
            object Identify(int resource) => new { id = resource, name = ModelCatalog.Name(path, resource, definitions),
                evidence = ModelCatalog.Evidence(path, resource, definitions), part = definitions?.FindModelResource(path, resource) };
            if (verb == "list") Json(new { schemaVersion = 1, resources = bank.Resources.Select(resource => Identify(resource.Id)).ToArray() });
            else Json(new { schemaVersion = 1, name = ModelCatalog.Name(path, id, definitions), identification = Identify(id), resources = bank.Resources.Length, resource = bank.Resources[id] });
        }
        else if (verb == "render") Png(SoftwareRenderer.Render(Scene.FromBank(bank, [id]), 640, 480, palette: Palette(path)));
        else if (verb == "edit-colour")
        {
            var polygons = bank.Resources[id].Entries[Number("entry")].Variants.SelectMany(item => item.Primitives).SelectMany(item => item.Polygons).ToArray();
            var polygon = polygons[Number("polygon")];
            var document = new AssetDocument(path, Path.GetDirectoryName(path)!, bank.Decoded, true);
            byte fill = checked((byte)Number("fill")), line = checked((byte)Number("line"));
            string mode = Option("colour-mode", "ega");
            if (mode is not ("ega" or "primary")) throw new ArgumentException("--colour-mode must be ega or primary.");
            document.Change(data => { data.Bytes[polygon.FileOffset + (mode == "ega" ? 4 : 1)] = fill; data.Bytes[polygon.FileOffset + (mode == "ega" ? 3 : 2)] = line; }, bytes => ModelBank.Parse(bytes));
            document.SaveCopy(Option("output"), bytes => ModelBank.Parse(bytes)); Console.WriteLine(document.DescribeChanges());
        }
        else if (verb == "edit-vertex")
        {
            var entry = bank.Resources[id].Entries[Number("entry")]; int vertex = Number("vertex");
            var selected = entry.Vectors[vertex];
            if (!entry.Variants.SelectMany(item => item.Primitives).SelectMany(item => item.Polygons).Any(face => face.Indices.Contains((byte)vertex)))
                throw new ArgumentException("Selected vector is not a polygon vertex.");
            if (bank.Resources.SelectMany(resource => resource.Entries).Any(other => other.Variants.SelectMany(item => item.Primitives).SelectMany(item => item.Polygons)
                .Any(face => (face.Flags & 3) is 1 or 3 && other.Vectors[face.NormalIndex].FileOffset == selected.FileOffset)))
                throw new ArgumentException("Selected vector also owns a face normal; edit is unsupported.");
            var document = new AssetDocument(path, Path.GetDirectoryName(path)!, bank.Decoded, true);
            document.Change(data => { data.Write16(selected.FileOffset, checked((short)Number("x"))); data.Write16(selected.FileOffset + 2, checked((short)Number("y"))); data.Write16(selected.FileOffset + 4, checked((short)Number("z"))); }, bytes => ModelBank.Parse(bytes));
            document.SaveCopy(Option("output"), bytes => ModelBank.Parse(bytes)); Console.WriteLine(document.DescribeChanges());
        }
        else throw new ArgumentException("Unknown models command.");
    }
    else if (args[0] == "maps")
    {
        if (verb == "edit-placement")
        {
            string world = Path.GetFullPath(Option("world")); byte[] original = BinaryData.ReadFile(world);
            var placement = TerrainTile.ParsePlacements(original)[Number("object")];
            var terrain = ModelBank.Load(Option("terrain-bank")); int id = Number("resource");
            if ((uint)id >= terrain.Resources.Length) throw new ArgumentException("Resource is outside the terrain bank.");
            int a = Number("a"), b = Number("b"), altitude = Number("altitude");
            var document = new AssetDocument(world, Path.GetDirectoryName(world)!, original, false);
            document.Change(data => { data.Write16(placement.FileOffset, checked((short)id)); data.Write32(placement.FileOffset + 2, a); data.Write32(placement.FileOffset + 6, b); data.Write32(placement.FileOffset + 10, altitude); }, bytes => TerrainTile.ParsePlacements(bytes));
            document.SaveCopy(Option("output"), bytes => TerrainTile.ParsePlacements(bytes)); Console.WriteLine(document.DescribeChanges());
            return 0;
        }
        string path = Path.GetFullPath(Option("grid")); byte[] bytes = BinaryData.ReadFile(path); var tile = TerrainTile.Parse(bytes);
        if (verb == "inspect")
        {
            string world = Option("world", Path.ChangeExtension(path, ".wld"));
            Json(new { schemaVersion = 1, columns = TerrainTile.Columns, rows = TerrainTile.Rows, minimum = bytes.Min(), maximum = bytes.Max(), placements = File.Exists(world) ? TerrainTile.ParsePlacements(BinaryData.ReadFile(world)) : [] });
        }
        else if (verb == "render")
        {
            string view = Option("view", "briefing");
            if (view is not ("briefing" or "3d")) throw new ArgumentException("--view must be briefing or 3d.");
            var palette = Palette(path);
            var game = new GameInstallation(Option("game-dir", Path.GetDirectoryName(path)!));
            var definitions = CombatDefinitions.Load(game.FilePath("BTECH.EXE"));
            var landscape = definitions.LandscapeForPalette(palette.Name);
            var scene = Scene.Terrain(tile.Heights, TerrainTile.Columns, TerrainTile.Rows, landscape);
            string world = Option("world", Path.ChangeExtension(path, ".wld"));
            if (File.Exists(world))
            {
                var terrain = ModelBank.Load(game.FilePath("terpck.tbl"));
                foreach (var placement in TerrainTile.ParsePlacements(BinaryData.ReadFile(world)))
                {
                    if ((uint)placement.ResourceId >= terrain.Resources.Length) throw new InvalidDataException("Placement resource is outside terpck.tbl.");
                    scene.AddResource(terrain.Resources[placement.ResourceId], new(placement.PositionA, placement.PositionB, placement.Altitude));
                }
            }
            bool softenWhite = args.Contains("--soften-white");
            Png(view == "briefing" ? SoftwareRenderer.Briefing(scene, 480, 640, palette, softenWhite) : SoftwareRenderer.Render(scene, 640, 480, tilt: -0.8, palette: palette, softenWhite: softenWhite));
        }
        else if (verb == "edit-height")
        {
            int column = Number("column"), row = Number("row");
            if ((uint)column >= 47 || (uint)row >= 87) throw new ArgumentException("Cell is outside the tile.");
            var document = new AssetDocument(path, Path.GetDirectoryName(path)!, bytes, false);
            document.Change(data => data.Bytes[row * 47 + column] = checked((byte)Number("height")), data => TerrainTile.Parse(data));
            document.SaveCopy(Option("output"), data => TerrainTile.Parse(data)); Console.WriteLine(document.DescribeChanges());
        }
        else throw new ArgumentException("Unknown maps command.");
    }
    else if (args[0] == "scenarios" && verb == "inspect") Json(new { schemaVersion = 1, scenarios = ScenarioData.Parse(BinaryData.ReadFile(Option("file"))) });
    else if (args[0] == "sounds")
    {
        var sounds = new SoundBank(BinaryData.ReadFile(Option("bank")));
        if (verb == "inspect") Json(new { schemaVersion = 1, tickRate = SoundBank.TickRate, sounds.Events });
        else if (verb == "render")
        {
            var preview = sounds.Render(Number("event"), Number("seconds", 10));
            using var output = new FileStream(Option("output"), FileMode.CreateNew, FileAccess.Write); output.Write(preview.Wave);
            string trace = Option("trace", "");
            if (trace.Length != 0) { using var file = new FileStream(trace, FileMode.CreateNew, FileAccess.Write); JsonSerializer.Serialize(file, preview.Trace); }
            Json(new { schemaVersion = 1, preview.Seconds, preview.AuthoredLoop, preview.Truncated, preview.MemoryProfile });
        }
        else throw new ArgumentException("Unknown sounds command.");
    }
    else throw new ArgumentException("Unknown command. Use help.");
    return 0;
}
catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException or InvalidOperationException or OverflowException or IndexOutOfRangeException)
{
    Console.Error.WriteLine(error.Message); return 1;
}
