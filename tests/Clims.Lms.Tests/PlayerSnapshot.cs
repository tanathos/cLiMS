namespace Clims.Lms.Tests;

/// <summary>Remembers a player's state and puts it back, for tests that must touch the real player.</summary>
public sealed class PlayerSnapshot
{
    private PlayerSnapshot(string playerId, PlayerStatus status, IReadOnlyList<int> queue)
    {
        PlayerId = playerId;
        Status = status;
        Queue = queue;
    }

    public string PlayerId { get; }

    public PlayerStatus Status { get; }

    public IReadOnlyList<int> Queue { get; }

    public static async Task<PlayerSnapshot> TakeAsync(ILmsClient client, string playerId)
    {
        var status = await client.Player.GetStatusAsync(playerId, 0, 0, TrackTags.None);
        var ids = new List<int>();
        for (var start = 0; start < status.QueueLength; start += 200)
        {
            var page = await client.Queue.GetQueueAsync(playerId, start, 200, TrackTags.None);
            ids.AddRange(page.Items.Select(i => i.Track.Id));
        }

        return new PlayerSnapshot(playerId, status, ids);
    }

    /// <summary>Restores queue (only if it changed), position, volume, modes and play/pause state.</summary>
    public async Task RestoreAsync(ILmsClient client)
    {
        var now = await TakeAsync(client, PlayerId);
        if (!now.Queue.SequenceEqual(Queue))
        {
            if (Queue.Count == 0)
            {
                await client.Queue.ClearAsync(PlayerId);
            }
            else
            {
                await client.Queue.LoadTracksAsync(PlayerId, Queue, Status.CurrentIndex ?? 0);
            }
        }
        else if (now.Status.CurrentIndex != Status.CurrentIndex && Status.CurrentIndex is { } index)
        {
            await client.Player.JumpToAsync(PlayerId, index);
        }

        if (Status.QueueLength > 0)
        {
            await client.Player.SeekAsync(PlayerId, Status.Elapsed);
        }

        await client.Player.SetVolumeAsync(PlayerId, Status.Volume);
        await client.Player.SetRepeatAsync(PlayerId, Status.Repeat);
        await client.Player.SetShuffleAsync(PlayerId, Status.Shuffle);
        await client.Player.PauseAsync(PlayerId, Status.Mode != PlayMode.Play);
    }
}
