using System.Text;
using CVC.File.Compression;

namespace CVC;

public class CVideoStream
{
    private long _length;
    private long _position;
    private long _framesPosition;
    
    private byte[] _lastIntraCodedFrame;
    private List<KeyFrameInfo> _keyFrameIndex = new();
    
    private Stream _stream;
    private BinaryReader _reader;
    private BinaryWriter _writer;
    private CVideoMeta _meta;
    private CVideoSteamMode _mode;
    
    public long Length => _length;  
    public long Position => _position;

    private Semaphore _semaphore = new(1, 1);

    public CVideoStream(Stream stream, CVideoMeta meta, CVideoSteamMode mode)
    {
        _mode = mode;
        _stream = stream;
        _meta = meta;
        _position = 0;
        
        if (mode == CVideoSteamMode.Reading)
        {
            _reader = new BinaryReader(stream, Encoding.ASCII, true);
            _length = _reader.ReadInt64();
            _framesPosition = _reader.BaseStream.Position;
            
            // Loading _keyFrameIndex
            _stream.Seek(-sizeof(long), SeekOrigin.End);
            var indexPosition = _reader.ReadInt64();
            
            _stream.Seek(indexPosition, SeekOrigin.Begin);
    
            var indexEntryCount = _reader.ReadInt32();
    
            _keyFrameIndex = new List<KeyFrameInfo>(indexEntryCount);
            for (int i = 0; i < indexEntryCount; i++)
            {
                _keyFrameIndex.Add(new KeyFrameInfo
                {
                    FrameNumber = _reader.ReadInt64(),
                    StreamPosition = _reader.ReadInt64()
                });
            }
            
            _stream.Seek(_framesPosition, SeekOrigin.Begin);
        }
        else // mode == Writing
        {
            _writer = new BinaryWriter(stream, Encoding.ASCII, true);
        }
    }

    #region Reading
    public byte[] ReadFrame()
    {
        _semaphore.WaitOne();

        FrameType frameType;
        byte[] frame;
        try
        {
            if (_position >= _length)
                return Array.Empty<byte>();

            frameType = (FrameType)_reader.ReadByte();
            int frameLength = _reader.ReadInt32();
            frame = _reader.ReadBytes(frameLength);

            _position++;
        }
        finally
        {
            _semaphore.Release();
        }

        if (frameType == FrameType.IntraCoded)
        {
            var decompressedFrame = RLE.Decompress(Brotli.Decompress(frame));
            
            _lastIntraCodedFrame = decompressedFrame;
            return decompressedFrame;
        }
        else // if(frameType == FrameType.PredictedFrame)
        {
            var decompressedFrame = RLE.DecompressAsSBytes(Brotli.Decompress(frame));
            
            var result = PFrameDecoder.Instance.Convert(_lastIntraCodedFrame, decompressedFrame, _meta.Width, _meta.Height);
            _lastIntraCodedFrame = result;
            
            return result;
        }
    }

    public void SkipFrame()
    {
        _semaphore.WaitOne();

        try
        {
            if (_position >= _length)
                return;

            _ = (FrameType)_reader.ReadByte();
            var frameLength = _reader.ReadInt32();
            _ = _reader.ReadBytes(frameLength);
            _position++;
        }
        finally
        {
            _semaphore.Release();
        }
    }
    
    public long Seek(long offset, SeekOrigin origin)
    {
        if (_mode == CVideoSteamMode.Writing)
            throw new InvalidOperationException("Cannot seek while in writing mode.");

        long targetPosition;
        switch (origin)
        {
            case SeekOrigin.Begin:
                targetPosition = offset;
                break;
            case SeekOrigin.Current:
                targetPosition = _position + offset;
                break;
            case SeekOrigin.End:
                targetPosition = _length + offset;
                break;
            default:
                throw new ArgumentException("Invalid SeekOrigin", nameof(origin));
        }

        if (targetPosition < 0 || targetPosition >= _length)
            throw new ArgumentOutOfRangeException(nameof(offset), "Seek position is outside the bounds of the stream");
    
        if (targetPosition == _position)
            return _position;
    
        _semaphore.WaitOne();
        try
        {
            var keyFrame = _keyFrameIndex
                .LastOrDefault(kf => kf.FrameNumber <= targetPosition);

            if (keyFrame.FrameNumber == 0 && targetPosition > 0)
                keyFrame = _keyFrameIndex.First();

            _reader.BaseStream.Position = keyFrame.StreamPosition;
            _position = keyFrame.FrameNumber;
            _lastIntraCodedFrame = null;
        }
        finally
        {
            _semaphore.Release();
        }

        while (_position < targetPosition)
            ReadFrame();
        
        return _position;
    }
    #endregion

    #region Writing
    
    private long _lengthPosition;
    private bool _isWritingStarted;
    
    internal void StartWriting()
    {
        if (_isWritingStarted)
            throw new InvalidOperationException();
        
        _lengthPosition = _writer.BaseStream.Position;
        _writer.Write(long.MaxValue);
        _isWritingStarted = true;
    }

    internal void EndWriting()
    {
        if (!_isWritingStarted)
            throw new InvalidOperationException();

        var currentFramesPos = _writer.BaseStream.Position;
        
        _writer.Write(_keyFrameIndex.Count);
        foreach (var keyFrame in _keyFrameIndex)
        {
            _writer.Write(keyFrame.FrameNumber);
            _writer.Write(keyFrame.StreamPosition);
        }
        
        _writer.Write(currentFramesPos);
        
        _writer.BaseStream.Position = _lengthPosition;
        _writer.Write(_position);
        _isWritingStarted = false;
    
        _writer.Close();
    }

    /// <summary>
    /// If the average delta of the changed pixels is less than or equal to this value, a PFrame will be created instead of an IFrame
    /// </summary>
    public double PFrameK = 0.01;
    
    /// <summary>
    /// Encodes frame
    /// </summary>
    /// <param name="frame">Source frame</param>
    /// <exception cref="InvalidOperationException"></exception>
    public void WriteFrame(byte[] frame)
    {
        if (!_isWritingStarted)
            throw new InvalidOperationException();
        
        if (_position == 0)
        {
            EncodeIFrame(frame);
            _position++;
            return;
        }

        _position++;
        var pFrame = PFrameEncoder.Instance.Convert(_lastIntraCodedFrame, frame, _meta.Width, _meta.Height);
        
        double sum = 0;
        for (int i = 0; i < pFrame.Length; i++)
        {
            sum += pFrame[i];
        }

        var average = Math.Abs(pFrame.Length > 0 ? sum / pFrame.Length : 0);
        
        if(average <= PFrameK)
            EncodePFrame(frame, pFrame);
        else
            EncodeIFrame(frame);
    }

    /// <param name="frame">Source frame</param>
    private void EncodeIFrame(byte[] frame)
    {
        _keyFrameIndex.Add(new KeyFrameInfo
        {
            FrameNumber = _position,
            StreamPosition = _writer.BaseStream.Position
        });
        
        _lastIntraCodedFrame = frame;
        
        var rle = RLE.Compress(frame);
        var compressed = Brotli.Compress(rle);
        
        _writer.Write((byte)FrameType.IntraCoded);
        _writer.Write(compressed.Length);
        _writer.Write(compressed);
    }
    
    /// <param name="sourceFrame">Source frame</param>
    /// <param name="pFrame">Calculated PFrame</param>
    private void EncodePFrame(byte[] sourceFrame, sbyte[] pFrame)
    {
        _lastIntraCodedFrame = sourceFrame;
        
        var rle = RLE.Compress(pFrame);
        var compressed = Brotli.Compress(rle);
        
        _writer.Write((byte)FrameType.PredictedFrame);
        _writer.Write(compressed.Length);
        _writer.Write(compressed);
    }
    #endregion
}
