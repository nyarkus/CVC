using ILGPU;
using ILGPU.Runtime;

namespace CVC.File;

public class PFrameEncoder : IDisposable
{
    private readonly Context _context;
    private readonly Accelerator _accelerator;
    
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
        
        using var buffer1 = _accelerator.Allocate2DDenseX<byte>((height, width));
        using var buffer2 = _accelerator.Allocate2DDenseX<byte>((height, width));
        using var outputBuffer = _accelerator.Allocate2DDenseX<sbyte>((height, width));

        buffer1.CopyFromCPU(encodedFrame1);
        buffer2.CopyFromCPU(encodedFrame2);
        
        _kernel((height, width), buffer1.View, buffer2.View, outputBuffer.View);
    
        _accelerator.Synchronize();

        var result = outputBuffer.GetAsArray2D();
        sbyte[] output = new sbyte[height * width];
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
        _accelerator.Dispose();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}