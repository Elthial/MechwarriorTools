using System.Buffers.Binary;

namespace MechwarriorTooling.Core;

/// <summary>Little-endian disk access with source-qualified bounds failures.</summary>
public sealed class BinaryData(byte[] bytes, string source)
{
    public byte[] Bytes { get; } = bytes;
    public string Source { get; } = source;
    public void Require(int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > Bytes.Length - count)
            throw new InvalidDataException($"{Source}: range 0x{offset:X}+0x{count:X} exceeds {Bytes.Length} bytes.");
    }
    public byte U8(int offset) { Require(offset, 1); return Bytes[offset]; }
    public ushort U16(int offset) { Require(offset, 2); return BinaryPrimitives.ReadUInt16LittleEndian(Bytes.AsSpan(offset)); }
    public short I16(int offset) => unchecked((short)U16(offset));
    public uint U32(int offset) { Require(offset, 4); return BinaryPrimitives.ReadUInt32LittleEndian(Bytes.AsSpan(offset)); }
    public int I32(int offset) => unchecked((int)U32(offset));
    public void Write16(int offset, short value) { Require(offset, 2); BinaryPrimitives.WriteInt16LittleEndian(Bytes.AsSpan(offset), value); }
    public void Write32(int offset, int value) { Require(offset, 4); BinaryPrimitives.WriteInt32LittleEndian(Bytes.AsSpan(offset), value); }
    public string Text(int offset)
    {
        Require(offset, 1);
        int end = Array.IndexOf(Bytes, (byte)0, offset);
        if (end < 0 || end - offset > 256) throw new InvalidDataException($"{Source}: unterminated name.");
        return System.Text.Encoding.ASCII.GetString(Bytes, offset, end - offset).TrimEnd();
    }
    public static byte[] ReadFile(string path)
    {
        if (new FileInfo(path).Length > NativeArchive.MaximumBytes)
            throw new InvalidDataException($"{path}: file exceeds tooling safety limit.");
        return File.ReadAllBytes(path);
    }
}
