using System.Diagnostics;
using CVC.Decoder;
using CVC.File;
using ManagedBass;
using PlaybackState = ManagedBass.PlaybackState;

namespace CVC.Players;

public static class ConsolePlayer
{
#if PLATFORM_WINDOWS
    public static int GetMaxWidth()
    {
        int result;
        ConsoleHelper.SetCurrentFont("Consolas", 2);
        result = Console.LargestWindowWidth;
        ConsoleHelper.SetCurrentFont("Consolas", 16);
        return result;
    }

    public static int GetMaxHeight()
    {
        int result;
        ConsoleHelper.SetCurrentFont("Consolas", 2);
        result = Console.LargestWindowHeight;
        ConsoleHelper.SetCurrentFont("Consolas", 16);
        return result;
    }
#endif

    public static void Play(CVideoFile video, string chars = " .:-=+*#%@")
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler? cancelHandler = (_, args) =>
        {
            args.Cancel = true;
            cancellation.Cancel();
        };

        Console.CancelKeyPress += cancelHandler;

        var bassInitialized = false;
        var streamHandle = 0;
        var oldWidth = Console.WindowWidth;
        var oldHeight = Console.WindowHeight;

        try
        {
            if (!Bass.Init())
            {
                Console.WriteLine($"Failed to initialize BASS: {Bass.LastError}");
                return;
            }

            bassInitialized = true;
            PrepareConsole(video);

            using var decoder = new FrameDecoder(video, chars);
            decoder.Start();
            decoder.WaitUntilBuffered(
                0,
                Math.Min(decoder.BufferSize, Math.Max(1, (int)Math.Ceiling(video.Meta.Fps))),
                TimeSpan.FromSeconds(10),
                cancellation.Token);

            var audio = video.Sound.ToArray();
            streamHandle = Bass.CreateStream(audio, 0, audio.Length, BassFlags.Default);
            if (streamHandle == 0)
            {
                Console.WriteLine($"Failed to load audio: {Bass.LastError}");
                return;
            }

            Bass.ChannelPlay(streamHandle);
            RunPlaybackLoop(video, decoder, streamHandle, cancellation.Token);
        }
        finally
        {
            if (streamHandle != 0)
                Bass.StreamFree(streamHandle);

            if (bassInitialized)
                Bass.Free();

            RestoreConsole(oldWidth, oldHeight);
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static void RunPlaybackLoop(
        CVideoFile video,
        FrameDecoder decoder,
        int streamHandle,
        CancellationToken cancellationToken)
    {
        var frameDuration = TimeSpan.FromSeconds(1.0 / video.Meta.Fps);
        var maxFrameWait = TimeSpan.FromMilliseconds(Math.Max(1, frameDuration.TotalMilliseconds * 0.75));
        var lastDisplayedFrame = -1;
        var totalFrames = (int)(video.VideoStream?.Length ?? 0);

        while (!cancellationToken.IsCancellationRequested &&
               Bass.ChannelIsActive(streamHandle) == PlaybackState.Playing)
        {
            var targetFrame = GetAudioFrame(streamHandle, video.Meta.Fps);
            if (totalFrames > 0)
                targetFrame = Math.Clamp(targetFrame, 0, totalFrames - 1);

            if (targetFrame == lastDisplayedFrame)
            {
                Thread.Sleep(1);
                continue;
            }

            decoder.RequestFrame(targetFrame);
            var frame = decoder.WaitForFrame(targetFrame, maxFrameWait, cancellationToken);
            if (frame is null)
            {
#if DEBUG
                Debug.WriteLine($"Frame {targetFrame} was not ready. Last buffered: {decoder.LastDecodedFrame}");
#endif
                Thread.Sleep(1);
                continue;
            }

            Console.SetCursorPosition(0, 0);
            Console.Write(frame);
            lastDisplayedFrame = targetFrame;
        }
    }

    private static int GetAudioFrame(int streamHandle, double fps)
    {
        var bytePosition = Bass.ChannelGetPosition(streamHandle);
        var audioTime = Bass.ChannelBytes2Seconds(streamHandle, bytePosition);
        return (int)(audioTime * fps);
    }

    private static void PrepareConsole(CVideoFile video)
    {
        Console.Clear();
        Console.CursorVisible = false;

#if PLATFORM_WINDOWS
        ConsoleHelper.SetCurrentFont("Consolas", 2);
        Console.SetWindowPosition(0, 0);
        Console.WindowWidth = video.Meta.Width + 1;
        Console.WindowHeight = video.Meta.Height + 1;
#endif
    }

    private static void RestoreConsole(int oldWidth, int oldHeight)
    {
#if PLATFORM_WINDOWS
        Console.WindowWidth = oldWidth;
        Console.WindowHeight = oldHeight;
        ConsoleHelper.SetCurrentFont("Consolas", 16);
#endif
        Console.CursorVisible = true;
    }
}
