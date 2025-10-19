namespace CVC;

public class CVideoFileBuilder
{
    private Stream _destination;
    private CVideoFile _file;

    public CVideoFileBuilder(Stream destination)
    {
        _destination = destination;
        _file = new CVideoFile();
    }

    public CVideoFileBuilder WithMeta(CVideoMeta meta)
    {
        _file.Meta = meta;
        return this;
    }

    public CVideoFileBuilder WithSound(byte[] sound)
    {
        _file.Sound = sound;
        return this;
    }

    public CVideoStream GetVideoStream()
    {
        if (_file.Meta is null || _file.Sound is null)
            throw new InvalidOperationException();

        _file.Save(_destination);
        var stream = new CVideoStream(_destination, _file.Meta, CVideoSteamMode.Writing);
        stream.StartWriting();
        _file.VideoStream = stream;

        return stream;
    }

    public CVideoFile Build()
    {
        if (_file.VideoStream is null)
            throw new InvalidOperationException();
        
        _file.VideoStream.EndWriting();
        
        return _file;
    }
}