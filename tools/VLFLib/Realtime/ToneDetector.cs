using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace VLFLib.Realtime;

public sealed class ToneDetector : IDisposable
{
    // Audio configuration
    private const int RATE = 32_000;
    private const int WINDOW_SAMP = 32; // 1 ms @ 32 kHz
    private const float FREQ = 7_800f;
    private const int TONE_STREAK_LEN = 2; // 2 frames of confirmation

    private const int CALIB_FRAMES = 5;
    private int _calibCnt = 0;
    private double _noiseSum = 0;
    private double _threshold = 0.02;      // fallback

    private readonly Goertzel _go = new(FREQ, RATE, WINDOW_SAMP);
    private WasapiCapture? _capture;


    private readonly MMDevice? _device;
    private readonly bool _isLiveInput;
    private readonly string? _wavFile;

    private int _runLen = 0;     // current 1-run length
    private bool _inToneRun = false; // are we inside a 1-run?

    // confirmation state
    private int _toneStreak = 0;

    public event Action<char>? OnBit;
    public event Action<string>? OnDetectionEventMessage;


    /// <param name="device">Optional explicit MMDevice (mic, loop-back, VAC…)</param>
    public ToneDetector(bool liveInput, string? wavFile = null, MMDevice? device = null)
    {
        _isLiveInput = liveInput;
        _device = device;

        if (_isLiveInput)
        {
            // loopback from default render device (speakers / stereo mix)
            var render = new MMDeviceEnumerator()
                             .GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
            _capture = new WasapiCapture(render);
            _capture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(RATE, 1);

            _capture.DataAvailable += (object? sender, WaveInEventArgs e) =>
            {
                // reinterpret the incoming bytes as floats
                var floats = MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded));

                // walk through it in WINDOW_SAMP‐sized hops:
                for (int offset = 0; offset + WINDOW_SAMP <= floats.Length; offset += WINDOW_SAMP)
                {
                    // take one 1 ms block
                    var block = floats.Slice(offset, WINDOW_SAMP);

                    // ─── 1) per-block normalization ──────────────────
                    float maxAmp = 0f;
                    for (int i = 0; i < WINDOW_SAMP; i++)
                        maxAmp = Math.Max(maxAmp, Math.Abs(block[i]));
                    if (maxAmp > 0f)
                    {
                        float inv = 1f / maxAmp;
                        for (int i = 0; i < WINDOW_SAMP; i++)
                            block[i] *= inv;
                    }

                    // ─── 2) now your existing detector sees a full-scale ±1 block
                    ProcessFrame(block);
                }
            };

        }
        else
        {
            _wavFile = wavFile ?? throw new ArgumentNullException(nameof(wavFile));
        }
    }

    public void Start()
    {
        if (_isLiveInput)
        {
            _capture?.StartRecording();
        }
        else
        {
            float[] mono = WavUtils.LoadAndResample(_wavFile!, RATE);
            for (int i = 0; i + WINDOW_SAMP <= mono.Length; i += WINDOW_SAMP)
                ProcessFrame(mono.AsSpan(i, WINDOW_SAMP));
        }
    }

    public void Stop() => _capture?.StopRecording();

    private void ProcessFrame(ReadOnlySpan<float> frame)
    {
        double power = _go.ProcessFrame(frame);

        if (_calibCnt < CALIB_FRAMES)
        {
            _noiseSum += power;
            _calibCnt++;
            if (_calibCnt == CALIB_FRAMES)
                _threshold = 3 * (_noiseSum / CALIB_FRAMES);   // 3× noise floor
        }

        bool rawTone = power > _threshold;

        // 2-frame confirmation: need two consecutive tone frames
        if (rawTone) 
            _toneStreak++; 
        else 
            _toneStreak = 0;

        bool tone = _toneStreak >= TONE_STREAK_LEN;

        OnDetectionEventMessage?.Invoke($"Power: {power:F3}, IsTone: {tone}");

        // ---------- run-length tracker ----------
        if (tone)
        {
            _runLen++;
            _inToneRun = true;
        }
        else
        {
            if (_inToneRun)
            {
                // finished a burst → send to decoder
                OnBit?.Invoke('1');    
                
                for (int i = 1; i < _runLen; i++) 
                    OnBit?.Invoke('1');

                _runLen = 0;
                _inToneRun = false;
            }
            OnBit?.Invoke('0');
        }
    }


    public void Dispose() 
    {
        _capture?.Dispose();
    }

}
public static class Extensions
{
    /// <summary>
    /// Extension method to simplify chaining operations on objects.
    /// </summary>
    /// <typeparam name="T">The type of the object.</typeparam>
    /// <typeparam name="R">The type of the result.</typeparam>
    /// <param name="obj">The object to operate on.</param>
    /// <param name="func">The function to apply to the object.</param>
    /// <returns>The result of applying the function to the object.</returns>
    public static R Let<T, R>(this T obj, Func<T, R> func)
    {
        return func(obj);
    }
}
