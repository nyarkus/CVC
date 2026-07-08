using System.Diagnostics;

namespace CVC.Decoder
{
    public sealed class FrameDecoder : IDisposable
    {
        private readonly object _sync = new();
        private readonly SortedDictionary<int, string> _bufferedFrames = new();
        private readonly CVideoStream _stream;
        private readonly CVideoMeta _meta;
        private readonly string _chars;
        private readonly CancellationTokenSource _cancellation = new();

        private Task? _worker;
        private Exception? _workerError;
        private int _requestedFrame;
        private int _nextFrameToRead;
        private bool _streamPositionKnown;
        private bool _disposed;

        public int BufferSize { get; set; } = 240;
        public int BackBufferSize { get; set; } = 8;

        public long LastDecodedFrame
        {
            get
            {
                lock (_sync)
                    return _bufferedFrames.Count == 0 ? -1 : _bufferedFrames.Keys.Max();
            }
        }

        public bool BufferIsFull
        {
            get
            {
                lock (_sync)
                    return CountBufferedFramesAhead(_requestedFrame) >= GetTargetBufferSize(_requestedFrame);
            }
        }

        public FrameDecoder(CVideoFile video, string chars = " .:-=+*#%@")
        {
            if (video.VideoStream is null)
                throw new InvalidOperationException("Video stream is not available.");

            _stream = video.VideoStream;
            _meta = video.Meta;
            _chars = chars;
        }

        public void Start()
        {
            ThrowIfDisposed();

            lock (_sync)
            {
                _worker ??= Task.Factory.StartNew(
                    DecodeLoop,
                    _cancellation.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }
        }

        public void RecalculateBuffer()
        {
            Start();
            _worker?.Wait();
        }

        public void Seek(int targetFrame)
        {
            RequestFrame(targetFrame, forceSeek: true);
        }

        public void RequestFrame(int frame)
        {
            RequestFrame(frame, forceSeek: false);
        }

        public string? ReadFrame(int frame)
        {
            RequestFrame(frame);

            lock (_sync)
            {
                ThrowWorkerErrorIfAny();
                return _bufferedFrames.TryGetValue(frame, out var content) ? content : null;
            }
        }

        public string? WaitForFrame(int frame, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            RequestFrame(frame);

            var timeoutAt = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
            using var registration = cancellationToken.Register(PulseWaiters, useSynchronizationContext: false);

            lock (_sync)
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ThrowWorkerErrorIfAny();

                    if (_bufferedFrames.TryGetValue(frame, out var content))
                        return content;

                    var remainingTicks = timeoutAt - Stopwatch.GetTimestamp();
                    if (remainingTicks <= 0)
                        return null;

                    var waitTime = TimeSpan.FromSeconds((double)remainingTicks / Stopwatch.Frequency);
                    Monitor.Wait(_sync, waitTime > TimeSpan.FromMilliseconds(50) ? TimeSpan.FromMilliseconds(50) : waitTime);
                }
            }
        }

        public bool WaitUntilBuffered(int startFrame, int minFrames, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            RequestFrame(startFrame);

            minFrames = Math.Max(1, minFrames);
            var timeoutAt = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
            using var registration = cancellationToken.Register(PulseWaiters, useSynchronizationContext: false);

            lock (_sync)
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ThrowWorkerErrorIfAny();

                    if (CountBufferedFramesAhead(startFrame) >= Math.Min(minFrames, GetTargetBufferSize(startFrame)))
                        return true;

                    var remainingTicks = timeoutAt - Stopwatch.GetTimestamp();
                    if (remainingTicks <= 0)
                        return false;

                    var waitTime = TimeSpan.FromSeconds((double)remainingTicks / Stopwatch.Frequency);
                    Monitor.Wait(_sync, waitTime > TimeSpan.FromMilliseconds(50) ? TimeSpan.FromMilliseconds(50) : waitTime);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cancellation.Cancel();
            PulseWaiters();

            try
            {
                _worker?.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException) { }

            _cancellation.Dispose();
        }

        private void RequestFrame(int frame, bool forceSeek)
        {
            ThrowIfDisposed();
            Start();

            int clampedFrame = ClampFrame(frame);
            lock (_sync)
            {
                _requestedFrame = clampedFrame;

                if (forceSeek || ShouldReposition(clampedFrame))
                {
                    _streamPositionKnown = false;
                    PruneBuffer(clampedFrame, keepOnlyNearTarget: !forceSeek);
                }
                else
                {
                    PruneBuffer(clampedFrame, keepOnlyNearTarget: false);
                }

                Monitor.PulseAll(_sync);
            }
        }

        private void DecodeLoop()
        {
            try
            {
                while (!_cancellation.IsCancellationRequested)
                {
                    int targetFrame;

                    lock (_sync)
                    {
                        targetFrame = _requestedFrame;

                        if (CountBufferedFramesAhead(targetFrame) >= GetTargetBufferSize(targetFrame))
                        {
                            Monitor.Wait(_sync, TimeSpan.FromMilliseconds(20));
                            continue;
                        }
                    }

                    EnsureStreamPosition(targetFrame);

                    if (_nextFrameToRead >= _stream.Length)
                    {
                        lock (_sync)
                            Monitor.Wait(_sync, TimeSpan.FromMilliseconds(20));
                        continue;
                    }

                    int frameIndex = _nextFrameToRead;
                    byte[] encodedFrame = _stream.ReadFrame();
                    _nextFrameToRead = (int)_stream.Position;

                    if (encodedFrame.Length == 0)
                    {
                        lock (_sync)
                            Monitor.Wait(_sync, TimeSpan.FromMilliseconds(20));
                        continue;
                    }

                    var decodedFrame = FrameConverter.Instance.Convert(
                        encodedFrame,
                        _chars,
                        _meta.ColorCount,
                        _meta.Width,
                        _meta.Height);

                    lock (_sync)
                    {
                        _bufferedFrames[frameIndex] = decodedFrame;
                        PruneBuffer(_requestedFrame, keepOnlyNearTarget: false);
                        Monitor.PulseAll(_sync);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    _workerError = ex;
                    Monitor.PulseAll(_sync);
                }
            }
        }

        private void EnsureStreamPosition(int targetFrame)
        {
            bool shouldSeek;

            lock (_sync)
            {
                shouldSeek = !_streamPositionKnown || _nextFrameToRead < targetFrame || _nextFrameToRead > targetFrame + BufferSize;
            }

            if (!shouldSeek)
                return;

            int seekTarget = ClampFrame(targetFrame);
            _stream.Seek(seekTarget, SeekOrigin.Begin);

            lock (_sync)
            {
                _nextFrameToRead = (int)_stream.Position;
                _streamPositionKnown = true;
                PruneBuffer(seekTarget, keepOnlyNearTarget: true);
            }
        }

        private bool ShouldReposition(int targetFrame)
        {
            if (!_streamPositionKnown)
                return true;

            if (_bufferedFrames.ContainsKey(targetFrame))
                return false;

            return targetFrame < _nextFrameToRead - BackBufferSize || targetFrame > _nextFrameToRead + BufferSize;
        }

        private int CountBufferedFramesAhead(int startFrame)
        {
            int endFrame = startFrame + BufferSize;
            int count = 0;

            foreach (int frame in _bufferedFrames.Keys)
            {
                if (frame >= startFrame && frame < endFrame)
                    count++;
            }

            return count;
        }

        private int GetTargetBufferSize(int startFrame)
        {
            return Math.Min(BufferSize, Math.Max(0, (int)_stream.Length - startFrame));
        }

        private void PruneBuffer(int targetFrame, bool keepOnlyNearTarget)
        {
            int firstFrame = keepOnlyNearTarget ? targetFrame : Math.Max(0, targetFrame - BackBufferSize);
            int lastFrame = targetFrame + BufferSize - 1;
            int[] outdatedFrames = _bufferedFrames.Keys
                .Where(frame => frame < firstFrame || frame > lastFrame)
                .ToArray();

            foreach (int frame in outdatedFrames)
                _bufferedFrames.Remove(frame);
        }

        private int ClampFrame(int frame)
        {
            if (_stream.Length <= 0)
                return 0;

            return Math.Clamp(frame, 0, (int)_stream.Length - 1);
        }

        private void PulseWaiters()
        {
            lock (_sync)
                Monitor.PulseAll(_sync);
        }

        private void ThrowWorkerErrorIfAny()
        {
            if (_workerError is not null)
                throw new InvalidOperationException("Frame decoder worker failed.", _workerError);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FrameDecoder));
        }
    }
}
