using ILGPU;
using ILGPU.Runtime;

namespace CVC.File;

public class PFrameEncoder : IDisposable
{
    private readonly Context _context;
    private readonly Accelerator _accelerator;
    private readonly object _sync = new();
    private bool _disposed;
    
    private static readonly Lazy<PFrameEncoder> _lazyInstance = new(() => new PFrameEncoder());
    
    public static PFrameEncoder Instance => _lazyInstance.Value;

    private readonly Action<Index2D, ArrayView2D<byte, Stride2D.DenseX>, ArrayView2D<byte, Stride2D.DenseX>, ArrayView2D<sbyte, Stride2D.DenseX>> _kernel;

    private PFrameEncoder()
    {
        _context = Context.Create(builder => builder.Default().EnableAlgorithms());
        _accelerator = _context.GetPreferredDevice(false).CreateAccelerator(_context);
        
        _kernel = _accelerator.LoadAutoGroupedStreamKernel<
            Index2D, 
            ArrayView2D<byte, Stride2D.DenseX>, 
            ArrayView2D<byte, Stride2D.DenseX>, 
            ArrayView2D<sbyte, Stride2D.DenseX>>(GpuKernels.CalculateDeltaKernel);
    }
    
    public sbyte[] Convert(byte[] frame1, byte[] frame2, int width, int height)
    {
        int frameLength = ValidateInput(frame1, frame2, width, height);

        byte[,] encodedFrame1 = new byte[height, width];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                encodedFrame1[y, x] = frame1[index];
            }
        }
        
        byte[,] encodedFrame2 = new byte[height, width];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                encodedFrame2[y, x] = frame2[index];
            }
        }

        sbyte[,] result;
        lock (_sync)
        {
            ThrowIfDisposed();

            using var buffer1 = _accelerator.Allocate2DDenseX<byte>((height, width));
            using var buffer2 = _accelerator.Allocate2DDenseX<byte>((height, width));
            using var outputBuffer = _accelerator.Allocate2DDenseX<sbyte>((height, width));

            buffer1.CopyFromCPU(encodedFrame1);
            buffer2.CopyFromCPU(encodedFrame2);

            _kernel((height, width), buffer1.View, buffer2.View, outputBuffer.View);

            _accelerator.Synchronize();

            result = outputBuffer.GetAsArray2D();
        }

        sbyte[] output = new sbyte[frameLength];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                output[index] = result[y, x];
            }
        }

        return output;
    }
    
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _accelerator.Dispose();
            _context.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private static int ValidateInput(byte[] frame1, byte[] frame2, int width, int height)
    {
        if (frame1 is null)
            throw new ArgumentNullException(nameof(frame1));

        if (frame2 is null)
            throw new ArgumentNullException(nameof(frame2));

        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width must be greater than zero.");

        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "Height must be greater than zero.");

        int expectedLength;
        try
        {
            expectedLength = checked(width * height);
        }
        catch (OverflowException ex)
        {
            throw new ArgumentException("Frame dimensions are too large.", nameof(width), ex);
        }

        if (frame1.Length != expectedLength)
        {
            throw new ArgumentException(
                $"First frame length must be exactly {expectedLength} bytes for a {width}x{height} frame.",
                nameof(frame1));
        }

        if (frame2.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Second frame length must be exactly {expectedLength} bytes for a {width}x{height} frame.",
                nameof(frame2));
        }

        return expectedLength;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PFrameEncoder));
    }
}
