using ILGPU;
using System.Text;
using ILGPU.Runtime;

namespace CVC.Decoder;

public class FrameConverter : IDisposable
{
    private readonly Context _context;
    private readonly Accelerator _accelerator;
    private readonly object _sync = new();
    private bool _disposed;
    
    private static readonly Lazy<FrameConverter> _lazyInstance = new(() => new FrameConverter());
    
    public static FrameConverter Instance => _lazyInstance.Value;

    private readonly Action<Index2D, ArrayView2D<int, Stride2D.DenseX>, ArrayView2D<int, Stride2D.DenseX>, ArrayView<int>> _kernel;

    private FrameConverter()
    {
        _context = Context.Create(builder => builder.Default().EnableAlgorithms());
        _accelerator = _context.GetPreferredDevice(false).CreateAccelerator(_context);
        
        _kernel = _accelerator.LoadAutoGroupedStreamKernel<
            Index2D, 
            ArrayView2D<int, Stride2D.DenseX>, 
            ArrayView2D<int, Stride2D.DenseX>, 
            ArrayView<int>>(GpuKernels.DecodeKernel);
    }
    
    public string Convert(byte[] input, string chars, byte colors, int width, int height)
    {
        int decodedFrameCapacity = ValidateInput(input, chars, colors, width, height);

        int[,] encodedFrame = new int[height, width];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                encodedFrame[y, x] = input[index];
            }
        }

        int[,] result;
        lock (_sync)
        {
            ThrowIfDisposed();

            using var inputBuffer = _accelerator.Allocate2DDenseX<int>((height, width));
            using var outputBuffer = _accelerator.Allocate2DDenseX<int>((height, width));
            using var propertiesBuffer = _accelerator.Allocate1D<int>(2);

            inputBuffer.CopyFromCPU(encodedFrame);
            propertiesBuffer.CopyFromCPU(new int[] { chars.Length, colors });

            _kernel((height, width), inputBuffer.View, outputBuffer.View, propertiesBuffer.View);

            _accelerator.Synchronize();

            result = outputBuffer.GetAsArray2D();
        }

        StringBuilder decodedFrame = new StringBuilder(decodedFrameCapacity);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int charIndex = result[y, x];
                decodedFrame.Append(chars[Math.Clamp(charIndex, 0, chars.Length - 1)]);
            }
            decodedFrame.AppendLine();
        }

        return decodedFrame.ToString();
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

    private static int ValidateInput(byte[] input, string chars, byte colors, int width, int height)
    {
        if (input is null)
            throw new ArgumentNullException(nameof(input));

        if (chars is null)
            throw new ArgumentNullException(nameof(chars));

        if (chars.Length == 0)
            throw new ArgumentException("Character set cannot be empty.", nameof(chars));

        if (colors < 2)
            throw new ArgumentOutOfRangeException(nameof(colors), "Color count must be at least 2.");

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

        if (input.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Input frame length must be exactly {expectedLength} bytes for a {width}x{height} frame.",
                nameof(input));
        }

        try
        {
            return checked(expectedLength + height);
        }
        catch (OverflowException ex)
        {
            throw new ArgumentException("Decoded frame dimensions are too large.", nameof(height), ex);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(FrameConverter));
    }
}
