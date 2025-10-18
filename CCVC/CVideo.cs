using Google.FlatBuffers;
using Schemes.CV;
using System;
using System.IO.Compression;

namespace CCVC;

public class CVideo
{
    public const int CurrentVersion = 1;

    private byte[][]? _frames = new byte[0][];
    private double _fps;
    private byte[] _sound;
    private int _version;
    private int _width;
    private int _height;
    private byte _colors;

    private Schemes.CV.ConsoleVideo? _loadedVideo;

    //public IReadOnlyList<byte> Frames { get { return _frames.ToList(); } }
    public double FPS { get { return _fps; } }
    public MemoryStream Sound { get { return new MemoryStream(_sound); } }
    public int Version { get { return _version; } }
    public int Width { get { return _width; } }
    public int Height { get { return _height; } }
    public byte ColorCount { get { return _colors; } }

    public byte[] GetFrame(int index)
    {
        if (_loadedVideo is null)
            return _frames![index];
        else
        {
            var frame = _loadedVideo.Value.Frames(index + 1)!.Value;
            List<byte> bytes = new();
            for (var i = 0; i < frame.ContentLength; i++)
                bytes.Add(frame.Content(i));
            return bytes.ToArray();
        }
    }
    
    public int GetFramesCount()
    {
        if (_loadedVideo is null)
            return _frames!.Length;
        else
            return _loadedVideo.Value.FramesLength;
    }

    public int GetLength()
    {
        if (_loadedVideo is null)
            return _frames!.Length;
        else
            return _loadedVideo.Value.FramesLength;
    }
    public void Save(Stream stream)
    {
        FlatBufferBuilder builder = new(1024);
        var sound = Schemes.CV.ConsoleVideo.CreateSoundVector(builder, _sound);

        
        List<Offset<Frame>> frameOffsets = new();
        for(int i = 0; i < GetFramesCount(); i++)
        {
            var contentVector = Schemes.CV.Frame.CreateContentVector(builder, GetFrame(i));
            frameOffsets.Add(Schemes.CV.Frame.CreateFrame(builder, contentVector));
        }
        
        var frames = Schemes.CV.ConsoleVideo.CreateFramesVector(builder, frameOffsets.ToArray());

        var video = Schemes.CV.ConsoleVideo.CreateConsoleVideo(builder, FPS, sound, frames, CurrentVersion, _width, _height, _colors);

        builder.Finish(video.Value);
        var buffer = builder.SizedByteArray();

        var gzip = new GZipStream(stream, CompressionLevel.SmallestSize);

        gzip.Write(buffer, 0, buffer.Length);
        gzip.Close();
        stream.Close();
    }
    public void Save(string filename)
        => Save(File.Create(filename));

    
    public static CVideo Load(string filename)
    {
        byte[] bytes;
        using (MemoryStream stream = new())
        {
            using (GZipStream gzip = new(File.OpenRead(filename), CompressionMode.Decompress))
                gzip.CopyTo(stream);
            

            bytes = stream.ToArray();
        }


        var video = Schemes.CV.ConsoleVideo.GetRootAsConsoleVideo(new(bytes));

        var version = video.Version;
        if(version > CurrentVersion)
        {
            throw new Exception("The file version is higher than the supported one. Loading is not possible");
        }

        List<byte> sound = new();
        
        for(int i = 0; i < video.SoundLength; i++)
            sound.Add(video.Sound(i));

        return new CVideo(video, video.Fps, sound.ToArray(), video.Width, video.Height, video.Colors, version);
    }

    public CVideo(byte[][] frames, double fps, byte[] sound, int width, int height, byte colorCount, int version = CurrentVersion)
    {
        _frames = frames;
        _fps = fps;
        _sound = sound;
        _loadedVideo = null;
        _colors = colorCount;
        _width = width;
        _height = height;
    }
    public CVideo(Schemes.CV.ConsoleVideo video, double fps, byte[] sound, int width, int height, byte colorCount, int version = CurrentVersion)
    {
        _loadedVideo = video;
        _fps = fps;
        _sound = sound;
        _frames = null;
        _colors = colorCount;
        _width = width;
        _height = height;
    }
}
