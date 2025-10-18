using ILGPU;
using System.Text;
using ILGPU.Runtime;

namespace CCVC.Decoder;

public class FrameConverter : IDisposable
{
    private readonly Context _context;
    private readonly Accelerator _accelerator;
    
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
        int[,] encodedFrame = new int[height, width];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                encodedFrame[y, x] = input[index];
            }
        }
        
        using var inputBuffer = _accelerator.Allocate2DDenseX<int>((height, width));
        using var outputBuffer = _accelerator.Allocate2DDenseX<int>((height, width));
        using var propertiesBuffer = _accelerator.Allocate1D<int>(2);

        inputBuffer.CopyFromCPU(encodedFrame);
        propertiesBuffer.CopyFromCPU(new int[] { chars.Length, colors });
        
        _kernel((height, width), inputBuffer.View, outputBuffer.View, propertiesBuffer.View);
    
        _accelerator.Synchronize();

        var result = outputBuffer.GetAsArray2D();

        StringBuilder decodedFrame = new StringBuilder(width * height + height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int charIndex = result[y, x];
                decodedFrame.Append(chars[charIndex < chars.Length ? charIndex : 0]);
            }
            decodedFrame.AppendLine();
        }

        return decodedFrame.ToString();
    }
    
    public void Dispose()
    {
        _accelerator.Dispose();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}