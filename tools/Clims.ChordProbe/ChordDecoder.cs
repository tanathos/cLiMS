namespace Clims.ChordProbe;

/// <summary>
/// Picks the chord of every frame. Each chord is scored on how like its template the chroma is and on whether its root is in the
/// bass; changing chord costs something, so a passing note does not count. Offline, the best path over the whole track is taken
/// (Viterbi). Causal, only what has been heard so far counts, as with a live feed (the chroma still looks a little ahead).
/// </summary>
internal static class ChordDecoder
{
    private const float Sharpness = 10f;
    private const float BassWeight = 0.15f;
    private const float NoChordScore = 0.60f;
    private const float QuietDb = -50f;

    public static int[] Decode(IReadOnlyList<ChromaFrame> frames, IReadOnlyList<Chord> chords, float switchCost, bool causal)
    {
        var score = new float[chords.Count];
        var from = new int[frames.Count, chords.Count];
        var path = new int[frames.Count];
        for (var t = 0; t < frames.Count; t++)
        {
            var jump = score.Max() - switchCost;
            var best = Array.IndexOf(score, score.Max());
            for (var c = 0; c < chords.Count; c++)
            {
                from[t, c] = t == 0 || score[c] >= jump ? c : best;
                score[c] = (t == 0 ? 0 : Math.Max(score[c], jump)) + Sharpness * Score(frames[t], chords[c]);
            }

            var top = score.Max();
            for (var c = 0; c < chords.Count; c++)
            {
                score[c] -= top;
            }

            path[t] = Array.IndexOf(score, 0f);
        }

        if (!causal)
        {
            for (var t = frames.Count - 2; t >= 0; t--)
            {
                path[t] = from[t + 1, path[t + 1]];
            }
        }

        return path;
    }

    private static float Score(ChromaFrame frame, Chord chord)
    {
        if (frame.Level < QuietDb)
        {
            return chord.Root < 0 ? 2f : 0f;
        }

        if (chord.Root < 0)
        {
            return NoChordScore;
        }

        var like = 0f;
        for (var i = 0; i < 12; i++)
        {
            like += frame.Treble[i] * chord.Template[i];
        }

        return like + BassWeight * frame.Bass[chord.Root];
    }
}
