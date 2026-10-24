using NAudio.Wave;

namespace boardzz.audio;

// Rolling buffer holding the last N seconds of the record mix (48 kHz stereo, 16-bit to halve RAM).
internal sealed class clipRecorder
{
    private readonly object gate = new();
    private short[] ring = [];
    private int position;
    private int filled;

    public int seconds { get; private set; }

    public clipRecorder(int seconds) => resize(seconds);

    public void resize(int newSeconds)
    {
        lock (gate)
        {
            seconds = newSeconds;
            ring = new short[newSeconds * audioEngine.mixFormat.SampleRate * audioEngine.mixFormat.Channels];
            position = 0;
            filled = 0;
        }
    }

    // Called on the audio thread.
    public void write(ReadOnlySpan<float> samples)
    {
        lock (gate)
        {
            if (ring.Length == 0) return;   // recording is off
            foreach (float sample in samples)
            {
                ring[position] = (short)(Math.Clamp(sample, -1f, 1f) * short.MaxValue);
                if (++position == ring.Length) position = 0;
            }
            filled = Math.Min(filled + samples.Length, ring.Length);
        }
    }

    public bool saveWav(string path, int sampleRate)
    {
        short[] clip;
        lock (gate)
        {
            if (filled == 0) return false;
            clip = new short[filled];
            int start = (position - filled + ring.Length) % ring.Length;
            int firstPart = Math.Min(filled, ring.Length - start);
            Array.Copy(ring, start, clip, 0, firstPart);
            Array.Copy(ring, 0, clip, firstPart, filled - firstPart);
        }

        using var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, 2));
        writer.WriteSamples(clip, 0, clip.Length);
        return true;
    }
}
