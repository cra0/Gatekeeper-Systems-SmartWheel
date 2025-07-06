namespace VLFLib.Realtime;

/// <summary>A single-frequency Goertzel detector returning magnitude-squared.</summary>
internal sealed class Goertzel
{
    private readonly double _coeff;
    private readonly int _n;        // expected window length

    public Goertzel(double targetFreq, int sampleRate, int windowSamples)
    {
        _coeff = 2.0 * Math.Cos(2.0 * Math.PI * targetFreq / sampleRate);
        _n = windowSamples;
    }

    /// <summary>
    /// Processes exactly <c>_n</c> float samples and returns magnitude² at the target frequency.
    /// </summary>
    /// <exception cref="ArgumentException">If <paramref name="frame"/> length ≠ <c>_n</c>.</exception>
    public double ProcessFrame(ReadOnlySpan<float> frame)
    {
        if (frame.Length != _n)
            throw new ArgumentException($"Expected {_n} samples, got {frame.Length}", nameof(frame));

        double q0 = 0, q1 = 0, q2 = 0;

        for (int i = 0; i < frame.Length; i++)
        {
            double s = frame[i];            // already −1 … +1
            q0 = _coeff * q1 - q2 + s;
            q2 = q1;
            q1 = q0;
        }

        // power = |G(k)|²
        return q1 * q1 + q2 * q2 - q1 * q2 * _coeff;
    }
}

