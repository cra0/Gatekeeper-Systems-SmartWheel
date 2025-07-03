using NAudio.Wave;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;


namespace VLFLib;

public partial class VLFSignal : IVLFSignal
{
    public event IVLFSignal.VLFEvent? OnSignalParseStarted;
    public event IVLFSignal.VLFResultEvent? OnSignalParseCompleted;
    public event IVLFSignal.VLFExceptionEvent? OnSignalError;
    public event IVLFSignal.VLFConsolePrintEvent? OnSignalConsolePrint;


    public byte[] Data
    {
        get { return _vlfdata.ToArray(); }
    }

    private byte[] _buffer;
    private List<int> _samples;
    private List<int> _startMarkers;
    private List<int> _endMarkers;

    private List<BlipType> _blips;
    private List<byte> _vlfdata;

    // Threshold defines
    private const int highThreshold = 42; // Threshold for high amplitude
    private const int lowThreshold = 10; // Threshold for low amplitude
    private const int lowThresholdCountMax = 4; // Maximum count for low threshold to consider it stable

    // Marker defines
    private const int thresholdStartEnd = 550; //old: 700
    private const int thresholdBinaryZero = 140; //old: 170 old: 180
    private const int thresholdBinaryOne = 289; //old: 349 old 350

    //Visual image parameters
    private const int imageWidth = 9900;
    private const int imageHeight = 400;


    public VLFSignal()
    {
        _buffer = Array.Empty<byte>();
        _samples = new List<int>();
        _startMarkers = new List<int>();
        _endMarkers = new List<int>();
        _blips = new List<BlipType>();
        _vlfdata = new List<byte>();
    }

    /// <summary>
    /// Parses a WAV file to extract signal data.
    /// </summary>
    /// <remarks>If the specified file does not exist, the <see cref="OnSignalError"/> event is raised with a
    /// <see cref="FileNotFoundException"/> detailing the missing file.</remarks>
    /// <param name="wavfilePath">The full path to the WAV file to be parsed. Must be a valid file path.</param>
    /// <returns><see langword="true"/> if the WAV file was successfully parsed; otherwise, <see langword="false"/>.</returns>
    public bool ParseWavSignal(string wavfilePath)
    {
        if (File.Exists(wavfilePath) == false)
        {
            OnSignalError?.Invoke(this, new FileNotFoundException($"WAV file not found: {wavfilePath}"));
            return false;
        }
        return InternalParseWavFromFile(wavfilePath);
    }

    /// <summary>
    /// Parses the provided VLF data sequence and determines its validity.
    /// </summary>
    /// <param name="dataSequence">The byte array representing the VLF data sequence to be parsed. Cannot be null.</param>
    /// <returns><see langword="true"/> if the data sequence is successfully parsed and determined to be valid;  otherwise, <see
    /// langword="false"/>.</returns>
    public bool ParseFromVLFDataSequence(byte[] dataSequence)
    {
        return InternalParseDataSequence(dataSequence);
    }

    /// <summary>
    /// Renders a visual representation of the waveform data to a file.
    /// </summary>
    /// <remarks>This method generates a PNG image that visually represents the waveform data stored in the
    /// current instance. The visualization includes the waveform plot, start and end markers for high amplitude
    /// segments, and binary annotations (e.g., "1" or "0") for detected segments. If no samples are available, the
    /// method will invoke the <c>OnSignalError</c> event and return <see langword="false"/>.  The caller is responsible
    /// for ensuring that the <paramref name="filePath"/> is valid and writable. If the file already exists, it will be
    /// overwritten.</remarks>
    /// <param name="filePath">The path to the file where the waveform visualization will be saved. Must be a valid file path.</param>
    /// <returns><see langword="true"/> if the visualization was successfully rendered and saved; otherwise, <see
    /// langword="false"/>.</returns>
    public bool RenderWavVisualToFile(string filePath)
    {
        if (_samples.Count == 0)
        {
            OnSignalError?.Invoke(this,
                new InvalidOperationException("No samples available to render."));
            return false;
        }

        using Image<Rgba32> img = new(imageWidth, imageHeight, Color.White);

        float midY = imageHeight / 2f;
        float sx = imageWidth / (float)_samples.Count;
        float sy = imageHeight / 256f;

        var pts = new PointF[_samples.Count];
        for (int i = 0; i < _samples.Count; i++)
            pts[i] = new PointF(i * sx, midY - _samples[i] * sy);

        img.Mutate(ctx => ctx.DrawLine(Color.Blue, 1f, pts));

        var font = Utilities.ResolveFont("Arial", 12);
        var red = Pens.Solid(Color.Red, 1f);
        var green = Pens.Solid(Color.Green, 1f);
        var purple = Pens.Solid(Color.Purple, 1f);

        for (int i = 0; i < _startMarkers.Count && i < _endMarkers.Count; i++)
        {
            float x1 = _startMarkers[i] * sx;
            float x2 = _endMarkers[i] * sx;

            img.Mutate(ctx =>
            {
                ctx.DrawLine(red, new PointF(x1, 0), new PointF(x1, imageHeight));
                ctx.DrawLine(green, new PointF(x2, 0), new PointF(x2, imageHeight));
                ctx.DrawLine(purple, new PointF(x1, midY), new PointF(x2, midY));
            });

            int len = _endMarkers[i] - _startMarkers[i];
            var type = DetermineBlipType(len);

            if (type is BlipType.BINARY_ONE or BlipType.BINARY_ZERO)
            {
                string digit = type == BlipType.BINARY_ONE ? "1" : "0";
                float tx = x1 + (x2 - x1) / 2 - 5;
                float ty = midY + 2;

                img.Mutate(ctx => ctx.DrawText(digit, font, Color.Black, new PointF(tx, ty)));
            }

            ConsolePrint("High amplitude segment detected. StartMarker: {0}, EndMarker: {1}, Segment length: {2}",
                         _startMarkers[i], _endMarkers[i], len);
        }

        filePath = Utilities.NormalizeOutputPath(filePath);
        img.SaveAsPng(filePath);
        ConsolePrint("Waveform visualization saved to {0}", filePath);
        return true;
    }

    /// <summary>
    /// Writes the collected samples to a specified stream.
    /// </summary>
    /// <param name="outputStream">The stream to write to.</param>
    /// <returns><see langword="true"/> if the samples were successfully written to the stream; otherwise, <see langword="false"/>
    /// if an error occurred or no samples are available. </returns>
    public bool DumpSamplesToStream(Stream outputStream)
    {
        if (_samples.Count == 0)
        {
            OnSignalError?.Invoke(this, new InvalidOperationException("No samples available to dump."));
            return false;
        }
        if (outputStream == null || !outputStream.CanWrite)
        {
            OnSignalError?.Invoke(this, new ArgumentException("Output stream is null or not writable."));
            return false;
        }
        try
        {
            using (var sw = new StreamWriter(outputStream, leaveOpen: true))
            {
                for (int i = 0; i < _samples.Count; i++)
                {
                    sw.WriteLine($"Sample {i}: {_samples[i]}");
                }
                sw.Flush();
            }
            ConsolePrint("Samples dumped to provided stream.");
            return true;
        }
        catch (Exception ex)
        {
            OnSignalError?.Invoke(this, ex);
            return false;
        }
    }

    /// <summary>
    /// Dumps the collected samples to a specified file.
    /// </summary>
    /// <param name="filePath"></param>
    /// <returns></returns>
    public bool DumpSamplesToFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            OnSignalError?.Invoke(this, new ArgumentException("File path is null or empty."));
            return false;
        }
        try
        {
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                return DumpSamplesToStream(fs);
            }
        }
        catch (Exception ex)
        {
            OnSignalError?.Invoke(this, ex);
            return false;
        }
    }

    /// <summary>
    /// Save the raw VLF data to a file.
    /// </summary>
    /// <param name="filePath">Output file path.</param>
    /// <returns></returns>
    public bool SaveDataToFile(string filePath)
    {
        if (_vlfdata.Count == 0)
        {
            OnSignalError?.Invoke(this, new InvalidOperationException("No VLF data available to save."));
            return false;
        }
        try
        {
            using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                fs.Write(_vlfdata.ToArray(), 0, _vlfdata.Count);
            }
            ConsolePrint("VLF data saved to {0}", filePath);
            return true;
        }
        catch (Exception ex)
        {
            OnSignalError?.Invoke(this, ex);
            return false;
        }
    }

    /// <summary>
    /// Saves the current VLFSignal's Data as a VLF WAV file.
    /// </summary>
    /// <param name="filePath">Output file path.</param>
    /// <param name="sampleRate">Sample rate for the WAV file (default: 44100).</param>
    /// <returns>True if successful, false otherwise.</returns>
    public bool ToWavFile(string filePath, int sampleRate = 44100)
    {
        if (_buffer == null || _buffer.Length == 0)
        {
            OnSignalError?.Invoke(this, new InvalidOperationException("No data available to save as WAV."));
            return false;
        }
        try
        {
            using (WaveFileWriter wfw = new WaveFileWriter(filePath, new WaveFormat(sampleRate, 8, 1)))
            {
                wfw.Write(_buffer, 0, _buffer.Length);
            }
            ConsolePrint("VLF signal saved as WAV to {0}", filePath);
            return true;
        }
        catch (Exception ex)
        {
            OnSignalError?.Invoke(this, ex);
            return false;
        }
    }

    private void FlushInternalBuffers()
    {
        _buffer = Array.Empty<byte>();
        _samples = new List<int>();
        _startMarkers = new List<int>();
        _endMarkers = new List<int>();
        _blips = new List<BlipType>();
        _vlfdata = new List<byte>();
    }

    private bool InternalParseWavFromFile(string inputWavFilePath)
    {
        OnSignalParseStarted?.Invoke();
        using (var reader = new WaveFileReader(inputWavFilePath))
        {
            if (reader.WaveFormat.BitsPerSample != 8 || reader.WaveFormat.Channels != 1)
            {
                OnSignalError?.Invoke(this, new InvalidOperationException("Unsupported WAV format. Only 8-bit mono WAV files are supported."));
                return false;
            }
            return InternalParseWav(reader);
        }
    }

    private bool InternalParseWav(WaveFileReader reader)
    {
        int sampleRate = reader.WaveFormat.SampleRate;
        int bytesPerSample = reader.WaveFormat.BitsPerSample / 8;
        int channels = reader.WaveFormat.Channels;
        int blockAlign = reader.WaveFormat.BlockAlign;

        //Read the entire WAV file into a byte array
        _buffer = new byte[reader.Length];
        reader.Read(_buffer, 0, _buffer.Length);

        // Create samples list
        _samples = new List<int>();
        for (int i = 0; i < _buffer.Length; i += blockAlign)
        {
            int rawSample = _buffer[i] - 128;  // Centralize around zero
            _samples.Add(rawSample);
        }

        // Detect start and end markers of high amplitude segments
        _startMarkers = new List<int>();
        _endMarkers = new List<int>();
        int lowThresholdCount = 0; // Counter for low threshold
        bool isInsideSegment = false;

        for (int i = 0; i < _samples.Count; i++)
        {
            if (Math.Abs(_samples[i]) > highThreshold && !isInsideSegment)
            {
                _startMarkers.Add(i);
                isInsideSegment = true;
                ConsolePrint("Start marker detected at sample {0}", i);
            }

            if (isInsideSegment)
            {
                if (Math.Abs(_samples[i]) <= lowThreshold)
                {
                    lowThresholdCount++;
                    if (lowThresholdCount > lowThresholdCountMax) // Ensure stable low threshold detection
                    {
                        _endMarkers.Add(i);
                        isInsideSegment = false;
                        ConsolePrint("End marker detected at sample {0}", i);
                    }
                }
                else
                {
                    lowThresholdCount = 0; // Reset count if the sample exceeds the low threshold
                }
            }
        }

        // Sanity check for markers
        if (_startMarkers.Count != _endMarkers.Count)
        {
            ConsolePrint("Data integrity error! Start markers count: {0} != End markers count: {1}", _startMarkers.Count, _endMarkers.Count);
            OnSignalError?.Invoke(this, new InvalidOperationException($"Data integrity error! Start markers count: {_startMarkers.Count} != End markers count: {_endMarkers.Count}"));
            return false;
        }

        if (_startMarkers.Count > 0 && _endMarkers.Count > 0)
        {
            _blips = new List<BlipType>();
            bool isProcessingPacket = false;

            for (int i = 0; i < _startMarkers.Count && i < _endMarkers.Count; i++)
            {
                int currentStartMarker = _startMarkers[i];
                int currentEndMarker = _endMarkers[i];

                int blipLength = (currentEndMarker - currentStartMarker);

                if ((blipLength >= thresholdStartEnd) && !isProcessingPacket)
                {
                    isProcessingPacket = true;
                    _blips.Add(BlipType.START_MARKER);
                }
                else if ((blipLength >= thresholdStartEnd) && isProcessingPacket)
                {
                    isProcessingPacket = false;
                    _blips.Add(BlipType.END_MARKER);
                }
                else if ((blipLength >= thresholdBinaryZero &&
                            blipLength < thresholdBinaryOne) && isProcessingPacket)
                {
                    _blips.Add(BlipType.BINARY_ZERO);
                }
                else if ((blipLength >= thresholdBinaryOne) && isProcessingPacket)
                {
                    _blips.Add(BlipType.BINARY_ONE);
                }
            }

            // Decode binary data and write to file
            for (int i = 0; i < _blips.Count; i++)
            {
                if (_blips[i] == BlipType.START_MARKER)
                {
                    int byteValue = 0;
                    for (int j = 0; j < 8 && i + 1 + j < _blips.Count; j++)
                    {
                        if (_blips[i + 1 + j] == BlipType.BINARY_ONE)
                        {
                            byteValue |= (1 << (7 - j));
                        }
                    }
                    _vlfdata.Add((byte)byteValue);
                    i += 8; // Move to next set of bits
                }
            }


            var expectedDataCount = (_startMarkers.Count() / 10);
            if (_vlfdata.Count == expectedDataCount)
            {
                OnSignalParseCompleted?.Invoke(this, true);
                return true;
            }
            else
            {
                ConsolePrint($"Data decode Error! Expecting: {expectedDataCount} got {_vlfdata.Count}");
                OnSignalError?.Invoke(this,
                    new InvalidOperationException($"Data decode Error! Expecting: " +
                    $"{expectedDataCount} got {_vlfdata.Count}"));
                return false;
            }
        }
        else
        {
            ConsolePrint("Data integrity error! No suitable markers to process.");
            OnSignalError?.Invoke(this, new InvalidOperationException("Data integrity error! No suitable markers to process."));
            return false;
        }     
    }

    private bool InternalParseDataSequence(byte[] dataSequence)
    {
        if (dataSequence == null || dataSequence.Length == 0)
        {
            OnSignalError?.Invoke(this, new ArgumentException("Data sequence is null or empty."));
            return false;
        }
        FlushInternalBuffers();

        int sampleRate = 44100; // Default sample rate
        float amplitude = 0.8f; // Default amplitude
        float silenceLength = 0.555f; // Default silence length

        // Create WAV data with proper headers using WaveFileWriter
        byte[] wavFileContent;
        using (var ms = new MemoryStream())
        {
            using (var writer = new WaveFileWriter(ms, new WaveFormat(sampleRate, 8, 1)))
            {
                var waveformBytes = BytesToVLFSignalWavBytes(dataSequence, sampleRate, amplitude, silenceLength);
                writer.Write(waveformBytes, 0, waveformBytes.Length);
            }
            wavFileContent = ms.ToArray();
        }

        // Parse the properly formatted WAV data this is pretty lazy, but it works
        using (var wavMs = new MemoryStream(wavFileContent))
        {
            return InternalParseWav(new WaveFileReader(wavMs));
        }
    }

    private void ConsolePrint(string message, params object[] args)
    {
        OnSignalConsolePrint?.Invoke(string.Format(message, args));
    }

    /// <summary>
    /// Saves the current VLFSignal's Data as a VLF WAV file to a provided stream.
    /// </summary>
    /// <param name="outputStream">The stream to write the WAV data to. Must be writable and seekable.</param>
    /// <param name="sampleRate">Sample rate for the WAV file (default: 44100).</param>
    /// <returns>True if successful, false otherwise.</returns>
    public bool ToWavStream(Stream outputStream, int sampleRate = 44100)
    {
        if (_buffer == null || _buffer.Length == 0)
        {
            OnSignalError?.Invoke(this, new InvalidOperationException("No data available to save as WAV."));
            return false;
        }
        if (outputStream == null || !outputStream.CanWrite)
        {
            OnSignalError?.Invoke(this, new ArgumentException("Output stream is null or not writable."));
            return false;
        }
        try
        {
            using (var wfw = new WaveFileWriter(outputStream, new WaveFormat(sampleRate, 8, 1)))
            {
                wfw.Write(_buffer, 0, _buffer.Length);
            }
            ConsolePrint("VLF signal written as WAV to provided stream.");
            return true;
        }
        catch (Exception ex)
        {
            OnSignalError?.Invoke(this, ex);
            return false;
        }
    }

    /// <summary>
    /// Saves the current VLFSignal's Data as a VLF WAV file and returns the WAV as a byte array.
    /// </summary>
    /// <param name="sampleRate">Sample rate for the WAV file (default: 44100).</param>
    /// <returns>WAV file as byte array, or null if failed.</returns>
    public byte[]? ToWavBytes(int sampleRate = 44100)
    {
        if (_buffer == null || _buffer.Length == 0)
        {
            OnSignalError?.Invoke(this, new InvalidOperationException("No data available to save as WAV."));
            return null;
        }
        try
        {
            using (var ms = new MemoryStream())
            {
                if (!ToWavStream(ms, sampleRate))
                    return null;
                ConsolePrint("VLF signal exported as WAV byte array.");
                return ms.ToArray();
            }
        }
        catch (Exception ex)
        {
            OnSignalError?.Invoke(this, ex);
            return null;
        }
    }
}