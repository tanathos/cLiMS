using Clims.Tui.Data;
using Clims.Tui.Ui;
using Terminal.Gui.Input;

namespace Clims.Tui.Tests;

public class OnlineTabTests
{
    private static (OnlineTab Tab, FakeOnlineApi Api, List<string> Notices) Create(bool demo = false)
    {
        var api = new FakeOnlineApi();
        var tab = new OnlineTab(demo ? null : new OnlineSource(api, () => "mac"), action => action());
        var notices = new List<string>();
        tab.Notice += notices.Add;
        return (tab, api, notices);
    }

    private static void Until(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            Thread.Sleep(10);
        }

        Assert.True(condition(), "Timed out waiting for the tab.");
    }

    private static IEnumerable<string> Texts(OnlineTab tab) => tab.List.Rows.Select(r => r.Text);

    private static void Select(OnlineTab tab, string text) =>
        tab.List.SelectIndex(tab.List.Rows.ToList().FindIndex(r => r.Text == text));

    private static void Enter(OnlineTab tab) => tab.List.NewKeyDownEvent(Key.Enter);

    [Fact]
    public void The_apps_are_listed_in_a_radio_and_an_apps_group_once_the_tab_is_shown()
    {
        var (tab, _, _) = Create();
        Assert.Contains("Asking the server…", Texts(tab));

        tab.Show();
        Until(() => Texts(tab).Contains("Spotty"));

        Assert.Equal(new[] { "Radio", "Local Radio", "Station search", "Apps", "Spotty" }, Texts(tab));
    }

    [Fact]
    public void Demo_mode_says_there_is_nothing_to_browse()
    {
        var (tab, _, _) = Create(demo: true);
        tab.Show();
        Assert.Contains(Texts(tab), t => t.Contains("demo mode"));
    }

    [Fact]
    public void An_app_is_opened_level_by_level_and_Esc_goes_back_to_where_it_was()
    {
        var (tab, api, _) = Create();
        tab.Show();
        Until(() => Texts(tab).Contains("Spotty"));
        Select(tab, "Spotty");
        Enter(tab);
        Until(() => Texts(tab).Contains("Albums"));
        Assert.True(tab.Deep);

        Select(tab, "Albums");
        Enter(tab);
        Until(() => Texts(tab).Contains("Satellites by Mew"));
        Assert.Equal("Spotty › Albums", tab.List.Rows[0].Text);

        Assert.True(tab.HandleKey(Key.Esc));
        Assert.Contains("Albums", Texts(tab));
        Assert.True(tab.HandleKey(Key.Esc));
        Assert.False(tab.Deep);
        Assert.Equal("Spotty", tab.List.SelectedRow?.Text);
        Assert.False(tab.HandleKey(Key.Esc));
        Assert.Contains("spotty/2//0", api.Browsed);
    }

    [Fact]
    public void Enter_plays_something_playable_and_a_and_n_queue_it()
    {
        var (tab, api, notices) = Create();
        tab.Show();
        Until(() => Texts(tab).Contains("Spotty"));
        Select(tab, "Spotty");
        Enter(tab);
        Until(() => Texts(tab).Contains("Albums"));
        Select(tab, "Albums");
        Enter(tab);
        Until(() => Texts(tab).Contains("Satellites by Mew"));
        Select(tab, "Satellites by Mew");

        Enter(tab);
        Until(() => { lock (api.Played) { return api.Played.Count == 1; } });
        Assert.True(tab.HandleKey(Key.A));
        Assert.True(tab.HandleKey(Key.N));
        Until(() => { lock (api.Played) { return api.Played.Count == 3; } });

        Assert.Equal(new[] { "spotty Add 2.0", "spotty Insert 2.0", "spotty Play 2.0" }, api.Played.Order());
        Until(() => notices.Contains("Playing Satellites by Mew"));
    }

    private static OnlineTab OpenLikedSongs(FakeOnlineApi _, OnlineTab tab)
    {
        tab.Show();
        Until(() => Texts(tab).Contains("Spotty"));
        Select(tab, "Spotty");
        Enter(tab);
        Until(() => Texts(tab).Contains("Liked Songs"));
        Select(tab, "Liked Songs");
        Enter(tab);
        Until(() => Texts(tab).Contains("Three"));
        return tab;
    }

    [Fact]
    public void Shift_A_adds_every_track_of_the_list_and_Shift_R_does_nothing()
    {
        var (tab, api, _) = Create();
        OpenLikedSongs(api, tab);

        Assert.True(tab.HandleKey(Key.A.WithShift));
        // The folder is tried as a whole first; this fake server does not queue it, so the tracks follow one by one.
        Until(() => { lock (api.Played) { return api.Played.Count == 4; } });
        Assert.Equal(new[] { "spotty Add 3", "spotty Add 3.0", "spotty Add 3.1", "spotty Add 3.2" }, api.Played);
        Assert.False(tab.HandleKey(Key.R.WithShift));
    }

    [Fact]
    public void Alt_A_replaces_the_queue_with_the_list_and_starts_it()
    {
        var (tab, api, notices) = Create();
        OpenLikedSongs(api, tab);

        Assert.True(tab.HandleKey(Key.A.WithAlt));
        Until(() => { lock (api.Played) { return api.Played.Count == 4; } });

        Assert.Equal(new[] { "spotty Play 3", "spotty Play 3.0", "spotty Add 3.1", "spotty Add 3.2" }, api.Played);
        Until(() => notices.Contains("Playing 3 tracks"));
    }

    [Fact]
    public void Alt_A_uses_one_command_for_the_whole_folder_when_the_server_queues_it_all()
    {
        var (tab, api, notices) = Create();
        api.FolderQueuesAll = true;
        OpenLikedSongs(api, tab);

        Assert.True(tab.HandleKey(Key.A.WithAlt));
        Until(() => notices.Contains("Playing 3 tracks"));

        Assert.Equal(new[] { "spotty Play 3" }, api.Played);
    }

    [Fact]
    public void Load_more_keeps_the_place_and_selects_the_first_new_line()
    {
        var (tab, api, _) = Create();
        api.LikedCount = 150;
        OpenLikedSongs(api, tab);
        Until(() => Texts(tab).Any(t => t.StartsWith("Load more")));
        tab.List.Offset = 60;
        Select(tab, Texts(tab).First(t => t.StartsWith("Load more")));

        Enter(tab);
        Until(() => Texts(tab).Contains("Track 149"));

        Assert.Equal("Track 100", tab.List.SelectedRow?.Text);
        Assert.Equal(60, tab.List.Offset);
    }

    [Fact]
    public void The_whole_list_keys_do_nothing_where_there_is_nothing_to_play()
    {
        var (tab, api, _) = Create();
        tab.Show();
        Until(() => Texts(tab).Contains("Spotty"));
        Select(tab, "Spotty");
        Enter(tab);
        Until(() => Texts(tab).Contains("Albums"));

        Assert.False(tab.HandleKey(Key.A.WithShift));
        Assert.False(tab.HandleKey(Key.A.WithAlt));
        Assert.Empty(api.Played);
    }

    [Fact]
    public void A_search_asks_for_text_and_lists_the_answer()
    {
        var (tab, api, _) = Create();
        tab.Show();
        Until(() => Texts(tab).Contains("Station search"));
        Select(tab, "Station search");
        Enter(tab);
        Assert.True(tab.Searching);

        tab.CancelSearch();
        Assert.False(tab.Searching);
        Assert.Empty(api.Browsed);
    }

    [Fact]
    public void Coming_back_from_the_footer_opens_the_levels_the_station_was_chosen_in_with_it_selected()
    {
        var (tab, api, _) = Create();
        tab.Show();
        Until(() => Texts(tab).Contains("Spotty"));
        Select(tab, "Spotty");
        Enter(tab);
        Until(() => Texts(tab).Contains("Albums"));
        Select(tab, "Albums");
        Enter(tab);
        Until(() => Texts(tab).Contains("Satellites by Mew"));
        Select(tab, "Satellites by Mew");
        Enter(tab);
        Until(() => { lock (api.Played) { return api.Played.Count == 1; } });

        // The user wandered off: back to the top, then asks for the place of what plays.
        Assert.True(tab.HandleKey(Key.Esc));
        Assert.True(tab.HandleKey(Key.Esc));
        Assert.False(tab.Deep);
        tab.ShowPlayed();
        Until(() => Texts(tab).Contains("Satellites by Mew"));

        Assert.Equal("Satellites by Mew", tab.List.SelectedRow?.Text);
        Assert.Equal("Spotty › Albums", tab.List.Rows[0].Text);
    }
}
