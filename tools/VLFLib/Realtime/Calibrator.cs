using System;
using System.Collections.Generic;

namespace VLFLib.Realtime;

/// <summary>
/// Sliding-window median estimator.
/// Threshold = _factor × median(power over last _window frames).
/// </summary>
public sealed class Calibrator
{
    private readonly Queue<double> _buf;
    private readonly int _window;
    private readonly double _factor;
    private double _threshold = 1e-6;

    public Calibrator(int windowFrames = 250, double threshFactor = 2.0)
    {
        _window = windowFrames;      // e.g. 250 frames ≈ 250 ms at 1 ms/frame
        _factor = threshFactor;      // tune: 1.5–2.0 for quiet WAV, 2.5–3.0 live
        _buf = new Queue<double>(_window + 1);
    }

    /// <summary>Add one power measurement and update the threshold.</summary>
    public void Update(double p)
    {
        _buf.Enqueue(p);
        if (_buf.Count > _window) _buf.Dequeue();

        if (_buf.Count == _window)
        {
            var arr = _buf.ToArray();
            Array.Sort(arr);                 // _window is small → fast enough
            double median = arr[_window / 2];
            _threshold = _factor * median;
        }
    }

    /// <summary>True when the sliding window is fully populated.</summary>
    public bool IsReady => _buf.Count == _window;

    /// <summary>Current decision threshold.</summary>
    public double GetThreshold() => _threshold;
}
