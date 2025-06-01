using NAudio.Wave;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;

namespace VLFLib;

[SupportedOSPlatform("windows")]
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
            OnSignalError?.Invoke(this, new InvalidOperationException("No samples available to render."));
            return false;
        }

        // Visualization parameters
        Bitmap bitmap = new Bitmap(imageWidth, imageHeight);
        Graphics g = Graphics.FromImage(bitmap);
        g.Clear(Color.White);

        // Plot waveform
        float midHeight = imageHeight / 2f;
        float scaleX = imageWidth / (float)_samples.Count;
        float scaleY = imageHeight / 256f;
        Pen pen = new Pen(Color.Blue, 1);

        for (int i = 1; i < _samples.Count; i++)
        {
            float x1 = (i - 1) * scaleX;
            float y1 = midHeight - _samples[i - 1] * scaleY;
            float x2 = i * scaleX;
            float y2 = midHeight - _samples[i] * scaleY;
            g.DrawLine(pen, x1, y1, x2, y2);
        }

        // Draw start and end markers
        if (_startMarkers.Count > 0 && _endMarkers.Count > 0)
        {
            for (int i = 0; i < _startMarkers.Count && i < _endMarkers.Count; i++)
            {
                // Draw start marker
                float startX = _startMarkers[i] * scaleX;
                g.DrawLine(Pens.Red, startX, 0, startX, imageHeight);

                // Draw end marker
                float endX = _endMarkers[i] * scaleX;
                g.DrawLine(Pens.Green, endX, 0, endX, imageHeight);

                g.DrawLine(Pens.Purple, startX, (imageHeight / 2), endX, (imageHeight / 2));

                // Draw 1 or 0
                int blipLength = _endMarkers[i] - _startMarkers[i];
                var blipType = DetermineBlipType(blipLength);

                Font font = new Font("Arial", 12);
                Brush brush = Brushes.Black;

                if (blipType == BlipType.BINARY_ONE)
                    g.DrawString("1", font, brush, startX + ((endX - startX) / 2) - 5, (imageHeight / 2) + 2);
                else if (blipType == BlipType.BINARY_ZERO)
                    g.DrawString("0", font, brush, startX + ((endX - startX) / 2) - 5, (imageHeight / 2) + 2);

                ConsolePrint("High amplitude segment detected. StartMarker: {0}, EndMarker: {1}, Segment length: {2}", 
                    _startMarkers[i], _endMarkers[i], blipLength);
            }
        }
        else
        {
            ConsolePrint("No valid high amplitude segments detected!");
        }

        // Save the image
        if (File.Exists(filePath))
            File.Delete(filePath);
        
        bitmap.Save(filePath, ImageFormat.Png);
        ConsolePrint("Waveform visualization saved to {0}", filePath);
        return true;
    }

    /// <summary>
    /// Writes the collected samples to a specified file.
    /// </summary>
    /// <param name="filePath">The path of the file where the samples will be written. Must be a valid file path.</param>
    /// <returns><see langword="true"/> if the samples were successfully written to the file; otherwise, <see langword="false"/>
    /// if an error occurred or no samples are available. </returns>
    public bool DumpSamplesToFile(string filePath)
    {
        if (_samples.Count == 0)
        {
            OnSignalError?.Invoke(this, new InvalidOperationException("No samples available to dump."));
            return false;
        }
        try
        {
            using (StreamWriter sw = new StreamWriter(filePath, false))
            {
                for (int i = 0; i < _samples.Count; i++)
                {
                    sw.WriteLine($"Sample {i}: {_samples[i]}");  // Already centralized around zero
                }
            }
            ConsolePrint("Samples dumped to {0}", filePath);
            return true;
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

}