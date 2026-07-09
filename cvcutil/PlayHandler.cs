using System.CommandLine;
using System.CommandLine.Invocation;
using CVC.File;

namespace cvcutil;

public class PlayHandler
{
    public static void Play(string path, string? charset, string? charsetPreset)
    {
        if (charset == null && charsetPreset != null)
            charset = charsetPreset switch
            {
                "classic" => " .:-=+*#%@",
                "bricks" => " ░▒▓█",
                "binary" => " █",
                _ => ""
            };
        using var file = File.OpenRead(path);
        var video = CVideoFile.FromStream(file);
        
        CVC.Players.ConsolePlayer.Play(video, charset!);
    }
}
