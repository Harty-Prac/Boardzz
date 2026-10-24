using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace boardzz.ui;

// Small fixed grid of emojis. Emoji.Wpf is only used to draw them in colour.
public partial class emojiPicker : UserControl
{
    private static readonly string[] choices =
    [
        "😂", "🤣", "😭", "😡", "😱", "🤯", "😎", "🥳",
        "🤡", "💀", "👻", "🔥", "💥", "💯", "👏", "🙏",
        "👍", "👎", "❤️", "💔", "🎉", "🎵", "📢", "🚨",
        "⚠️", "❌", "✅", "❓", "😴", "🤢", "🥶", "👀",
        "🐐", "🐸", "🐶", "🐱", "🍿", "🍕", "🏆", "🎮",
        "💣", "⏰", "💸", "🚀", "🤔", "😏", "🙄", "😐"
    ];

    private string? selected;

    public emojiPicker()
    {
        InitializeComponent();
    }

    public string? emoji
    {
        get => selected;
        set
        {
            selected = string.IsNullOrEmpty(value) ? null : value;
            showSelection();
        }
    }

    private void onFieldClick(object sender, RoutedEventArgs e)
    {
        if (grid.Children.Count == 0) buildGrid();
        popup.IsOpen = !popup.IsOpen;
    }

    private void buildGrid()
    {
        foreach (var choice in choices)
        {
            var image = new Image { Width = 26, Height = 26 };
            Emoji.Wpf.Image.SetSource(image, choice);

            var cell = new Button
            {
                Content = image,
                Width = 40,
                Height = 40,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            cell.Click += (_, _) => pick(choice);
            grid.Children.Add(cell);
        }
    }

    private void pick(string? choice)
    {
        emoji = choice;
        popup.IsOpen = false;
    }

    private void onClearClick(object sender, RoutedEventArgs e) => pick(null);

    private void showSelection()
    {
        if (selected == null)
        {
            selectedImage.Visibility = Visibility.Collapsed;
            fieldText.Text = "Click to select";
            fieldText.Margin = new Thickness(0);
            return;
        }

        Emoji.Wpf.Image.SetSource(selectedImage, selected);
        selectedImage.Visibility = Visibility.Visible;
        fieldText.Text = "Change";
        fieldText.Margin = new Thickness(8, 0, 0, 0);
    }
}
