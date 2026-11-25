namespace Clims.Visualizer;

/// <summary>A live feed of spectrum bands for what is being played right now.</summary>
public interface IVisualSource
{
    /// <summary>Number of bands in <see cref="TakeBands"/>.</summary>
    int Bands { get; }

    VisualState State { get; }

    /// <summary>
    /// Fills <paramref name="bands"/> (length <see cref="Bands"/>, each 0 to 1) with the loudest value of each band since the
    /// last call, so a beat between two repaints is not lost. False, leaving the array alone, unless <see cref="State"/> is
    /// <see cref="VisualState.Live"/>.
    /// </summary>
    bool TakeBands(float[] bands);

    /// <summary>
    /// Fills <paramref name="chroma"/> (length <see cref="Chromagram.Notes"/>, from <see cref="Chromagram.FirstMidi"/>, each 0 to 1) with the loudest value of each
    /// note since the last call. False, leaving the array alone, unless <see cref="State"/> is <see cref="VisualState.Live"/>.
    /// </summary>
    bool TakeChroma(float[] chroma);

    /// <summary>
    /// The strength (above 0, up to 1) of the strongest drum hit since the last call, or 0 when there was none or
    /// <see cref="State"/> is not <see cref="VisualState.Live"/>.
    /// </summary>
    float TakeBeat();
}
