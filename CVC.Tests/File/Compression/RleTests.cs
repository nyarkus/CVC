using CVC.File.Compression;

namespace CVC.Tests;

public class RleTests
{
    [Fact]
    public void RoundTripsLongRuns()
    {
        byte[] source = Enumerable
            .Repeat((byte)7, 300)
            .Concat(new byte[] { 1, 2, 2, 3, 3, 3, 4 })
            .ToArray();

        var compressed = RLE.Compress(source);
        var decompressed = RLE.Decompress(compressed);

        Assert.Equal(source, decompressed);
        Assert.True(compressed.Length < source.Length);
    }
}
