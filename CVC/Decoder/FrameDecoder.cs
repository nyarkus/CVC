using System.Collections.Concurrent;
using System.Diagnostics;

namespace CVC.Decoder
{
    // TODO: Rename this class to VideoBuffer or something like this
    public class FrameDecoder
    {
        private string _chars;

        private CVideoStream _stream;
        private CVideoMeta _meta;

        private ConcurrentDictionary<long, Frame> _bufferedFrames = new();
        private int _framesInBuffer = 0;

        public int BufferSize { get; set; } = 240;

        private ReaderWriterLockSlim _bufferLock = new ReaderWriterLockSlim();

        public long LastDecodedFrame
        {
            get
            {
                _bufferLock.EnterReadLock();
                try
                {
                    return _bufferedFrames.IsEmpty ? -1 : _bufferedFrames.Keys.Max();
                }
                finally
                {
                    _bufferLock.ExitReadLock();
                }
            }
        }

        public void Seek(int targetFrame)
        {
            _bufferLock.EnterWriteLock();
            try
            {
                _stream.Seek(Math.Clamp(targetFrame, 0, _stream.Length - 1), SeekOrigin.Begin);
                
                var outdated = _bufferedFrames.Keys
                    .Where(k => k < targetFrame - 10 || k > targetFrame + 10)
                    .ToList();

                foreach (var key in outdated)
                {
                    if (_bufferedFrames.TryRemove(key, out _))
                        _framesInBuffer--;
                }
            }
            finally
            {
                _bufferLock.ExitWriteLock();
            }
        }

        public bool BufferIsFull
        {
            get
            {
                if (_stream.Length < BufferSize)
                    return _framesInBuffer >= _stream.Length - _stream.Position;
                else
                    return _framesInBuffer >= BufferSize;
            }
        }

        public void RecalculateBuffer()
        {
            while (_stream.Position < _stream.Length)
            {
                try
                {
                    if (BufferIsFull)
                    {
                        Thread.Sleep(1);
                        continue;
                    }

                    int neededFrames = BufferSize - _framesInBuffer;
                    if (neededFrames <= 0) continue;

                    List<byte[]> preloaded = new(neededFrames);
                    
                    lock (_stream)
                    {
                        for (int i = 0; i < neededFrames && _stream.Position < _stream.Length; i++)
                        {
                            byte[] frameData = _stream.ReadFrame();
                            if (frameData.Length == 0) break;
                            preloaded.Add(frameData);
                        }
                    }

                    if (preloaded.Count == 0) continue;

                    var tempBuffer = new ConcurrentDictionary<long, Frame>();
                    var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
                    
                    long startPosition = _stream.Position - preloaded.Count;

                    Parallel.For(0, preloaded.Count, options, i =>
                    {
                        try
                        {
                            string content = FrameConverter.Instance.Convert(preloaded[i], _chars, _meta.ColorCount,
                                _meta.Width, _meta.Height);
                            long index = startPosition + i;
                            tempBuffer.TryAdd(index, new Frame(content, index));
                        }
                        catch (Exception ex)
                        {
                            #if DEBUG
                            Debug.WriteLine(ex);
                            #endif
                        }
                    });

                    _bufferLock.EnterWriteLock();
                    try
                    {
                        foreach (var pair in tempBuffer)
                        {
                            if (_bufferedFrames.TryAdd(pair.Key, pair.Value))
                            {
                                _framesInBuffer++;
                            }
                        }
                    }
                    finally
                    {
                        _bufferLock.ExitWriteLock();
                    }
                }
                catch(Exception ex)
                {
                    #if DEBUG
                    Debug.WriteLine($"Error in RecalculateBuffer: {ex}");
                    #endif
                }
            }
        }

        public string ReadFrame(int frame)
        {
            _bufferLock.EnterReadLock();
            try
            {
                if (_bufferedFrames.TryGetValue(frame, out Frame frameData))
                    return frameData.Content;
            }
            finally
            {
                _bufferLock.ExitReadLock();
            }
            
            return null;
        }

        public FrameDecoder(CVideoFile video, string chars = " .:-=+*#%@")
        {
            if (video.VideoStream is null)
                throw new InvalidOperationException();
            
            _stream = video.VideoStream;
            _meta = video.Meta;
            _chars = chars;

            ThreadPool.SetMinThreads(Environment.ProcessorCount * 2, Environment.ProcessorCount * 2);
        }
    }

    internal struct Frame
    {
        public string Content { get; }
        public long Index { get; }

        public Frame(string content, long index)
        {
            Content = content;
            Index = index;
        }
    }
}