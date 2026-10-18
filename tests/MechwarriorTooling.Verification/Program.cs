using System.Buffers.Binary;
using System.Text.Json;
using MechwarriorTooling.Core;
using MechwarriorTooling.Rendering;

int passed = 0, failed = 0;
void Check(string name, Action body)
{
    try { body(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + error.Message); }
}
void Require(bool condition, string message = "Assertion failed") { if (!condition) throw new InvalidOperationException(message); }
void Reject(Action body)
{
    try { body(); } catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException or IOException) { return; }
    throw new Exception("Malformed/unsafe input was accepted.");
}
byte[] SampleModel()
{
    // Paragraph base 16, header 24: near references must NOT add header address.
    byte[] bytes = new byte[112];
    void Word(int at, int value) => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), (ushort)value);
    Word(0, 8); Word(2, 1); Word(34, 1); Word(36, 24);
    bytes[40] = 0; bytes[41] = 4; Word(42, 32); Word(44, 1); Word(46, 56);
    Word(48, -10); Word(50, 0); Word(52, 0);
    Word(54, 10); Word(56, 0); Word(58, 0);
    Word(60, 0); Word(62, 0); Word(64, 20);
    Word(66, 0); Word(68, -16384); Word(70, 0); // normal, not a vertex
    Word(72, 1); Word(74, 60); // variant -> primitive at 76
    Word(78, 1); Word(80, 68); // one polygon at 84
    bytes[84] = 0x81; bytes[85] = 7; bytes[89] = 3; Word(90, 76);
    bytes[92] = 0; bytes[93] = 1; bytes[94] = 2; bytes[95] = 255;
    return bytes;
}
byte[] SamplePalette()
{
    byte[] bytes = new byte[128];
    for (int colour = 0; colour < 32; colour++)
        bytes[colour * 2] = bytes[colour * 2 + 1] = bytes[64 + colour * 2] = bytes[65 + colour * 2] = (byte)((colour & 15) * 0x11);
    return bytes;
}
Check("EGA alternate fields and native interior/outline roles", () =>
{
    byte[] bytes = SampleModel(); bytes[87] = 4; bytes[88] = 14;
    var face = Scene.FromBank(ModelBank.Parse(bytes), [0]).Faces.Single();
    Require(face.Colour == 14 && face.LineColour == 4);
});
Check("EGA scanline and column pattern parity", () =>
{
    byte[] bytes = SamplePalette(); bytes[32] = 0x22; bytes[33] = 0x33; bytes[96] = 0x33; bytes[97] = 0x22;
    var palette = EgaPalette.FromEgaChunk(bytes);
    Require(palette.PolygonPixel(16, 0, 0) == 3 && palette.PolygonPixel(16, 1, 0) == 2);
    Require(palette.PolygonPixel(16, 0, 1) == 2 && palette.PolygonPixel(16, 1, 1) == 3);
    Require(palette.LinePixel(16) == 2);
});
Check("EGA equal patterns use native solid low byte", () =>
{
    byte[] bytes = SamplePalette(); bytes[32] = bytes[96] = 0x22; bytes[33] = bytes[97] = 0x33;
    var palette = EgaPalette.FromEgaChunk(bytes); Require(palette.PolygonPixel(16, 0, 0) == 2 && palette.PolygonPixel(16, 0, 1) == 2);
});
Check("EGA hardware palette translation", () =>
{
    byte[] bytes = SamplePalette(); bytes[6] = bytes[7] = 0;
    var palette = EgaPalette.FromEgaChunk(bytes); Require(palette.PolygonPixel(3, 0, 0) == 0);
});
Check("EGA invalid chunk bounded", () => Reject(() => EgaPalette.FromEgaChunk(new byte[127])));
Check("EGA unloaded bank must not truncate", () =>
{
    try { EgaPalette.Initial.PolygonPixel(32, 0, 0); throw new Exception("Palette index truncated."); } catch (NotSupportedException) { }
});
Check("captured tone descriptors", () =>
{
    Require(DosToneMemoryProfile.Combat.Descriptor(0) == (0x1F4A, 0x1D3B));
    Require(DosToneMemoryProfile.Combat.Descriptor(1) == (0x000E, 0x0070));
});
Check("captured footstep modulation bytes", () => Require(Enumerable.Range(0, 7).Select(index => DosToneMemoryProfile.Combat.ReadByte((ushort)(0xE + index)))
    .SequenceEqual(new byte[] { 0x70, 0, 0x0E, 0, 0x70, 0, 0x54 })));
Check("tone memory boundary and restored vector", () =>
{
    Require(DosToneMemoryProfile.Combat.ReadByte(0x211D) == 0);
    Reject(() => DosToneMemoryProfile.Combat.ReadByte(0x211E)); Reject(() => DosToneMemoryProfile.Combat.ReadByte(0x18));
    Require(new DosToneMemoryProfile(false).ReadWord(0) == 0xCA60);
});
Check("raw archive exact replay", () => { byte[] source = [1, 2, 3, 4]; Require(NativeArchive.Decode(NativeArchive.StoreRaw(source)).SequenceEqual(source)); });
Check("RLE exact counts", () => Require(NativeArchive.Decode([1, 5, 0, 0, 0, 0x83, 9, 2, 4, 5]).SequenceEqual(new byte[] { 9, 9, 9, 4, 5 })));
Check("RLE truncated literal", () => Reject(() => NativeArchive.Decode([1, 2, 0, 0, 0, 2, 7])));
Check("RLE overrun", () => Reject(() => NativeArchive.Decode([1, 1, 0, 0, 0, 0x82, 7])));
Check("raw length mismatch", () => Reject(() => NativeArchive.Decode([0, 2, 0, 0, 0, 1])));
Check("bounded decoded allocation", () => Reject(() => NativeArchive.Decode([2, 255, 255, 255, 127])));
Check("unknown compression", () => Reject(() => NativeArchive.Decode([3, 0, 0, 0, 0])));
Check("LZW one literal", () => Require(NativeArchive.Decode([2, 1, 0, 0, 0, 65, 0]).SequenceEqual(new byte[] { 65 })));
Check("LZW truncation", () => Reject(() => NativeArchive.Decode([2, 2, 0, 0, 0, 65, 0])));
Check("LZW next-free phrase", () => Require(NativeArchive.Decode([2, 3, 0, 0, 0, 65, 2, 2]).SequenceEqual("AAA"u8.ToArray())));
Check("LZW clear discards packet padding", () => Require(NativeArchive.Decode([2, 2, 0, 0, 0, 65, 0, 2, 255, 255, 255, 255, 255, 255, 66, 0]).SequenceEqual("AB"u8.ToArray())));
Check("paragraph-relative model references", () => { var bank = ModelBank.Parse(SampleModel()); Require(bank.Resources[0].Entries[0].Vectors[2].Z == 20); });
Check("normals excluded from render vertices", () => { var scene = Scene.FromBank(ModelBank.Parse(SampleModel()), [0]); Require(scene.Faces.SelectMany(face => face.Vertices).All(vertex => vertex.Y == 0)); });
Check("invalid vertex index", () => { byte[] bytes = SampleModel(); bytes[93] = 4; Reject(() => ModelBank.Parse(bytes)); });
Check("truncated model tables", () => Reject(() => ModelBank.Parse(SampleModel()[..87])));
Check("unterminated polygon", () => { byte[] bytes = SampleModel(); Array.Fill(bytes, (byte)0, 95, bytes.Length - 95); Reject(() => ModelBank.Parse(bytes)); });
Check("GRD dimensions", () => Require(TerrainTile.Parse(new byte[4089]).Heights.Length == 4089));
Check("GRD wrong size", () => Reject(() => TerrainTile.Parse(new byte[4090])));
Check("WLD partial record", () => Reject(() => TerrainTile.ParsePlacements(new byte[15])));
Check("WLD signed positions", () => { byte[] bytes = new byte[14]; BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(2), -50000); Require(TerrainTile.ParsePlacements(bytes)[0].PositionA == -50000); });
Check("scenario bounds", () => { byte[] bytes = new byte[0xE40]; bytes[0] = 16; Reject(() => ScenarioData.Parse(bytes)); });
Check("turntable quarter / half / full", () => { Require(Math.Abs(PreviewClock.Angle(2.5) - Math.PI / 2) < 1e-12); Require(Math.Abs(PreviewClock.Angle(5) - Math.PI) < 1e-12); Require(PreviewClock.Angle(10) == 0); Require(PreviewClock.Cycle(10, 3) == 1); });
Check("deterministic render", () => { var scene = Scene.FromBank(ModelBank.Parse(SampleModel()), [0]); Require(SoftwareRenderer.Render(scene, 160, 120).Pixels.SequenceEqual(SoftwareRenderer.Render(scene, 160, 120).Pixels)); });
Check("PNG signature", () => { using var file = new MemoryStream(); new RasterImage(16, 16).WritePng(file); Require(file.ToArray().AsSpan(1, 3).SequenceEqual("PNG"u8)); });
Check("radar row/column orientation", () =>
{
    var scene = new Scene();
    scene.Points.Add(new(new(100, 0, 0), 10, 4));
    scene.Points.Add(new(new(0, 100, 0), 10, 1));
    var image = SoftwareRenderer.Briefing(scene, 100, 100, EgaPalette.Initial);
    double Centre(byte colour, bool horizontal) => image.Pixels.Select((pixel, index) => (pixel, index))
        .Where(item => item.pixel == SoftwareRenderer.Ega[colour]).Average(item => horizontal ? item.index % 100 : item.index / 100);
    Require(Centre(4, true) > Centre(1, true) && Centre(1, false) < Centre(4, false));
});
Check("terrain has explicit native ground, no invented height-band greens", () =>
{
    var landscape = new LandscapeColours(2, "Synthetic Arctic", "arctic.pal", 0, 15);
    var scene = Scene.Terrain([0, 10, 100, 255], 2, 2, landscape);
    Require(scene.BackgroundColour == 15 && scene.Faces.All(face => face.Colour == 15 && face.LineColour == 15));
    Require(scene.Faces.Count == 1 && scene.Faces[0].BasePlane && scene.Faces[0].Vertices.All(vertex => vertex.Z == 0));
    var image = SoftwareRenderer.Briefing(scene, 100, 100, EgaPalette.Initial);
    Require(image.Pixels.All(pixel => pixel == SoftwareRenderer.Ega[15]));
    Reject(() => Scene.Terrain([0], 2, 2, landscape));
});
Check("map base is a finite flat plane that orbits and zooms with the scene", () =>
{
    var scene = Scene.Terrain(new byte[47 * 87], 47, 87, new(2, "Synthetic Arctic", "arctic.pal", 11, 15));
    var flat = scene.Faces.Single();
    Require(flat.Vertices.Min(vertex => vertex.X) == -87 * 512 && flat.Vertices.Max(vertex => vertex.X) == 0);
    Require(flat.Vertices.Min(vertex => vertex.Y) == 0 && flat.Vertices.Max(vertex => vertex.Y) == 47 * 512);
    var initial = SoftwareRenderer.Render(scene, 320, 240, yaw: 0.4, tilt: -0.8);
    var rotated = SoftwareRenderer.Render(scene, 320, 240, yaw: 1.2, tilt: -0.4);
    var zoomed = SoftwareRenderer.Render(scene, 320, 240, yaw: 0.4, tilt: -0.8, zoom: 1.5);
    Require(initial.Pixels[0] != SoftwareRenderer.Ega[15]);
    Require(!initial.Pixels.SequenceEqual(rotated.Pixels));
    Require(zoomed.Pixels.Count(pixel => pixel == SoftwareRenderer.Ega[15]) > initial.Pixels.Count(pixel => pixel == SoftwareRenderer.Ega[15]));
    Require(initial.Pixels.Count(pixel => pixel == SoftwareRenderer.Ega[15]) > 1000);
});
Check("top-down world projection retains GRD cell picking coordinates", () =>
{
    var scene = Scene.Terrain(new byte[4], 2, 2, new(1, "Synthetic", "tropic.pal", 9, 2));
    scene.Points.Add(new(new((0.5f - 87) * 512, (47 - 0.5f) * 512, 0), 1, 4));
    scene.Points.Add(new(new((1.5f - 87) * 512, (47 - 1.5f) * 512, 0), 1, 1));
    var image = SoftwareRenderer.Briefing(scene, 100, 100, EgaPalette.Initial);
    Require(image.Pixels[25 * 100 + 25] == SoftwareRenderer.Ega[4]);
    Require(image.Pixels[75 * 100 + 75] == SoftwareRenderer.Ega[1]);
});
Check("soft-white display is optional and changes only final EGA white pixels", () =>
{
    var scene = Scene.Terrain(new byte[4], 2, 2, new(2, "Synthetic Arctic", "arctic.pal", 11, 15));
    scene.Points.Add(new(new((0.5f - 87) * 512, (47 - 0.5f) * 512, 0), 1, 4));
    var native = SoftwareRenderer.Briefing(scene, 100, 100, EgaPalette.Initial);
    var soft = SoftwareRenderer.Briefing(scene, 100, 100, EgaPalette.Initial, softenWhite: true);
    Require(native.Pixels.Contains(SoftwareRenderer.Ega[15]) && native.Pixels.Contains(SoftwareRenderer.Ega[4]));
    Require(soft.Pixels.SequenceEqual(native.Pixels.Select(pixel => pixel == SoftwareRenderer.Ega[15] ? SoftwareRenderer.SoftWhite : pixel)));
    Require(scene.BackgroundColour == 15 && EgaPalette.Initial.PolygonPixel(15, 0, 0) == 15);
});
Check("point primitive has a visible preview", () =>
{
    var scene = new Scene(); scene.Points.Add(new(System.Numerics.Vector3.Zero, 10, 14));
    Require(SoftwareRenderer.Render(scene, 160, 120).Pixels.Any(pixel => pixel == SoftwareRenderer.Ega[14]));
});
Check("resource reference index bounded", () => { byte[] bytes = SampleModel(); bytes[40] = 255; Reject(() => ModelBank.Parse(bytes)); });
Check("unknown primitive is rejected", () => { byte[] bytes = SampleModel(); bytes[77] = 9; Reject(() => ModelBank.Parse(bytes)); });
Check("undo redo and failed-change isolation", () =>
{
    var document = new AssetDocument(Path.GetFullPath("synthetic.grd"), Path.GetFullPath("original-install"), new byte[4089], false);
    document.Change(data => data.Bytes[0] = 7, data => TerrainTile.Parse(data)); Require(document.Modified);
    document.Undo(); Require(!document.Modified); document.Redo(); Require(document.Bytes[0] == 7);
    Reject(() => document.Change(data => data.Bytes[0] = 9, _ => throw new InvalidDataException("reject")));
    Require(document.Bytes[0] == 7);
});
Check("protect source and original installation", () =>
{
    string root = Path.GetFullPath("original-install");
    var document = new AssetDocument(Path.Combine(root, "tile0.grd"), root, new byte[4089], false);
    Reject(() => document.SaveCopy(Path.Combine(root, "tile0.grd"), data => TerrainTile.Parse(data)));
    Reject(() => document.SaveCopy(Path.Combine(root, "different.grd"), data => TerrainTile.Parse(data)));
});
Check("safe copy reopens and cannot overwrite", () =>
{
    string folder = Path.Combine(Path.GetTempPath(), "mw-tooling-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
    string output = Path.Combine(folder, "copy.grd");
    try
    {
        var document = new AssetDocument(Path.GetFullPath("original-install/tile0.grd"), Path.GetFullPath("original-install"), new byte[4089], false);
        document.SaveCopy(output, data => TerrainTile.Parse(data)); Require(File.ReadAllBytes(output).Length == 4089);
        Reject(() => document.SaveCopy(output, data => TerrainTile.Parse(data)));
    }
    finally { if (File.Exists(output)) File.Delete(output); Directory.Delete(folder); }
});

int argument = Array.IndexOf(args, "--game-dir");
Check("indexed PNG and RGBA transparency roundtrip", () =>
{
    uint[] palette = PngInterchange.Colours(EgaPalette.Initial);
    var picture = new IndexedPicture(3, 8, 2, Enumerable.Range(0, 16).Select(index => (byte)index).ToArray());
    byte[] png = PngInterchange.Export(picture, palette, true);
    Require(PngInterchange.Import(png, 3, palette, true).Pixels.SequenceEqual(picture.Pixels));
    var decoded = PngInterchange.Decode(png); Require(decoded.Image.Pixels[0] >> 24 == 0);
    using var rgba = new MemoryStream(); decoded.Image.WritePng(rgba);
    Require(PngInterchange.Import(rgba.ToArray(), 3, palette, true).Pixels.SequenceEqual(picture.Pixels));
    png[^1] ^= 1; Reject(() => PngInterchange.Decode(png));
});
Check("PNG import rejects out-of-palette pixels and partial alpha", () =>
{
    var image = new RasterImage(1, 1); image.Pixels[0] = 0xFF123456;
    using var stream = new MemoryStream(); image.WritePng(stream);
    Reject(() => PngInterchange.Import(stream.ToArray(), 0, PngInterchange.Colours(EgaPalette.Initial), false));
    image.Pixels[0] = 0x80000000; using var alpha = new MemoryStream(); image.WritePng(alpha);
    Reject(() => PngInterchange.Import(alpha.ToArray(), 0, PngInterchange.Colours(EgaPalette.Initial), true));
});
Check("OBJ topology import, native writer and resource append", () =>
{
    const string mesh = "o custom\nv 0 0 0\nv 10 0 0\nv 0 10 0\nusemtl mw_p07_l00_e07_o00_f81\nf 1 2 3\n";
    var imported = ObjInterchange.Import(mesh); byte[] bytes = NativeModelWriter.Write([imported]); var bank = ModelBank.Parse(bytes);
    var entry = bank.Resources[0].Entries[0]; Require(entry.Vectors[entry.Variants[0].Primitives[0].Polygons[0].NormalIndex].Z == 16384);
    var exported = ObjInterchange.Export(bank, [0], "mesh.mtl", EgaPalette.Initial);
    Require(exported.Obj.Contains("o mw_r0000_", StringComparison.Ordinal) && exported.Mtl.Contains("newmtl mw_", StringComparison.Ordinal));
    var appended = ModelBank.Parse(NativeModelWriter.Replace(bank, 1, imported)); Require(appended.Resources.Length == 2);
    Require(ObjInterchange.Import(ObjInterchange.SelectResource(exported.Obj, 0)).Entries.Length == 1);
    Reject(() => ObjInterchange.Import(mesh.Replace("10 0 0", "0 0 0", StringComparison.Ordinal)));
    Reject(() => ObjInterchange.Import(mesh.Replace("mw_p07_l00_e07_o00_f81", "Unknown", StringComparison.Ordinal)));
});
Check("MWPCM and WAV sample/loop roundtrip", () =>
{
    var clip = new PcmClip(22050, 2, [-32768, 32767, 0, 100, -100, 200, 400, -400], 1, 4);
    var native = PcmClip.ReadNative(clip.WriteNative()); var wave = PcmClip.ReadWave(native.WriteWave());
    Require(wave.Samples.SequenceEqual(clip.Samples) && wave.SampleRate == clip.SampleRate && wave.Channels == 2 && wave.LoopStart == 1 && wave.LoopEnd == 4);
    byte[] invalid = clip.WriteNative(); invalid[4] = 2; Reject(() => PcmClip.ReadNative(invalid));
    Reject(() => (clip with { LoopStart = 4, LoopEnd = 1 }).WriteNative());
    Reject(() => PcmClip.ReadWave(new byte[12]));
});
Check("campaign nibble RLE retains meaningful bytes", () =>
{
    byte[] source = Enumerable.Range(0, 256).Select(index => (byte)index).Concat(Enumerable.Repeat((byte)255, 32)).ToArray();
    byte[] decoded = CampaignPictures.Expand(CampaignPictures.Compress(source)); Require(decoded.AsSpan(0, source.Length).SequenceEqual(source));
});
Check("float WAV conversion clamps and rejects non-finite samples", () =>
{
    byte[] wave = new PcmClip(44100, 1, new short[4]).WriteWave();
    BinaryPrimitives.WriteUInt16LittleEndian(wave.AsSpan(20), 3);
    BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(28), 44100 * 4);
    BinaryPrimitives.WriteUInt16LittleEndian(wave.AsSpan(32), 4); BinaryPrimitives.WriteUInt16LittleEndian(wave.AsSpan(34), 32);
    BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(44), BitConverter.SingleToInt32Bits(0.5f));
    BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(48), BitConverter.SingleToInt32Bits(-2f));
    Require(PcmClip.ReadWave(wave).Samples.SequenceEqual(new short[] { 16384, -32768 }));
    BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(48), BitConverter.SingleToInt32Bits(float.NaN)); Reject(() => PcmClip.ReadWave(wave));
});
Check("MIDI input validates events rather than just extension/header", () =>
{
    byte[] midi = [.. "MThd"u8, 0, 0, 0, 6, 0, 0, 0, 1, 0, 96, .. "MTrk"u8, 0, 0, 0, 4, 0, 255, 47, 0];
    Require(MidiInterchange.Validate(midi).SequenceEqual(midi));
    byte[] wrongFormat = (byte[])midi.Clone(); wrongFormat[9] = 1; Reject(() => MidiInterchange.Validate(wrongFormat));
    midi[^2] = 48; Reject(() => MidiInterchange.Validate(midi));
});
Check("user visual OTH and TER catalogue with unknowns preserved", () =>
{
    string[] effects = ["AC shell", "Missile", "Explosion white", "Explosion yellow", "Explosion red", "Explosion smoke 1", "Explosion smoke 2", "Explosion smoke 3", "Sparks small", "Sparks large"];
    for (int resource = 0; resource < effects.Length; resource++)
    {
        Require(ModelCatalog.Name("OTHPCK.TBL", resource) == effects[resource]);
        Require(ModelCatalog.Evidence("othpck.tbl", resource).StartsWith("User visual", StringComparison.Ordinal));
    }
    for (int resource = 0; resource < 30; resource++)
        Require(ModelCatalog.Name("terpck.tbl", resource) == (resource == 1 ? "Red selection" : new[] { 0, 12, 13, 14, 15, 16, 17, 24, 25 }.Contains(resource) ? "River" : "Hill"));
    Require(ModelCatalog.Name("othpck.tbl", 10).StartsWith("Resource", StringComparison.Ordinal));
    Require(ModelCatalog.Name("terpck.tbl", 30).StartsWith("Resource", StringComparison.Ordinal));
    Require(ModelCatalog.Name("unknown.tbl", 0).StartsWith("Resource", StringComparison.Ordinal));
});
if (argument >= 0)
{
    var game = new GameInstallation(args[argument + 1]); CombatDefinitions? definitions = null;
    Check("private reviewed EXEPACK/chassis tables", () => { definitions = CombatDefinitions.Load(game.FilePath("BTECH.EXE")); Require(definitions.Mechs.Select(mech => mech.Tonnage).SequenceEqual(new[] { 20, 35, 45, 55, 60, 70, 75, 85 })); });
    Check("private native model writer replays every installed bank", () =>
    {
        foreach (var asset in game.Files.Where(file => file.Name.EndsWith("pck.tbl", StringComparison.OrdinalIgnoreCase)))
        {
            var original = ModelBank.Load(game.FilePath(asset.Name)); var rewritten = ModelBank.Parse(NativeModelWriter.Write(original.Resources));
            Require(rewritten.Resources.Length == original.Resources.Length);
            for (int resource = 0; resource < original.Resources.Length; resource++)
            {
                var first = original.Resources[resource]; var next = rewritten.Resources[resource];
                Require(next.Flags == first.Flags && next.ScaleShift == first.ScaleShift && next.Reference.SequenceEqual(first.Reference));
                var originalFaces = first.Entries.SelectMany(entry => entry.Variants).SelectMany(variant => variant.Primitives).SelectMany(record => record.Polygons).ToArray();
                var faces = next.Entries.SelectMany(entry => entry.Variants).SelectMany(variant => variant.Primitives).SelectMany(record => record.Polygons).ToArray();
                Require(faces.Length == originalFaces.Length);
                for (int face = 0; face < faces.Length; face++) Require(faces[face].Indices.SequenceEqual(originalFaces[face].Indices) && faces[face].Flags == originalFaces[face].Flags && faces[face].AlternateLine == originalFaces[face].AlternateLine);
                Require(Scene.FromBank(original, [resource]).Faces.Count == Scene.FromBank(rewritten, [resource]).Faces.Count);
            }
        }
    });
    Check("private all 780 Mech shapes pass OBJ topology import", () =>
    {
        foreach (var mech in definitions!.Mechs)
        {
            var bank = ModelBank.Load(game.FilePath(mech.BankName));
            foreach (var resource in bank.Resources)
            {
                var export = ObjInterchange.Export(bank, [resource.Id], "mesh.mtl", EgaPalette.Initial);
                var imported = ObjInterchange.Import(export.Obj, resource);
                var replay = ModelBank.Parse(NativeModelWriter.Write([imported]));
                Require(replay.Resources[0].Entries.Sum(entry => entry.Variants.Sum(variant => variant.Primitives.Sum(primitive => primitive.Polygons.Length)))
                    == resource.Entries.Sum(entry => entry.Variants.Sum(variant => variant.Primitives.Sum(primitive => primitive.Polygons.Length))));
            }
        }
    });
    Check("private all BMP/SCR indexed PNG roundtrips", () =>
    {
        uint[] palette = PngInterchange.Colours(EgaPalette.Initial);
        foreach (var asset in game.Files.Where(file => Path.GetExtension(file.Name).Equals(".bmp", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(file.Name).Equals(".scr", StringComparison.OrdinalIgnoreCase)))
        {
            var bank = BitmapAsset.Parse(BinaryData.ReadFile(game.FilePath(asset.Name)));
            foreach (var picture in bank.Pictures)
            {
                var imported = PngInterchange.Import(PngInterchange.Export(picture, palette, bank.TransparentZero), picture.Id, palette, bank.TransparentZero);
                var replay = BitmapAsset.Parse(bank.Replace(picture.Id, imported));
                Require(replay.Pictures[picture.Id].Pixels.SequenceEqual(picture.Pixels));
            }
        }
    });
    Check("private every campaign picture PNG roundtrips", () =>
    {
        foreach (var asset in game.Files.Where(file => file.Name.EndsWith("pics.bin", StringComparison.OrdinalIgnoreCase)))
        {
            bool indexed = asset.Name.Equals("mw_pics.bin", StringComparison.OrdinalIgnoreCase);
            var bank = new CampaignPictures(BinaryData.ReadFile(game.FilePath(asset.Name)), indexed);
            uint[] palette = bank.Palette ?? PngInterchange.Colours(EgaPalette.Initial);
            foreach (var picture in bank.Pictures)
                Require(PngInterchange.Import(PngInterchange.Export(picture, palette, false), picture.Id, palette, false).Pixels.SequenceEqual(picture.Pixels));
            var first = bank.Pictures[0]; var replay = new CampaignPictures(bank.Replace(first.Id, first), indexed);
            Require(replay.Pictures.Length == bank.Pictures.Length);
            for (int image = 0; image < bank.Pictures.Length; image++) Require(replay.Pictures[image].Pixels.SequenceEqual(bank.Pictures[image].Pixels));
        }
    });
    Check("private every MUS already is byte-exact MIDI format 0", () =>
    {
        foreach (var asset in game.Files.Where(file => Path.GetExtension(file.Name).Equals(".mus", StringComparison.OrdinalIgnoreCase)))
        { byte[] bytes = BinaryData.ReadFile(game.FilePath(asset.Name)); Require(MidiInterchange.Validate(bytes).SequenceEqual(bytes)); }
    });
    foreach (var asset in game.Files.Where(file => file.Name.EndsWith("pck.tbl", StringComparison.OrdinalIgnoreCase)))
        Check("private parse/replay " + asset.Name, () =>
        {
            var bank = ModelBank.Load(game.FilePath(asset.Name));
            var replay = ModelBank.Parse(NativeArchive.Decode(NativeArchive.StoreRaw(bank.Decoded)));
            Require(replay.Decoded.SequenceEqual(bank.Decoded));
            int chassis = Array.FindIndex(CombatDefinitions.BankPrefixes, prefix => asset.Name.Equals(prefix + "pck.tbl", StringComparison.OrdinalIgnoreCase));
            if (chassis >= 0 && definitions != null)
            {
                Require(bank.Resources.Length == (chassis is 1 or 2 or 3 ? 120 : 84));
                foreach (var sequence in definitions.Sequences(chassis))
                    for (int frame = 0; frame < sequence.ResourceFrames.Length; frame++)
                    {
                        int[] parts = definitions.PartResources(chassis, sequence, frame);
                        Require(parts.Length == 6 && parts.All(part => part >= 0 && part < bank.Resources.Length));
                        Require(Scene.FromBank(bank, parts).Faces.Count > 0);
                    }
            }
        });
    Check("private all 780 Mech shapes have native body-part and animation references", () =>
    {
        int total = 0;
        foreach (var mech in definitions!.Mechs)
        {
            var catalogue = definitions.ModelResources(mech.Index);
            var bank = ModelBank.Load(game.FilePath(mech.BankName));
            Require(catalogue.Select(item => item.ResourceId).SequenceEqual(Enumerable.Range(0, bank.Resources.Length)));
            Require(catalogue.All(item => item.Uses.Length > 0));
            Require(catalogue.Any(item => item.Uses.Select(use => use.SequenceId).Distinct().Count() > 1));
            foreach (var sequence in definitions.Sequences(mech.Index))
                for (int frame = 0; frame < sequence.ResourceFrames.Length; frame++)
                {
                    int[] resources = definitions.PartResources(mech.Index, sequence, frame);
                    for (int part = 0; part < 6; part++)
                    {
                        var entry = catalogue.Single(item => item.ResourceId == resources[part]);
                        Require(entry.BodyPart == CombatDefinitions.BodyParts[part]);
                        Require(entry.Uses.Any(use => use.SequenceId == sequence.Id && use.LogicalFrame == frame && use.PhysicalFrame == sequence.ResourceFrames[frame]));
                    }
                }
            Require(catalogue[0].BodyPart == "Left arm" && catalogue[5].BodyPart == "Head");
            Require(catalogue[6].BodyPart == "Left arm" && catalogue[14].BodyPart == "Right arm");
            Require(ModelCatalog.Name(mech.BankName, 14, definitions).Contains("Right arm", StringComparison.Ordinal));
            Require(definitions.FindModelResource(mech.BankName, bank.Resources.Length) == null);
            total += catalogue.Length;
        }
        Require(total == 780);
    });
    Check("private GI collision graph", () => ModelBank.ValidateSurfaceBank(NativeArchive.Decode(BinaryData.ReadFile(game.FilePath("terpck.gi")))));
    Check("private effect end marker and every sequence frame", () =>
    {
        var effects = definitions!.EffectSequences; var bank = ModelBank.Load(game.FilePath("othpck.tbl"));
        Require(effects.Select(effect => effect.Resources.Length).SequenceEqual(new[] { 6, 2 }));
        foreach (var effect in effects)
            foreach (int resource in effect.Resources)
            {
                Require(resource < bank.Resources.Length);
                var scene = Scene.FromBank(bank, [resource]); Require(scene.Faces.Count + scene.Points.Count > 0);
                Require(SoftwareRenderer.Render(scene, 160, 120).Pixels.Any(pixel => pixel != 0xFF101923));
            }
    });
    Check("private all GRD / WLD files", () =>
    {
        int total = 0;
        foreach (var file in game.Files.Where(file => file.Category == "Maps"))
            if (file.Name.EndsWith(".grd", StringComparison.OrdinalIgnoreCase)) TerrainTile.Parse(BinaryData.ReadFile(game.FilePath(file.Name)));
            else total += TerrainTile.ParsePlacements(BinaryData.ReadFile(game.FilePath(file.Name))).Length;
        Require(total == 86);
    });
    Check("private scenario selectors", () => Require(ScenarioData.Parse(BinaryData.ReadFile(game.FilePath("snario.dat"))).Length == 60));
    Check("private all fifteen speaker events with captured modulation", () =>
    {
        var bank = new SoundBank(BinaryData.ReadFile(game.FilePath("btechsnd.snd")));
        Require(bank.Events.Length == 15 && bank.Events.Count(sound => sound.Flow == "loop") == 3);
        for (int id = 0; id < bank.Events.Length; id++)
        {
            var preview = bank.Render(id, 2); Require(preview.Wave.Length > 44 && preview.Seconds <= 2);
            Require(preview.Truncated == (bank.Events[id].Flow == "loop"));
            Require(preview.Trace.Length > 0 && preview.Trace.Any(tick => tick.Level != 0));
            if (id is 0 or 2 or 3 or 5 or 6 or 7 or 12 or 13 or 14) Require(preview.Trace.Any(tick => tick.ModulationOffset == 0x1F4A && tick.PhaseLimit == 0x1D3B));
            if (id == 11) Require(preview.Trace.Any(tick => tick.ModulationOffset == 0xE && tick.PhaseLimit == 0x70));
        }
    });
    Check("private all PAL EGA chunks", () =>
    {
        foreach (var file in game.Files.Where(file => file.Category == "Palette"))
        {
            var palette = EgaPalette.Load(game.FilePath(file.Name));
            for (byte colour = 0; colour < 32; colour++)
                for (int parity = 0; parity < 4; parity++) Require(palette.PolygonPixel(colour, parity & 1, parity >> 1) < 16);
        }
    });
    Check("private native landscape rows and both map views follow matching palettes", () =>
    {
        Require(definitions!.Landscapes.Select(item => item.PaletteFile).SequenceEqual(new[] { "desert.pal", "tropic.pal", "arctic.pal" }));
        foreach (var landscape in definitions.Landscapes)
        {
            var palette = EgaPalette.Load(game.FilePath(landscape.PaletteFile));
            Require(definitions.LandscapeForPalette(landscape.PaletteFile.ToUpperInvariant()) == landscape);
            var scene = Scene.Terrain([0, 30, 90, 255], 2, 2, landscape);
            var colours = Enumerable.Range(0, 4).Select(parity => SoftwareRenderer.Ega[palette.PolygonPixel(landscape.Ground, parity & 1, parity >> 1)]).ToHashSet();
            foreach (var image in new[] { SoftwareRenderer.Briefing(scene, 100, 100, palette), SoftwareRenderer.Render(scene, 100, 100, tilt: -0.8, palette: palette) })
            {
                Require(image.Pixels.All(pixel => colours.Contains(pixel) || pixel == 0xFF101923));
                Require(image.Pixels.Any(colours.Contains));
            }
            if (landscape.Index == 2) Require(!colours.Contains(SoftwareRenderer.Ega[2]) && !colours.Contains(SoftwareRenderer.Ega[10]));
            Console.WriteLine($"  {landscape.Name}: native sky {landscape.Sky}, ground {landscape.Ground}; EGA ground {string.Join(", ", colours.Select(colour => $"{colour:X8}"))}");
        }
        Reject(() => definitions.LandscapeForPalette("dmgpal.pal"));
    });
    Check("private Phoenix Hawk idle/jump body colours match after EGA mapping", () =>
    {
        var palette = EgaPalette.Load(game.FilePath("tropic.pal")); var bank = ModelBank.Load(game.FilePath("phapck.tbl"));
        var sequences = definitions!.Sequences(2);
        var idle = Scene.FromBank(bank, definitions.PartResources(2, sequences.Single(sequence => sequence.Id == 0), 0));
        var jump = Scene.FromBank(bank, definitions.PartResources(2, sequences.Single(sequence => sequence.Id == 2), 0));
        string[] Colours(Scene scene) => scene.Faces.Select(face => $"{palette.PolygonPixel(face.Colour, 0, 0)}:{palette.LinePixel(face.LineColour)}").Order().ToArray();
        Require(Colours(idle).SequenceEqual(Colours(jump)));
    });
    Check("private reported chassis use EGA polygon fields", () =>
    {
        foreach (string prefix in new[] { "pha", "sha", "mar", "bma" })
        {
            var bank = ModelBank.Load(game.FilePath(prefix + "pck.tbl"));
            foreach (var model in bank.Resources)
            {
                var expected = model.Entries.Where(entry => entry.Variants.Length != 0).SelectMany(entry => entry.Variants[0].Primitives.Take(1)).SelectMany(primitive => primitive.Polygons).ToArray();
                var scene = Scene.FromBank(bank, [model.Id]); Require(scene.Faces.Count == expected.Length);
                for (int polygon = 0; polygon < expected.Length; polygon++)
                    Require(scene.Faces[polygon].Colour == expected[polygon].AlternateLine && scene.Faces[polygon].LineColour == expected[polygon].AlternateFill);
            }
        }
    });
    Check("private labelled objective has yellow and black EGA faces", () =>
    {
        var bank = ModelBank.Load(game.FilePath("othpck.tbl")); var palette = EgaPalette.Load(game.FilePath("tropic.pal"));
        var scene = Scene.FromBank(bank, [ModelCatalog.ObjectiveBuildingResource]);
        Require(ModelCatalog.Name("othpck.tbl", 14).StartsWith("Objective building", StringComparison.Ordinal));
        Require(scene.Faces.Any(face => palette.PolygonPixel(face.Colour, 0, 0) == 14) && scene.Faces.Any(face => palette.PolygonPixel(face.Colour, 0, 0) == 0));
        Require(SoftwareRenderer.Render(scene, 320, 200, palette: palette).Pixels.Contains(SoftwareRenderer.Ega[14]));
    });
    int reportArgument = Array.IndexOf(args, "--reference-report");
    if (reportArgument >= 0) Check("independent Python inspector structure counts", () =>
    {
        using var report = JsonDocument.Parse(File.ReadAllBytes(args[reportArgument + 1]));
        foreach (string group in new[] { "modelBanks", "otherRenderBanks" })
        {
            if (!report.RootElement.TryGetProperty(group, out var records)) continue;
            foreach (var record in records.EnumerateArray())
            {
                var bank = ModelBank.Load(game.FilePath(record.GetProperty("file").GetString()!));
                Require(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(BinaryData.ReadFile(game.FilePath(record.GetProperty("file").GetString()!))))
                    .Equals(record.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase), "Original model-bank fingerprint changed.");
                var entries = bank.Resources.SelectMany(resource => resource.Entries).ToArray();
                var variants = entries.SelectMany(entry => entry.Variants).ToArray();
                var primitives = variants.SelectMany(variant => variant.Primitives).ToArray();
                var expected = record.GetProperty("renderStructureCounts");
                Require(bank.Decoded.Length == record.GetProperty("decodedBytes").GetInt32());
                Require(entries.Length == expected.GetProperty("entries").GetInt32());
                Require(variants.Length == expected.GetProperty("variants").GetInt32());
                Require(primitives.Length == expected.GetProperty("records").GetInt32());
                Require(primitives.Sum(primitive => primitive.Polygons.Length) == expected.GetProperty("polygons").GetInt32());
                foreach (var kind in expected.GetProperty("types").EnumerateObject())
                    Require(primitives.Count(primitive => primitive.Kind == byte.Parse(kind.Name)) == kind.Value.GetInt32());
            }
        }
    });
}
Console.WriteLine($"{passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;
