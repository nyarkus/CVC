using CCVC.Decoder;
using NAudio.Wave;
using System;
using System.Diagnostics;
using System.Threading;
using ManagedBass;
using PlaybackState = ManagedBass.PlaybackState;

namespace CCVC.Players
{
    public class ConsolePlayer
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
        public static void Play(CVideo video)
        {
            if (!Bass.Init())
            {
                Console.WriteLine($"Failed to initialize BASS: {Bass.LastError}");
                return;
            }
            
            Console.Clear();
            Console.CursorVisible = false;

#if PLATFORM_WINDOWS
            ConsoleHelper.FontInfo info = new();
            ConsoleHelper.GetCurrentConsoleFontEx(1, true, ref info);
            ConsoleHelper.SetCurrentFont("Consolas", 2);
#endif

            var oldWidth = Console.WindowWidth;
            var oldHeight = Console.WindowHeight;
#if PLATFORM_WINDOWS
            Console.SetWindowPosition(0, 0);
            Console.WindowWidth = video.Width + 1;
            Console.WindowHeight = video.Height + 1;
#endif

            var decoder = new FrameDecoder(
                video.Width,
                video.Height,
                video.FPS,
                new CVideoFrameReader(video)
            );
            _ = Task.Run(decoder.RecalculateBuffer);
            while (!decoder.BufferIsFull)
                Thread.Sleep(1);

            var audio = video.Sound.ToArray();
            int streamHandle = Bass.CreateStream(audio, 0, audio.Length, BassFlags.Default);
            if (streamHandle == 0)
            {
                Console.WriteLine($"Failed to load audio: {Bass.LastError}");
                Bass.Free();
                return;
            }

            var swGlobal = Stopwatch.StartNew();
            Bass.ChannelPlay(streamHandle);

            double accumulatedTime = 0;
            double frameTime = 1.0 / video.FPS;
            int targetFrame = 0;
            
            while (Bass.ChannelIsActive(streamHandle) == PlaybackState.Playing)
            {
                long bytePosition = Bass.ChannelGetPosition(streamHandle);
                double audioTime = Bass.ChannelBytes2Seconds(streamHandle, bytePosition);

                targetFrame = (int)(audioTime * video.FPS);

                if (targetFrame > decoder.LastDecodedFrame)
                {
                    decoder.Seek(targetFrame);
                }

                string frame = decoder.ReadFrame(targetFrame);
                if (!string.IsNullOrEmpty(frame))
                {
                    Console.SetCursorPosition(0, 0);
                    Console.Write(frame);
                }
                else
                {
                    Debug.WriteLine($"Frame {targetFrame} not found in buffer");
                }

                accumulatedTime += frameTime;
                double sleepTime = accumulatedTime - swGlobal.Elapsed.TotalSeconds;
                if (sleepTime > 0)
                {
                    Task.Delay((int)(sleepTime * 1000)).Wait();
                }
            }
            
            Bass.StreamFree(streamHandle);
            Bass.Free();

            decoder = null;
            
#if PLATFORM_WINDOWS
            Console.WindowWidth = oldWidth;
            Console.WindowHeight = oldHeight;
            ConsoleHelper.SetCurrentFont("Consolas", 16);
#endif
            Console.CursorVisible = true;
        }
    }
}