using System.Windows;
using System.Windows.Threading;

namespace boardzz.ui;

// Small dialog for a tab's name and emoji.
public partial class tabWindow : Window
{
    public string resultName { get; private set; } = "";
    public string? resultEmoji { get; private set; }

    public tabWindow(string name, string? emoji, string title)
    {
        InitializeComponent();
        heading.Text = title;
        Title = title;
        nameBox.Text = name;
        emojiField.emoji = emoji;
        SourceInitialized += (_, _) => darkTitleBar.apply(this);
        Loaded += (_, _) => Dispatcher.InvokeAsync(() =>
        {
            nameBox.Focus();
            nameBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void onSaveClick(object sender, RoutedEventArgs e)
    {
        string name = nameBox.Text.Trim();
        resultName = name.Length > 0 ? name : "Tab";
        resultEmoji = emojiField.emoji;
        DialogResult = true;
    }
}
