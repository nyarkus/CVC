using System.Text;

namespace CVC.File;

public class CVideoFile
{
    internal CVideoFile() { }

    private Stream _fileStream = Stream.Null;
    
    public CVideoMeta Meta { get; internal set; } = null!;
    public CVideoStream? VideoStream { get; internal set; }
    public byte[] Sound { get; internal set; } = Array.Empty<byte>();

    public static CVideoFile FromStream(Stream stream)
    {
        var file = new CVideoFile();
        file._fileStream = stream;
        
        file.Meta = CVideoMeta.FromStream(stream);
        
        using BinaryReader br = new BinaryReader(stream, Encoding.UTF8, true);
        
        var soundLength = br.ReadInt32();
        if (soundLength < 0)
            throw new InvalidDataException("Sound payload length cannot be negative.");

        file.Sound = br.ReadBytes(soundLength);
        if (file.Sound.Length != soundLength)
            throw new EndOfStreamException($"Expected {soundLength} sound payload bytes, got {file.Sound.Length}.");

        file.VideoStream = new CVideoStream(stream, file.Meta, CVideoSteamMode.Reading);
        
        return file;
    }

    internal void Save(Stream destination)
    {
        using (BinaryWriter bw = new BinaryWriter(destination, Encoding.ASCII, true))
        {
            Meta.Save(bw);
        
            bw.Write(Sound.Length);
            bw.Write(Sound);   
        }
    }

    public void Close()
    {
        _fileStream?.Close();
        VideoStream = null;
    }
}
