using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Runtime.InteropServices;

namespace VLFLib.Realtime;

public sealed class ToneDetector : IDisposable
{
    // Audio configuration
    private const int RATE = 32_000;
    private const int WINDOW_SAMP = 32; // 1 ms @ 32 kHz
    private const float FREQ = 7_800f;
    private const float POWER_THRESH = 5.0f;

    private readonly Goertzel _go = new(FREQ, RATE, WINDOW_SAMP);

    private readonly bool _isLiveInput;
    private readonly IWaveIn? _capture;
    private readonly string? _wavFile;

    private int _runLen = 0;     // current 1-run length
    private bool _inToneRun = false; // are we inside a 1-run?




    public event Action<char>? DetectionResult;
    public event Action<string>? DebugMessage;

    public ToneDetector(bool liveInput, string? wavFile = null)
    {
        _isLiveInput = liveInput;
        if (_isLiveInput)
        {
            _capture = new WasapiCapture
            {
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(RATE, 1)
            };
            _capture.DataAvailable += (_, a) => Feed(a.Buffer.AsSpan(0, a.BytesRecorded));
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

    private void Feed(ReadOnlySpan<byte> buf)
    {
        while (buf.Length >= WINDOW_SAMP * sizeof(float))
        {
            var frame = MemoryMarshal.Cast<byte, float>(buf[..(WINDOW_SAMP * sizeof(float))]);
            ProcessFrame(frame);
            buf = buf[(WINDOW_SAMP * sizeof(float))..];
        }
    }

    private void ProcessFrame(ReadOnlySpan<float> frame)
    {
        double power = _go.ProcessFrame(frame);
        bool isTone = power > POWER_THRESH;
        DebugMessage?.Invoke($"Power: {power:F3}, IsTone: {isTone}");

        // ---------- run-length tracker ----------
        if (isTone)
        {
            // inside / continuing a 1-run
            _runLen++;
            _inToneRun = true;
            DetectionResult?.Invoke('1');   // still stream the 1/0 for logging
        }
        else
        {
            // hit a 0-frame
            DetectionResult?.Invoke('0');

            if (_inToneRun)
            {
                _runLen = 0;
                _inToneRun = false;
            }
        }
    }


    public void Dispose() 
    {
        _capture?.Dispose();
    }

}
