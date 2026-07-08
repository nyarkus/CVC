using CVC.Encoder;
using CVC.Players;
using Converter;

class Program
{
    public static void Main(string[] args)
    {
        Convert();
    }

    private static int ReadInt(string text, int max = int.MaxValue)
    {
        while (true)
        {
            Console.WriteLine(text);
            if (int.TryParse(Console.ReadLine(), out var value) && value > 0 && value <= max)
                return value;
        }
    }

    private static string ReadRequiredPath(string text)
    {
        while (true)
        {
            Console.WriteLine(text);
            var value = Console.ReadLine()?.Trim('"');
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
    }

    public static void Convert()
    {
        #if PLATFORM_WINDOWS
        var maxWidth = ConsolePlayer.GetMaxWidth();
        var maxHeight = ConsolePlayer.GetMaxHeight();
        #else
        var maxWidth = Console.LargestWindowWidth;
        var maxHeight = Console.LargestWindowHeight;
        #endif
        
        Console.Clear();

        string source = ReadRequiredPath("Enter a source video:");

        byte colors = (byte)ReadInt($"How many colors do you want to use? (max is {byte.MaxValue})", byte.MaxValue);
        Console.WriteLine($"Your resolution limit is {maxWidth}x{maxHeight}");
        int width = ReadInt($"Specify the width of the video (in number of characters. Your max is {maxWidth})", maxWidth);
        int height = ReadInt($"Specify the heigh of the video (in number of characters. Your max is {maxHeight})", maxHeight);
        
        string output = ReadRequiredPath("Where to save the .ccv file?");
        output = Path.ChangeExtension(output, ".ccv");

        Console.WriteLine("Converting...");
        using var destination = File.Open(output, FileMode.Create, FileAccess.Write);
        CVC.Encoder.Converter.ConvertFromVideo(new FFmpeg(), source, destination, width, height, colors);
    }
}
