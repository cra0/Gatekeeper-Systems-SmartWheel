using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace VLFLib.Realtime;

public static class WavUtils
{

    public static void RecordWav(string outFile, int seconds = 5, int? deviceIndex = null)
    {
        WasapiCapture capture;
        if (deviceIndex.HasValue)
        {
            var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            if (deviceIndex.Value < 0 || deviceIndex.Value >= devices.Count)
                throw new ArgumentOutOfRangeException(nameof(deviceIndex), "Invalid device index.");
            capture = new WasapiCapture(devices[deviceIndex.Value]);
        }
        else
        {
            capture = new WasapiCapture(); // default input device
        }

        var writer = new WaveFileWriter(outFile, capture.WaveFormat);

        capture.DataAvailable += (s, a) =>
        {
            writer.Write(a.Buffer, 0, a.BytesRecorded);
        };

        capture.RecordingStopped += (s, a) =>
        {
            writer.Dispose();
            capture.Dispose();
        };

        capture.StartRecording();
        Thread.Sleep(seconds * 1000);
        capture.StopRecording();
    }

    /// <summary>
    /// Loads a WAV file, converts it to mono, resamples to the target sample rate, and returns the audio as a float array.
    /// </summary>
    /// <param name="path">Path to the WAV file.</param>
    /// <param name="targetFs">Target sample rate in Hz (default: 32,000).</param>
    /// <returns>Mono audio samples as a float array.</returns>
    public static float[] LoadAndResample(string path, int targetFs = 32_000)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be null or empty.", nameof(path));
        if (targetFs <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetFs));

        using var reader = new WaveFileReader(path);

        ISampleProvider src = reader.ToSampleProvider();
        if (reader.WaveFormat.Channels > 1)
            src = new StereoToMonoSampleProvider(src);

        if (src.WaveFormat.SampleRate != targetFs)
            src = new WdlResamplingSampleProvider(src, targetFs);

        int estimated = (int)(reader.Length / reader.BlockAlign *
                              (targetFs / (double)reader.WaveFormat.SampleRate));
        var buffer = new List<float>(estimated);

        float[] tmp = new float[4096];
        int read;
        while ((read = src.Read(tmp, 0, tmp.Length)) > 0)
            buffer.AddRange(tmp.AsSpan(0, read));

        return buffer.ToArray();
    }


}
