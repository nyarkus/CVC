using NAudio.Wave;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CCVC.Encoder
{
    public class Converter
    {
        public static CVideo ConvertFromVideo(FFmpegManager ffmpeg, string filename, int width, int height, byte countOfColors = 10)
        {
            List<byte[]> list = new();

            double fps = ffmpeg.GetFPS(filename);

            {
                var framesDict = new ConcurrentDictionary<int, byte[]>();

                int frameIndex = 0;
                ffmpeg.ExtractFramesToMemory(filename, fps, frameStream =>
                {
                    int currentIndex = Interlocked.Increment(ref frameIndex) - 1;

                    var frame = FrameConverter.Instance.Convert(frameStream, countOfColors, width, height);
                    framesDict[currentIndex] = frame;

                });

                list = framesDict.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
            }
            
            var audio = ffmpeg.ExtractAndResampleSoundToMemory(filename);

            var video = new CVideo(list.ToList().ToArray(), fps, sound: audio.ToArray(), width, height, countOfColors);
            
            return video;
        }
    }
}
