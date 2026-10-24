using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using boardzz.audio;
using boardzz.ui;

namespace boardzz;

public partial class mainWindow : Window
{
    private const int gridSize = 5;
    private const int maxTabs = 9;
    internal const double maxSoundSeconds = 10 * 60;

    private static readonly Brush idleBorder = frozenBrush(0x3A, 0x3F, 0x4B);
    private static readonly Brush playingBorder = frozenBrush(0x3D, 0xD6, 0x8C);
    private static readonly Brush emptyBackground = frozenBrush(0x1E, 0x21, 0x28);
    private static readonly Brush filledBackground = frozenBrush(0x26, 0x2A, 0x33);

    private readonly List<boardTab> tabs = [];
    private int currentTab;

    // Every pad on every tab (playback, cleanup and file bookkeeping span all tabs).
    private IEnumerable<padSlot> slots => tabs.SelectMany(t => t.slots);
    private padSlot[] currentSlots => tabs[currentTab].slots;

    public mainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => darkTitleBar.apply(this);

        var board = boardStore.load();
        foreach (var record in board.tabs.Take(maxTabs)) createTab(record.name, record.emoji, record.pads);
        if (tabs.Count == 0) createTab("Main", null, []);
        selectTab(Math.Clamp(board.selected, 0, tabs.Count - 1));

        initPipeline();
    }

    // ---------- saved board ----------

    private boardTab createTab(string name, string? emoji, List<padRecord> pads)
    {
        var padStyle = (Style)FindResource("padStyle");
        var tab = new boardTab(name, emoji, gridSize * gridSize);

        for (int i = 0; i < tab.slots.Length; i++)
        {
            var slot = new padSlot(new Button { Style = padStyle });
            slot.button.Click += (_, _) => onPadClick(slot);
            slot.button.ContextMenu = buildMenu(slot);
            tab.slots[i] = slot;
        }

        foreach (var record in pads)
        {
            if (record.index < 0 || record.index >= tab.slots.Length) continue;
            string path = Path.Combine(boardStore.soundsFolder, record.file);
            if (!File.Exists(path)) continue;

            var slot = tab.slots[record.index];
            slot.filePath = path;
            slot.name = record.name;
            slot.emoji = record.emoji;
            slot.startSeconds = record.start;
            slot.endSeconds = record.end;
            slot.volume = record.volume;
        }

        foreach (var slot in tab.slots) refresh(slot);
        tabs.Add(tab);
        return tab;
    }

    private void saveBoard()
    {
        try
        {
            var tabRecords = tabs.Select(tab => new tabRecord(tab.name, tab.emoji, tab.slots
                .Select((slot, index) => (slot, index))
                .Where(p => p.slot.filePath != null)
                .Select(p => new padRecord(p.index, Path.GetFileName(p.slot.filePath!), p.slot.name, p.slot.emoji,
                    p.slot.startSeconds, p.slot.endSeconds, p.slot.volume))
                .ToList())).ToList();
            boardStore.save(new boardRecord(currentTab, tabRecords));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't save the board:\n{ex.Message}", "Boardzz",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Deleted after pending playback cleanup has run, so the file is no longer open.
    private void deleteLater(string? oldPath) =>
        Dispatcher.InvokeAsync(() => boardStore.deleteIfUnused(oldPath, slots.Select(s => s.filePath)),
            DispatcherPriority.Background);

    internal static string? pickAudioFile(Window owner)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a sound",
            Filter = "Audio files (*.mp3;*.wav)|*.mp3;*.wav"
        };
        if (dialog.ShowDialog(owner) != true) return null;

        try
        {
            using var reader = new AudioFileReader(dialog.FileName);
            if (reader.TotalTime.TotalSeconds > maxSoundSeconds)
            {
                MessageBox.Show(owner, $"That sound is {(int)reader.TotalTime.TotalMinutes} minutes long. The maximum is 10 minutes.",
                    "Boardzz", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"Couldn't open that file:\n{ex.Message}", "Boardzz",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        return dialog.FileName;
    }

    private void onPadClick(padSlot slot)
    {
        if (slot.filePath == null) addSound(slot);
        else if (slot.isPlaying) stop(slot);
        else play(slot);
    }

    private ContextMenu buildMenu(padSlot slot)
    {
        var edit = new MenuItem { Header = "Edit / crop…" };
        edit.Click += (_, _) => editSound(slot);

        var change = new MenuItem { Header = "Change sound…" };
        change.Click += (_, _) => addSound(slot);

        var clear = new MenuItem { Header = "Clear" };
        clear.Click += (_, _) => clearSlot(slot);

        var menu = new ContextMenu();
        menu.Items.Add(edit);
        menu.Items.Add(change);
        menu.Items.Add(clear);
        menu.Opened += (_, _) =>
        {
            bool hasSound = slot.filePath != null;
            edit.IsEnabled = hasSound;
            clear.IsEnabled = hasSound;
        };
        return menu;
    }

    // ---------- editing ----------

    private void addSound(padSlot slot)
    {
        var path = pickAudioFile(this);
        if (path == null) return;
        openEditor(slot, path, Path.GetFileNameWithoutExtension(path), slot.emoji, 0, 0, 1f);
    }

    private void editSound(padSlot slot)
    {
        if (slot.filePath == null) return;
        openEditor(slot, slot.filePath, slot.name, slot.emoji, slot.startSeconds, slot.endSeconds, slot.volume);
    }

    // Returns true when the pad was saved.
    private bool openEditor(padSlot slot, string path, string name, string? emoji, double start, double end, float volume)
    {
        stop(slot);
        var editor = new cropWindow(path, name, emoji, start, end, volume) { Owner = this };
        if (editor.ShowDialog() != true) return false;

        string storedPath;
        try
        {
            storedPath = boardStore.import(editor.resultPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't copy the sound into Boardzz:\n{ex.Message}", "Boardzz",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        string? oldPath = slot.filePath;
        slot.filePath = storedPath;
        slot.name = editor.resultName;
        slot.emoji = editor.resultEmoji;
        slot.startSeconds = editor.resultStart;
        slot.endSeconds = editor.resultEnd;
        slot.volume = editor.resultVolume;
        refresh(slot);
        saveBoard();
        deleteLater(oldPath);
        return true;
    }

    private void clearSlot(padSlot slot)
    {
        stop(slot);
        string? oldPath = slot.filePath;
        slot.reset();
        refresh(slot);
        saveBoard();
        deleteLater(oldPath);
    }

    // ---------- playback (pads go through the engine to the target device) ----------

    private void play(padSlot slot)
    {
        AudioFileReader? reader = null;
        try
        {
            reader = new AudioFileReader(slot.filePath!);
            if (slot.startSeconds > 0) reader.CurrentTime = TimeSpan.FromSeconds(slot.startSeconds);

            ISampleProvider source = reader;
            if (slot.endSeconds > slot.startSeconds)
                source = new OffsetSampleProvider(reader) { Take = TimeSpan.FromSeconds(slot.endSeconds - slot.startSeconds) };
            source = new VolumeSampleProvider(source) { Volume = slot.volume };

            var voice = new padVoice(audioEngine.toMixFormat(source));
            slot.voice = voice;
            slot.reader = reader;
            engine.play(voice);
            refresh(slot);
        }
        catch (Exception ex)
        {
            reader?.Dispose();
            slot.voice = null;
            slot.reader = null;
            refresh(slot);
            MessageBox.Show(this, $"Couldn't play {slot.filePath}:\n{ex.Message}", "Boardzz",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void stop(padSlot slot)
    {
        if (slot.voice == null) return;
        slot.voice.stop();
        if (!engine.isRunning) finishPad(slot);   // nothing is pulling audio, so clean up now
    }

    // The mixer finished (or dropped) a pad.
    private void onPadEnded(ISampleProvider provider)
    {
        var slot = slots.FirstOrDefault(s => ReferenceEquals(s.voice, provider));
        if (slot != null) finishPad(slot);
    }

    private static void finishPad(padSlot slot)
    {
        slot.reader?.Dispose();
        slot.voice = null;
        slot.reader = null;
        refresh(slot);
    }

    // ---------- view ----------

    private static void refresh(padSlot slot)
    {
        bool hasSound = slot.filePath != null;
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (hasSound && slot.emoji != null)
        {
            var icon = new Image { Width = 30, Height = 30, Margin = new Thickness(0, 0, 0, 4) };
            Emoji.Wpf.Image.SetSource(icon, slot.emoji);
            content.Children.Add(icon);
        }
        content.Children.Add(new TextBlock
        {
            Text = hasSound ? slot.name : "+",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            FontSize = hasSound ? 13 : 26,
            Opacity = hasSound ? 1 : 0.45
        });
        slot.button.Content = content;
        slot.button.ToolTip = hasSound ? describe(slot) : "Click to choose a sound";
        slot.button.Background = hasSound ? filledBackground : emptyBackground;
        slot.button.BorderBrush = slot.isPlaying ? playingBorder : idleBorder;
    }

    private static string describe(padSlot slot)
    {
        string range = slot.endSeconds > slot.startSeconds
            ? $"{slot.startSeconds:0.00}s → {slot.endSeconds:0.00}s"
            : "full track";
        return $"{slot.name}\n{Path.GetFileName(slot.filePath)} · {range} · {slot.volume * 100:0}%";
    }

    private void onClosed(object? sender, EventArgs e)
    {
        meterTimer.Stop();
        engine.Dispose();
        foreach (var slot in slots) slot.reader?.Dispose();
    }

    private static Brush frozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
