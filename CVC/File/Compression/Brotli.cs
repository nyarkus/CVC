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
        return Decompress(data, int.MaxValue);
    }

    public static byte[] Decompress(byte[] data, int maxOutputBytes)
    {
        if (data is null)
            throw new ArgumentNullException(nameof(data));

        if (maxOutputBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maxOutputBytes));

        using (var inputStream = new MemoryStream(data))
        {
            using (var outputStream = new MemoryStream())
            {
                using (var compressedStream = new BrotliStream(inputStream, CompressionMode.Decompress))
                {
                    var buffer = new byte[81920];
                    while (true)
                    {
                        var read = compressedStream.Read(buffer, 0, buffer.Length);
                        if (read == 0)
                            break;

                        if (outputStream.Length + read > maxOutputBytes)
                            throw new InvalidDataException("Brotli payload exceeds the maximum decoded size.");

                        outputStream.Write(buffer, 0, read);
                    }
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
