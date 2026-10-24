using System.IO;
using NAudio.Wave;

namespace boardzz.audio;

// Pre-computed loudness peaks for drawing a waveform fast at any zoom level.
// Two resolutions: fine blocks for zoomed-in views, coarse blocks for the full track.
public sealed class peakData
{
    public const int fineBlock = 32;
    public const int coarseBlock = 2048;

    private readonly float[] finePeaks;
    private readonly float[] coarsePeaks;

    public int sampleRate { get; }
    public long frameCount { get; }
    public float globalPeak { get; }
    public double duration => frameCount / (double)sampleRate;

    private peakData(float[] finePeaks, float[] coarsePeaks, int sampleRate, long frameCount, float globalPeak)
    {
        this.finePeaks = finePeaks;
        this.coarsePeaks = coarsePeaks;
        this.sampleRate = sampleRate;
        this.frameCount = frameCount;
        this.globalPeak = globalPeak;
    }

    public static peakData load(string path)
    {
        using var reader = new AudioFileReader(path);
        int channels = reader.WaveFormat.Channels;
        int rate = reader.WaveFormat.SampleRate;

        var fine = new List<float>();
        var coarse = new List<float>();
        var buffer = new float[rate * channels];
        float fineMax = 0, coarseMax = 0, global = 0;
        int fineCount = 0, coarseCount = 0;
        long frames = 0;
        int read;

        while ((read = reader.Read(buffer.AsSpan())) > 0)
        {
            for (int i = 0; i + channels <= read; i += channels)
            {
                float level = 0;
                for (int c = 0; c < channels; c++)
                {
                    float v = Math.Abs(buffer[i + c]);
                    if (v > level) level = v;
                }

                if (level > fineMax) fineMax = level;
                if (level > coarseMax) coarseMax = level;
                if (level > global) global = level;

                if (++fineCount == fineBlock) { fine.Add(fineMax); fineMax = 0; fineCount = 0; }
                if (++coarseCount == coarseBlock) { coarse.Add(coarseMax); coarseMax = 0; coarseCount = 0; }
                frames++;
            }
        }

        if (fineCount > 0) fine.Add(fineMax);
        if (coarseCount > 0) coarse.Add(coarseMax);
        if (frames == 0) throw new InvalidDataException("the file contains no audio");

        return new peakData(fine.ToArray(), coarse.ToArray(), rate, frames, global);
    }

    // Loudest point between two times, picking the cheapest resolution that is still accurate.
    public float peakBetween(double fromSeconds, double toSeconds)
    {
        long f0 = (long)(Math.Max(0, fromSeconds) * sampleRate);
        long f1 = (long)(Math.Min(duration, toSeconds) * sampleRate);
        if (f1 <= f0) f1 = f0 + 1;

        bool useCoarse = f1 - f0 >= coarseBlock;
        var blocks = useCoarse ? coarsePeaks : finePeaks;
        int size = useCoarse ? coarseBlock : fineBlock;
        if (blocks.Length == 0) return 0;

        long first = Math.Min(f0 / size, blocks.Length - 1);
        long last = Math.Min((f1 - 1) / size, blocks.Length - 1);

        float peak = 0;
        for (long i = first; i <= last; i++)
            if (blocks[i] > peak) peak = blocks[i];
        return peak;
    }
}
