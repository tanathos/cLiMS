namespace Clims.Tui.Ui;

/// <summary>Where lyrics lines go on screen: word wrapping, and which rows of the wrapped text are visible.</summary>
public static class LyricsLayout
{
    /// <summary>One screen row of wrapped lyrics.</summary>
    /// <param name="Line">Index of the lyrics line this row belongs to.</param>
    /// <param name="Text">The row's text.</param>
    public readonly record struct Row(int Line, string Text);

    /// <summary>Wraps every line at spaces to <paramref name="width"/> columns (a word longer than that is cut). An empty line stays one empty row.</summary>
    public static List<Row> Rows(IReadOnlyList<string> lines, int width)
    {
        var rows = new List<Row>();
        width = Math.Max(1, width);
        for (var i = 0; i < lines.Count; i++)
        {
            var rest = lines[i].Trim();
            if (rest.Length == 0)
            {
                rows.Add(new Row(i, ""));
                continue;
            }

            while (rest.Length > width)
            {
                var cut = rest.LastIndexOf(' ', width);
                if (cut <= 0)
                {
                    cut = width;
                }

                rows.Add(new Row(i, rest[..cut].TrimEnd()));
                rest = rest[cut..].TrimStart();
            }

            rows.Add(new Row(i, rest));
        }

        return rows;
    }

    /// <summary>Index of the first row of lyrics line <paramref name="line"/>, or 0 when there is none.</summary>
    public static int FirstRowOf(IReadOnlyList<Row> rows, int line)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Line == line)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// The first visible row: <paramref name="focusRow"/> in the middle of <paramref name="height"/> rows (a negative focus
    /// means none: start at the top), then moved by <paramref name="manual"/> rows, and kept inside the text.
    /// </summary>
    public static int Top(int rowCount, int height, int focusRow, int manual)
    {
        var top = (focusRow < 0 ? 0 : focusRow - height / 2) + manual;
        return Math.Clamp(top, 0, Math.Max(0, rowCount - height));
    }
}
