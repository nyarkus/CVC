using CVC.Encoder;

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
        bool overwrite)
    {
        try
        {
            Validate(input, output, width, height, colors, fps, pFrameK, overwrite);

            var outputPath = GetOutputPath(input, output);
            var fileMode = overwrite ? FileMode.Create : FileMode.CreateNew;

            Console.WriteLine("Converting...");
            Console.WriteLine($"Input:\t{input}");
            Console.WriteLine($"Output:\t{outputPath}");
            Console.WriteLine($"Size:\t{width}x{height}");
            Console.WriteLine($"Colors:\t{colors}");
            if (fps.HasValue)
                Console.WriteLine($"FPS:\t{fps.Value}");
            if (pFrameK.HasValue)
                Console.WriteLine($"PFrame:\t{pFrameK.Value}");

            using var destination = File.Open(outputPath, fileMode, FileAccess.Write);
            CVC.Encoder.Converter.ConvertFromVideo(
                new FFmpeg(),
                input,
                destination,
                width,
                height,
                colors,
                fps,
                pFrameK,
                framesEncoded =>
                {
                    if (framesEncoded % 30 == 0)
                        Console.Write($"\rEncoded frames: {framesEncoded}");
                });

            Console.WriteLine();
            Console.WriteLine("Done.");
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
}
