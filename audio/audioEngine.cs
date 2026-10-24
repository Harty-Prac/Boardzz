using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace boardzz.audio;

// The pipeline:  sources ──► [record mix ──► clip buffer]
//                sources (mic only) + [pad mix ──► pad meter] ──► [live mix] ──► target device
// The target device's clock drives everything, so recording and playback stay in step.
internal sealed class audioEngine : IDisposable
{
    public static readonly WaveFormat mixFormat = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);
    private const int outputLatencyMs = 40;

    private readonly MixingSampleProvider liveMixer = new(mixFormat) { ReadFully = true };
    private readonly MixingSampleProvider recordMixer = new(mixFormat) { ReadFully = true };
    private readonly MixingSampleProvider padMixer = new(mixFormat) { ReadFully = true };
    private readonly MixingSampleProvider sourceLiveMixer = new(mixFormat) { ReadFully = true };
    private readonly List<captureSource> sources = [];
    private readonly clipRecorder recorder;
    private WasapiPlayer? output;
    private volatile float sourcePeak;
    private volatile float padPeak;
    private volatile float outputPeak;

    public MMDeviceEnumerator enumerator { get; } = new();
    public IReadOnlyList<captureSource> activeSources => sources;
    public int recordSeconds { get; private set; } = 60;
    public bool recording { get; private set; }

    // 1 = 100%, up to 2 = 200%. Read on the audio thread; float writes are atomic.
    public float sourceVolume { get; set; } = 1;
    public float targetVolume { get; set; } = 1;
    public bool isRunning => output != null;

    public event Action<ISampleProvider>? inputEnded;   // raised on the audio thread
    public event Action<string>? outputFailed;          // raised on the audio thread

    public audioEngine()
    {
        recorder = new clipRecorder(0);   // off until setRecording
        padMixer.MixerInputEnded += (_, e) => inputEnded?.Invoke(e.SampleProvider);
        liveMixer.AddMixerInput(new padMeter(this));
        liveMixer.AddMixerInput(new gainProvider(sourceLiveMixer, () => sourceVolume));
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
            if (source.liveProvider != null) sourceLiveMixer.RemoveMixerInput(source.liveProvider);
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
                if (source.liveProvider != null) sourceLiveMixer.AddMixerInput(source.liveProvider);
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

    // Turning it off frees the buffer; any change starts a fresh, empty buffer.
    public void setRecording(bool on, int seconds)
    {
        if (on == recording && seconds == recordSeconds) return;
        recording = on;
        recordSeconds = seconds;
        recorder.resize(on ? seconds : 0);
    }

    public bool saveClip(string path) => recorder.saveWav(path, mixFormat.SampleRate);

    // ---------- pads ----------

    public void play(ISampleProvider pad) => padMixer.AddMixerInput(pad);

    // Loudest sample sent to the target since the last call, for the output level meter.
    public float takeOutputPeak()
    {
        float peak = outputPeak;
        outputPeak = 0;
        return peak;
    }

    // Loudest pad sample since the last call (0..1), for the live waveform.
    public float takePadPeak()
    {
        float peak = padPeak;
        padPeak = 0;
        return peak;
    }

    // Converts any sample provider to the mixer's 48 kHz stereo format.
    public static ISampleProvider toMixFormat(ISampleProvider source)
    {
        if (source.WaveFormat.Channels == 1) source = new MonoToStereoSampleProvider(source);
        if (source.WaveFormat.SampleRate != mixFormat.SampleRate)
            source = new WdlResamplingSampleProvider(source, mixFormat.SampleRate);
        return source;
    }

    // Called on the audio thread by padMeter.
    internal int readPads(Span<float> buffer)
    {
        int count = padMixer.Read(buffer);
        float peak = padPeak;
        foreach (float sample in buffer[..count])
        {
            float level = Math.Abs(sample);
            if (level > peak) peak = level;
        }
        padPeak = peak;
        return count;
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
            gainProvider.apply(buffer, engine.sourceVolume);
            engine.recorder.write(buffer);

            float peak = engine.sourcePeak;
            foreach (float sample in buffer)
            {
                float level = Math.Abs(sample);
                if (level > peak) peak = level;
            }
            engine.sourcePeak = peak;

            engine.liveMixer.Read(buffer);
            gainProvider.apply(buffer, engine.targetVolume);

            float outPeak = engine.outputPeak;
            foreach (float sample in buffer)
            {
                float level = Math.Abs(sample);
                if (level > outPeak) outPeak = level;
            }
            engine.outputPeak = outPeak;
            return buffer.Length;
        }
    }
}

// Sits between the pad mix and the live mix and records how loud the pads are.
internal sealed class padMeter(audioEngine engine) : ISampleProvider
{
    public WaveFormat WaveFormat => audioEngine.mixFormat;

    public int Read(Span<float> buffer) => engine.readPads(buffer);
}

// Multiplies a provider by a gain that can change while playing.
internal sealed class gainProvider(ISampleProvider source, Func<float> gain) : ISampleProvider
{
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(Span<float> buffer)
    {
        int count = source.Read(buffer);
        apply(buffer[..count], gain());
        return count;
    }

    public static void apply(Span<float> buffer, float gain)
    {
        if (gain == 1f) return;
        for (int i = 0; i < buffer.Length; i++) buffer[i] *= gain;
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
