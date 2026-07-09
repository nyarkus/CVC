using System.Text;
using CVC.File.Compression;

namespace CVC.File;

public class CVideoStream
{
    private long _length;
    private long _position;
    private long _framesPosition;
    private long _indexPosition;
    
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
            _reader = new BinaryReader(stream, Encoding.UTF8, true);
            _length = _reader.ReadInt64();
            if (_length < 0)
                throw new InvalidDataException("Video stream length cannot be negative.");

            _framesPosition = _reader.BaseStream.Position;
            
            // Loading _keyFrameIndex
            _stream.Seek(-sizeof(long), SeekOrigin.End);
            _indexPosition = _reader.ReadInt64();
            if (_indexPosition < _framesPosition || _indexPosition > _stream.Length - sizeof(long) - sizeof(int))
                throw new InvalidDataException("Keyframe index position is outside the stream bounds.");
            
            _stream.Seek(_indexPosition, SeekOrigin.Begin);
    
            var indexEntryCount = _reader.ReadInt32();
            if (indexEntryCount < 0)
                throw new InvalidDataException("Keyframe index entry count cannot be negative.");

            var indexPayloadLength = _stream.Length - sizeof(long) - _indexPosition - sizeof(int);
            var maxIndexEntryCount = indexPayloadLength / (sizeof(long) * 2);
            if (indexEntryCount > maxIndexEntryCount || indexEntryCount > _length)
                throw new InvalidDataException("Keyframe index entry count is outside the stream bounds.");
    
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

                if (keyFrame.StreamPosition < _framesPosition || keyFrame.StreamPosition >= _indexPosition)
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
            _writer = new BinaryWriter(stream, Encoding.UTF8, true);
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
            int expectedFrameLength = GetExpectedFrameLength();
            var decompressedFrame = RLE.Decompress(
                Brotli.Decompress(frame, GetMaxRleFrameLength()),
                expectedFrameLength);
            ValidateDecodedFrameLength(decompressedFrame.Length, "I-frame");
            
            _lastIntraCodedFrame = decompressedFrame;
            return decompressedFrame;
        }
        else if (frameType == FrameType.PredictedFrame)
        {
            if (_lastIntraCodedFrame is null)
                throw new InvalidDataException("Predicted frame cannot be decoded before an intra-coded frame.");

            int expectedFrameLength = GetExpectedFrameLength();
            var decompressedFrame = RLE.DecompressAsSBytes(
                Brotli.Decompress(frame, GetMaxRleFrameLength()),
                expectedFrameLength);
            ValidateDecodedFrameLength(decompressedFrame.Length, "P-frame");
            
            var result = PFrameDecoder.Instance.Convert(_lastIntraCodedFrame, decompressedFrame, _meta.Width, _meta.Height);
            _lastIntraCodedFrame = result;
            
            return result;
        }

        throw new InvalidDataException($"Unknown frame type: {frameType}.");
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

        if (targetPosition < 0 || targetPosition > _length)
            throw new ArgumentOutOfRangeException(nameof(offset), "Seek position is outside the bounds of the stream");
    
        if (targetPosition == _position)
            return _position;

        if (targetPosition == _length)
        {
            _semaphore.WaitOne();
            try
            {
                _reader.BaseStream.Position = _indexPosition;
                _position = _length;
                _lastIntraCodedFrame = null;
            }
            finally
            {
                _semaphore.Release();
            }

            return _position;
        }
    
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
    /// Defines how the encoder chooses between I-frames and P-frames.
    /// </summary>
    public FrameEncodingMode EncodingMode = FrameEncodingMode.Fast;
    
    public System.IO.Compression.CompressionLevel BrotliCompressionLevel = System.IO.Compression.CompressionLevel.Optimal;

    public EncodingStatistics? Statistics;
    
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

        switch (EncodingMode)
        {
            case FrameEncodingMode.Fast:
                EncodeFast(frame, pFrame);
                break;
            case FrameEncodingMode.BestSize:
                EncodeBestSize(frame, pFrame);
                break;
            case FrameEncodingMode.Hybrid:
                EncodeHybrid(frame, pFrame);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(EncodingMode), EncodingMode, "Unknown frame encoding mode.");
        }

        _position++;
    }

    private void EncodeFast(byte[] frame, sbyte[] pFrame)
    {
        if (CalculateAverageDelta(pFrame) <= PFrameK && CanApplyPFrameLosslessly(_lastIntraCodedFrame!, frame, pFrame))
            EncodePFrame(frame, pFrame);
        else
            EncodeIFrame(frame);
    }

    private void EncodeBestSize(byte[] frame, sbyte[] pFrame)
    {
        if (!CanApplyPFrameLosslessly(_lastIntraCodedFrame!, frame, pFrame))
        {
            EncodeIFrame(frame);
            return;
        }

        var compressedIFrame = CompressIFrame(frame);
        var compressedPFrame = CompressPFrame(pFrame);

        if (GetIFrameSizeCost(compressedIFrame) <= GetPFrameSizeCost(compressedPFrame))
            WriteIFrame(frame, compressedIFrame);
        else
            WritePFrame(frame, compressedPFrame);
    }

    private void EncodeHybrid(byte[] frame, sbyte[] pFrame)
    {
        if (!CanApplyPFrameLosslessly(_lastIntraCodedFrame!, frame, pFrame))
        {
            EncodeIFrame(frame);
            return;
        }

        if (CalculateAverageDelta(pFrame) <= PFrameK)
        {
            EncodePFrame(frame, pFrame);
            return;
        }

        EncodeBestSize(frame, pFrame);
    }

    /// <param name="frame">Source frame</param>
    private void EncodeIFrame(byte[] frame)
        => WriteIFrame(frame, CompressIFrame(frame));
    
    private void WriteIFrame(byte[] frame, byte[] compressed)
    {
        _keyFrameIndex.Add(new KeyFrameInfo
        {
            FrameNumber = _position,
            StreamPosition = _writer.BaseStream.Position
        });
        
        var stableFrame = frame.ToArray();
        _lastIntraCodedFrame = stableFrame;
        
        _writer.Write((byte)FrameType.IntraCoded);
        _writer.Write(compressed.Length);
        _writer.Write(compressed);
        
        Statistics?.IFrames.Record(compressed.Length);
    }
    
    /// <param name="sourceFrame">Source frame</param>
    /// <param name="pFrame">Calculated PFrame</param>
    private void EncodePFrame(byte[] sourceFrame, sbyte[] pFrame)
        => WritePFrame(sourceFrame, CompressPFrame(pFrame));

    private void WritePFrame(byte[] sourceFrame, byte[] compressed)
    {
        _lastIntraCodedFrame = sourceFrame.ToArray();

        _writer.Write((byte)FrameType.PredictedFrame);
        _writer.Write(compressed.Length);
        _writer.Write(compressed);
        
        Statistics?.PFrames.Record(compressed.Length);
    }

    private byte[] CompressIFrame(byte[] frame)
        => Brotli.Compress(RLE.Compress(frame), BrotliCompressionLevel);
    
    private byte[] CompressPFrame(sbyte[] pFrame)
        => Brotli.Compress(RLE.Compress(pFrame), BrotliCompressionLevel);
    
    private static int GetIFrameSizeCost(byte[] compressed)
        => compressed.Length + sizeof(long) * 2;
    
    private static int GetPFrameSizeCost(byte[] compressed)
        => compressed.Length;

    private static double CalculateAverageDelta(sbyte[] pFrame)
    {
        double sum = 0;
        for (int i = 0; i < pFrame.Length; i++)
        {
            int delta = pFrame[i];
            sum += delta < 0 ? -delta : delta;
        }

        return pFrame.Length > 0 ? sum / pFrame.Length : 0;
    }

    private byte[] ReadExactBytes(int count)
    {
        if (count < 0)
            throw new InvalidDataException("Frame payload length cannot be negative.");

        if (_mode == CVideoSteamMode.Reading && _reader.BaseStream.Position + count > _indexPosition)
            throw new EndOfStreamException("Frame payload extends beyond the video frame data.");

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

    private int GetMaxRleFrameLength()
    {
        int expectedLength = GetExpectedFrameLength();
        try
        {
            return checked(expectedLength * 2);
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
