using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;

namespace Anibel.App.Views;

// Each half-star is one point on the core's 10-point scale.
public sealed class StarRating : UserControl
{
    private readonly TextBlock[] _fills = new TextBlock[5];
    private readonly Button[] _steps = new Button[10];
    public event EventHandler<int>? RatingSelected;
    public static readonly DependencyProperty RatingProperty = DependencyProperty.Register(
        nameof(Rating), typeof(double), typeof(StarRating), new PropertyMetadata(0.0, OnRatingChanged));
    public double Rating { get => (double)GetValue(RatingProperty); set => SetValue(RatingProperty, value); }

    public StarRating()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        for (var star = 0; star < 5; star++)
        {
            var cell = new Grid { Width = 40, Height = 40 };
            for (var half = 0; half < 2; half++)
            {
                var score = star * 2 + half + 1;
                var button = new Button
                {
                    Width = 20, Height = 40, MinWidth = 0, Padding = new Thickness(0),
                    HorizontalAlignment = half == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0),
                    Tag = score,
                };
                var label = $"{score / 2.0:0.#} / 5";
                AutomationProperties.SetName(button, label);
                ToolTipService.SetToolTip(button, label);
                button.Click += (_, _) => RatingSelected?.Invoke(this, score);
                button.KeyDown += OnStepKeyDown;
                _steps[score - 1] = button;
                cell.Children.Add(button);
            }
            var outline = Star("☆");
            cell.Children.Add(outline);
            var fill = Star("★");
            fill.Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
            _fills[star] = fill;
            cell.Children.Add(fill);
            row.Children.Add(cell);
        }
        Content = row;
        Render();
    }

    private static TextBlock Star(string text) => new()
    {
        Text = text, FontSize = 32, Width = 40, Height = 40,
        TextAlignment = TextAlignment.Center, IsHitTestVisible = false,
    };
    private static void OnRatingChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((StarRating)sender).Render();
    private void Render()
    {
        var score = double.IsFinite(Rating) ? Math.Clamp(Rating, 0, 10) : 0;
        for (var star = 0; star < 5; star++)
            _fills[star].Clip = new RectangleGeometry { Rect = new Rect(0, 0, 40 * Math.Clamp(score / 2 - star, 0, 1), 40) };
        var selected = Math.Clamp((int)Math.Round(score) - 1, 0, 9);
        for (var i = 0; i < 10; i++) _steps[i].IsTabStop = i == selected;
    }
    private void OnStepKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var index = (int)((Button)sender).Tag - 1;
        var next = e.Key switch
        {
            VirtualKey.Left or VirtualKey.Down => Math.Max(0, index - 1),
            VirtualKey.Right or VirtualKey.Up => Math.Min(9, index + 1),
            VirtualKey.Home => 0,
            VirtualKey.End => 9,
            _ => -1,
        };
        if (next < 0) return;
        e.Handled = true;
        _steps[next].Focus(FocusState.Keyboard);
    }
}
