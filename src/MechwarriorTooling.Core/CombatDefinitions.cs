using System.Security.Cryptography;

namespace MechwarriorTooling.Core;

public sealed record WeaponMount(string Weapon, int BodyLocation);
public sealed record MechDefinition(int Index, string Name, string BankName, int Tonnage,
    ushort[] HeaderWords, ushort[] Armour, ushort[] InternalStructure, WeaponMount[] Weapons);
public sealed record MechSequence(int Id, string Name, int[] ResourceFrames, int PartStride, int Prefix);
public sealed record MechAnimationUse(int SequenceId, string Animation, int LogicalFrame, int PhysicalFrame);
public sealed record MechPartResource(int ResourceId, string Chassis, string BodyPart, MechAnimationUse[] Uses);
public sealed record LandscapeColours(int Index, string Name, string PaletteFile, byte Sky, byte Ground);

/// <summary>Reviewed BTECH EXEPACK image and authored chassis/animation tables, not a Reko dependency.</summary>
public sealed class CombatDefinitions
{
    private readonly BinaryData data;
    private readonly Dictionary<int, MechPartResource[]> resourceCatalogues = [];
    public static IReadOnlyList<string> BodyParts { get; } = Array.AsReadOnly(new[] { "Left arm", "Right arm", "Positioned left leg", "Right leg", "Torso", "Head" });
    private const int EffectSequenceEnd = 0x17; // 0810:08E4: sentinel, not an OTHPCK resource.
    public MechDefinition[] Mechs { get; }
    public LandscapeColours[] Landscapes { get; }
    public LandscapeColours LandscapeForPalette(string paletteFile) => Landscapes.SingleOrDefault(landscape =>
        landscape.PaletteFile.Equals(Path.GetFileName(paletteFile), StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidDataException("Map previews require desert.pal, tropic.pal or arctic.pal and reviewed BTECH landscape colours; damage palettes are not landscapes.");
    public (string Name, int[] Resources)[] EffectSequences =>
    [
        ("Impact", Enumerable.Range(0, 7).Select(index => (int)data.U16(0x0E96 + index * 2)).TakeWhile(id => id != EffectSequenceEnd).ToArray()),
        ("Machine gun", Enumerable.Range(0, 3).Select(index => (int)data.U16(0x0EA4 + index * 2)).TakeWhile(id => id != EffectSequenceEnd).ToArray())
    ];
    public static readonly string[] BankPrefixes = ["loc", "jen", "pha", "sha", "rif", "ham", "mar", "bma"];
    private static readonly string[] SequenceNames = ["Idle", "Walk", "Begin jump", "Sequence 3 (unknown)", "Fall", "Recover", "Landing", "Failed recovery", "Firing pause"];
    private CombatDefinitions(byte[] segment)
    {
        data = new BinaryData(segment, "BTECH chassis tables");
        // 0810:2716, EGA scheme 0: camera fill A is sky; shared fill B is ground.
        string[] landscapeNames = ["Desert", "Tropical", "Arctic"];
        string[] landscapePalettes = ["desert.pal", "tropic.pal", "arctic.pal"];
        Landscapes = Enumerable.Range(0, 3).Select(index => new LandscapeColours(index, landscapeNames[index], landscapePalettes[index],
            data.U8(0x1E88 + index * 0x1E), data.U8(0x1E8A + index * 0x1E))).ToArray();
        Mechs = Enumerable.Range(0, 8).Select(index =>
        {
            int at = 0x1178 + index * 0xE0;
            int mountCount = data.U16(at + 0x12);
            if (mountCount > 10) throw new InvalidDataException("Chassis exceeds ten weapon mounts.");
            var mounts = Enumerable.Range(0, mountCount).Select(slot =>
            {
                int mount = at + 0x4A + slot * 8, kind = data.U16(mount);
                if (kind >= 10) throw new InvalidDataException("Unknown weapon kind.");
                return new WeaponMount(data.Text(data.U16(0x2286 + kind * 2)), data.U16(mount + 2));
            }).ToArray();
            ushort[] Words(int offset, int count) => Enumerable.Range(0, count).Select(word => data.U16(offset + word * 2)).ToArray();
            return new MechDefinition(index, data.Text(data.U16(0x229C + index * 2)), BankPrefixes[index] + "pck.tbl",
                data.U16(at), Words(at, 10), Words(at + 0xB2, 9), Words(at + 0xC4, 8), mounts);
        }).ToArray();
    }
    public static CombatDefinitions Load(string executable)
    {
        byte[] packed = BinaryData.ReadFile(executable);
        const string knownHash = "1705B607E798DDFCE93DA73F999FA98E770E0359C739EC939D1CB7CEFC5D3A4C";
        if (!Convert.ToHexString(SHA256.HashData(packed)).Equals(knownHash, StringComparison.Ordinal))
            throw new InvalidDataException("Unreviewed BTECH.EXE revision. Model/map browsing remains available; chassis animation requires the reviewed executable.");
        return new CombatDefinitions(Unpack(packed).AsSpan(0x283E0, 0x4EDC).ToArray());
    }
    public MechSequence[] Sequences(int chassis)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chassis);
        if (chassis >= 8) throw new ArgumentOutOfRangeException(nameof(chassis));
        return Enumerable.Range(0, 9).Where(sequence => data.U8(0x24C6 + chassis * 9 + sequence) != 0)
            .Select(sequence =>
            {
                int count = data.U8(0x24A0 + sequence), map = data.U16(0x248E + sequence * 2);
                if (count == 0 || map < 0x245E || map + count > 0x248E) throw new InvalidDataException("Animation map escapes authored table.");
                return new MechSequence(sequence, SequenceNames[sequence], Enumerable.Range(0, count).Select(frame => (int)data.U8(map + frame)).ToArray(),
                    data.U8(0x24AA + sequence), data.U16(0x24B4 + sequence * 2));
            }).ToArray();
    }
    public int[] PartResources(int chassis, MechSequence sequence, int frame)
    {
        if ((uint)frame >= sequence.ResourceFrames.Length) throw new ArgumentOutOfRangeException(nameof(frame));
        int first = sequence.ResourceFrames[frame];
        if (sequence.Prefix > 9) throw new InvalidDataException("Invalid animation prefix.");
        for (int prior = 0; prior < sequence.Prefix; prior++)
            if (data.U8(0x24C6 + chassis * 9 + prior) != 0) first += data.U8(0x24AA + prior) * 6;
        return Enumerable.Range(0, 6).Select(part => first + part * sequence.PartStride).ToArray();
    }
    /// <summary>Invert 0810:9C06's part binding, retaining every logical frame alias.</summary>
    public MechPartResource[] ModelResources(int chassis)
    {
        if (resourceCatalogues.TryGetValue(chassis, out var cached)) return cached;
        var references = new List<(int Resource, int Part, MechAnimationUse Use)>();
        foreach (var sequence in Sequences(chassis))
            for (int frame = 0; frame < sequence.ResourceFrames.Length; frame++)
            {
                int[] resources = PartResources(chassis, sequence, frame);
                for (int part = 0; part < BodyParts.Count; part++)
                    references.Add((resources[part], part, new(sequence.Id, sequence.Name, frame, sequence.ResourceFrames[frame])));
            }
        var catalogue = references.GroupBy(reference => reference.Resource).OrderBy(group => group.Key).Select(group =>
        {
            int part = group.Select(reference => reference.Part).Distinct().Single();
            return new MechPartResource(group.Key, Mechs[chassis].Name, BodyParts[part], group.Select(reference => reference.Use).ToArray());
        }).ToArray();
        resourceCatalogues.Add(chassis, catalogue);
        return catalogue;
    }
    public MechPartResource? FindModelResource(string bank, int resource)
    {
        int chassis = Array.FindIndex(Mechs, mech => mech.BankName.Equals(Path.GetFileName(bank), StringComparison.OrdinalIgnoreCase));
        return chassis < 0 ? null : ModelResources(chassis).SingleOrDefault(item => item.ResourceId == resource);
    }
    private static byte[] Unpack(byte[] packed)
    {
        var input = new BinaryData(packed, "BTECH EXEPACK");
        int start = input.U16(8) * 16, stub = start + input.U16(0x16) * 16;
        if (input.U16(0) != 0x5A4D || input.U16(6) != 0 || input.U16(0x14) != 0x12
            || input.U16(stub + 0x10) != 0x4252 || input.U16(stub + 0x0E) != 1 || input.U16(stub + 0x0C) * 16 != 0x2D2C0)
            throw new InvalidDataException("Unsupported EXEPACK header.");
        int stubEnd = stub + input.U16(stub + 6);
        input.Require(stub, stubEnd - stub);
        byte[] decoded = new byte[0x2D2C0];
        int source = stub, destination = decoded.Length;
        while (source > start && packed[source - 1] == 255) source--;
        for (;;)
        {
            if (source - start < 3) throw new InvalidDataException("EXEPACK command truncated.");
            byte command = packed[--source]; source -= 2;
            int count = input.U16(source);
            if (count > destination) throw new InvalidDataException("EXEPACK output underflow.");
            destination -= count;
            if ((command & 0xFE) == 0xB0)
            {
                if (source <= start) throw new InvalidDataException("EXEPACK repeat truncated.");
                decoded.AsSpan(destination, count).Fill(packed[--source]);
            }
            else if ((command & 0xFE) == 0xB2)
            {
                if (count > source - start) throw new InvalidDataException("EXEPACK literal truncated.");
                source -= count; packed.AsSpan(source, count).CopyTo(decoded.AsSpan(destination));
            }
            else throw new InvalidDataException("Unknown EXEPACK command.");
            if ((command & 1) != 0) break;
        }
        if (destination != 0) throw new InvalidDataException("Incomplete EXEPACK image.");
        var image = new BinaryData(decoded, "BTECH image");
        int cursor = stub + 0x12D;
        for (int group = 0; group < 16; group++)
        {
            if (cursor > stubEnd - 2) throw new InvalidDataException("Relocation table truncated.");
            int count = input.U16(cursor); cursor += 2;
            for (int item = 0; item < count; item++)
            {
                if (cursor > stubEnd - 2) throw new InvalidDataException("Relocation group truncated.");
                int at = group * 0x10000 + input.U16(cursor); cursor += 2;
                image.Write16(at, unchecked((short)(image.U16(at) + 0x0810)));
            }
        }
        return decoded;
    }
}
