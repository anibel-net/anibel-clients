using Anibel.App.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;

namespace Anibel.App.Views;

public sealed partial class ProfileEditView : UserControl
{
    private string _avatar;
    private string _wallpaper;
    private string? _avatarPath;
    private string? _wallpaperPath;

    public ProfileEditView(ProfileDto profile)
    {
        InitializeComponent();
        _avatar = profile.Avatar ?? "";
        _wallpaper = profile.Wallpaper ?? "";
        DisplayNameBox.Text = profile.DisplayName ?? "";
        BioBox.Text = profile.Bio ?? "";
        AvatarPreview.DisplayName = profile.Username;
        UpdatePreviews();
    }

    public object Patch => new
    {
        displayName = DisplayNameBox.Text, bio = BioBox.Text,
        avatar = _avatar, wallpaper = _wallpaper,
        avatarPath = _avatarPath, wallpaperPath = _wallpaperPath,
    };

    public void ShowError(string message) { ErrorBar.Message = message; ErrorBar.IsOpen = true; }

    private async void OnPickImageClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind } button) return;
        button.IsEnabled = false;
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp" }) picker.FileTypeFilter.Add(extension);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.CurrentWindow!));
            var file = await picker.PickSingleFileAsync();
            if (file is null) return;
            if (kind == "avatar") _avatarPath = file.Path; else _wallpaperPath = file.Path;
            ErrorBar.IsOpen = false;
            UpdatePreviews();
        }
        catch (Exception ex) { ShowError(Services.Ui.DisplayMessage(ex)); }
        finally { button.IsEnabled = true; }
    }

    private void OnRemoveImageClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag as string == "avatar") { _avatar = ""; _avatarPath = null; }
        else { _wallpaper = ""; _wallpaperPath = null; }
        UpdatePreviews();
    }

    private void UpdatePreviews()
    {
        AvatarPreview.ProfilePicture = Image(_avatarPath ?? _avatar, 160);
        WallpaperPreview.Source = Image(_wallpaperPath ?? _wallpaper, 900);
    }

    private static BitmapImage? Image(string source, int width) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri) ? new BitmapImage(uri) { DecodePixelWidth = width } : null;
}
