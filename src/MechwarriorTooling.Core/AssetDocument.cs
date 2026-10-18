using System.Security.Cryptography;
using System.Text.Json;

namespace MechwarriorTooling.Core;

/// <summary>One editable file with exact undo snapshots and non-overwriting, verified export.</summary>
public sealed class AssetDocument(string sourcePath, string installationRoot, byte[] decoded, bool archive)
{
    private readonly Stack<byte[]> undo = new();
    private readonly Stack<byte[]> redo = new();
    private readonly byte[] original = (byte[])decoded.Clone();
    public string SourcePath { get; } = Path.GetFullPath(sourcePath);
    public byte[] Bytes { get; private set; } = (byte[])decoded.Clone();
    public bool IsArchive { get; } = archive;
    public bool Modified => !Bytes.AsSpan().SequenceEqual(original);
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public void Change(Action<BinaryData> edit, Action<byte[]> validate)
    {
        byte[] next = (byte[])Bytes.Clone();
        edit(new BinaryData(next, SourcePath));
        validate(next);
        if (Bytes.AsSpan().SequenceEqual(next)) return;
        if (undo.Count >= 100 || (long)Bytes.Length * (undo.Count + redo.Count + 2) > 64 * 1024 * 1024)
            throw new InvalidOperationException("Undo history limit reached; save and reopen your working copy.");
        undo.Push(Bytes); redo.Clear(); Bytes = next;
    }
    public void Undo() { if (undo.TryPop(out var previous)) { redo.Push(Bytes); Bytes = previous; } }
    public void Redo() { if (redo.TryPop(out var next)) { undo.Push(Bytes); Bytes = next; } }
    public void SaveCopy(string destination, Action<byte[]> validate)
    {
        string output = Path.GetFullPath(destination), root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installationRoot));
        if (output.Equals(SourcePath, StringComparison.OrdinalIgnoreCase)
            || output.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a working-copy location outside the original installation.");
        for (DirectoryInfo? folder = Directory.GetParent(output); folder != null; folder = folder.Parent)
            if (folder.LinkTarget != null) throw new InvalidOperationException("Save-copy paths through directory links are not supported.");
        validate(Bytes);
        byte[] saved = IsArchive ? NativeArchive.StoreRaw(Bytes) : (byte[])Bytes.Clone();
        byte[] replay = IsArchive ? NativeArchive.Decode(saved) : saved;
        validate(replay);
        if (!Bytes.AsSpan().SequenceEqual(replay)) throw new InvalidDataException("Serialized output does not match the edited document.");
        // Verify actual disk bytes before publication. Move(..., false) protects an
        // existing destination even if another process creates it during serialization.
        string temporary = Path.Combine(Path.GetDirectoryName(output)!, ".mw-tooling-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(saved); file.Flush(true); }
            byte[] disk = BinaryData.ReadFile(temporary), reopened = IsArchive ? NativeArchive.Decode(disk) : disk;
            validate(reopened);
            if (!Bytes.AsSpan().SequenceEqual(reopened)) throw new InvalidDataException("Saved bytes failed verification.");
            File.Move(temporary, output, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public string DescribeChanges() => JsonSerializer.Serialize(new
    {
        schemaVersion = 1, source = SourcePath, modified = Modified,
        originalDecodedSha256 = Convert.ToHexString(SHA256.HashData(original)),
        editedDecodedSha256 = Convert.ToHexString(SHA256.HashData(Bytes)),
        byteChanges = Bytes.Select((value, offset) => (value, offset)).Where(item => original[item.offset] != item.value)
            .Select(item => new { offset = $"0x{item.offset:X}", before = original[item.offset], after = item.value })
    }, new JsonSerializerOptions { WriteIndented = true });
}
