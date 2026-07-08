using CVC.File;
using System.Text;

namespace CVC.Tests;

public class CVideoFileTests
{
    [Fact]
    public void ReadsSingleFrameFile()
    {
        var meta = CVideoMeta.Create(fps: 24, width: 4, height: 2, colorCount: 16);
        byte[] sound = [1, 2, 3, 5, 8, 13];
        byte[] frame = [0, 1, 2, 3, 4, 5, 6, 7];

        using var stream = CVideoTestFile.Create(meta, sound, frame);

        var video = CVideoFile.FromStream(stream);

        Assert.Equal(CVideoMeta.CurrentVersion, video.Meta.FileVersion);
        Assert.Equal(24, video.Meta.Fps);
        Assert.Equal(4, video.Meta.Width);
        Assert.Equal(2, video.Meta.Height);
        Assert.Equal(16, video.Meta.ColorCount);
        Assert.Equal(sound, video.Sound);
        Assert.NotNull(video.VideoStream);
        Assert.Equal(1, video.VideoStream.Length);
        Assert.Equal(0, video.VideoStream.Position);
        Assert.Equal(frame, video.VideoStream.ReadFrame());
        Assert.Empty(video.VideoStream.ReadFrame());
    }

    [Fact]
    public void ReadsPredictedFrameFile()
    {
        var meta = CVideoMeta.Create(fps: 30, width: 3, height: 2, colorCount: 32);
        byte[] firstFrame = [10, 20, 30, 40, 50, 60];
        byte[] predictedFrame = [11, 21, 31, 41, 51, 61];

        using var stream = CVideoTestFile.Create(
            meta,
            sound: [],
            configure: videoStream => videoStream.PFrameK = 1,
            firstFrame,
            predictedFrame);

        Assert.Equal(
            new[] { FrameType.IntraCoded, FrameType.PredictedFrame },
            CVideoTestFile.ReadFrameTypes(stream));

        var video = CVideoFile.FromStream(stream);

        Assert.NotNull(video.VideoStream);
        Assert.Equal(2, video.VideoStream.Length);
        Assert.Equal(firstFrame, video.VideoStream.ReadFrame());
        Assert.Equal(predictedFrame, video.VideoStream.ReadFrame());
        Assert.Empty(video.VideoStream.ReadFrame());
    }

    [Fact]
    public void RejectsTruncatedSoundPayload()
    {
        var meta = CVideoMeta.Create(fps: 24, width: 2, height: 2, colorCount: 16);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            meta.Save(writer);
            writer.Write(16);
            writer.Write(new byte[] { 1, 2, 3 });
        }

        stream.Position = 0;

        Assert.Throws<EndOfStreamException>(() => CVideoFile.FromStream(stream));
    }
}
