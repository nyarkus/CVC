namespace CVC.File;

public sealed class EncodingStatistics
{
    private readonly FrameSizeStatistics _iFrames = new();
    private readonly FrameSizeStatistics _pFrames = new();

    public TimeSpan Duration { get; private set; }
    public long OutputBytes { get; private set; }

    public FrameSizeStatistics IFrames => _iFrames;
    public FrameSizeStatistics PFrames => _pFrames;

    public void Complete(TimeSpan duration, long outputBytes)
    {
        Duration = duration;
        OutputBytes = outputBytes;
    }
}

public sealed class FrameSizeStatistics
{
    private long _totalBytes;

    public long Count { get; private set; }
    public int WorstBytes { get; private set; }
    public double AverageBytes => Count == 0 ? 0 : (double)_totalBytes / Count;

    public void Record(int bytes)
    {
        Count++;
        _totalBytes += bytes;
        WorstBytes = Math.Max(WorstBytes, bytes);
    }
}