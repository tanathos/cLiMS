using System.Buffers.Binary;

namespace Clims.Visualizer.Tests;

/// <summary>Builds vismux DATA datagrams the way the source does.</summary>
internal static class Datagram
{
    public static byte[] Data(uint sequence, uint bufSize, uint bufIndex, uint rate, bool running, short[] samples, bool bigEndianSamples = false)
    {
        var packet = new byte[VismuxProtocol.HeaderLength + 16 + samples.Length * 2];
        packet[0] = 2;
        packet[1] = VismuxProtocol.PacketData;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(2), (ushort)(16 + samples.Length * 2));
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(4), sequence);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(16), bufSize);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(20), bufIndex);
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(24), rate);
        packet[28] = (byte)(running ? 1 : 0);
        packet[29] = (byte)(bigEndianSamples ? 1 : 0);
        for (var i = 0; i < samples.Length; i++)
        {
            if (bigEndianSamples)
            {
                BinaryPrimitives.WriteInt16BigEndian(packet.AsSpan(32 + i * 2), samples[i]);
            }
            else
            {
                BinaryPrimitives.WriteInt16LittleEndian(packet.AsSpan(32 + i * 2), samples[i]);
            }
        }

        return packet;
    }
}
