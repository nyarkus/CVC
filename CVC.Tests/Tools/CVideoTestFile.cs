using System.Text;
using CVC.File;

namespace CVC.Tests;

internal static class CVideoTestFile
{
    public static MemoryStream Create(
        CVideoMeta meta,
        byte[] sound,
        params byte[][] frames)
    {
        return Create(meta, sound, configure: null, frames);
    }

    public static MemoryStream Create(
        CVideoMeta meta,
        byte[] sound,
        Action<CVideoStream>? configure,
        params byte[][] frames)
    {
        var stream = new MemoryStream();
        var builder = new CVideoFileBuilder(stream)
            .WithMeta(meta)
            .WithSound(sound);
        var videoStream = builder.GetVideoStream();

        configure?.Invoke(videoStream);

        foreach (var frame in frames)
            videoStream.WriteFrame(frame);

        builder.Build();
        stream.Position = 0;
        return stream;
    }

    public static FrameType[] ReadFrameTypes(MemoryStream stream)
    {
        var originalPosition = stream.Position;

        try
        {
            stream.Position = 0;
            _ = CVideoMeta.FromStream(stream);

            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            var soundLength = reader.ReadInt32();
            stream.Seek(soundLength, SeekOrigin.Current);

            var frameCount = checked((int)reader.ReadInt64());
            var frameTypes = new FrameType[frameCount];
            for (var i = 0; i < frameTypes.Length; i++)
            {
                frameTypes[i] = (FrameType)reader.ReadByte();
                var frameLength = reader.ReadInt32();
                stream.Seek(frameLength, SeekOrigin.Current);
            }

            return frameTypes;
        }
        finally
        {
            stream.Position = originalPosition;
        }
    }
}
