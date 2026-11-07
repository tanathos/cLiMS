using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Clims.Lms.Tests;

public class ServerDiscoveryTests
{
    private static byte[] Reply(params (string Tag, string Value)[] tags)
    {
        var bytes = new List<byte> { (byte)'E' };
        foreach (var (tag, value) in tags)
        {
            var v = Encoding.UTF8.GetBytes(value);
            bytes.AddRange(Encoding.ASCII.GetBytes(tag));
            bytes.Add((byte)v.Length);
            bytes.AddRange(v);
        }

        return bytes.ToArray();
    }

    [Fact]
    public void The_request_asks_for_the_five_tags()
    {
        var request = ServerDiscovery.BuildRequest();
        Assert.Equal((byte)'e', request[0]);
        Assert.Equal("NAME\0JSON\0VERS\0UUID\0CLIP\0", Encoding.ASCII.GetString(request, 1, request.Length - 1));
    }

    [Fact]
    public void A_reply_is_read_into_a_server()
    {
        var server = ServerDiscovery.ParseReply(
            Reply(("NAME", "Mnemonic Music"), ("JSON", "9000"), ("VERS", "9.1.1"), ("UUID", "abc"), ("CLIP", "9090")),
            "192.168.1.20")!;
        Assert.Equal("Mnemonic Music", server.Name);
        Assert.Equal(new Uri("http://192.168.1.20:9000/"), server.BaseUri);
        Assert.Equal("9.1.1", server.Version);
        Assert.Equal(9090, server.CliPort);
    }

    [Fact]
    public void Garbage_and_requests_are_not_servers()
    {
        Assert.Null(ServerDiscovery.ParseReply(Encoding.ASCII.GetBytes("e..."), "1.2.3.4"));
        Assert.Null(ServerDiscovery.ParseReply(ReadOnlySpan<byte>.Empty, "1.2.3.4"));
        Assert.Null(ServerDiscovery.ParseReply(Reply(("XXXX", "y")), "1.2.3.4"));
    }

    [Fact]
    public void A_truncated_reply_does_not_throw()
    {
        var bytes = Reply(("NAME", "Mnemonic Music"), ("JSON", "9000"));
        Assert.NotNull(ServerDiscovery.ParseReply(bytes.AsSpan(0, bytes.Length - 2), "h"));
    }

    [Fact]
    public async Task Servers_that_answer_over_a_real_socket_are_found_once_each()
    {
        using var fake = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)fake.Client.LocalEndPoint!).Port;
        var responder = Task.Run(async () =>
        {
            var received = await fake.ReceiveAsync();
            Assert.Equal((byte)'e', received.Buffer[0]);
            var reply = Reply(("NAME", "Test LMS"), ("JSON", "9001"), ("UUID", "u1"));
            await fake.SendAsync(reply, received.RemoteEndPoint);
            await fake.SendAsync(reply, received.RemoteEndPoint); // the same server heard twice
        });

        var found = new List<DiscoveredServer>();
        var discovery = new ServerDiscovery(new ServerDiscoveryOptions
        {
            Port = port,
            Duration = TimeSpan.FromMilliseconds(600),
            Targets = [IPAddress.Loopback],
        });
        await foreach (var s in discovery.DiscoverAsync())
        {
            found.Add(s);
        }

        await responder;
        var one = Assert.Single(found);
        Assert.Equal("Test LMS", one.Name);
        Assert.Equal(9001, one.WebPort);
    }

    [Fact]
    public async Task Nothing_answering_ends_the_search_empty()
    {
        var discovery = new ServerDiscovery(new ServerDiscoveryOptions
        {
            Port = 1,
            Duration = TimeSpan.FromMilliseconds(300),
            Targets = [IPAddress.Loopback],
        });
        Assert.Empty(await discovery.DiscoverAsync().ToListAsync());
    }

    [Fact]
    public void Every_interface_broadcast_is_offered_besides_the_limited_one()
    {
        var all = ServerDiscovery.BroadcastAddresses();
        Assert.Equal(IPAddress.Broadcast, all[0]);
    }
}
