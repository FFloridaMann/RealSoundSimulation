using System;
using System.IO;

namespace HeadsetBench
{
    internal static class Wav
    {
        // 16-bit PCM stereo; plays anywhere. Clips at full scale (the bench reports peaks separately).
        public static void Write(string path, float[] interleaved, int channels, int rate)
        {
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var w = new BinaryWriter(fs);
            int dataBytes = interleaved.Length * 2;
            w.Write("RIFF".ToCharArray()); w.Write(36 + dataBytes); w.Write("WAVE".ToCharArray());
            w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)channels);
            w.Write(rate); w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
            w.Write("data".ToCharArray()); w.Write(dataBytes);
            foreach (float s in interleaved)
            {
                float c = Math.Clamp(s, -1f, 1f);
                w.Write((short)Math.Round(c * 32767f));
            }
        }
    }
}
