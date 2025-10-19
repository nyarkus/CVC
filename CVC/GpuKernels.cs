using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;

namespace CVC;

public static class GpuKernels
{
    public static void DecodeKernel(
        Index2D index, 
        ArrayView2D<int, Stride2D.DenseX> input, 
        ArrayView2D<int, Stride2D.DenseX> output, 
        ArrayView<int> properties)
    {
        float k = (float)properties[1] / properties[0];
        int value = (int)(input[index] / k + 0.5f);
        output[index] = XMath.Clamp(value, 0, properties[0] - 1);
    }
    
    public static void EncodeKernel(
        Index2D index,
        ArrayView2D<uint, Stride2D.DenseX> inputTexture,
        ArrayView2D<int, Stride2D.DenseX> outputBuffer,
        ArrayView<int> properties)
    {
        uint pixelValue = inputTexture[index];
        
        // BGRA8888 is stored in memory as 0xAARRGGBB (little-endian)
        float b = (pixelValue & 0xFF) / 255.0f;
        float g = ((pixelValue >> 8) & 0xFF) / 255.0f;
        float r = ((pixelValue >> 16) & 0xFF) / 255.0f;
        
        float grayValue = r * 0.3f + g * 0.59f + b * 0.11f;
        int colorIndex = (int)(grayValue * properties[0]);
        int clampedIndex = XMath.Clamp(colorIndex, 0, properties[0] - 1);
        
        outputBuffer[index] = clampedIndex;
    }

    public static void ApplyDeltaKernel(
        Index2D index,
        ArrayView2D<byte, Stride2D.DenseX> intraCoded,
        ArrayView2D<sbyte, Stride2D.DenseX> predictedFrame,
        ArrayView2D<byte, Stride2D.DenseX> output)
    {
        byte full = intraCoded[index];
        sbyte delta = predictedFrame[index];
        
        output[index] = (byte)XMath.Clamp(full + delta, 0, 255);
    }

    public static void CalculateDeltaKernel(
        Index2D index,
        ArrayView2D<byte, Stride2D.DenseX> frame1,
        ArrayView2D<byte, Stride2D.DenseX> frame2,
        ArrayView2D<sbyte, Stride2D.DenseX> output)
    {
        byte first =  frame1[index];
        byte second = frame2[index];

        var result = (sbyte)XMath.Clamp(second - first, -128, 127);
        
        output[index] = result;
    }
}