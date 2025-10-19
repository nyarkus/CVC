using CVC;
using ILGPU;
using ILGPU.Runtime;
using SkiaSharp;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CVC.Encoder;

public class FrameConverter : IDisposable
{
    private readonly Context _context;
    private readonly Accelerator _accelerator;

    private static readonly Lazy<FrameConverter> _instance = new(() => new FrameConverter());
    public static FrameConverter Instance => _instance.Value;

    private readonly Action<Index2D, ArrayView2D<uint, Stride2D.DenseX>, ArrayView2D<int, Stride2D.DenseX>, ArrayView<int>> _kernel;

    private FrameConverter()
    {
        _context = Context.Create(builder => builder.Default().EnableAlgorithms());
        _accelerator = _context.GetPreferredDevice(false).CreateAccelerator(_context);
        
        _kernel = _accelerator.LoadAutoGroupedStreamKernel<
            Index2D,
            ArrayView2D<uint, Stride2D.DenseX>,
            ArrayView2D<int, Stride2D.DenseX>,
            ArrayView<int>>(GpuKernels.EncodeKernel);
    }

    public byte[] Convert(Stream image, byte countOfColors, int width, int height)
    {
        using var originalBitmap = SKBitmap.Decode(image);
        var imageInfo = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var finalBitmap = new SKBitmap(imageInfo);
        using (var canvas = new SKCanvas(finalBitmap))
        {
            canvas.DrawBitmap(originalBitmap, new SKRect(0, 0, width, height));
        }
        
        var pixelSpanUint = MemoryMarshal.Cast<byte, uint>(finalBitmap.GetPixelSpan());
        var pixelArray = new uint[height, width];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                pixelArray[y, x] = pixelSpanUint[y * width + x];
            }
        }
        
        using var inputBuffer = _accelerator.Allocate2DDenseX<uint>((height, width));
        using var outputBuffer = _accelerator.Allocate2DDenseX<int>((height, width));
        using var propertiesBuffer = _accelerator.Allocate1D<int>(1);
        
        inputBuffer.CopyFromCPU(pixelArray);
        propertiesBuffer.CopyFromCPU(new int[] { countOfColors });
        
        _kernel((height, width), inputBuffer.View, outputBuffer.View, propertiesBuffer.View);
        _accelerator.Synchronize();

        var result = outputBuffer.GetAsArray2D();
        byte[] frameData = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                frameData[y * width + x] = (byte)result[y, x];
            }
        }
        return frameData;
    }

    public void Dispose()
    {
        _accelerator.Dispose();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}