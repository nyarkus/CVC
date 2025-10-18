using CCVC.Encoder;

namespace Converter
{
    internal class FFmpeg : FFmpegManager
    {
        public override void CheckFFmpeg()
        {
            if(OperatingSystem.IsLinux()) return;
            if (File.Exists(_ffmpegPath)) return;

            Console.WriteLine("FFMpeg not found.");
            Environment.Exit(-1);
        }
    }
}
