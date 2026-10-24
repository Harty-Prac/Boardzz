using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using boardzz.ui;

namespace boardzz;

// The tab bar above the grid: up to 9 named tabs, each with an optional emoji and its own grid.
public partial class mainWindow
{
    private static readonly Brush selectedTabBrush = frozenBrush(0x58, 0x65, 0xF2);
    private static readonly Brush idleTabBrush = frozenBrush(0x23, 0x24, 0x28);

    private void selectTab(int index)
    {
        currentTab = index;
        padGrid.Children.Clear();
        foreach (var slot in currentSlots) padGrid.Children.Add(slot.button);
        buildTabBar();
    }

    private void buildTabBar()
    {
        tabBar.Children.Clear();

        for (int i = 0; i < tabs.Count; i++)
        {
            int index = i;
            var tab = tabs[i];

            var content = new StackPanel { Orientation = Orientation.Horizontal };
            if (tab.emoji != null)
            {
                var icon = new Image { Width = 18, Height = 18, Margin = new Thickness(0, 0, 6, 0) };
                Emoji.Wpf.Image.SetSource(icon, tab.emoji);
                content.Children.Add(icon);
            }
            content.Children.Add(new TextBlock { Text = tab.name, VerticalAlignment = VerticalAlignment.Center });

            var button = new Button
            {
                Content = content,
                Style = (Style)FindResource("listButton"),
                Background = index == currentTab ? selectedTabBrush : idleTabBrush,
                Margin = new Thickness(2, 0, 2, 0),
                Padding = new Thickness(12, 7, 12, 7),
                ToolTip = "Double-click or right-click to rename"
            };
            button.Click += (_, _) =>
            {
                if (index != currentTab)
                {
                    selectTab(index);
                    saveBoard();
                }
            };
            button.MouseDoubleClick += (_, _) => editTab(index);
            button.ContextMenu = buildTabMenu(index);
            tabBar.Children.Add(button);
        }

        if (tabs.Count < maxTabs)
        {
            var add = new Button
            {
                Content = "+  Add tab",
                Style = (Style)FindResource("listButton"),
                Opacity = 0.7,
                Margin = new Thickness(2, 0, 2, 0),
                Padding = new Thickness(12, 7, 12, 7)
            };
            add.Click += (_, _) => addTab();
            tabBar.Children.Add(add);
        }
    }

    private ContextMenu buildTabMenu(int index)
    {
        var rename = new MenuItem { Header = "Rename / emoji…" };
        rename.Click += (_, _) => editTab(index);

        var delete = new MenuItem { Header = "Delete tab", IsEnabled = tabs.Count > 1 };
        delete.Click += (_, _) => deleteTab(index);

        var menu = new ContextMenu();
        menu.Items.Add(rename);
        menu.Items.Add(delete);
        return menu;
    }

    private void addTab()
    {
        var dialog = new tabWindow($"Tab {tabs.Count + 1}", null, "Add tab") { Owner = this };
        if (dialog.ShowDialog() != true) return;

        createTab(dialog.resultName, dialog.resultEmoji, []);
        selectTab(tabs.Count - 1);
        saveBoard();
    }

    private void editTab(int index)
    {
        var tab = tabs[index];
        var dialog = new tabWindow(tab.name, tab.emoji, "Edit tab") { Owner = this };
        if (dialog.ShowDialog() != true) return;

        tab.name = dialog.resultName;
        tab.emoji = dialog.resultEmoji;
        buildTabBar();
        saveBoard();
    }

    private async void deleteTab(int index)
    {
        if (tabs.Count <= 1) return;
        var tab = tabs[index];

        if (tab.hasSounds)
        {
            var answer = MessageBox.Show(this, $"Delete \"{tab.name}\" and all its sounds?", "Boardzz",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }

        foreach (var slot in tab.slots) slot.voice?.stop();
        var files = tab.slots.Select(s => s.filePath).ToList();

        tabs.RemoveAt(index);
        selectTab(Math.Min(currentTab > index ? currentTab - 1 : currentTab, tabs.Count - 1));
        saveBoard();

        // Give the mixer a moment to drop the stopped pads, then release and delete their files.
        await Task.Delay(300);
        foreach (var slot in tab.slots) finishPad(slot);
        foreach (var file in files) deleteLater(file);
    }
}
