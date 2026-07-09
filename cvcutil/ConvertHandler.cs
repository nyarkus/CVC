using System.IO.Compression;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using CVC.Encoder;
using CVC.File;

namespace cvcutil;

public static class ConvertHandler
{
    public static int Convert(
        string input,
        string? output,
        int width,
        int height,
        byte colors,
        double? fps,
        double? pFrameK,
        string? encodingMode,
        string? brotliCompression,
        bool overwrite)
    {
        try
        {
            DateTimeOffset currentTime = DateTimeOffset.UtcNow;
            
            var parsedEncodingMode = ParseEncodingMode(encodingMode);
            var parsedBrotliCompression = ParseBrotliCompression(brotliCompression);
            Validate(input, output, width, height, colors, fps, pFrameK, overwrite);

            var outputPath = GetOutputPath(input, output);
            var outputFullPath = Path.GetFullPath(outputPath);
            var outputDirectory = Path.GetDirectoryName(outputFullPath) ?? Directory.GetCurrentDirectory();
            var tempPath = Path.Combine(
                outputDirectory,
                $".{Path.GetFileName(outputFullPath)}.{Guid.NewGuid():N}.tmp");

            Console.WriteLine("Converting...");
            var sb = new StringBuilder();
            sb.AppendLine($"Input\t\t\t:\t{input}");
            sb.AppendLine($"Output\t\t\t:\t{outputFullPath}");
            sb.AppendLine($"Size\t\t\t:\t{width}x{height}");
            sb.AppendLine($"Colors\t\t\t:\t{colors.ToString()}");
            if (fps.HasValue)
                sb.AppendLine($"FPS\t\t\t:\t{fps.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            if (pFrameK.HasValue)
                sb.AppendLine($"PFrame\t\t\t:\t{pFrameK.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            sb.AppendLine($"Encoding Mode\t\t:\t{FormatEncodingMode(parsedEncodingMode)}");
            sb.AppendLine($"Brotli Compression\t:\t{FormatBrotliCompressionMode(parsedBrotliCompression)}");

            Console.WriteLine(sb.ToString());

            EncodingStatistics stats = new();
            try
            {
                using (var destination = File.Open(tempPath, FileMode.CreateNew, FileAccess.Write))
                {
                    CVC.Encoder.Converter.ConvertFromVideo(
                        new FFmpeg(),
                        input,
                        destination,
                        width,
                        height,
                        colors,
                        fps,
                        pFrameK,
                        framesEncoded => { },
                        parsedEncodingMode, 
                        parsedBrotliCompression,
                        stats);
                    
                    stats.Complete(DateTimeOffset.UtcNow - currentTime, destination.Length);
                }

                File.Move(tempPath, outputFullPath, overwrite);
            }
            catch
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);

                throw;
            }
            Console.WriteLine();
            Console.WriteLine("Done.");
            sb = new StringBuilder();
            sb.AppendLine("Statistics:");
            sb.AppendLine();
            sb.AppendLine(
                $"Encoding Duration\t:\t{stats.Duration.ToString("g", System.Globalization.CultureInfo.InvariantCulture)}");
            sb.AppendLine($"Output file size\t:\t{stats.OutputBytes} bytes");
            sb.AppendLine();
            sb.AppendLine($"IFrames count\t\t:\t{stats.IFrames.Count}");
            sb.AppendLine($"Average IFrame size\t:\t{stats.IFrames.AverageBytes} bytes");
            sb.AppendLine($"Worst IFrame size\t:\t{stats.IFrames.WorstBytes} bytes");
            sb.AppendLine();
            sb.AppendLine($"PFrames count\t\t:\t{stats.PFrames.Count}");
            sb.AppendLine($"Average PFrame size\t:\t{stats.PFrames.AverageBytes} bytes");
            sb.AppendLine($"Worst PFrame size\t:\t{stats.PFrames.WorstBytes} bytes");
            
            Console.WriteLine(sb.ToString());
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Conversion failed: {ex.Message}");
            return 1;
        }
    }

    private static void Validate(
        string input,
        string? output,
        int width,
        int height,
        byte colors,
        double? fps,
        double? pFrameK,
        bool overwrite)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Input path is required.");

        if (!File.Exists(input))
            throw new FileNotFoundException("Input video file was not found.", input);

        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than zero.");

        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");

        if (colors < 2)
            throw new ArgumentOutOfRangeException(nameof(colors), "Colors must be in range 2-255.");

        if (fps.HasValue && fps.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(fps), "FPS must be greater than zero.");

        if (pFrameK.HasValue && pFrameK.Value < 0)
            throw new ArgumentOutOfRangeException(nameof(pFrameK), "P-frame threshold must be zero or greater.");

        var outputPath = GetOutputPath(input, output);
        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(outputDirectory) && !Directory.Exists(outputDirectory))
            throw new DirectoryNotFoundException($"Output directory does not exist: {outputDirectory}");

        if (!overwrite && File.Exists(outputPath))
            throw new IOException($"Output file already exists: {outputPath}. Use --overwrite to replace it.");
    }

    private static string GetOutputPath(string input, string? output)
    {
        if (!string.IsNullOrWhiteSpace(output))
            return output.Trim('"');

        return Path.ChangeExtension(input.Trim('"'), ".ccv");
    }

    private static FrameEncodingMode ParseEncodingMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return FrameEncodingMode.Fast;

        string normalized = value.Trim().Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
        return normalized switch
        {
            "fast" => FrameEncodingMode.Fast,
            "bestsize" => FrameEncodingMode.BestSize,
            "hybrid" => FrameEncodingMode.Hybrid,
            _ => throw new ArgumentException("Encoding mode must be one of: fast, best-size, hybrid.")
        };
    }

    public static CompressionLevel ParseBrotliCompression(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return CompressionLevel.Optimal;
        
        string normalized = value.Trim().Replace("-", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
        return normalized switch
        {
            "slowest" => CompressionLevel.SmallestSize,
            "optimal" => CompressionLevel.Optimal,
            "fastest" => CompressionLevel.Fastest,
            "no" => CompressionLevel.NoCompression,
            _ => throw new ArgumentException("Brotli compression mode must be one of: slowest, optimal, fastest, no")
        };
    }

    private static string FormatEncodingMode(FrameEncodingMode mode)
    {
        return mode switch
        {
            FrameEncodingMode.Fast => "fast",
            FrameEncodingMode.BestSize => "best-size",
            FrameEncodingMode.Hybrid => "hybrid",
            _ => mode.ToString()
        };
    }
    
    private static string FormatBrotliCompressionMode(CompressionLevel mode)
    {
        return mode switch
        {
            CompressionLevel.SmallestSize => "slowest",
            CompressionLevel.Optimal => "optimal",
            CompressionLevel.Fastest => "fastest",
            CompressionLevel.NoCompression => "no",
            _ => mode.ToString()
        };
    }
}
