using CCVC.Encoder;
using CCVC.Players;
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
            if (int.TryParse(Console.ReadLine(), out var value))
                return value;
        }
    }

    public static void Convert()
    {
        #if PLATFORM_WINDOWS
        var maxWidth = ConsolePlayer.GetMaxWidth();
        var maxHeight = ConsolePlayer.GetMaxHeight();
        #endif
        var maxWidth = Console.LargestWindowWidth;
        var maxHeight = Console.LargestWindowHeight;
        
        Console.Clear();

        Console.WriteLine("Enter a source video:");
        string source = Console.ReadLine().Trim('"');

        byte colors = (byte)ReadInt($"How many colors do you want to use? (max is {byte.MaxValue})", byte.MaxValue);
        Console.WriteLine($"Your resolution limit is {maxWidth}x{maxHeight}");
        int width = ReadInt($"Specify the width of the video (in number of characters. Your max is {maxWidth})", maxWidth);
        int height = ReadInt($"Specify the heigh of the video (in number of characters. Your max is {maxHeight})", maxHeight);
        
        Console.WriteLine("Where to save the .ccv file?");
        string output = Console.ReadLine().Trim('"');
        output = Path.ChangeExtension(output, ".ccv");

        Console.WriteLine("Converting...");
        var video = CCVC.Encoder.Converter.ConvertFromVideo(new FFmpeg(), source, width, height, colors);

        Console.WriteLine("Saving...");
        video.Save(output);
    }
}