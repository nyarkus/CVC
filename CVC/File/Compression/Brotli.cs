using System.IO.Compression;

namespace CVC.File.Compression;

public static class Brotli
{
    public static byte[] Compress(byte[] data)
    {
        if (data is null)
            throw new ArgumentNullException(nameof(data));

        using (var outputStream = new MemoryStream())
        {
            using (var compressedStream = new BrotliStream(outputStream, CompressionLevel.Optimal))
            {
                compressedStream.Write(data, 0, data.Length);
            }
            return outputStream.ToArray();
        }
    }
    
    public static byte[] Decompress(byte[] data)
    {
        if (data is null)
            throw new ArgumentNullException(nameof(data));

        using (var inputStream = new MemoryStream(data))
        {
            using (var outputStream = new MemoryStream())
            {
                using (var compressedStream = new BrotliStream(inputStream, CompressionMode.Decompress))
                {
                    compressedStream.CopyTo(outputStream);
                }
                return outputStream.ToArray();
            }
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
