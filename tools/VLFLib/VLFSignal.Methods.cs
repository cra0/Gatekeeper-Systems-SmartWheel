using NAudio.Wave;

namespace VLFLib;

public partial class VLFSignal
{

    public static BlipType DetermineBlipType(int blipLength)
    {
        if (blipLength > thresholdBinaryZero && blipLength >= thresholdBinaryOne && blipLength < thresholdStartEnd)
        {
            return BlipType.BINARY_ONE;
        }
        else if (blipLength >= thresholdBinaryZero &&
                  blipLength < thresholdBinaryOne)
        {
            return BlipType.BINARY_ZERO;
        }
        else
        {
            return BlipType.UNK_MARKER;
        }
    }

    internal static void AddSilence(List<byte> waveform, int sampleRate, float duration)
    {
        int length = (int)(sampleRate * duration);
        for (int i = 0; i < length; i++)
        {
            waveform.Add(128); // Center value for silence in 8-bit PCM
        }
    }

    static void AddSignal(List<byte> waveform, int sampleRate, float duration, float amplitude, double frequency = 5000, bool fadeIn = false, bool fadeOut = false)
    {
        int length = (int)(sampleRate * duration); // Calculate the number of samples for the given duration
        int fadeInLength = fadeIn ? 10 : 0; // Sharper fade-in
        int fadeOutLength = fadeOut ? 10 : 0; // Sharper fade-out

        for (int i = 0; i < length; i++)
        {
            double t = (double)i / sampleRate;
            float currentAmplitude = amplitude;

            if (fadeIn && i < fadeInLength)
            {
                currentAmplitude *= (float)i / fadeInLength; // Gradually increase amplitude
            }
            else if (fadeOut && i >= (length - fadeOutLength))
            {
                currentAmplitude *= (float)(length - i) / fadeOutLength; // Gradually decrease amplitude
            }
            else
            {
                currentAmplitude = amplitude;
            }

            byte value = (byte)(128 + 127 * currentAmplitude * Math.Sin(2 * Math.PI * frequency * t));
            waveform.Add(value);
        }
    }

    internal static void EncodeByteToWaveform(byte data, List<byte> waveform, int sampleRate, float amplitude)
    {
        // Generate the start marker
        AddSignal(waveform, sampleRate, 0.021f, amplitude, frequency: 7000, fadeIn: true, fadeOut: true);
        AddSilence(waveform, sampleRate, 0.0035f);

        for (int i = 7; i >= 0; i--)
        {
            bool bit = (data & (1 << i)) != 0;

            if (bit)
            {
                // Generate 1
                AddSignal(waveform, sampleRate, 0.008f, amplitude, frequency: 7000, fadeIn: true, fadeOut: true);
            }
            else
            {
                // Generate 0
                AddSignal(waveform, sampleRate, 0.004f, amplitude, frequency: 7000, fadeIn: true, fadeOut: true);
            }

            // Add silence between bits
            AddSilence(waveform, sampleRate, 0.004f);
        }

        // Generate the end marker
        AddSignal(waveform, sampleRate, 0.016f, amplitude, frequency: 7000, fadeIn: true, fadeOut: true);
        AddSilence(waveform, sampleRate, 0.004f);
    }

    public static byte[] BytesToVLFSignalWavBytes(byte[] dataSequence, int sampleRate, float amplitude, float silenceLength)
    {
        if (dataSequence == null || dataSequence.Length == 0)
        {
            throw new ArgumentException("Input data sequence array cannot be null or empty.");
        }

        List<byte> waveform = new List<byte>();

        // Add initial silence
        AddSilence(waveform, sampleRate, silenceLength);

        //Encode each byte into waveform
        foreach (byte data in dataSequence)
        {
            EncodeByteToWaveform(data, waveform, sampleRate, amplitude);
        }

        // Add end silence
        AddSilence(waveform, sampleRate, silenceLength);

        return waveform.ToArray();
    }

    public static byte[] BytesToVLFSignalWavBytes(byte[] dataSequence)
    {
        return BytesToVLFSignalWavBytes(dataSequence, 44100, 0.8f, 0.555f);
    }

    public static void SaveVLFWavFile(byte[] dataSequence, int sampleRate, float amplitude, float silenceLength, string filePath)
    {
        if (dataSequence == null || dataSequence.Length == 0)
        {
            throw new ArgumentException("Input data sequence array cannot be null or empty.");
        }

        if (string.IsNullOrEmpty(filePath))
        {
            throw new ArgumentException("File path cannot be null or empty.");
        }

        using (WaveFileWriter writer = new WaveFileWriter(filePath, new WaveFormat(sampleRate, 8, 1)))
        {
            byte[] waveformBytes = BytesToVLFSignalWavBytes(dataSequence, sampleRate, amplitude, silenceLength);
            writer.Write(waveformBytes, 0, waveformBytes.Length);
        }
        return;
    }
}
