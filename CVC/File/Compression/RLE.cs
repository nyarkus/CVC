namespace CVC.File.Compression;

/// <summary>
/// Run-Length Encoding
/// </summary>
public static class RLE
{
    public static byte[] Compress(byte[] data)
    {
        if (data is null)
            throw new ArgumentNullException(nameof(data));

        using (var ms = new MemoryStream())
        {
            for (int i = 0; i < data.Length; i++)
            {
                byte currentByte = data[i];
                byte runLength = 1;
                
                while (i + 1 < data.Length && data[i] == data[i + 1] && runLength < byte.MaxValue)
                {
                    runLength++;
                    i++;
                }
                
                ms.WriteByte(runLength);
                ms.WriteByte(currentByte);
            }
            return ms.ToArray();
        }
    }
    
    public static byte[] Decompress(byte[] data)
    {
        if (data is null)
            throw new ArgumentNullException(nameof(data));

        if (data.Length % 2 != 0)
            throw new InvalidDataException("RLE payload length must be even.");

        using (var ms = new MemoryStream())
        {
            for (int i = 0; i < data.Length; i += 2)
            {
                byte runLength = data[i];
                byte value = data[i + 1];
                
                for (int j = 0; j < runLength; j++)
                {
                    ms.WriteByte(value);
                }
            }
            return ms.ToArray();
        }
    }
    
    #region sbytes

    public static byte[] Compress(sbyte[] data)
    {
        if (data is null)
            throw new ArgumentNullException(nameof(data));

        byte[] buffer = new byte[data.Length];
        Buffer.BlockCopy(data, 0, buffer, 0, data.Length);
        
        return Compress(buffer);
    }
    
    public static sbyte[] DecompressAsSBytes(byte[] data)
    {
        var result = Decompress(data);
        sbyte[] buffer = new sbyte[result.Length];
        
        Buffer.BlockCopy(result, 0, buffer, 0, result.Length);

        return buffer;
    }

    #endregion
}
