using CCVC;
using System.Security.Principal;
using Microsoft.Win32;
using ConsolePlayer;

class Program
{
    public static void Main(string[] args)
    {
        string source;
        if (args.Length == 0)
        {
            Console.WriteLine("Enter a path to .ccv file:");
            source = Console.ReadLine().Trim('"');
            
        }
        else
            source = args[0].Trim('"');

        var video = CVideo.Load(source);
        CCVC.Players.ConsolePlayer.Play(video);
    }
}