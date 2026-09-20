using Anibel.App.Core;
using Anibel.App.Services;
using Anibel.App.Views.Converters;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Anibel.App.Views;

public sealed partial class CommentThreadView : UserControl
{
    public static readonly DependencyProperty CommentProperty = DependencyProperty.Register(
        nameof(Comment), typeof(CommentDto), typeof(CommentThreadView), new PropertyMetadata(null, OnCommentChanged));
    public CommentDto? Comment { get => (CommentDto?)GetValue(CommentProperty); set => SetValue(CommentProperty, value); }
    public event EventHandler<CommentDto>? ReplyRequested;

    public CommentThreadView() { InitializeComponent(); }
    private static void OnCommentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) =>
        ((CommentThreadView)sender).Render();
    private void Render()
    {
        if (Comment is not { } comment) return;
        var name = string.IsNullOrWhiteSpace(comment.User?.DisplayName) ? CommentDisplay.Username(comment) : comment.User.DisplayName;
        AuthorButton.Content = name;
        Avatar.DisplayName = name;
        AuthorButton.IsEnabled = AvatarButton.IsEnabled = !string.IsNullOrWhiteSpace(comment.User?.Username);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(AvatarButton, name);
        ToolTipService.SetToolTip(AuthorButton, "@" + comment.User?.Username);
        Avatar.ProfilePicture = Uri.TryCreate(comment.User?.Avatar, UriKind.Absolute, out var uri) ? new BitmapImage(uri) { DecodePixelWidth = 96 } : null;
        BodyText.Text = comment.Content;
        DateText.Text = CommentDisplay.DateLabel(comment);
        Replies.ItemsSource = comment.Replies;
        ReplyBorder.Visibility = comment.Replies.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OnAuthorClick(object sender, RoutedEventArgs e)
    {
        if (Comment?.User?.Username is { Length: > 0 } name)
            WeakReferenceMessenger.Default.Send(new OpenProfileMessage(name));
    }
    private void OnReplyClick(object sender, RoutedEventArgs e)
    {
        if (Comment is { } comment) ReplyRequested?.Invoke(this, comment);
    }
    private void OnChildReply(object? sender, CommentDto comment) => ReplyRequested?.Invoke(this, comment);
    private void OnCopyClick(object sender, RoutedEventArgs e) => Ui.CopyToClipboard(Comment?.Content);
}
