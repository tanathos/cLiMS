namespace Clims.ChordProbe;

/// <summary>A chord the decoder can name, with the chroma it is expected to give.</summary>
/// <param name="Name">"E5", "F#m", "C", or "N.C.".</param>
/// <param name="Root">Pitch class from C, or -1 for no chord.</param>
/// <param name="Template">Length 1.</param>
internal sealed record Chord(string Name, int Root, float[] Template)
{
    private static readonly string[] Notes = ["C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B"];

    // Where the first six harmonics of a note fall, in semitones above it.
    private static readonly int[] Harmonics = [0, 0, 7, 0, 4, 7];

    /// <summary>No chord, then power chords, major and minor on every root.</summary>
    public static IReadOnlyList<Chord> Vocabulary()
    {
        var chords = new List<Chord> { new("N.C.", -1, new float[12]) };
        foreach (var (suffix, intervals) in new (string, int[])[] { ("5", [0, 7]), ("", [0, 4, 7]), ("m", [0, 3, 7]) })
        {
            for (var root = 0; root < 12; root++)
            {
                var template = new float[12];
                foreach (var interval in intervals)
                {
                    for (var h = 0; h < Harmonics.Length; h++)
                    {
                        template[(root + interval + Harmonics[h]) % 12] += MathF.Pow(0.6f, h);
                    }
                }

                var length = MathF.Sqrt(template.Sum(v => v * v));
                chords.Add(new Chord(Notes[root] + suffix, root, [.. template.Select(v => v / length)]));
            }
        }

        return chords;
    }
}
