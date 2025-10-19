using System.Text;

namespace CVC;

public class CVideoMeta
{
    public const ushort CurrentVersion = 1;
    public ushort FileVersion { get; private set; }
    
    public double Fps { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public byte ColorCount { get; private set; }

    public static CVideoMeta FromStream(Stream stream)
    {
        var meta = new CVideoMeta();
        using BinaryReader br = new BinaryReader(stream, Encoding.ASCII, true);
        meta.FileVersion = br.ReadUInt16();
        
        meta.Fps = br.ReadDouble();
        meta.Width = br.ReadInt32();
        meta.Height = br.ReadInt32();
        meta.ColorCount = br.ReadByte();
        
        return meta;
    }

    public static CVideoMeta Create(double fps, int width, int height, byte colorCount)
    {
        var meta = new CVideoMeta();
        meta.FileVersion = CurrentVersion;

        meta.Fps = fps;
        meta.Width = width;
        meta.Height = height;
        meta.ColorCount = colorCount;

        return meta;
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write(FileVersion);
        writer.Write(Fps);
        writer.Write(Width);
        writer.Write(Height);
        writer.Write(ColorCount);
    }
}