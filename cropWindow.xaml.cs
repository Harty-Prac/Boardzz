using System.IO;
using System.Windows;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using boardzz.audio;
using boardzz.ui;

namespace boardzz;

public partial class cropWindow : Window
{
    private const string playGlyph = "";
    private const string stopGlyph = "";
    private const int outputLatencyMs = 40;

    private readonly DispatcherTimer playheadTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private string filePath;
    private double initialStart;
    private double initialEnd;
    private peakData? peaks;
    private int loadVersion;

    private WasapiPlayer? previewOut;
    private AudioFileReader? previewReader;
    private VolumeSampleProvider? previewVolume;

    public string resultPath => filePath;
    public string resultName { get; private set; } = "";
    public double resultStart { get; private set; }
    public double resultEnd { get; private set; }
    public float resultVolume { get; private set; } = 1f;
    public string? resultEmoji { get; private set; }

    public cropWindow(string filePath, string name, string? emoji, double start, double end, float volume)
    {
        InitializeComponent();
        this.filePath = filePath;
        initialStart = start;
        initialEnd = end;

        nameBox.Text = name;
        emojiField.emoji = emoji;
        fileText.Text = Path.GetFileName(filePath);
        volumeSlider.Value = Math.Round(Math.Clamp(volume, 0f, 1f) * 100);

        playheadTimer.Tick += (_, _) => updatePlayhead();
        waveform.selectionChanged += updateSelectionText;
        waveform.dragStarted += stopPreview;
        SourceInitialized += (_, _) => darkTitleBar.apply(this);
    }

    private async void onLoaded(object sender, RoutedEventArgs e) => await loadPeaks();

    private async Task loadPeaks()
    {
        int version = ++loadVersion;
        stopPreview();
        peaks = null;
        waveform.clear();
        saveButton.IsEnabled = false;
        playButton.IsEnabled = false;
        lengthText.Text = "–";
        rangeText.Text = "";
        loadingText.Text = "Loading waveform…";
        loadingText.Visibility = Visibility.Visible;

        try
        {
            var path = filePath;
            var data = await Task.Run(() => peakData.load(path));
            if (version != loadVersion) return;

            peaks = data;
            double end = initialEnd > initialStart ? Math.Min(initialEnd, data.duration) : data.duration;
            double start = Math.Clamp(initialStart, 0, end);
            waveform.load(data, start, end);

            loadingText.Visibility = Visibility.Collapsed;
            saveButton.IsEnabled = true;
            playButton.IsEnabled = true;
            updateSelectionText();
        }
        catch (Exception ex)
        {
            if (version != loadVersion) return;
            loadingText.Text = $"Couldn't read this file: {ex.Message}";
        }
    }

    private async void onBrowseClick(object sender, RoutedEventArgs e)
    {
        var path = mainWindow.pickAudioFile(this);
        if (path == null) return;

        var oldDefaultName = Path.GetFileNameWithoutExtension(filePath);
        filePath = path;
        fileText.Text = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(nameBox.Text) || nameBox.Text == oldDefaultName)
            nameBox.Text = Path.GetFileNameWithoutExtension(path);

        initialStart = 0;
        initialEnd = 0;
        await loadPeaks();
    }

    // ---------- preview ----------

    private void onPlayClick(object sender, RoutedEventArgs e)
    {
        if (previewOut != null) stopPreview();
        else startPreview();
    }

    private void startPreview()
    {
        if (peaks == null) return;

        AudioFileReader? reader = null;
        WasapiPlayer? output = null;
        try
        {
            reader = new AudioFileReader(filePath) { CurrentTime = TimeSpan.FromSeconds(waveform.selectionStart) };
            var cropped = new OffsetSampleProvider(reader)
            {
                Take = TimeSpan.FromSeconds(waveform.selectionEnd - waveform.selectionStart)
            };
            var volume = new VolumeSampleProvider(cropped) { Volume = (float)(volumeSlider.Value / 100) };

            output = new WasapiPlayerBuilder().WithLatency(outputLatencyMs).Build();
            output.Init(volume);
            var ownOutput = output;
            output.PlaybackStopped += (_, _) =>
            {
                if (ReferenceEquals(previewOut, ownOutput)) stopPreview();
            };

            previewReader = reader;
            previewOut = output;
            previewVolume = volume;
            output.Play();
            playIcon.Text = stopGlyph;
            playheadTimer.Start();
        }
        catch (Exception ex)
        {
            output?.Dispose();
            reader?.Dispose();
            MessageBox.Show(this, $"Couldn't play the preview:\n{ex.Message}", "Boardzz",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void stopPreview()
    {
        playheadTimer.Stop();
        var output = previewOut;
        var reader = previewReader;
        previewOut = null;
        previewReader = null;
        previewVolume = null;

        output?.Dispose();
        reader?.Dispose();
        waveform.setPlayhead(-1);
        playIcon.Text = playGlyph;
    }

    private void updatePlayhead()
    {
        if (previewReader != null) waveform.setPlayhead(previewReader.CurrentTime.TotalSeconds);
    }

    // ---------- fields ----------

    private void onVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (volumeText != null) volumeText.Text = $"{(int)Math.Round(e.NewValue)}%";
        if (previewVolume != null) previewVolume.Volume = (float)(e.NewValue / 100);
    }

    private void updateSelectionText()
    {
        if (peaks == null) return;
        double start = waveform.selectionStart, end = waveform.selectionEnd;
        lengthText.Text = $"{end - start:0.00}s";
        rangeText.Text = $"{formatTime(start)}  →  {formatTime(end)}    of {formatTime(peaks.duration)}";
    }

    private static string formatTime(double seconds) => TimeSpan.FromSeconds(seconds).ToString(@"m\:ss\.ff");

    private void onSaveClick(object sender, RoutedEventArgs e)
    {
        resultName = string.IsNullOrWhiteSpace(nameBox.Text)
            ? Path.GetFileNameWithoutExtension(filePath)
            : nameBox.Text.Trim();
        resultStart = waveform.selectionStart;
        resultEnd = waveform.selectionEnd;
        resultVolume = (float)(volumeSlider.Value / 100);
        resultEmoji = emojiField.emoji;
        DialogResult = true;
    }

    private void onClosed(object? sender, EventArgs e) => stopPreview();
}
