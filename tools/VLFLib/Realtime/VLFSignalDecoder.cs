

namespace VLFLib.Realtime;

/// <summary>
/// Decodes a Very Low Frequency (VLF) signal from a ToneDetector's output.
/// The signal format is: Start Marker (21 '1's), 8-bit code (5 '1's for '0', 9 '1's for '1'),
/// each separated by '000', followed by End Marker (17 '1's).
/// </summary>
public sealed class VLFSignalDecoder
{
    private readonly ToneDetector _detector;
    private int _oneCount = 0;
    private int _zeroCount = 0;
    private int _bitCount = 0;

    private enum State { Idle, Decoding, WaitingForEnd }
    private State _currentState = State.Idle;

    // Events for the calling class to subscribe to
    public event Action? StartMarkerDetected;
    public event Action<char>? BitDecoded; // '0' or '1'
    public event Action? EndMarkerDetected;

    /// <summary>
    /// Initializes the decoder with a ToneDetector instance.
    /// </summary>
    /// <param name="detector">The ToneDetector providing the signal.</param>
    public VLFSignalDecoder(ToneDetector detector)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
        //_detector.DetectionResult += OnDetectionResult;
    }

    private void OnDetectionResult(char result)
    {
        if (result == '1')
        {
            _oneCount++;
            _zeroCount = 0;
        }
        else // '0'
        {
            if (_oneCount > 0)
            {
                _zeroCount++;
                if (_zeroCount >= 3)
                {
                    // Token completed with _oneCount '1's
                    ProcessToken(_oneCount);
                    _oneCount = 0;
                    _zeroCount = 0;
                }
            }
            else
            {
                _zeroCount++;
            }
        }
    }

    private void ProcessToken(int count)
    {
        switch (_currentState)
        {
            case State.Idle:
                if (count == 21)
                {
                    StartMarkerDetected?.Invoke();
                    _currentState = State.Decoding;
                    _bitCount = 0;
                }
                break;

            case State.Decoding:
                if (count == 5)
                {
                    BitDecoded?.Invoke('0');
                    _bitCount++;
                }
                else if (count == 9)
                {
                    BitDecoded?.Invoke('1');
                    _bitCount++;
                }
                else
                {
                    // Invalid token length, reset to Idle
                    _currentState = State.Idle;
                    return;
                }

                if (_bitCount == 8)
                {
                    _currentState = State.WaitingForEnd;
                }
                break;

            case State.WaitingForEnd:
                if (count == 17)
                {
                    EndMarkerDetected?.Invoke();
                    _currentState = State.Idle;
                }
                else
                {
                    // Invalid end marker, reset to Idle
                    _currentState = State.Idle;
                }
                break;
        }
    }
}