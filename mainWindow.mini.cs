using System.Windows;

namespace boardzz;

// Mini mode: a small, always-on-top board (pads + tabs only). The button beside the gear toggles it.
public partial class mainWindow
{
    private const double miniWidth = 400, miniHeight = 470;
    private const double fullMinWidth = 640, fullMinHeight = 520;

    private static bool compactPads;   // read by the static pad renderer
    private bool miniMode;
    private Size fullSize = new(820, 740);
    private Size miniSize = new(miniWidth, miniHeight);

    private void setMini(bool mini)
    {
        if (mini != miniMode)
        {
            // Remember the size of the mode we are leaving.
            if (miniMode) miniSize = new Size(Width, Height);
            else fullSize = new Size(Width, Height);
        }
        miniMode = mini;
        compactPads = mini;

        MinWidth = mini ? 300 : fullMinWidth;
        MinHeight = mini ? 340 : fullMinHeight;
        var size = mini ? miniSize : fullSize;
        Width = size.Width;
        Height = size.Height;
        Topmost = mini;

        routingToggle.Visibility = mini ? Visibility.Collapsed : Visibility.Visible;
        routingBar.Visibility = !mini && routingOpen ? Visibility.Visible : Visibility.Collapsed;
        padGrid.Margin = new Thickness(mini ? 4 : 8);

        miniButton.Content = mini ? "" : "";   // expand / shrink
        miniButton.ToolTip = mini ? "Expand to full size" : "Mini mode";

        foreach (var slot in slots) refresh(slot);
        buildTabBar();
    }

    private void onMiniClick(object sender, RoutedEventArgs e)
    {
        setMini(!miniMode);
        saveSettings();
    }
}
