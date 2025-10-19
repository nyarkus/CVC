using NAudio.Wave;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CVC.Encoder
{
    public class Converter
    {
        public static CVideoFile ConvertFromVideo(FFmpegManager ffmpeg, string source, Stream destination , int width, int height, byte colorCount = 10)
        {
            double fps = ffmpeg.GetFPS(source);
            var sound = ffmpeg.ExtractAndResampleSoundToMemory(source);

            var builder = new CVideoFileBuilder(destination)
                .WithMeta(CVideoMeta.Create(fps, width, height, colorCount))
                .WithSound(sound.ToArray());

            var videoStream = builder.GetVideoStream();
            
            ffmpeg.ExtractFramesToMemory(source, fps, frameStream =>
            {
                var frame = FrameConverter.Instance.Convert(frameStream, colorCount, width, height);
                videoStream.WriteFrame(frame);
            });
            
            return builder.Build();
        }
    }
}
