using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace boardzz;

// Show/hide of the routing bar and the source/target volume sliders (0–200%).
public partial class mainWindow
{
    private const double maxVolumePercent = 200;
    private readonly DispatcherTimer volumeSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool routingLoaded;
    private bool routingOpen;

    private void loadRouting(appSettings settings)
    {
        sourceVolumeSlider.Value = Math.Clamp(settings.sourceVolume * 100, 0, maxVolumePercent);
        targetVolumeSlider.Value = Math.Clamp(settings.targetVolume * 100, 0, maxVolumePercent);
        applyVolumes();   // ValueChanged doesn't fire if a saved value equals the default
        showRouting(settings.showRouting);
        setMini(settings.miniMode);
        sourceVolumeSlider.MouseDoubleClick += (_, _) => sourceVolumeSlider.Value = 100;
        targetVolumeSlider.MouseDoubleClick += (_, _) => targetVolumeSlider.Value = 100;

        // Dragging fires many changes, so save once the slider settles.
        volumeSaveTimer.Tick += (_, _) =>
        {
            volumeSaveTimer.Stop();
            saveSettings();
        };
        Closing += (_, _) =>
        {
            if (volumeSaveTimer.IsEnabled) saveSettings();
        };
        routingLoaded = true;
    }

    private void showRouting(bool show)
    {
        routingOpen = show;
        routingBar.Visibility = show && !miniMode ? Visibility.Visible : Visibility.Collapsed;
        routingToggleIcon.Text = show ? "" : "";   // chevron down / up
        routingToggleText.Text = show ? "Hide audio routing" : "Audio routing";
    }

    private void onRoutingToggleClick(object sender, RoutedEventArgs e)
    {
        showRouting(!routingOpen);
        saveSettings();
    }

    private void onVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (sourceVolumeText == null || targetVolumeText == null) return;   // still building the window
        applyVolumes();

        if (!routingLoaded) return;
        volumeSaveTimer.Stop();
        volumeSaveTimer.Start();
    }

    private void applyVolumes()
    {
        float source = (float)(sourceVolumeSlider.Value / 100);
        float target = (float)(targetVolumeSlider.Value / 100);
        engine.sourceVolume = source;
        engine.targetVolume = target;
        sourceVolumeText.Text = $"{sourceVolumeSlider.Value:0}%";
        targetVolumeText.Text = $"{targetVolumeSlider.Value:0}%";
    }
}
