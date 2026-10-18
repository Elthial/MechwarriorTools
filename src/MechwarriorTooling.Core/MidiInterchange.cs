using System.Buffers.Binary;

namespace MechwarriorTooling.Core;

/// <summary>Shipped .mus files already are standard MIDI format 0: preserve all event bytes.</summary>
public static class MidiInterchange
{
    public static byte[] Validate(byte[] bytes)
    {
        if (bytes.Length < 22 || !bytes.AsSpan(0, 4).SequenceEqual("MThd"u8) || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4)) != 6
            || BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(8)) != 0 || BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(10)) != 1)
            throw new InvalidDataException("Native music requires standard MIDI format 0 with one track; export/merge to format 0 in your MIDI editor.");
        ushort division = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(12));
        if (division == 0 || (division & 0x8000) != 0 || !bytes.AsSpan(14, 4).SequenceEqual("MTrk"u8)
            || BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(18)) != bytes.Length - 22)
            throw new InvalidDataException("Invalid MIDI timing division or track extent.");
        int at = 22; byte running = 0; bool ended = false;
        int Variable()
        {
            int value = 0;
            for (int count = 0; count < 4; count++)
            {
                if (at >= bytes.Length) throw new InvalidDataException("Truncated MIDI variable length.");
                byte next = bytes[at++]; value = (value << 7) | (next & 127); if ((next & 128) == 0) return value;
            }
            throw new InvalidDataException("Oversized MIDI variable length.");
        }
        while (at < bytes.Length)
        {
            Variable(); if (at >= bytes.Length || ended) throw new InvalidDataException("Invalid/trailing MIDI event.");
            byte status = bytes[at];
            if ((status & 128) != 0) at++; else status = running;
            if (status == 255)
            {
                if (at >= bytes.Length) throw new InvalidDataException("Truncated MIDI meta event.");
                byte type = bytes[at++]; int length = Variable(); if (length > bytes.Length - at) throw new InvalidDataException("MIDI meta extent.");
                if (type == 0x2F) { if (length != 0) throw new InvalidDataException("Invalid MIDI end marker."); ended = true; }
                at += length; // Meta events do not replace the channel running status in the shipped format-0 streams.
            }
            else if (status is 0xF0 or 0xF7)
            { int length = Variable(); if (length > bytes.Length - at) throw new InvalidDataException("MIDI SysEx extent."); at += length; running = 0; }
            else if (status is 0xF8 or 0xFA or 0xFB or 0xFC or 0xFE)
            { /* Shipped MUS streams include MIDI real-time Start events. They preserve running channel status. */ }
            else
            {
                if (status < 0x80 || status >= 0xF0) throw new InvalidDataException($"Unsupported MIDI status 0x{status:X2} at file offset 0x{at:X}.");
                running = status; int count = (status & 0xF0) is 0xC0 or 0xD0 ? 1 : 2;
                if (count > bytes.Length - at || bytes.AsSpan(at, count).ContainsAnyInRange((byte)128, (byte)255)) throw new InvalidDataException("Invalid MIDI channel data.");
                at += count;
            }
        }
        if (!ended) throw new InvalidDataException("Missing MIDI end-of-track marker."); return (byte[])bytes.Clone();
    }
}
