using System.Text;

namespace CVC.File;

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

        meta.Validate();
        
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

        meta.Validate();

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

    private void Validate()
    {
        if (FileVersion != CurrentVersion)
            throw new InvalidDataException($"Unsupported CVC file version: {FileVersion}.");

        if (double.IsNaN(Fps) || double.IsInfinity(Fps) || Fps <= 0)
            throw new InvalidDataException("FPS must be greater than zero.");

        if (Width <= 0)
            throw new InvalidDataException("Width must be greater than zero.");

        if (Height <= 0)
            throw new InvalidDataException("Height must be greater than zero.");

        if (ColorCount < 2)
            throw new InvalidDataException("Color count must be at least 2.");
    }
}
