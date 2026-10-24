using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace boardzz.audio;

// The pipeline:  sources ──► [record mix ──► clip buffer]
//                sources (mic only) + pads ──► [live mix] ──► target device
// The target device's clock drives everything, so recording and playback stay in step.
internal sealed class audioEngine : IDisposable
{
    public static readonly WaveFormat mixFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    private const int outputLatencyMs = 40;

    private readonly MixingSampleProvider liveMixer = new(mixFormat) { ReadFully = true };
    private readonly MixingSampleProvider recordMixer = new(mixFormat) { ReadFully = true };
    private readonly List<captureSource> sources = [];
    private readonly clipRecorder recorder;
    private WasapiPlayer? output;
    private volatile float sourcePeak;

    public MMDeviceEnumerator enumerator { get; } = new();
    public IReadOnlyList<captureSource> activeSources => sources;
    public int recordSeconds => recorder.seconds;
    public bool isRunning => output != null;

    public event Action<ISampleProvider>? inputEnded;   // raised on the audio thread
    public event Action<string>? outputFailed;          // raised on the audio thread

    public audioEngine(int recordSeconds)
    {
        recorder = new clipRecorder(recordSeconds);
        liveMixer.MixerInputEnded += (_, e) => inputEnded?.Invoke(e.SampleProvider);
    }

    // ---------- target ----------

    public void setTarget(MMDevice device)
    {
        output?.Dispose();
        output = null;

        var next = new WasapiPlayerBuilder().WithDevice(device).WithLatency(outputLatencyMs).Build();
        try
        {
            next.Init(new engineOutput(this));
            next.PlaybackStopped += (_, e) =>
            {
                if (e.Exception != null) outputFailed?.Invoke(e.Exception.Message);
            };
            next.Play();
            output = next;
        }
        catch
        {
            next.Dispose();
            throw;
        }
    }

    // ---------- sources ----------

    public async Task<List<string>> setSources(IEnumerable<sourceChoice> choices)
    {
        foreach (var source in sources)
        {
            recordMixer.RemoveMixerInput(source.recordProvider);
            if (source.liveProvider != null) liveMixer.RemoveMixerInput(source.liveProvider);
            source.Dispose();
        }
        sources.Clear();

        var errors = new List<string>();
        foreach (var choice in choices)
        {
            try
            {
                var source = await captureSource.start(choice, enumerator);
                sources.Add(source);
                recordMixer.AddMixerInput(source.recordProvider);
                if (source.liveProvider != null) liveMixer.AddMixerInput(source.liveProvider);
            }
            catch (Exception ex)
            {
                errors.Add($"{choice.label}: {ex.Message}");
            }
        }
        return errors;
    }

    // ---------- clip buffer ----------

    // Loudest source sample since the last call (0..1), for the input level meter.
    public float takeSourcePeak()
    {
        float peak = sourcePeak;
        sourcePeak = 0;
        return peak;
    }

    public void resizeRecording(int seconds) => recorder.resize(seconds);

    public bool saveClip(string path) => recorder.saveWav(path, mixFormat.SampleRate);

    // ---------- pads ----------

    public void play(ISampleProvider pad) => liveMixer.AddMixerInput(pad);

    // Converts any sample provider to the mixer's 48 kHz stereo format.
    public static ISampleProvider toMixFormat(ISampleProvider source)
    {
        if (source.WaveFormat.Channels == 1) source = new MonoToStereoSampleProvider(source);
        if (source.WaveFormat.SampleRate != mixFormat.SampleRate)
            source = new WdlResamplingSampleProvider(source, mixFormat.SampleRate);
        return source;
    }

    public void Dispose()
    {
        output?.Dispose();
        output = null;
        foreach (var source in sources) source.Dispose();
        sources.Clear();
        enumerator.Dispose();
    }

    // Pulled by the target device: feeds the clip buffer, then outputs the live mix.
    private sealed class engineOutput(audioEngine engine) : ISampleProvider
    {
        public WaveFormat WaveFormat => mixFormat;

        public int Read(Span<float> buffer)
        {
            engine.recordMixer.Read(buffer);
            engine.recorder.write(buffer);

            float peak = engine.sourcePeak;
            foreach (float sample in buffer)
            {
                float level = Math.Abs(sample);
                if (level > peak) peak = level;
            }
            engine.sourcePeak = peak;

            engine.liveMixer.Read(buffer);
            return buffer.Length;
        }
    }
}

// A playing pad that can be stopped from the UI thread; the mixer drops it on the next read.
internal sealed class padVoice(ISampleProvider source) : ISampleProvider
{
    private volatile bool stopped;

    public WaveFormat WaveFormat => source.WaveFormat;

    public void stop() => stopped = true;

    public int Read(Span<float> buffer) => stopped ? 0 : source.Read(buffer);
}
