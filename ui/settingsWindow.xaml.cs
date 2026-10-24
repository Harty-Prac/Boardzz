using System.Windows;

namespace boardzz.ui;

// Settings page. Each option is passed in, and read back after Save.
public partial class settingsWindow : Window
{
    private readonly int maxSeconds;

    public bool overlapPads { get; private set; }
    public bool recording { get; private set; }
    public int recordSeconds { get; private set; }

    public settingsWindow(bool overlapPads, bool recording, int recordSeconds, int maxSeconds)
    {
        InitializeComponent();
        this.maxSeconds = maxSeconds;
        this.recordSeconds = recordSeconds;

        overlapBox.IsChecked = overlapPads;
        recordBox.IsChecked = recording;
        secondsBox.Text = recordSeconds.ToString();
        secondsHint.Text = $"seconds (1 to {maxSeconds})";
        SourceInitialized += (_, _) => darkTitleBar.apply(this);
    }

    private void onSaveClick(object sender, RoutedEventArgs e)
    {
        overlapPads = overlapBox.IsChecked == true;
        recording = recordBox.IsChecked == true;
        if (int.TryParse(secondsBox.Text.Trim(), out int seconds))
            recordSeconds = Math.Clamp(seconds, 1, maxSeconds);
        DialogResult = true;
    }
}
