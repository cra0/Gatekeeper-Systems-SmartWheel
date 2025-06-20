using System.Runtime.Versioning;
using VLFLib;

namespace VLFSignalTool
{
    [SupportedOSPlatform("windows")]
    internal class Program
    {
        static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                PrintUsage();
                return 1;
            }

            string mode = args[0].ToLowerInvariant();
            string inputPath = args[1];

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