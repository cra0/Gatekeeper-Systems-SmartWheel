using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Text;
using VLFLib;
using VLFLib.Realtime;

namespace VLFSignalTool
{
    internal class Program
    {

        static int Main(string[] args)
        {
            if (args.Length > 3)
            {
                PrintUsage();
                return 1;
            }

            string mode = args[0].ToLowerInvariant();
            string inputPath = string.Empty;

            if (mode == "-realtime")
            {
                var decoder = new ToneDecoder();
                decoder.OnByteDecoded += b =>
                {
                    TimeSpan ts = TimeSpan.FromMilliseconds(Environment.TickCount);
                    Console.WriteLine($"[{ts:mm\\:ss\\.fff}] Decoded byte: 0x{b:X2}");
                };
#if tst
                // QuickSampleDump.RecordWav(outFile: "sample.wav");
                //Console.WriteLine("Done.");
                // Console.ReadKey();
                //Console.WriteLine("Loading ToneDetector..");
                var detector = new ToneDetector(false, "sample_gtp6.wav");
                var decoder = new ToneDecoder();

                detector.OnDetectionEventMessage += msg =>
                {
                    //Console.WriteLine($"Debug: {msg}");
                };

                detector.OnBit += decoder.FeedBit;
                decoder.OnByteDecoded += b =>
                {
                    Console.WriteLine($"0x{b:X2}");
                };

                detector.Start();

                Console.WriteLine("Done. Moving on.");
                Console.ReadKey();

                detector.Stop();

#endif

                var devices = WavUtils.ListInputDevices();
                for (int i = 0; i < devices.Count; i++)
                {
                    Console.WriteLine($"{i}: {devices[i].FriendlyName}");
                }

                Console.WriteLine("Select a device index to start real-time detection (or -1 for default):");
                int deviceIndex;
                while (true)
                {
                    if (int.TryParse(Console.ReadLine(), out deviceIndex) && 
                        (deviceIndex == -1 || (deviceIndex >= 0 && deviceIndex < devices.Count)))
                    {
                        break;
                    }
                    Console.WriteLine("Invalid input. Please enter a valid device index.");
                }
                Console.WriteLine($"Selected device: {devices[deviceIndex].FriendlyName}");


                var detector2 = new ToneDetector(liveInput: true, device: devices[deviceIndex]);
                detector2.OnBit += decoder.FeedBit;
                detector2.OnDetectionEventMessage += msg =>
                {
                    //Console.WriteLine($"{msg}");
                };

                detector2.Start();
                Console.WriteLine("Real-time detection started. Press any key to stop...");
                Console.ReadKey();
                detector2.Stop();
                Console.WriteLine("Real-time detection stopped.");
             
                return 1;
            }

            inputPath = args[1];
            string? optPath = args.Length == 3 ? args[2] : null;
            if (!File.Exists(inputPath))
            {
                Console.Error.WriteLine($"Error: File not found: {inputPath}");
                return 1;
            }

            if (mode == "-decode")
            {
                string outputPath = Path.ChangeExtension(inputPath, ".bin");
                var signal = new VLFSignal();
                signal.OnSignalConsolePrint += msg => Console.WriteLine(msg);
                signal.OnSignalError += (sender, ex) => Console.Error.WriteLine($"Error: {ex.Message}");

                Console.WriteLine($"Parsing WAV file: {inputPath}");
                if (!signal.ParseWavSignal(inputPath))
                {
                    Console.Error.WriteLine("Failed to parse WAV file. Ensure it is a valid 8-bit mono WAV.");
                    return 1;
                }

                try
                {
                    File.WriteAllBytes(outputPath, signal.Data);
                    Console.WriteLine($"Decoded data written to {outputPath}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error writing output: {ex.Message}");
                    return 1;
                }
            }
            else if (mode == "-encode")
            {
                string outputPath = Path.ChangeExtension(inputPath, ".wav");
                byte[] data;
                try
                {
                    data = File.ReadAllBytes(inputPath);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error reading input: {ex.Message}");
                    return 1;
                }

                if (data.Length == 0)
                {
                    Console.Error.WriteLine("Error: Input binary file is empty.");
                    return 1;
                }

                var signal = new VLFSignal();
                signal.OnSignalConsolePrint += msg => Console.WriteLine(msg);
                signal.OnSignalError += (sender, ex) => Console.Error.WriteLine($"Error: {ex.Message}");

                if (!signal.ParseFromVLFDataSequence(data))
                {
                    Console.Error.WriteLine("Failed to parse binary data.");
                    return 1;
                }

                if (!signal.ToWavFile(outputPath))
                {
                    Console.Error.WriteLine("Failed to write WAV file.");
                    return 1;
                }

                Console.WriteLine($"Encoded WAV written to {outputPath}");
            }
            else if (mode == "-visualize")
            {
                string outputPath = optPath ?? Path.ChangeExtension(inputPath, ".png");

                var signal = new VLFSignal();
                signal.OnSignalConsolePrint += Console.WriteLine;
                signal.OnSignalError += (_, ex) => Console.Error.WriteLine($"Error: {ex.Message}");

                Console.WriteLine($"Parsing WAV file: {inputPath}");
                if (!signal.ParseWavSignal(inputPath))
                {
                    Console.Error.WriteLine("Failed to parse WAV file. Ensure it is a valid 8-bit mono WAV.");
                    return 1;
                }

                if (!signal.RenderWavVisualToFile(outputPath))
                {
                    Console.Error.WriteLine("Failed to render waveform image.");
                    return 1;
                }

                Console.WriteLine($"Waveform PNG written to {outputPath}");
            }
            else
            {
                Console.Error.WriteLine("Unknown mode. Use -decode or -encode.");
                PrintUsage();
                return 1;
            }

            return 0;
        }

        static void PrintUsage()
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  VLFSignalTool -decode <input.wav>");
            Console.WriteLine("  VLFSignalTool -encode <input.bin>");
            Console.WriteLine("Examples:");
            Console.WriteLine("  VLFSignalTool -decode sound.wav   # Produces sound.bin");
            Console.WriteLine("  VLFSignalTool -encode data.bin    # Produces data.wav");
        }
    }
}