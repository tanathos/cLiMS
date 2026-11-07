namespace Clims.Lms.Tests;

/// <summary>Opt-in tests that change the real player. They restore it afterwards, but never run them while someone is listening.</summary>
public class MutatingIntegrationTests
{
    [MutatingFact]
    public async Task Volume_can_be_changed_and_is_restored()
    {
        using var client = Integration.CreateClient();
        var id = Integration.PlayerId!;
        var snapshot = await PlayerSnapshot.TakeAsync(client, id);
        try
        {
            var target = snapshot.Status.Volume > 20 ? snapshot.Status.Volume - 5 : snapshot.Status.Volume + 5;
            await client.Player.SetVolumeAsync(id, target);
            Assert.Equal(target, (await client.Player.GetStatusAsync(id, 0, 0, TrackTags.None)).Volume);
        }
        finally
        {
            await snapshot.RestoreAsync(client);
        }

        Assert.Equal(snapshot.Status.Volume, (await client.Player.GetStatusAsync(id, 0, 0, TrackTags.None)).Volume);
    }
}
