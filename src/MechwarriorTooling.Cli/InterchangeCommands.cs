using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using MechwarriorTooling.Core;
using MechwarriorTooling.Rendering;

internal static class InterchangeCommands
{
    internal const string Help = """
        Asset interchange (all outputs must be new files outside the original installation):
          models export-obj --bank BANK.tbl --resource ID --output mesh.obj [--palette FILE.pal]
          models export-obj --bank BANK.tbl --all --output bank.obj [--game-dir PATH]
          models import-obj --bank BANK.tbl --resource ID --input mesh.obj --output COPY.tbl
          models import-obj --bank BANK.tbl --all --input bank.obj --output COPY.tbl
            ID == bank resource count appends a resource; does not add a ninth playable chassis.
          mechs export-obj --game-dir PATH --mech Jenner --sequence 1 --frame 0 --output pose.obj
          graphics inspect --file ASSET [--format bitmap|campaign|indexed]
          graphics export-png --file ASSET --image ID --output sprite.png [--format ...] [--palette FILE.pal]
          graphics export-png --file ASSET --all --output NEW_DIRECTORY [--format ...]
          graphics import-png --file ASSET --image ID --input edited.png --output COPY [--format ...] [--palette FILE.pal]
            Supports combat .bmp/.scr and all campaign MW_*PICS.BIN banks. Exported PNGs are indexed/Aseprite-ready.
            Campaign transparency is optional --transparent-zero; BMP zero is always native transparency.
          sounds export-wav --bank btechsnd.snd --event ID --output sound.wav [--seconds 10]
          sounds export-pcm --bank btechsnd.snd --event ID --output sound.mwpcm [--seconds 10]
          sounds import-wav --input sound.wav --output sound.mwpcm [--loop-start FRAME --loop-end FRAME]
          sounds export-wav --input sound.mwpcm --output sound.wav
          sounds inspect-pcm --input sound.mwpcm
          music export-midi --input song.mus --output song.mid
          music import-midi --input song.mid --output song.mus
            Songs already use MIDI format 0. MWPCM is a new sampled-audio format, NOT a DOS SND encoding.
        """;
    public static int? Run(string[] args)
    {
        if (args.Length < 2) return null;
        string command = args[0] + " " + args[1];
        if (command is not ("models export-obj" or "models import-obj" or "mechs export-obj" or "graphics inspect" or "graphics export-png" or "graphics import-png"
            or "sounds export-wav" or "sounds export-pcm" or "sounds import-wav" or "sounds inspect-pcm" or "music export-midi" or "music import-midi")) return null;
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
        bool Flag(string name) => args.Contains("--" + name);
        void Json(object value) => Console.WriteLine(JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
        string Source(string key) => Path.GetFullPath(Option(key));
        void Save(string path, byte[] bytes, string? source = null)
        {
            path = Path.GetFullPath(path);
            string installation = Option("game-dir", "");
            if (installation.Length == 0 && source != null)
            {
                string parent = Path.GetDirectoryName(Path.GetFullPath(source))!;
                if (Directory.EnumerateFiles(parent).Any(file => Path.GetFileName(file).Equals("BTECH.EXE", StringComparison.OrdinalIgnoreCase))) installation = parent;
            }
            if (source != null && path.Equals(Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Cannot overwrite the source.");
            if (installation.Length > 0 && path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(installation)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Export outside the source/original installation directory.");
            for (var folder = Directory.GetParent(path); folder != null; folder = folder.Parent)
                if (folder.LinkTarget != null) throw new InvalidOperationException("Output through directory links is unsupported.");
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write); file.Write(bytes);
        }
        EgaPalette Palette(string source)
        {
            string explicitPath = Option("palette", ""); if (explicitPath.Length > 0) return EgaPalette.Load(explicitPath);
            var game = new GameInstallation(Option("game-dir", Path.GetDirectoryName(source)!));
            return game.Files.Any(file => file.Name.Equals("tropic.pal", StringComparison.OrdinalIgnoreCase)) ? EgaPalette.Load(game.FilePath("tropic.pal")) : EgaPalette.Initial;
        }
        if (args[0] == "music")
        {
            string input = Source("input"); Save(Option("output"), MidiInterchange.Validate(BinaryData.ReadFile(input)), input);
            Json(new { format = "Standard MIDI format 0", byteExact = true }); return 0;
        }
        if (args[0] == "sounds")
        {
            PcmClip clip;
            if (args[1] == "import-wav")
            {
                if (Flag("bank")) throw new ArgumentException("Sampled WAV imports create .mwpcm assets; they cannot replace DOS speaker bytecode losslessly.");
                string input = Source("input"); clip = PcmClip.ReadWave(BinaryData.ReadFile(input));
                clip = clip with { LoopStart = Number("loop-start", clip.LoopStart), LoopEnd = Number("loop-end", clip.LoopEnd) };
                Save(Option("output"), clip.WriteNative(), input);
            }
            else if (args[1] == "inspect-pcm") clip = PcmClip.ReadNative(BinaryData.ReadFile(Source("input")));
            else if (Flag("bank"))
            {
                string input = Source("bank"); var sound = new SoundBank(BinaryData.ReadFile(input)).Render(Number("event"), Number("seconds", 10));
                clip = PcmClip.ReadWave(sound.Wave); Save(Option("output"), args[1] == "export-pcm" ? clip.WriteNative() : sound.Wave, input);
                Json(new { sound.Truncated, sound.AuthoredLoop, sound.MemoryProfile });
            }
            else
            {
                string input = Source("input"); clip = PcmClip.ReadNative(BinaryData.ReadFile(input)); Save(Option("output"), clip.WriteWave(), input);
            }
            Json(new { clip.SampleRate, clip.Channels, clip.Frames, clip.Seconds, clip.LoopStart, clip.LoopEnd }); return 0;
        }
        if (args[0] == "graphics")
        {
            string source = Source("file"); byte[] bytes = BinaryData.ReadFile(source);
            string kind = Option("format", bytes.AsSpan(0, Math.Min(4, bytes.Length)).SequenceEqual("BMP:"u8) || bytes.AsSpan(0, Math.Min(4, bytes.Length)).SequenceEqual("SCR:"u8)
                ? "bitmap" : Path.GetFileName(source).Equals("mw_pics.bin", StringComparison.OrdinalIgnoreCase) ? "indexed" : "campaign");
            if (kind is not ("bitmap" or "campaign" or "indexed")) throw new ArgumentException("Unknown graphics format.");
            var bitmap = kind == "bitmap" ? BitmapAsset.Parse(bytes) : null;
            var campaign = bitmap == null ? new CampaignPictures(bytes, kind == "indexed") : null;
            var pictures = bitmap?.Pictures ?? campaign!.Pictures;
            uint[] palette = campaign?.Palette ?? PngInterchange.Colours(Palette(source));
            bool transparent = bitmap?.TransparentZero == true || Flag("transparent-zero");
            if (args[1] == "inspect") Json(new { format = kind, transparentZero = transparent, pictures = pictures.Select(picture => new { picture.Id, picture.Width, picture.Height }) });
            else if (args[1] == "export-png")
            {
                if (Flag("all"))
                {
                    string directory = Path.GetFullPath(Option("output"));
                    if (Directory.Exists(directory) || File.Exists(directory)) throw new IOException("Choose a new export directory.");
                    // Validate location before creating an export directory.
                    string parent = Path.GetDirectoryName(source)!;
                    if (Directory.EnumerateFiles(parent).Any(file => Path.GetFileName(file).Equals("BTECH.EXE", StringComparison.OrdinalIgnoreCase))
                        && directory.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Export outside the original directory.");
                    Directory.CreateDirectory(directory);
                    foreach (var picture in pictures) Save(Path.Combine(directory, $"image-{picture.Id:D3}.png"), PngInterchange.Export(picture, palette, transparent), source);
                    Save(Path.Combine(directory, "images.json"), JsonSerializer.SerializeToUtf8Bytes(new { format = kind, source = Path.GetFileName(source), transparentZero = transparent,
                        pictures = pictures.Select(picture => new { picture.Id, picture.Width, picture.Height }) }), source);
                }
                else
                {
                    var picture = pictures.Single(picture => picture.Id == Number("image")); Save(Option("output"), PngInterchange.Export(picture, palette, transparent), source);
                }
            }
            else
            {
                int id = Number("image"); var picture = PngInterchange.Import(BinaryData.ReadFile(Source("input")), id, palette, transparent);
                byte[] output = bitmap != null ? bitmap.Replace(id, picture) : campaign!.Replace(id, picture);
                Save(Option("output"), output, source);
            }
            return 0;
        }
        string bankPath; ModelBank bank; IEnumerable<int> resourceIds;
        if (args[0] == "mechs")
        {
            var game = new GameInstallation(Option("game-dir")); var definitions = CombatDefinitions.Load(game.FilePath("BTECH.EXE"));
            var mech = definitions.Mechs.Single(mech => mech.Name.Equals(Option("mech"), StringComparison.OrdinalIgnoreCase));
            bankPath = game.FilePath(mech.BankName); bank = ModelBank.Load(bankPath);
            var sequence = definitions.Sequences(mech.Index).Single(sequence => sequence.Id == Number("sequence"));
            resourceIds = definitions.PartResources(mech.Index, sequence, Number("frame"));
        }
        else { bankPath = Source("bank"); bank = ModelBank.Load(bankPath); resourceIds = Flag("all") ? Enumerable.Range(0, bank.Resources.Length) : [Number("resource")]; }
        if (args[1] == "export-obj")
        {
            string output = Path.GetFullPath(Option("output")), material = Path.ChangeExtension(output, ".mtl");
            string metadata = output + ".mw.json";
            if (File.Exists(output) || File.Exists(material) || File.Exists(metadata)) throw new IOException("OBJ/MTL/metadata output already exists.");
            var exported = ObjInterchange.Export(bank, resourceIds, Path.GetFileName(material), Palette(bankPath));
            string directory = Option("game-dir", Path.GetDirectoryName(bankPath)!);
            var game = new GameInstallation(directory); CombatDefinitions? definitions = null;
            if (game.Files.Any(file => file.Name.Equals("BTECH.EXE", StringComparison.OrdinalIgnoreCase))) definitions = CombatDefinitions.Load(game.FilePath("BTECH.EXE"));
            byte[] manifest = JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1, format = "OBJ native ABC Z-up", bank = Path.GetFileName(bankPath),
                sha256 = Convert.ToHexString(SHA256.HashData(bank.Decoded)),
                resources = resourceIds.Select(id => new { id, usage = definitions?.FindModelResource(bankPath, id), header = bank.Resources[id] }) }, new JsonSerializerOptions { WriteIndented = true });
            Save(material, System.Text.Encoding.UTF8.GetBytes(exported.Mtl), bankPath);
            Save(output, System.Text.Encoding.UTF8.GetBytes(exported.Obj), bankPath); Save(metadata, manifest, bankPath);
        }
        else
        {
            string text = System.Text.Encoding.UTF8.GetString(BinaryData.ReadFile(Source("input"))).TrimStart('\uFEFF'); byte[] decoded;
            if (Flag("all"))
            {
                var resources = bank.Resources.Select(resource => resource.Entries.SelectMany(entry => entry.Variants).SelectMany(variant => variant.Primitives).All(primitive => primitive.Kind == 0)
                    ? ObjInterchange.Import(ObjInterchange.SelectResource(text, resource.Id), resource) : resource).ToArray();
                decoded = NativeModelWriter.Write(resources);
            }
            else
            {
                int id = Number("resource"); if (id < 0 || id > bank.Resources.Length) throw new ArgumentException("Resource is outside the bank or its append slot.");
                var original = id < bank.Resources.Length ? bank.Resources[id] : null;
                decoded = NativeModelWriter.Replace(bank, id, ObjInterchange.Import(ObjInterchange.SelectResource(text, id), original));
            }
            Save(Option("output"), NativeArchive.StoreRaw(decoded), bankPath);
            Json(new { resources = ModelBank.Parse(decoded).Resources.Length, note = "Geometry rebuilt with Q14 face normals. Playable chassis/animation tables are not expanded automatically." });
        }
        return 0;
    }
}
