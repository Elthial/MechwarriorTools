namespace MechwarriorTooling.Core;

/// <summary>Finite DOSBox-X combat capture profile, shared with SDLBackend_CombatPlatform.c.</summary>
public sealed class DosToneMemoryProfile(bool divideHandlerInstalled = true)
{
    public const string ProfileName = "DOSBox-X captured combat profile (load selector 0956)";
    public const ushort ZeroWindowStart = 0x1F4A, ZeroWindowBytes = 0x01D4;
    public const uint InstalledDivideVector = 0x1D3B1F4A;
    public string Name => divideHandlerInstalled ? ProfileName : "DOSBox-X captured BIOS profile (divide vector restored)";
    public static DosToneMemoryProfile Combat { get; } = new();
    public byte ReadByte(ushort offset)
    {
        if (offset < 6 * 4)
        {
            int interrupt = offset / 4;
            uint vector = interrupt == 0 ? divideHandlerInstalled ? InstalledDivideVector : 0xF000CA60u
                : interrupt < 5 ? 0x0070000Eu : 0xF000FF54u;
            return (byte)(vector >> ((offset & 3) * 8));
        }
        if (offset >= ZeroWindowStart && offset - ZeroWindowStart < ZeroWindowBytes) return 0;
        throw new InvalidDataException($"Tone fallback read 0000:{offset:X4} is outside the captured DOS-memory profile.");
    }
    public ushort ReadWord(ushort offset) => (ushort)(ReadByte(offset) | ReadByte(unchecked((ushort)(offset + 1))) << 8);
    public (ushort Offset, ushort PhaseLimit) Descriptor(ushort index)
    {
        ushort address = unchecked((ushort)(index * 4));
        return (ReadWord(address), ReadWord(unchecked((ushort)(address + 2))));
    }
}
