using CVC.File;

namespace CVC.Tests;

public class CVideoStreamTests
{
    [Fact]
    public void SeeksBackToPredictedFrame()
    {
        var meta = CVideoMeta.Create(fps: 12, width: 3, height: 2, colorCount: 64);
        byte[] firstFrame = [10, 20, 30, 40, 50, 60];
        byte[] predictedFrame = [11, 21, 31, 41, 51, 61];
        byte[] secondKeyFrame = [200, 201, 202, 203, 204, 205];

        using var stream = CVideoTestFile.Create(
            meta,
            sound: [],
            configure: videoStream => videoStream.PFrameK = 1,
            firstFrame,
            predictedFrame,
            secondKeyFrame);

        Assert.Equal(
            new[] { FrameType.IntraCoded, FrameType.PredictedFrame, FrameType.IntraCoded },
            CVideoTestFile.ReadFrameTypes(stream));

        var video = CVideoFile.FromStream(stream);
        var videoStream = Assert.IsType<CVideoStream>(video.VideoStream);

        Assert.Equal(firstFrame, videoStream.ReadFrame());

        Assert.Equal(2, videoStream.Seek(2, SeekOrigin.Begin));
        Assert.Equal(secondKeyFrame, videoStream.ReadFrame());

        Assert.Equal(1, videoStream.Seek(1, SeekOrigin.Begin));
        Assert.Equal(predictedFrame, videoStream.ReadFrame());
    }

    [Fact]
    public void RejectsWrongFrameSize()
    {
        var meta = CVideoMeta.Create(fps: 24, width: 4, height: 2, colorCount: 16);

        using var stream = new MemoryStream();
        var videoStream = new CVideoFileBuilder(stream)
            .WithMeta(meta)
            .WithSound([])
            .GetVideoStream();

        var exception = Assert.Throws<ArgumentException>(() => videoStream.WriteFrame([1, 2, 3]));

        Assert.Equal("frame", exception.ParamName);
    }

    [Fact]
    public void SeeksToEnd()
    {
        var meta = CVideoMeta.Create(fps: 24, width: 2, height: 2, colorCount: 16);
        byte[] frame = [1, 2, 3, 4];

        using var stream = CVideoTestFile.Create(meta, sound: [], frame);
        var video = CVideoFile.FromStream(stream);
        var videoStream = Assert.IsType<CVideoStream>(video.VideoStream);

        Assert.Equal(1, videoStream.Seek(0, SeekOrigin.End));
        Assert.Empty(videoStream.ReadFrame());
    }
}
