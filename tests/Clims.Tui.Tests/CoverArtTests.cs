using Clims.Tui.Settings;
using Clims.Tui.Ui;

namespace Clims.Tui.Tests;

[Collection("CoverArt state")]
public class CoverArtTests
{
    [Fact]
    public void Each_place_takes_its_own_style_and_the_library_its_size_from_the_settings()
    {
        try
        {
            CoverArt.Apply(new CoverStylesSettings
            {
                Footer = new CoverPlaceSettings { Style = CoverMode.Braille },
                Library = new CoverPlaceSettings { Style = CoverMode.HalfBlock, Size = CoverSize.Large },
                FullScreen = new CoverPlaceSettings { Style = CoverMode.Quadrant },
            });

            Assert.Equal(CoverMode.Braille, CoverArt.Style(CoverPlace.Footer));
            Assert.Equal(CoverMode.HalfBlock, CoverArt.Style(CoverPlace.Library));
            Assert.Equal(CoverMode.Quadrant, CoverArt.Style(CoverPlace.FullScreen));
            Assert.Equal(CoverSize.Large, CoverArt.LibrarySize);
        }
        finally
        {
            CoverArt.Apply(new CoverStylesSettings());
        }
    }

    [Fact]
    public void Where_sixel_cannot_be_used_the_fallback_is_drawn_and_it_is_never_sixel_itself()
    {
        try
        {
            CoverArt.Apply(new CoverStylesSettings { SixelFallback = CoverMode.Braille });
            Assert.Equal(CoverMode.Braille, CoverArt.CellMode(CoverPlace.Footer));

            CoverArt.Apply(new CoverStylesSettings { SixelFallback = CoverMode.Sixel });
            Assert.Equal(CoverMode.Quadrant, CoverArt.CellMode(CoverPlace.Footer));

            CoverArt.Apply(new CoverStylesSettings { Footer = new CoverPlaceSettings { Style = CoverMode.HalfBlock } });
            Assert.Equal(CoverMode.HalfBlock, CoverArt.CellMode(CoverPlace.Footer));
        }
        finally
        {
            CoverArt.Apply(new CoverStylesSettings());
        }
    }

    [Fact]
    public void A_missing_size_is_medium_and_the_three_sizes_grow()
    {
        try
        {
            CoverArt.Apply(new CoverStylesSettings { Library = new CoverPlaceSettings() });
            Assert.Equal(CoverSize.Medium, CoverArt.LibrarySize);
        }
        finally
        {
            CoverArt.Apply(new CoverStylesSettings());
        }

        Assert.Equal((16, 8), CoverArt.LibraryCells(CoverSize.Small));
        Assert.Equal((24, 12), CoverArt.LibraryCells(CoverSize.Medium));
        Assert.Equal((32, 16), CoverArt.LibraryCells(CoverSize.Large));
        Assert.Equal(CoverSize.Medium, CoverArt.NextSize(CoverSize.Small));
        Assert.Equal(CoverSize.Small, CoverArt.NextSize(CoverSize.Large));
    }

    [Fact]
    public void Cycling_visits_every_style_and_skips_sixel_when_the_terminal_has_none()
    {
        if (SixelCovers.Available)
        {
            return;
        }

        try
        {
            CoverArt.SetStyle(CoverPlace.Library, CoverMode.Braille);
            Assert.Equal(CoverMode.Sextant, CoverArt.NextStyle(CoverPlace.Library));
            CoverArt.SetStyle(CoverPlace.Library, CoverMode.C64Quadrant);
            Assert.Equal(CoverMode.HalfBlock, CoverArt.NextStyle(CoverPlace.Library));
        }
        finally
        {
            CoverArt.Apply(new CoverStylesSettings());
        }
    }
}
