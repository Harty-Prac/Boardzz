using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using boardzz.audio;

namespace boardzz;

// The bar under the grid:  Source → Boardzz (records the last N seconds) → Target
public partial class mainWindow
{
    private readonly audioEngine engine = new();
    private List<sourceChoice> chosenSources = [];
    private string? targetId;
    private string? activeTargetId;
    private readonly DispatcherTimer meterTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private double meterLevel;
    private double outputLevel;
    private static readonly Brush meterGreen = frozenBrush(0x3D, 0xD6, 0x8C);

    private void initPipeline()
    {
        var settings = boardStore.loadSettings();
        engine.setRecording(settings.recording, Math.Clamp(settings.recordSeconds, 1, (int)maxSoundSeconds));

        engine.inputEnded += provider => Dispatcher.InvokeAsync(() => onPadEnded(provider));
        engine.outputFailed += message => Dispatcher.InvokeAsync(() => targetText.Text = $"Stopped: {message}");

        targetId = settings.targetId;
        overlapPads = settings.overlapPads;
        loadRouting(settings);
        applyTarget();

        meterTimer.Tick += (_, _) => updateMeter();
        meterTimer.Start();

        foreach (var device in engine.enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            if (settings.micIds.Contains(device.ID))
                chosenSources.Add(new sourceChoice(sourceKind.microphone, device.ID, device.FriendlyName));
        if (settings.desktop) chosenSources.Add(sourceChoice.desktop);
        _ = applySources();
    }

    private void saveSettings()
    {
        try
        {
            boardStore.saveSettings(new appSettings(
                targetId,
                engine.recordSeconds,
                chosenSources.Where(c => c.kind == sourceKind.microphone).Select(c => c.id).ToList(),
                chosenSources.Any(c => c.kind == sourceKind.desktop),
                overlapPads,
                engine.recording,
                engine.sourceVolume,
                engine.targetVolume,
                routingOpen,
                miniMode));
        }
        catch (IOException) { }   // settings are a convenience; never block the app over them
    }

    // ---------- source ----------

    private void onSourceClick(object sender, RoutedEventArgs e)
    {
        sourceList.Children.Clear();

        addListHeader(sourceList, "Microphones");
        foreach (var device in engine.enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            addSourceCheck(new sourceChoice(sourceKind.microphone, device.ID, device.FriendlyName));

        addListHeader(sourceList, "Desktop");
        addSourceCheck(sourceChoice.desktop);

        addListHeader(sourceList, "Windows");
        foreach (var window in listWindows()) addSourceCheck(window);

        sourcePopup.IsOpen = true;
    }

    private void addSourceCheck(sourceChoice choice)
    {
        sourceList.Children.Add(new CheckBox
        {
            Content = new TextBlock { Text = choice.label, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 300 },
            Tag = choice,
            IsChecked = chosenSources.Any(c => c.kind == choice.kind && c.id == choice.id),
            Foreground = (System.Windows.Media.Brush)FindResource("textBrush"),
            Margin = new Thickness(0, 1, 0, 1),
            Cursor = Cursors.Hand
        });
    }

    private async void onSourcePopupClosed(object? sender, EventArgs e)
    {
        var picked = sourceList.Children.OfType<CheckBox>()
            .Where(c => c.IsChecked == true)
            .Select(c => (sourceChoice)c.Tag)
            .ToList();

        bool unchanged = picked.Count == chosenSources.Count &&
                         picked.All(p => chosenSources.Any(c => c.kind == p.kind && c.id == p.id));
        if (unchanged) return;

        chosenSources = picked;
        saveSettings();
        await applySources();
    }

    private async Task applySources()
    {
        sourceText.Text = chosenSources.Count == 0 ? "Pick sources…" : string.Join(", ", chosenSources.Select(c => c.label));
        sourceButton.IsEnabled = false;
        var errors = await engine.setSources(chosenSources);
        sourceButton.IsEnabled = true;

        updateRecordStatus();

        if (errors.Count > 0)
            MessageBox.Show(this, "Some sources couldn't start:\n" + string.Join("\n", errors), "Boardzz",
                MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static List<sourceChoice> listWindows()
    {
        int self = Environment.ProcessId;
        var windows = new List<sourceChoice>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == self || process.MainWindowHandle == IntPtr.Zero) continue;
                    string title = process.MainWindowTitle;
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    windows.Add(new sourceChoice(sourceKind.window, process.Id.ToString(), $"{title} ({process.ProcessName})"));
                }
                catch (InvalidOperationException) { }   // process exited while listing
                catch (System.ComponentModel.Win32Exception) { }   // no access (system process)
            }
        }
        return windows.OrderBy(w => w.label).ToList();
    }

    // Peak level shown on a -60..0 dB scale, falling back smoothly between peaks.
    private void updateMeter()
    {
        meterLevel = drawLevel(levelTrack, levelFill, engine.takeSourcePeak(), meterLevel);
        outputLevel = drawLevel(outputTrack, outputFill, engine.takeOutputPeak(), outputLevel);

        float padPeak = engine.takePadPeak();
        padWave.push(padPeak <= 0 ? 0 : Math.Clamp((20 * Math.Log10(padPeak) + 48) / 48, 0, 1));
    }

    private static double drawLevel(Border track, Border fill, float peak, double previous)
    {
        double target = peak <= 0 ? 0 : Math.Clamp((20 * Math.Log10(peak) + 60) / 60, 0, 1);
        double level = Math.Max(target, previous * 0.85);
        fill.Width = level * track.ActualWidth;
        fill.Background = level > 0.95 ? Brushes.IndianRed : level > 0.8 ? Brushes.Goldenrod : meterGreen;
        return level;
    }

    // ---------- boardzz (record buffer) ----------

    private void updateRecordStatus()
    {
        int running = engine.activeSources.Count;
        bool live = engine.recording && running > 0;
        recordDot.Opacity = live ? 1 : 0.25;
        saveClipButton.Visibility = engine.recording ? Visibility.Visible : Visibility.Collapsed;
        recordStatus.Text = !engine.recording ? "Recording off (Settings)"
            : running == 0 ? "No source, nothing recording"
            : $"Last {engine.recordSeconds} s from {running} source{(running == 1 ? "" : "s")}";
    }

    private void onSaveClipClick(object sender, RoutedEventArgs e)
    {
        var slot = currentSlots.FirstOrDefault(s => s.filePath == null);
        if (slot == null)
        {
            MessageBox.Show(this, "All 25 pads on this tab are full. Clear one or use another tab.", "Boardzz",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Directory.CreateDirectory(boardStore.soundsFolder);
        string path = Path.Combine(boardStore.soundsFolder, $"clip_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
        if (!engine.saveClip(path))
        {
            MessageBox.Show(this, "Nothing recorded yet. Pick a source first.", "Boardzz",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!openEditor(slot, path, $"Clip {DateTime.Now:HH:mm}", null, 0, 0, 1f)) deleteLater(path);
    }

    // ---------- target ----------

    private void onTargetClick(object sender, RoutedEventArgs e)
    {
        targetList.Children.Clear();
        foreach (var device in engine.enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            string id = device.ID;
            var item = new Button
            {
                Content = (id == activeTargetId ? "✓  " : "     ") + device.FriendlyName,
                Style = (Style)FindResource("listButton")
            };
            item.Click += (_, _) =>
            {
                targetPopup.IsOpen = false;
                targetId = id;
                applyTarget();
                saveSettings();
            };
            targetList.Children.Add(item);
        }

        // Apps can only play to speakers-type devices. The bundled VB-CABLE turns one into a microphone.
        if (!vbCable.isInstalled(engine.enumerator))
        {
            addHint(targetList, "To play into Discord or a game as a microphone, install the virtual mic below, " +
                                "then pick \"CABLE Input\" here and select \"CABLE Output\" as the mic in Discord.");

            bool bundled = vbCable.setupPath != null;
            var install = new Button
            {
                Content = bundled ? "Install virtual mic (VB-CABLE)" : "Get VB-CABLE (vb-audio.com)",
                Style = (Style)FindResource("listButton")
            };
            install.Click += async (_, _) =>
            {
                targetPopup.IsOpen = false;
                if (bundled) await installCable();
                else openLink(vbCable.donateUrl);
            };
            targetList.Children.Add(install);
        }

        addHint(targetList, vbCable.attribution);
        var donate = new Button { Content = "Donate to VB-Audio", Style = (Style)FindResource("listButton") };
        donate.Click += (_, _) =>
        {
            targetPopup.IsOpen = false;
            openLink(vbCable.donateUrl);
        };
        targetList.Children.Add(donate);

        targetPopup.IsOpen = true;
    }

    private void applyTarget()
    {
        MMDevice? device = null;
        if (targetId != null)
        {
            try { device = engine.enumerator.GetDevice(targetId); }
            catch (Exception) { device = null; }   // unplugged or removed
        }
        if (device == null) engine.enumerator.TryGetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out device);

        if (device == null)
        {
            targetText.Text = "No output device";
            return;
        }

        try
        {
            engine.setTarget(device);
            activeTargetId = device.ID;
            targetText.Text = device.FriendlyName;
        }
        catch (Exception ex)
        {
            targetText.Text = "Couldn't open device";
            MessageBox.Show(this, $"Couldn't open {device.FriendlyName}:\n{ex.Message}", "Boardzz",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task installCable()
    {
        targetText.Text = "Installing VB-CABLE…";
        try
        {
            if (!await vbCable.install())
            {
                applyTarget();
                return;   // user cancelled the admin prompt
            }
        }
        catch (Exception ex)
        {
            applyTarget();
            MessageBox.Show(this, $"Couldn't install VB-CABLE:\n{ex.Message}", "Boardzz",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Pick the new cable as the target straight away, if Windows already shows it.
        await Task.Delay(1500);
        var cable = engine.enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .FirstOrDefault(d => d.FriendlyName.Contains("CABLE Input", StringComparison.OrdinalIgnoreCase));
        if (cable != null)
        {
            targetId = cable.ID;
            saveSettings();
        }
        applyTarget();

        MessageBox.Show(this, cable != null
                ? "VB-CABLE is installed and selected as the target.\nIn Discord, set your microphone to \"CABLE Output\"."
                : "VB-CABLE was installed. Restart your PC, then pick \"CABLE Input\" as the target.",
            "Boardzz", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void openLink(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private void addHint(Panel list, string text)
    {
        list.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 300,
            FontSize = 12,
            Foreground = (Brush)FindResource("textBrush"),
            Opacity = 0.6,
            Margin = new Thickness(10, 10, 10, 4)
        });
    }

    // ---------- shared ----------

    private static void addListHeader(Panel list, string text)
    {
        list.Children.Add(new TextBlock
        {
            Text = text.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9A, 0xA0, 0xAA)),
            Margin = new Thickness(4, list.Children.Count == 0 ? 0 : 10, 4, 4)
        });
    }
}
