using System;
using System.Globalization;
using NVorbis;

namespace MeasureVorbisPeak;

/// <summary>
/// Decode an ogg the way DesktopGL OggStream does, then measure the float
/// peak before the 16-bit cast. MonoGame clamps with
/// (int)(32767f * sample) into a short, which is before OpenAL gain.
/// </summary>
static class Program
{
    static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("usage: MeasureVorbisPeak <file.ogg>");
            return 2;
        }
        float maxAbs = 0f;
        int clamps = 0;
        long samples = 0;
        bool hasClipped;
        using (var reader = new VorbisReader(args[0]))
        {
            float[] buffer = new float[4096];
            int read;
            while ((read = reader.ReadSamples(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < read; i++)
                {
                    float sample = buffer[i];
                    float abs = Math.Abs(sample);
                    if (abs > maxAbs)
                    {
                        maxAbs = abs;
                    }
                    if (WouldClamp(sample))
                    {
                        clamps++;
                    }
                }
                samples += read;
            }
            hasClipped = reader.HasClipped;
        }
        double peakDb = maxAbs <= 0f ? -999d : 20d * Math.Log10(maxAbs);
        Console.WriteLine("peak_db=" + peakDb.ToString("0.00", CultureInfo.InvariantCulture));
        Console.WriteLine("max_abs=" + maxAbs.ToString("0.000000", CultureInfo.InvariantCulture));
        Console.WriteLine("clamps=" + clamps.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("samples=" + samples.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("has_clipped=" + (hasClipped ? "true" : "false"));
        return 0;
    }

    static bool WouldClamp(float sample)
    {
        int temp = (int)(32767f * sample);
        return temp > short.MaxValue || temp < short.MinValue;
    }
}
