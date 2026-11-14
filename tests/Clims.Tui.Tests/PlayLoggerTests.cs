using Clims.Catalog;
using Clims.Tui.Data;
using Clims.Tui.Ui;
using Xunit;

namespace Clims.Tui.Tests;

public class PlayLoggerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "clims-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakePlayer _player = new("test");
    private readonly PlayHistoryStore _store;
    private readonly PlayLogger _logger;
    private DateTimeOffset _now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private readonly CatalogAlbum _album = new(5, "Album", 1, "Artist", 2020, null, false, 1);
    private readonly CatalogTrack[] _tracks =
    [
        new(1, 5, "One", "Artist", 1, 1, 1, TimeSpan.FromSeconds(200), null),
        new(2, 5, "Two", "Artist", 1, 1, 2, TimeSpan.FromSeconds(200), null),
    ];

    public PlayLoggerTests()
    {
        _store = new PlayHistoryStore(Path.Combine(_folder, "h.db"));
        _logger = new PlayLogger(_player, () => _store, () => null, () => _now);
    }

    public void Dispose()
    {
        _logger.Dispose();
        try
        {
            Directory.Delete(_folder, true);
        }
        catch (IOException)
        {
            // best effort
        }
    }

    [Fact]
    public void ATrackIsLoggedOnceItHasPlayedForHalfItsLength()
    {
        _player.Play(_album, _tracks, 0);
        _logger.Step();
        _player.Tick(TimeSpan.FromSeconds(60));
        _logger.Step();
        Assert.Empty(_store.All());

        _player.Tick(TimeSpan.FromSeconds(45));
        _logger.Step();
        _logger.Step();
        var play = Assert.Single(_store.All());
        Assert.Equal((1, 5, "One", "Artist", "Album"), (play.TrackId, play.AlbumId, play.Title, play.Artist, play.Album));
    }

    [Fact]
    public void ASkippedTrackIsNotLogged()
    {
        _player.Play(_album, _tracks, 0);
        _logger.Step();
        _player.Tick(TimeSpan.FromSeconds(20));
        _logger.Step();
        _player.Next();
        _logger.Step();
        Assert.Empty(_store.All());
    }

    [Fact]
    public void RepeatingATrackLogsItAgain()
    {
        _player.Play(_album, _tracks, 0);
        _logger.Step();
        _player.Tick(TimeSpan.FromSeconds(120));
        _logger.Step();
        _now = _now.AddSeconds(200);
        _player.SeekTo(0);
        _logger.Step();
        _player.Tick(TimeSpan.FromSeconds(120));
        _logger.Step();
        Assert.Equal(2, _store.All().Count);
    }

    [Fact]
    public void ARepeatSoonerThanHalfATrackAfterTheLastLogIsAGlitchNotAPlay()
    {
        _player.Play(_album, _tracks, 0);
        _logger.Step();
        _player.Tick(TimeSpan.FromSeconds(120));
        _logger.Step();
        _now = _now.AddSeconds(60);
        _player.SeekTo(0);
        _logger.Step();
        _player.Tick(TimeSpan.FromSeconds(120));
        _logger.Step();
        Assert.Single(_store.All());
    }

    [Fact]
    public void ThresholdIsHalfBetweenFiveSecondsAndFourMinutes()
    {
        Assert.Equal(TimeSpan.FromSeconds(100), PlayLogger.Threshold(TimeSpan.FromSeconds(200)));
        Assert.Equal(TimeSpan.FromSeconds(5), PlayLogger.Threshold(TimeSpan.FromSeconds(4)));
        Assert.Equal(TimeSpan.FromMinutes(4), PlayLogger.Threshold(TimeSpan.FromMinutes(40)));
        Assert.Equal(TimeSpan.FromSeconds(30), PlayLogger.Threshold(TimeSpan.Zero));
    }

    [Fact]
    public void WhenAndAgoReadNaturally()
    {
        var now = new DateTimeOffset(2026, 11, 13, 15, 0, 0, TimeSpan.Zero);
        Assert.Equal("Today 14:30", PlayedView.When(now.AddMinutes(-30), now));
        Assert.Equal("Yesterday 09:00", PlayedView.When(now.AddDays(-1).AddHours(-6), now));
        Assert.Equal("Tue 10 Nov 15:00", PlayedView.When(now.AddDays(-3), now));
        Assert.Equal("5 min ago", PlayedView.Ago(now.AddMinutes(-5), now));
        Assert.Equal("3 h ago", PlayedView.Ago(now.AddHours(-3), now));
        Assert.Equal("4 days ago", PlayedView.Ago(now.AddDays(-4), now));
    }
}
