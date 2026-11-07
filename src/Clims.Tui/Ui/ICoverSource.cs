using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Clims.Lms;
using Terminal.Gui.Drawing;

namespace Clims.Tui.Ui;

/// <summary>
/// Supplies cover images to the views. <see cref="TryGet"/> never blocks: it returns what is cached and views ask for
/// the rest with <see cref="Want"/>, then repaint when <see cref="Updated"/> fires.
/// </summary>
public interface ICoverSource
{
    /// <summary>Raised (on any thread) when a requested cover became available.</summary>
    event Action? Updated;

    /// <summary>The cover if it is ready, otherwise null.</summary>
    CoverImage? TryGet(CoverKey key);

    /// <summary>
    /// Declares which covers an owner (a view) shows right now. Covers that nobody wants any more are cancelled if not
    /// yet fetched, so scrolling fast does not build up a queue of requests for albums that are gone.
    /// </summary>
    void Want(object owner, IReadOnlyCollection<CoverKey> keys);
}
