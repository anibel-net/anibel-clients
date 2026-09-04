using Anibel.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Anibel.App.Views;

public sealed partial class EmptyState : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyState),
            new PropertyMetadata(Strings.EmptyStateDefault, OnTitleChanged));

    public static readonly DependencyProperty MessageProperty =
        DependencyProperty.Register(nameof(Message), typeof(string), typeof(EmptyState),
            new PropertyMetadata("", OnMessageChanged));

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(EmptyState),
            new PropertyMetadata("\uE7BA", OnGlyphChanged));

    public static readonly DependencyProperty ActionTextProperty =
        DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(EmptyState),
            new PropertyMetadata("", OnActionChanged));

    public static readonly DependencyProperty SecondaryTextProperty =
        DependencyProperty.Register(nameof(SecondaryText), typeof(string), typeof(EmptyState),
            new PropertyMetadata("", OnSecondaryChanged));

    public EmptyState()
    {
        InitializeComponent();
        Loaded += (_, _) => Apply();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public string SecondaryText
    {
        get => (string)GetValue(SecondaryTextProperty);
        set => SetValue(SecondaryTextProperty, value);
    }

    public event RoutedEventHandler? ActionClick;
    public event RoutedEventHandler? SecondaryClick;

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EmptyState)d).Apply();
    private static void OnMessageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EmptyState)d).Apply();
    private static void OnGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EmptyState)d).Apply();
    private static void OnActionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EmptyState)d).Apply();
    private static void OnSecondaryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((EmptyState)d).Apply();

    private void Apply()
    {
        if (TitleBlock is null)
        {
            return;
        }
        TitleBlock.Text = Title ?? "";
        MessageBlock.Text = Message ?? "";
        MessageBlock.Visibility = string.IsNullOrWhiteSpace(Message) ? Visibility.Collapsed : Visibility.Visible;
        if (!string.IsNullOrEmpty(Glyph))
        {
            GlyphIcon.Glyph = Glyph;
        }
        ActionButton.Content = ActionText ?? "";
        ActionButton.Visibility = string.IsNullOrWhiteSpace(ActionText) ? Visibility.Collapsed : Visibility.Visible;
        SecondaryButton.Content = SecondaryText ?? "";
        SecondaryButton.Visibility = string.IsNullOrWhiteSpace(SecondaryText) ? Visibility.Collapsed : Visibility.Visible;
        Actions.Visibility = ActionButton.Visibility == Visibility.Visible
            || SecondaryButton.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnActionClick(object sender, RoutedEventArgs e) => ActionClick?.Invoke(this, e);
    private void OnSecondaryClick(object sender, RoutedEventArgs e) => SecondaryClick?.Invoke(this, e);
}
