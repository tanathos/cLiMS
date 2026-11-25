namespace Clims.Visualizer.Tests;

public class VismuxProtocolTests
{
    [Fact]
    public void Request_is_a_16_byte_REQ_of_protocol_2()
    {
        var packet = VismuxProtocol.BuildRequest();

        Assert.Equal(16, packet.Length);
        Assert.Equal(2, packet[0]);
        Assert.Equal(VismuxProtocol.PacketReq, packet[1]);
        Assert.All(packet.Skip(2), b => Assert.Equal(0, b));
    }

    [Fact]
    public void Data_frame_is_decoded()
    {
        var datagram = Datagram.Data(7, 16384, 100, 44100, true, [1, -2, 300]);

        Assert.True(VismuxProtocol.TryParseData(datagram, out var frame));
        Assert.Equal(7u, frame!.Sequence);
        Assert.Equal(100u, frame.BufIndex);
        Assert.Equal(44100u, frame.Rate);
        Assert.True(frame.Running);
        Assert.Equal(new short[] { 1, -2, 300 }, frame.Samples);
        Assert.False(frame.IsFull);
    }

    [Fact]
    public void Big_endian_samples_are_swapped()
    {
        var datagram = Datagram.Data(1, 16384, 2, 48000, true, [258, -1], bigEndianSamples: true);

        Assert.True(VismuxProtocol.TryParseData(datagram, out var frame));
        Assert.Equal(new short[] { 258, -1 }, frame!.Samples);
    }

    [Fact]
    public void A_frame_with_the_whole_ring_is_full()
    {
        var datagram = Datagram.Data(1, 8, 0, 44100, true, new short[8]);

        Assert.True(VismuxProtocol.TryParseData(datagram, out var frame));
        Assert.True(frame!.IsFull);
    }

    [Fact]
    public void Wrong_length_or_type_is_rejected()
    {
        var datagram = Datagram.Data(1, 16384, 0, 44100, true, [1, 2]);

        Assert.False(VismuxProtocol.TryParseData(datagram.AsSpan(0, datagram.Length - 1), out _));
        datagram[1] = VismuxProtocol.PacketSubAck;
        Assert.False(VismuxProtocol.TryParseData(datagram, out _));
        Assert.False(VismuxProtocol.TryParseData(new byte[5], out _));
    }

    [Fact]
    public void SubAck_carries_the_source_mac()
    {
        var packet = new byte[16 + 18];
        packet[1] = VismuxProtocol.PacketSubAck;
        System.Text.Encoding.ASCII.GetBytes("b8:27:eb:01:02:03").CopyTo(packet, 16);

        Assert.Equal("b8:27:eb:01:02:03", VismuxProtocol.TryParseSubAck(packet));
        Assert.Null(VismuxProtocol.TryParseSubAck(Datagram.Data(1, 8, 0, 44100, true, [1, 2])));
    }
}
