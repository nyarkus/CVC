using System.CommandLine;
using System.CommandLine.Invocation;

namespace cvcutil;

public class PlayHandler
{
    public static void Play(string path, string charset)
    {
        using var file = File.OpenRead(path);
        var video = CVC.CVideoFile.FromStream(file);
        
        CVC.Players.ConsolePlayer.Play(video, charset);
    }
}
