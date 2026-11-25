using System.Buffers.Binary;
using System.Text;

namespace Clims.Visualizer;

/// <summary>
/// Wire format of the vismux source (https://github.com/PaulWebster/vismux, vismux.h): a 16-byte header, then for DATA a
/// 16-byte vis header and the int16 samples. Header fields are big-endian, the samples use the byte order in <c>format</c>.
/// </summary>
public static class VismuxProtocol
{
    public const int DefaultPort = 23483;
    public const byte PacketData = 0x01;
    public const byte PacketReq = 0x02;
    public const byte PacketSubAck = 0x04;
    public const int HeaderLength = 16;
    private const int VisHeaderLength = 16;

    /// <summary>The subscribe/keepalive packet: protocol version 2, type REQ, everything else zero.</summary>
    public static byte[] BuildRequest()
    {
        var packet = new byte[HeaderLength];
        packet[0] = 2;
        packet[1] = PacketReq;
        return packet;
    }

    /// <summary>The source MAC from a SUB_ACK, or null when the datagram is something else.</summary>
    public static string? TryParseSubAck(ReadOnlySpan<byte> datagram)
    {
        if (datagram.Length < HeaderLength || datagram[1] != PacketSubAck)
        {
            return null;
        }

        var payload = datagram[HeaderLength..];
        var end = payload.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? payload : payload[..end]);
    }

    /// <summary>Decodes a DATA datagram; false for other packet types and for anything inconsistent.</summary>
    public static bool TryParseData(ReadOnlySpan<byte> datagram, out VismuxFrame? frame)
    {
        frame = null;
        if (datagram.Length < HeaderLength + VisHeaderLength || datagram[1] != PacketData)
        {
            return false;
        }

        var payloadLength = BinaryPrimitives.ReadUInt16BigEndian(datagram[2..]);
        if (payloadLength != datagram.Length - HeaderLength || (payloadLength - VisHeaderLength) % 2 != 0)
        {
            return false;
        }

        var sequence = BinaryPrimitives.ReadUInt32BigEndian(datagram[4..]);
        var bufSize = BinaryPrimitives.ReadUInt32BigEndian(datagram[16..]);
        var bufIndex = BinaryPrimitives.ReadUInt32BigEndian(datagram[20..]);
        var rate = BinaryPrimitives.ReadUInt32BigEndian(datagram[24..]);
        var running = datagram[28] != 0;
        var bigEndianSamples = datagram[29] == 1;

        var audio = datagram[(HeaderLength + VisHeaderLength)..];
        var samples = new short[audio.Length / 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = bigEndianSamples
                ? BinaryPrimitives.ReadInt16BigEndian(audio[(i * 2)..])
                : BinaryPrimitives.ReadInt16LittleEndian(audio[(i * 2)..]);
        }

        if (bufSize == 0 || bufIndex >= bufSize || samples.Length > bufSize)
        {
            return false;
        }

        frame = new VismuxFrame(sequence, bufSize, bufIndex, rate, running, samples);
        return true;
    }
}
