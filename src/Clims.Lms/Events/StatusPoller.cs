using System.Runtime.CompilerServices;

namespace Clims.Lms;

/// <summary>
/// Fallback when the CLI socket is unavailable: polls <c>status</c> and turns the differences into the same events.
/// Slower and coarser than <see cref="CliEventStream"/> (a track change is seen up to one interval late) but needs
/// nothing beyond the HTTP port.
/// </summary>
public sealed class StatusPoller : ILmsEventStream
{
    private readonly IPlayerApi _player;
    private readonly string _playerId;
    private readonly TimeSpan _interval;

    /// <summary>Creates a poller for one player.</summary>
    public StatusPoller(IPlayerApi player, string playerId, TimeSpan? interval = null)
    {
        _player = player;
        _playerId = playerId;
        _interval = interval ?? TimeSpan.FromSeconds(2);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LmsEvent> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var timer = new PeriodicTimer(_interval);
        PlayerStatus? previous = null;
        var online = true;
        do
        {
            PlayerStatus? current = null;
            string? failure = null;
            try
            {
                current = await _player.GetStatusAsync(_playerId, null, 1, TrackTags.None, cancellationToken).ConfigureAwait(false);
            }
            catch (LmsException ex)
            {
                failure = ex.Message;
            }

            if (current is null)
            {
                if (online)
                {
                    online = false;
                    yield return new EventStreamStateEvent(false, failure);
                }

                continue;
            }

            if (!online)
            {
                online = true;
                yield return new EventStreamStateEvent(true);
            }

            foreach (var e in Diff(_playerId, previous, current))
            {
                yield return e;
            }

            previous = current;
        }
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Events implied by the change from <paramref name="previous"/> to <paramref name="current"/>. The first poll yields none.</summary>
    public static IEnumerable<LmsEvent> Diff(string playerId, PlayerStatus? previous, PlayerStatus current)
    {
        if (previous is null)
        {
            yield break;
        }

        if (current.Mode != previous.Mode)
        {
            yield return new PlaybackStateEvent(playerId, current.Mode);
        }

        if (current.CurrentIndex != previous.CurrentIndex || current.CurrentTrack?.Id != previous.CurrentTrack?.Id)
        {
            yield return new TrackChangedEvent(playerId, current.CurrentIndex, current.CurrentTrack?.Title);
        }

        if (current.Volume != previous.Volume)
        {
            yield return new VolumeChangedEvent(playerId, current.Volume);
        }

        if (current.QueueTimestamp != previous.QueueTimestamp || current.QueueLength != previous.QueueLength)
        {
            yield return new QueueChangedEvent(playerId);
        }

        if (current.Repeat != previous.Repeat || current.Shuffle != previous.Shuffle)
        {
            yield return new ModeChangedEvent(playerId);
        }

        if (current.IsPoweredOn != previous.IsPoweredOn)
        {
            yield return new PowerChangedEvent(playerId, current.IsPoweredOn);
        }
    }
}
