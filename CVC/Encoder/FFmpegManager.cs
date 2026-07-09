using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CVC.Encoder;

public abstract class FFmpegManager
{
    protected readonly string _ffmpegPath;

    public FFmpegManager()
    {
        if(OperatingSystem.IsWindows())
            _ffmpegPath = "ffmpeg.exe";
        else if(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            _ffmpegPath = "ffmpeg";
        else
            throw new PlatformNotSupportedException("ffmpeg integration is supported only on Windows, Linux, and macOS.");
    }

    public abstract void CheckFFmpeg();
    
    public MemoryStream ExtractAndResampleSoundToMemory(string videoPath)
    {
        CheckFFmpeg();

        if (!HasAudioStream(videoPath))
            return new MemoryStream();

        var memory = new MemoryStream();
        var process = new Process
        {
            StartInfo = CreateStartInfo(
                "-i", videoPath,
                "-f", "wav",
                "-ac", "1",
                "-acodec", "pcm_u8",
                "-ar", "8000",
                "-")
        };

        process.Start();
        var errorOutput = new StringBuilder();
        
        var errorReader = Task.Run(() =>
        {
            string? errorLine;
            while ((errorLine = process.StandardError.ReadLine()) != null)
            {
                errorOutput.AppendLine(errorLine);
                Debug.WriteLine(errorLine);
            }
        });
        
        process.StandardOutput.BaseStream.CopyTo(memory);
        
        process.WaitForExit();
        errorReader.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(CreateFFmpegErrorMessage(process.ExitCode, errorOutput));

        memory.Position = 0;
        return memory;
    }
    
    public double GetFPS(string videoPath)
    {
        CheckFFmpeg();

        var process = new Process
        {
            StartInfo = CreateStartInfo("-i", videoPath)
        };

        process.Start();

        string output = process.StandardError.ReadToEnd();
        process.WaitForExit();

        string fpsPattern = @"(\d+(?:\.\d+)?)\s*fps";
        Match match = Regex.Match(output, fpsPattern);

        if (match.Success)
            return double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);

        throw new Exception("Failed to get the FPS of the video");
    }

    private bool HasAudioStream(string videoPath)
    {
        var process = new Process
        {
            StartInfo = CreateStartInfo("-i", videoPath)
        };

        process.Start();

        string output = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return Regex.IsMatch(output, @"Stream\s+#\S+:\s+Audio:", RegexOptions.IgnoreCase);
    }

    public void ExtractFramesToMemory(string videoPath, double fps, Action<MemoryStream> onFrameReceived)
    {
        CheckFFmpeg();

        var process = new Process
        {
            StartInfo = CreateStartInfo(
                "-i", videoPath,
                "-vf", $"fps={fps.ToString(CultureInfo.InvariantCulture)}",
                "-f", "image2pipe",
                "-vcodec", "bmp",
                "-")
        };

        process.Start();
        var errorOutput = new StringBuilder();

        var errorReader = Task.Run(() =>
        {
            string? errorMessage;
            while ((errorMessage = process.StandardError.ReadLine()) != null)
            {
                errorOutput.AppendLine(errorMessage);
                Console.WriteLine(errorMessage);
            }
        });

        Exception? readError = null;
        try
        {
            using (var outputStream = process.StandardOutput.BaseStream)
            {
                while (true)
                {
                    var frameStream = ReadBmpFrameToMemory(outputStream);
                    if (frameStream == null) break;

                    onFrameReceived(frameStream);
                }
            }
        }
        catch (Exception ex)
        {
            readError = ex;
        }

        process.WaitForExit();
        errorReader.GetAwaiter().GetResult();

        if (readError is not null)
            throw new InvalidDataException("Failed to read BMP frames from ffmpeg output.", readError);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(CreateFFmpegErrorMessage(process.ExitCode, errorOutput));
    }
    
    private static MemoryStream? ReadBmpFrameToMemory(Stream stream)
    {
        var buffer = new byte[54];
        if (ReadExactlyOrEnd(stream, buffer) == 0)
            return null;

        if (buffer[0] != 0x42 || buffer[1] != 0x4D)
            throw new InvalidDataException("FFmpeg output did not contain a BMP frame.");

        int fileSize = BitConverter.ToInt32(buffer, 2);
        if (fileSize < buffer.Length)
            throw new InvalidDataException("FFmpeg produced an invalid BMP frame.");

        var ms = new MemoryStream();
        ms.Write(buffer, 0, 54);

        var remainingBytes = fileSize - 54;
        var readBuffer = new byte[4096];
        while (remainingBytes > 0)
        {
            int bytesToRead = Math.Min(readBuffer.Length, remainingBytes);
            stream.ReadExactly(readBuffer, 0, bytesToRead);

            ms.Write(readBuffer, 0, bytesToRead);
            remainingBytes -= bytesToRead;
        }

        ms.Position = 0;
        return ms;
    }

    private ProcessStartInfo CreateStartInfo(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }

    private static int ReadExactlyOrEnd(Stream stream, byte[] buffer)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int bytesRead = stream.Read(buffer, totalRead, buffer.Length - totalRead);
            if (bytesRead == 0)
            {
                if (totalRead == 0)
                    return 0;

                throw new EndOfStreamException("Unexpected end of FFmpeg BMP frame.");
            }

            totalRead += bytesRead;
        }

        return totalRead;
    }

    private static string CreateFFmpegErrorMessage(int exitCode, StringBuilder errorOutput)
    {
        var error = errorOutput.ToString().Trim();
        return string.IsNullOrEmpty(error)
            ? $"ffmpeg failed with exit code {exitCode}."
            : $"ffmpeg failed with exit code {exitCode}: {error}";
    }
}
