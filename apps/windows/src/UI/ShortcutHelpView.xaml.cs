using Anibel.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Anibel.App.Views;

public sealed partial class ShortcutHelpView : UserControl
{
    public ShortcutHelpView()
    {
        InitializeComponent();
        foreach (var shortcut in ShortcutMap.Bindings.Where(b => b.AfterG))
            AddRow(NavigationRows, shortcut.Description, shortcut.Keys);
        AddRow(NavigationRows, "Назад", "Alt + ←");
        AddRow(ActionRows, "Даведка", "? / F1");
        AddRow(ActionRows, "Пошук", "/");
        AddRow(ActionRows, "Пошук", "Ctrl + K");
        AddRow(ActionRows, "Наступны тайтл", "j");
        AddRow(ActionRows, "Папярэдні тайтл", "k");
        AddRow(ActionRows, "Перамяшчэнне па сетцы", "← ↑ ↓ →");
        AddRow(ActionRows, "Пачатак / канец сеткі", "Home / End");
        AddRow(ActionRows, "Адкрыць тайтл", "Enter");
        AddRow(ActionRows, "Наступны элемент", "Tab");
        AddRow(ActionRows, "Папярэдні элемент", "Shift + Tab");
        AddRow(MediaRows, "Прайграць / паўза", "Space / k");
        AddRow(MediaRows, "Гук / без гуку", "m");
        AddRow(MediaRows, "Поўны экран", "f");
        AddRow(MediaRows, "Перамотка", "← / →");
        AddRow(MediaRows, "Выйсці з поўнага экрана", "Esc");
    }

    private void AddRow(StackPanel rows, string description, string keys)
    {
        var row = new Grid { ColumnSpacing = 12, MinHeight = 30 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock
        {
            Text = description, FontSize = 13, TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var caps = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var tokens = keys == "/" ? new[] { "/" } : keys.Split(' ');
        foreach (var token in tokens)
        {
            // Arrows between two letters denote a sequence; arrows alone are keys.
            if (token == "+" || token == "/" && keys != "/" || token == "→" && keys.StartsWith("g ", StringComparison.Ordinal))
            {
                caps.Children.Add(new TextBlock { Text = token, FontSize = 12, Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center });
                continue;
            }
            caps.Children.Add(new Border
            {
                Style = (Style)Resources["KeyCap"],
                Child = new TextBlock
                {
                    Text = token, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            });
        }
        Grid.SetColumn(caps, 1);
        row.Children.Add(caps);
        rows.Children.Add(row);
    }

    private void OnSectionsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var columns = e.NewSize.Width >= 850 ? 3 : e.NewSize.Width >= 580 ? 2 : 1;
        FrameworkElement[] sections = [NavigationSection, ActionSection, MediaSection];
        for (var i = 0; i < 3; i++)
        {
            Sections.ColumnDefinitions[i].Width = i < columns ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumn(sections[i], i % columns);
            Grid.SetRow(sections[i], i / columns);
        }
    }
}
