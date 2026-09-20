using Anibel.App.Core;
using Anibel.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Anibel.App.Playback;

internal sealed class PlayerSettingsMenu(Func<PlayerController?> current, Func<Views.PlayerArgs?> arguments,
    Action<bool> changing, Action<string, bool> notice, Action<string> error)
{
    public void Hide() => _qualityMenu?.Hide();
    private MenuFlyout? _qualityMenu;
    public bool IsOpen => _qualityMenu?.IsOpen == true;

    public async Task ShowAsync(FrameworkElement anchor)
    {
        var controller = current();
        var engine = controller?.Engine as WindowsMediaEngine;
        _qualityMenu?.Hide();
        var menu = _qualityMenu = new MenuFlyout
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.TopEdgeAlignedRight,
        };
        menu.Items.Add(new MenuFlyoutItem { Text = "Загрузка…", IsEnabled = false });
        menu.ShowAt(anchor);
        try
        {
            if (engine is null)
            {
                menu.Items.Clear();
                menu.Items.Add(new MenuFlyoutItem { Text = "Якасць задаецца ўбудаваным плэерам", IsEnabled = false });
                return;
            }
            var core = App.Services.GetRequiredService<ICoreClient>();
            var qualities = await core.CallAsync<VideoQualitiesDto>(CoreCommand.VideoQualities, new { tracks = engine.ReadVideoTracks() });
            if (!ReferenceEquals(controller, current()) || !ReferenceEquals(engine, controller?.Engine)) { menu.Hide(); return; }
            menu.Items.Clear();
            if (arguments() is { DownloadId: null, EpisodeId: not null } args)
            {
                var download = new MenuFlyoutItem { Text = "Спампаваць MKV", Icon = new SymbolIcon(Symbol.Download) };
                download.Click += async (_, _) =>
                {
                    menu.Hide();
                    try
                    {
                        await App.Services.GetRequiredService<DownloadService>().EnqueueEpisode(new EpisodeDownloadRequest
                        {
                            EpisodeId = args.EpisodeId, EpisodeUrl = args.Url,
                            MediaId = "", MediaType = args.MediaType, Slug = args.Slug,
                            Title = args.TitleLabel, EpisodeLabel = args.EpisodeLabel,
                            EpisodeType = args.EpisodeType
                        });
                        if (ReferenceEquals(engine, current()?.Engine))
                        {
                            notice("MKV дададзены ў спампоўкі", false);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ReferenceEquals(engine, current()?.Engine))
                        {
                            notice(Ui.DisplayMessage(ex), true);
                        }
                    }
                };
                menu.Items.Add(download);
                menu.Items.Add(new MenuFlyoutSeparator());
            }
            AddTrackMenu(menu, engine, "Аўдыё",
                engine.ReadAudioTracks().Select(t => (t.Id, TrackLabel(t.Id, t.Title, t.Lang))),
                engine.AudioTrack, engine.SetAudioTrack);
            AddTrackMenu(menu, engine, "Субцітры",
                new[] { (0L, "Выключаны") }.Concat(engine.ReadSubtitleTracks()
                    .Select(t => (t.Id, TrackLabel(t.Id, t.Title, t.Lang)))),
                engine.SubTrack, engine.SetSubTrack);
            var currentQuality = qualities.Choices.FirstOrDefault(q => q.Selected)?.Label ?? "Аўта";
            var qualityMenu = new MenuFlyoutSubItem { Text = $"Якасць відэа · {currentQuality}" };
            menu.Items.Add(qualityMenu);
            if (engine.IsAdaptive)
            {
                var automatic = new MenuFlyoutItem { Text = "Аўта", Icon = engine.IsAutomaticQuality ? new SymbolIcon(Symbol.Accept) : null };
                automatic.Click += (_, _) =>
                {
                    menu.Hide();
                    if (ReferenceEquals(engine, current()?.Engine)) engine.SetAutomaticQuality();
                };
                qualityMenu.Items.Add(automatic);
            }
            foreach (var quality in qualities.Choices)
            {
                var item = new MenuFlyoutItem { Text = quality.Label,
                    Icon = quality.Selected ? new SymbolIcon(Symbol.Accept) : null };
                item.Click += async (_, _) =>
                {
                    menu.Hide();
                    try
                    {
                        if (!ReferenceEquals(engine, current()?.Engine)) return;
                        var selection = await core.CallAsync<VideoQualitiesDto>(CoreCommand.VideoQualities, new { tracks = engine.ReadVideoTracks(), select = quality.Id });
                        if (ReferenceEquals(engine, current()?.Engine) && selection.Video is { } id)
                        {
                            changing(true);
                            await engine.SetVideoTrackAsync(id);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!ReferenceEquals(_qualityMenu, menu) || anchor.XamlRoot is null) return;
                        menu.Items.Clear();
                        menu.Items.Add(new MenuFlyoutItem { Text = Ui.DisplayMessage(ex), IsEnabled = false });
                        menu.ShowAt(anchor);
                    }
                    finally
                    {
                        if (ReferenceEquals(engine, current()?.Engine)) changing(false);
                    }
                };
                qualityMenu.Items.Add(item);
            }
            if (qualities.Choices.Length <= 1)
            {
                if (qualities.Choices.Length > 0) qualityMenu.Items.Add(new MenuFlyoutSeparator());
                qualityMenu.Items.Add(new MenuFlyoutItem { Text = "Іншыя варыянты якасці недаступныя", IsEnabled = false });
            }
        }
        catch (Exception ex)
        {
            menu.Items.Clear();
            menu.Items.Add(new MenuFlyoutItem { Text = Ui.DisplayMessage(ex), IsEnabled = false });
        }
    }


    private static string TrackLabel(long id, string title, string language)
    {
        var name = string.IsNullOrWhiteSpace(title) ? $"Дарожка {id}" : title.Trim();
        return string.IsNullOrWhiteSpace(language) ? name : $"{name} · {language}";
    }

    private void AddTrackMenu(MenuFlyout menu, WindowsMediaEngine engine, string label,
        IEnumerable<(long Id, string Label)> tracks, long selected, Action<long> select)
    {
        var choices = tracks.ToArray();
        var currentLabel = choices.FirstOrDefault(t => t.Id == selected).Label ?? "Не выбрана";
        var submenu = new MenuFlyoutSubItem { Text = $"{label} · {currentLabel}" };
        foreach (var track in choices)
        {
            var item = new MenuFlyoutItem
            {
                Text = track.Label, Icon = track.Id == selected ? new SymbolIcon(Symbol.Accept) : null
            };
            item.Click += (_, _) =>
            {
                menu.Hide();
                if (!ReferenceEquals(engine, current()?.Engine)) return;
                try { select(track.Id); }
                catch (Exception ex) { error(Ui.DisplayMessage(ex)); }
            };
            submenu.Items.Add(item);
        }
        if (submenu.Items.Count == 0)
            submenu.Items.Add(new MenuFlyoutItem { Text = "Дарожкі недаступныя", IsEnabled = false });
        menu.Items.Add(submenu);
    }

}
