using System.Windows;
using boardzz.ui;

namespace boardzz;

// The settings page (gear button, top right) and the options it controls.
public partial class mainWindow
{
    // false: pressing a pad stops whatever is playing. true: pads play over each other.
    private bool overlapPads;

    private void onSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new settingsWindow(overlapPads, engine.recording, engine.recordSeconds, (int)maxSoundSeconds)
        {
            Owner = this
        };
        if (dialog.ShowDialog() != true) return;

        overlapPads = dialog.overlapPads;
        engine.setRecording(dialog.recording, dialog.recordSeconds);
        updateRecordStatus();
        saveSettings();
    }
}
