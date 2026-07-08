using System.Text;
using CVC.File.Compression;

namespace CVC.File;

public class CVideoStream
{
    private long _length;
    private long _position;
    private long _framesPosition;
    
    private byte[]? _lastIntraCodedFrame;
    private List<KeyFrameInfo> _keyFrameIndex = new();
    
    private Stream _stream;
    private BinaryReader _reader = null!;
    private BinaryWriter _writer = null!;
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
            if (_length < 0)
                throw new InvalidDataException("Video stream length cannot be negative.");

            _framesPosition = _reader.BaseStream.Position;
            
            // Loading _keyFrameIndex
            _stream.Seek(-sizeof(long), SeekOrigin.End);
            var indexPosition = _reader.ReadInt64();
            if (indexPosition < _framesPosition || indexPosition > _stream.Length - sizeof(long))
                throw new InvalidDataException("Keyframe index position is outside the stream bounds.");
            
            _stream.Seek(indexPosition, SeekOrigin.Begin);
    
            var indexEntryCount = _reader.ReadInt32();
            if (indexEntryCount < 0)
                throw new InvalidDataException("Keyframe index entry count cannot be negative.");
    
            _keyFrameIndex = new List<KeyFrameInfo>(indexEntryCount);
            long previousFrameNumber = -1;
            for (int i = 0; i < indexEntryCount; i++)
            {
                var keyFrame = new KeyFrameInfo
                {
                    FrameNumber = _reader.ReadInt64(),
                    StreamPosition = _reader.ReadInt64()
                };

                if (keyFrame.FrameNumber <= previousFrameNumber || keyFrame.FrameNumber < 0 || keyFrame.FrameNumber >= _length)
                    throw new InvalidDataException("Keyframe index contains an invalid frame number.");

                if (keyFrame.StreamPosition < _framesPosition || keyFrame.StreamPosition >= indexPosition)
                    throw new InvalidDataException("Keyframe index contains an invalid stream position.");

                _keyFrameIndex.Add(keyFrame);
                previousFrameNumber = keyFrame.FrameNumber;
            }

            if (_length > 0 && (_keyFrameIndex.Count == 0 || _keyFrameIndex[0].FrameNumber != 0))
                throw new InvalidDataException("Video stream must start with a keyframe.");
            
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
            frame = ReadExactBytes(frameLength);

            _position++;
        }
        finally
        {
            _semaphore.Release();
        }

        if (frameType == FrameType.IntraCoded)
        {
            var decompressedFrame = RLE.Decompress(Brotli.Decompress(frame));
            ValidateDecodedFrameLength(decompressedFrame.Length, "I-frame");
            
            _lastIntraCodedFrame = decompressedFrame;
            return decompressedFrame;
        }
        else if (frameType == FrameType.PredictedFrame)
        {
            if (_lastIntraCodedFrame is null)
                throw new InvalidDataException("Predicted frame cannot be decoded before an intra-coded frame.");

            var decompressedFrame = RLE.DecompressAsSBytes(Brotli.Decompress(frame));
            ValidateDecodedFrameLength(decompressedFrame.Length, "P-frame");
            
            var result = PFrameDecoder.Instance.Convert(_lastIntraCodedFrame, decompressedFrame, _meta.Width, _meta.Height);
            _lastIntraCodedFrame = result;
            
            return result;
        }

        throw new InvalidDataException($"Unknown frame type: {frameType}.");
    }

    public void SkipFrame()
    {
        _ = ReadFrame();
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

        ValidateSourceFrame(frame);
        
        if (_position == 0)
        {
            EncodeIFrame(frame);
            _position++;
            return;
        }

        if (_lastIntraCodedFrame is null)
            throw new InvalidOperationException("Cannot encode a predicted frame before an intra-coded frame.");

        var pFrame = PFrameEncoder.Instance.Convert(_lastIntraCodedFrame, frame, _meta.Width, _meta.Height);
        
        double sum = 0;
        for (int i = 0; i < pFrame.Length; i++)
        {
            int delta = pFrame[i];
            sum += delta < 0 ? -delta : delta;
        }

        var average = pFrame.Length > 0 ? sum / pFrame.Length : 0;
        
        if(average <= PFrameK && CanApplyPFrameLosslessly(_lastIntraCodedFrame, frame, pFrame))
            EncodePFrame(frame, pFrame);
        else
            EncodeIFrame(frame);

        _position++;
    }

    /// <param name="frame">Source frame</param>
    private void EncodeIFrame(byte[] frame)
    {
        _keyFrameIndex.Add(new KeyFrameInfo
        {
            FrameNumber = _position,
            StreamPosition = _writer.BaseStream.Position
        });
        
        var stableFrame = frame.ToArray();
        _lastIntraCodedFrame = stableFrame;
        
        var rle = RLE.Compress(stableFrame);
        var compressed = Brotli.Compress(rle);
        
        _writer.Write((byte)FrameType.IntraCoded);
        _writer.Write(compressed.Length);
        _writer.Write(compressed);
    }
    
    /// <param name="sourceFrame">Source frame</param>
    /// <param name="pFrame">Calculated PFrame</param>
    private void EncodePFrame(byte[] sourceFrame, sbyte[] pFrame)
    {
        _lastIntraCodedFrame = sourceFrame.ToArray();
        
        var rle = RLE.Compress(pFrame);
        var compressed = Brotli.Compress(rle);
        
        _writer.Write((byte)FrameType.PredictedFrame);
        _writer.Write(compressed.Length);
        _writer.Write(compressed);
    }

    private byte[] ReadExactBytes(int count)
    {
        if (count < 0)
            throw new InvalidDataException("Frame payload length cannot be negative.");

        byte[] data = _reader.ReadBytes(count);
        if (data.Length != count)
            throw new EndOfStreamException($"Expected {count} frame payload bytes, got {data.Length}.");

        return data;
    }

    private int GetExpectedFrameLength()
    {
        try
        {
            return checked(_meta.Width * _meta.Height);
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException("Video frame dimensions are too large.", ex);
        }
    }

    private void ValidateSourceFrame(byte[] frame)
    {
        if (frame is null)
            throw new ArgumentNullException(nameof(frame));

        int expectedLength = GetExpectedFrameLength();
        if (frame.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Source frame length must be exactly {expectedLength} bytes for a {_meta.Width}x{_meta.Height} frame.",
                nameof(frame));
        }
    }

    private void ValidateDecodedFrameLength(int actualLength, string frameKind)
    {
        int expectedLength = GetExpectedFrameLength();
        if (actualLength != expectedLength)
            throw new InvalidDataException($"{frameKind} decoded to {actualLength} bytes, expected {expectedLength}.");
    }

    private static bool CanApplyPFrameLosslessly(byte[] baseFrame, byte[] targetFrame, sbyte[] pFrame)
    {
        for (int i = 0; i < pFrame.Length; i++)
        {
            if (baseFrame[i] + pFrame[i] != targetFrame[i])
                return false;
        }

        return true;
    }
    #endregion
}
