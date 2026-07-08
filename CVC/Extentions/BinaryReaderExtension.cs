namespace CVC.Extentions;

public static class BinaryReaderExtension
{
    public static sbyte[] ReadSBytes(this BinaryReader reader, int count)
    {
        byte[] buffer = reader.ReadBytes(count);
        
        sbyte[] result = new sbyte[count];
        Buffer.BlockCopy(buffer, 0, result, 0, count);
    
        return result;
    }
}