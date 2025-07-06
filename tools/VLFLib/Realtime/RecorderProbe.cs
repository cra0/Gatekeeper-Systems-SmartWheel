using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VLFLib.Realtime;

/// <summary>
/// Utility: record a short snippet from any WaveIn device and save as WAV.
/// </summary>
public static class RecorderProbe
{
    /// <param name="path">e.g. "sample.wav"</param>
    /// <param name="deviceIndex">
    ///   Same numbering as WaveInEvent.DeviceCount / GetCapabilities(i).
    ///   Pass -1 to search for the default microphone.
    /// </param>
    /// <param name="seconds">length of snippet to capture</param>
    public static async Task CaptureAsync(
        string path,
        int deviceIndex = 0,
        int seconds = 3,
        int sampleRate = 44100)
    {
        if (seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (deviceIndex < -1 || deviceIndex >= WaveInEvent.DeviceCount)
            throw new ArgumentOutOfRangeException(nameof(deviceIndex));

        // 1. Pick device
        int dev = deviceIndex;
        if (dev == -1) dev = 0; // fallback to first if "default" not found

        var fmt = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1); // 32-bit float mono
        using var waveIn = new WaveInEvent
        {
            DeviceNumber = dev,
            WaveFormat = fmt,
            BufferMilliseconds = 50
        };

        // 2. Prepare output file
        using var writer = new WaveFileWriter(path, fmt);

        // 3. Wire the callback
        waveIn.DataAvailable += (_, e) => writer.Write(e.Buffer, 0, e.BytesRecorded);

        // 4. Record for the requested duration
        waveIn.StartRecording();
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        waveIn.StopRecording();   // flushes

        // 5. Dispose closes both waveIn and writer
        Console.WriteLine($"Saved {seconds}-second capture to '{Path.GetFullPath(path)}'");
    }
}