using System.Diagnostics;
using CVC.Encoder;

namespace cvcutil;

internal sealed class FFmpeg : FFmpegManager
{
    private bool _checked;

    public override void CheckFFmpeg()
    {
        if (_checked)
            return;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = "-version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (process is null)
                throw new InvalidOperationException("Failed to start ffmpeg.");

            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException("ffmpeg returned a non-zero exit code.");

            _checked = true;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("ffmpeg was not found. Install ffmpeg and make sure it is available in PATH.", ex);
        }
    }
}
