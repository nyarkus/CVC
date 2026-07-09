# Console Video Codec

CVC is a .NET library for working with the Console Video Codec and its `.ccv` container format. It includes APIs for encoding, decoding, reading, writing, and playing console video. The project also ships with `cvcutil`, a CLI tool for converting common video files to `.ccv` and playing them in the terminal.

## Requirements

- .NET 10
- `ffmpeg` available in `PATH` for video conversion
- A terminal that can display the selected output size

Audio playback uses BASS through ManagedBass. The repository currently includes native BASS binaries for x64 Linux and x64 Windows.

## cvcutil

`cvcutil` is the command-line tool for converting videos to `.ccv` and playing `.ccv` files.

### play

Plays a `.ccv` file:

```bash
cvcutil play video.ccv
```

Options:

- `--charset-preset`, `-p` - selects a built-in character set preset:
  - `classic` - ` .:-=+*#%@`
  - `blocks` - ` ░▒▓█`
  - `binary` - ` █`
- `--charset` - uses a custom character set. Characters must be ordered from darkest to brightest.

### convert

Converts a video file to `.ccv` using `ffmpeg`. Pass the input file path and the target console resolution with `--width`/`-w` and `--height`/`-h`.

By default, the output file is saved next to the input file with the same name and the `.ccv` extension.

```bash
cvcutil convert video.mp4 -w 200 -h 100
```

Options:

- `--output`, `-o` - output file path.
- `--fps` - output FPS. Defaults to the source video FPS.
- `--width`, `-w` - output width in console characters.
- `--height`, `-h` - output height in console characters.
- `--colors`, `-c` - number of grayscale levels to encode, from 2 to 255. Defaults to 10.
- `--overwrite` - overwrites the output file if it already exists.
- `--preset`, `-p` - encoding speed and size preset. Slower presets usually produce smaller files, but take longer to encode. Available presets: `fastest`, `fast`, `balanced`, `slow`, `slowest`. Defaults to `balanced`

Advanced options:

- `--pframe-k` - P-frame threshold. Lower values create more keyframes.
- `--encoding-mode` - frame encoding mode:
  - `fast` - uses a simple and fast `pframe-k` threshold check.
  - `best-size` - encodes both I-frame and P-frame candidates, then stores the smaller one.
  - `hybrid` - uses the fast threshold check for obviously similar frames, otherwise falls back to `best-size`.
- `--brotli-compression-mode` - Brotli compression level for encoded frame payloads. Available modes: `slowest`, `optimal`, `fastest`, `no`.

## CVC library

The `CVC` project can also be used directly as a library. The main entry point for reading `.ccv` files is `CVideoFile.FromStream`, which loads the file metadata, audio payload, and video stream index.

The source stream must stay open while the video stream, decoder, or player is being used.

### Playing a video

The simplest way to play a `.ccv` file is to use the built-in console player:

```csharp
using System.IO;
using CVC.File;
using CVC.Players;

using var stream = File.OpenRead("video.ccv");
var video = CVideoFile.FromStream(stream);

ConsolePlayer.Play(video);
```

You can also pass a custom character set. Characters must be ordered from darkest to brightest:

```csharp
ConsolePlayer.Play(video, " .:-=+*#%@");
```

`ConsolePlayer` handles frame decoding, frame timing, terminal output, and audio playback when the `.ccv` file contains audio.

### Buffered frame decoding

For custom players, use `CVC.Decoder.FrameDecoder`. It is a buffered frame decoder that runs a background worker and keeps a small window of decoded frames around the requested playback position.

Important members:

- `BufferSize` - how many frames ahead of the requested frame should be kept decoded.
- `BackBufferSize` - how many recently displayed frames behind the requested frame should be kept.
- `Start()` - starts the background decoding worker.
- `RequestFrame(frame)` - tells the decoder which frame the player currently needs.
- `WaitForFrame(frame, timeout, cancellationToken)` - waits for a decoded frame string and returns `null` if it is not ready in time.
- `WaitUntilBuffered(startFrame, minFrames, timeout, cancellationToken)` - waits until a minimum number of frames from `startFrame` is decoded.
- `LastDecodedFrame` - the highest decoded frame currently in the buffer.

The decoder returns already-rendered strings. It does not return raw pixel data. Each string is ready to write to the terminal or another text surface.

### Writing a custom player

A custom player usually needs three pieces:

1. Open the `.ccv` file with `CVideoFile.FromStream`.
2. Create a `FrameDecoder` with the character set you want to render with.
3. Drive playback timing yourself and ask the decoder for the frame that matches the current timestamp.

Minimal example:

```csharp
using System.Diagnostics;
using CVC.Decoder;
using CVC.File;

using var stream = File.OpenRead("video.ccv");
var video = CVideoFile.FromStream(stream);

using var decoder = new FrameDecoder(video, " .:-=+*#%@");
decoder.Start();

var totalFrames = (int)(video.VideoStream?.Length ?? 0);
if (totalFrames <= 0)
    return;

var frameTimeout = TimeSpan.FromSeconds(1.0 / video.Meta.Fps);
var stopwatch = Stopwatch.StartNew();

while (true)
{
    var frameIndex = Math.Clamp(
        (int)(stopwatch.Elapsed.TotalSeconds * video.Meta.Fps),
        0,
        totalFrames - 1);

    var frame = decoder.WaitForFrame(frameIndex, frameTimeout);
    if (frame is null)
    {
        Thread.Sleep(1);
        continue;
    }

    Console.SetCursorPosition(0, 0);
    Console.Write(frame);

    if (frameIndex == totalFrames - 1)
        break;
}
```

For audio-synchronized playback, use the current audio playback position instead of `Stopwatch.Elapsed` when calculating `frameIndex`. The built-in `ConsolePlayer` does this when the video contains audio.

### Reading raw brightness frames

`FrameDecoder` is designed for text output, so it returns rendered strings. If you need brightness values instead of characters, use the lower-level `CVideoStream` API directly.

`CVideoStream.ReadFrame()` returns a `byte[]` for one decoded frame. The array contains brightness levels in row-major order:

- index: `y * video.Meta.Width + x`
- value range: `0` to `video.Meta.ColorCount - 1`
- normalized brightness: `value / (video.Meta.ColorCount - 1f)`

Theoretical raw-frame player:

```csharp
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CVC.File;

using var stream = File.OpenRead("video.ccv");
var video = CVideoFile.FromStream(stream);
var videoStream = video.VideoStream!;

var stopwatch = Stopwatch.StartNew();

for (var frameIndex = 0L; frameIndex < videoStream.Length; frameIndex++)
{
    var frame = videoStream.ReadFrame();

    for (var y = 0; y < video.Meta.Height; y++)
    {
        for (var x = 0; x < video.Meta.Width; x++)
        {
            var level = frame[y * video.Meta.Width + x];
            var brightness = level / (video.Meta.ColorCount - 1f);

            DrawPixel(x, y, brightness);
        }
    }

    var nextFrameAt = TimeSpan.FromSeconds((frameIndex + 1) / video.Meta.Fps);
    var delay = nextFrameAt - stopwatch.Elapsed;
    if (delay > TimeSpan.Zero)
        Thread.Sleep(delay);
}
```

For a real player with seeking or asynchronous rendering, build a small buffer around `CVideoStream.ReadFrame()` in the same way `FrameDecoder` does for text frames: keep decoded `byte[]` frames near the requested playback position, seek with `CVideoStream.Seek(frameIndex, SeekOrigin.Begin)` when the requested frame jumps, and render from the buffered brightness arrays.
