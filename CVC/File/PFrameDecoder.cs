using ILGPU;
using ILGPU.Runtime;

namespace CVC.File;

public class PFrameDecoder : IDisposable
{
    private readonly Context _context;
    private readonly Accelerator _accelerator;
    private readonly object _sync = new();
    private bool _disposed;
    
    private static readonly Lazy<PFrameDecoder> _lazyInstance = new(() => new PFrameDecoder());
    
    public static PFrameDecoder Instance => _lazyInstance.Value;

    private readonly Action<Index2D, ArrayView2D<byte, Stride2D.DenseX>, ArrayView2D<sbyte, Stride2D.DenseX>, ArrayView2D<byte, Stride2D.DenseX>> _kernel;

    private PFrameDecoder()
    {
        _context = Context.Create(builder => builder.Default().EnableAlgorithms());
        _accelerator = _context.GetPreferredDevice(false).CreateAccelerator(_context);
        
        _kernel = _accelerator.LoadAutoGroupedStreamKernel<
            Index2D, 
            ArrayView2D<byte, Stride2D.DenseX>, 
            ArrayView2D<sbyte, Stride2D.DenseX>, 
            ArrayView2D<byte, Stride2D.DenseX>>(GpuKernels.ApplyDeltaKernel);
    }
    
    public byte[] Convert(byte[] iFrame, sbyte[] pFrame, int width, int height)
    {
        int frameLength = ValidateInput(iFrame, pFrame, width, height);

        byte[,] encodedIFrame = new byte[height, width];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                encodedIFrame[y, x] = iFrame[index];
            }
        }
        
        sbyte[,] encodedPFrame = new sbyte[height, width];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                encodedPFrame[y, x] = pFrame[index];
            }
        }

        byte[,] result;
        lock (_sync)
        {
            ThrowIfDisposed();

            using var iBuffer = _accelerator.Allocate2DDenseX<byte>((height, width));
            using var pBuffer = _accelerator.Allocate2DDenseX<sbyte>((height, width));
            using var outputBuffer = _accelerator.Allocate2DDenseX<byte>((height, width));

            iBuffer.CopyFromCPU(encodedIFrame);
            pBuffer.CopyFromCPU(encodedPFrame);

            _kernel((height, width), iBuffer.View, pBuffer.View, outputBuffer.View);

            _accelerator.Synchronize();

            result = outputBuffer.GetAsArray2D();
        }

        byte[] output = new byte[frameLength];
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

    private static int ValidateInput(byte[] iFrame, sbyte[] pFrame, int width, int height)
    {
        if (iFrame is null)
            throw new ArgumentNullException(nameof(iFrame));

        if (pFrame is null)
            throw new ArgumentNullException(nameof(pFrame));

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

        if (iFrame.Length != expectedLength)
        {
            throw new ArgumentException(
                $"I-frame length must be exactly {expectedLength} bytes for a {width}x{height} frame.",
                nameof(iFrame));
        }

        if (pFrame.Length != expectedLength)
        {
            throw new ArgumentException(
                $"P-frame length must be exactly {expectedLength} bytes for a {width}x{height} frame.",
                nameof(pFrame));
        }

        return expectedLength;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PFrameDecoder));
    }
}
