using CVC.File;

namespace CVC.Tests;

public class CVideoMetaTests
{
    [Fact]
    public void RejectsInvalidValues()
    {
        Assert.Throws<InvalidDataException>(() => CVideoMeta.Create(0, 1, 1, 2));
        Assert.Throws<InvalidDataException>(() => CVideoMeta.Create(24, 0, 1, 2));
        Assert.Throws<InvalidDataException>(() => CVideoMeta.Create(24, 1, 0, 2));
        Assert.Throws<InvalidDataException>(() => CVideoMeta.Create(24, 1, 1, 1));
    }
}
