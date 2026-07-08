namespace CVC.Encoder;

public class Converter
{
    public static CVideoFile ConvertFromVideo(
        FFmpegManager ffmpeg,
        string source,
        Stream destination,
        int width,
        int height,
        byte colorCount = 10,
        double? fps = null,
        double? pFrameK = null,
        Action<int>? onFrameEncoded = null)
    {
        double outputFps = fps ?? ffmpeg.GetFPS(source);
        var sound = ffmpeg.ExtractAndResampleSoundToMemory(source);

        var builder = new CVideoFileBuilder(destination)
            .WithMeta(CVideoMeta.Create(outputFps, width, height, colorCount))
            .WithSound(sound.ToArray());

        var videoStream = builder.GetVideoStream();
        if (pFrameK.HasValue)
            videoStream.PFrameK = pFrameK.Value;

        var framesEncoded = 0;

        ffmpeg.ExtractFramesToMemory(source, outputFps, frameStream =>
        {
            var frame = FrameConverter.Instance.Convert(frameStream, colorCount, width, height);
            videoStream.WriteFrame(frame);
            framesEncoded++;
            onFrameEncoded?.Invoke(framesEncoded);
        });

        return builder.Build();
    }
}