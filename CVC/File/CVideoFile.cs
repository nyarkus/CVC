using System.Text;

namespace CVC;

public class CVideoFile
{
    internal CVideoFile() { }

    private Stream _fileStream;
    
    public CVideoMeta Meta { get; internal set; }
    public CVideoStream? VideoStream { get; internal set; }
    public byte[] Sound { get; internal set; }

    public static CVideoFile FromStream(Stream stream)
    {
        var file = new CVideoFile();
        file._fileStream = stream;
        
        file.Meta = CVideoMeta.FromStream(stream);
        
        using BinaryReader br = new BinaryReader(stream, Encoding.UTF8, true);
        
        var soundLength = br.ReadInt32();
        file.Sound = br.ReadBytes(soundLength);

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