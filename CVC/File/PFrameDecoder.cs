using ILGPU;
using ILGPU.Runtime;

namespace CVC;

public class PFrameDecoder : IDisposable
{
    private readonly Context _context;
    private readonly Accelerator _accelerator;
    
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
        
        using var iBuffer = _accelerator.Allocate2DDenseX<byte>((height, width));
        using var pBuffer = _accelerator.Allocate2DDenseX<sbyte>((height, width));
        using var outputBuffer = _accelerator.Allocate2DDenseX<byte>((height, width));

        iBuffer.CopyFromCPU(encodedIFrame);
        pBuffer.CopyFromCPU(encodedPFrame);
        
        _kernel((height, width), iBuffer.View, pBuffer.View, outputBuffer.View);
    
        _accelerator.Synchronize();

        var result = outputBuffer.GetAsArray2D();
        byte[] output = new byte[height * width];
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