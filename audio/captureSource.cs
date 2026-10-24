using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace boardzz.audio;

internal enum sourceKind { microphone, desktop, window }

// What the user picked. id = device ID (microphone), "desktop", or a process ID (window).
internal sealed record sourceChoice(sourceKind kind, string id, string label)
{
    public static readonly sourceChoice desktop = new(sourceKind.desktop, "desktop", "Desktop audio");
}

// One running capture. It always feeds the record mix; microphones also feed the live mix
// so your voice reaches the target. Desktop/window audio is recorded only (you already hear it).
internal sealed class captureSource : IDisposable
{
    private static readonly TimeSpan bufferLength = TimeSpan.FromMilliseconds(300);
    private static readonly WaveFormat loopbackFormat = new(48000, 16, 2);

    private readonly WasapiRecorder recorder;
    private readonly BufferedWaveProvider recordBuffer;
    private readonly BufferedWaveProvider? liveBuffer;
    private byte[] silence = [];

    public sourceChoice choice { get; }
    public ISampleProvider recordProvider { get; }
    public ISampleProvider? liveProvider { get; }

    private captureSource(sourceChoice choice, WasapiRecorder recorder)
    {
        this.choice = choice;
        this.recorder = recorder;

        recordBuffer = new BufferedWaveProvider(recorder.WaveFormat, bufferLength) { DiscardOnBufferOverflow = true };
        recordProvider = audioEngine.toMixFormat(recordBuffer.ToSampleProvider());

        if (choice.kind == sourceKind.microphone)
        {
            liveBuffer = new BufferedWaveProvider(recorder.WaveFormat, bufferLength) { DiscardOnBufferOverflow = true };
            liveProvider = audioEngine.toMixFormat(liveBuffer.ToSampleProvider());
        }

        recorder.DataAvailable += onData;
    }

    public static async Task<captureSource> start(sourceChoice choice, MMDeviceEnumerator enumerator)
    {
        WasapiRecorder recorder;
        if (choice.kind == sourceKind.microphone)
        {
            recorder = new WasapiRecorderBuilder()
                .WithDevice(enumerator.GetDevice(choice.id))
                .WithFormat(audioEngine.mixFormat)
                .WithBufferLength(20)
                .Build();
        }
        else if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            recorder = await startLoopback(choice);
        }
        else
        {
            throw new PlatformNotSupportedException("Desktop and window capture need Windows 10 version 2004 or newer.");
        }

        try
        {
            var source = new captureSource(choice, recorder);
            recorder.StartRecording();
            return source;
        }
        catch
        {
            recorder.Dispose();
            throw;
        }
    }

    // Desktop = everything except Boardzz itself (so pads never echo into clips); window = that app only.
    [SupportedOSPlatform("windows10.0.19041.0")]
    private static Task<WasapiRecorder> startLoopback(sourceChoice choice)
    {
        bool isDesktop = choice.kind == sourceKind.desktop;
        uint processId = isDesktop ? (uint)Environment.ProcessId : uint.Parse(choice.id);
        var mode = isDesktop ? ProcessLoopbackMode.ExcludeTargetProcessTree : ProcessLoopbackMode.IncludeTargetProcessTree;

        return new WasapiRecorderBuilder()
            .WithProcessLoopback(processId, mode)
            .WithFormat(loopbackFormat)
            .BuildAsync();
    }

    private void onData(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        if ((flags & AudioClientBufferFlags.Silent) != 0)
        {
            if (silence.Length < data.Length) silence = new byte[data.Length];
            data = silence.AsSpan(0, data.Length);
        }
        recordBuffer.AddSamples(data);
        liveBuffer?.AddSamples(data);
    }

    public void Dispose()
    {
        recorder.DataAvailable -= onData;
        recorder.StopRecording();
        recorder.Dispose();
    }
}
