using Anibel.App.Core;
using Anibel.App.Playback;
using Anibel.App.Services;
using Anibel.App.ViewModels;
using Anibel.App.Views;
using Anibel.App.Views.Converters;
using Xunit;

namespace Anibel.App.Tests;

public class HomeViewModelTests
{
    [Fact]
    public async Task LoadAsync_fills_slider_and_updates()
    {
        var core = new FakeCoreClient
        {
            Slides = [new SlideDto { Id = "s1", Img = "https://cdn.example/s.jpg" }],
            UpdatesPage = new([new MediaCard { Slug = "u1", MediaType = "anime" }],1,false),
        };
        var vm = new HomeViewModel(core);
        await vm.LoadAsync();
        Assert.False(vm.IsBusy);
        Assert.True(vm.HasSlides);
        Assert.Single(vm.Slides);
        Assert.Single(vm.Updates);
        Assert.Equal("ALL", core.Calls.Last(c => c.Op == "updatesPage").Args.GetProperty("type").GetString());
        Assert.Null(vm.StatusMessage);
    }

    [Fact]
    public async Task SetUpdateTypeAsync_reloads_filtered_updates()
    {
        var core = new FakeCoreClient
        {
            UpdatesPage = new([new MediaCard { Slug = "m1", MediaType = "manga" }],1,false),
        };
        var vm = new HomeViewModel(core);
        await vm.LoadAsync();
        await vm.SetUpdateTypeAsync("MANGA");
        Assert.Equal("MANGA", core.Calls.Last(c => c.Op == "updatesPage").Args.GetProperty("type").GetString());
        Assert.Equal("MANGA", vm.UpdateType);
        Assert.Single(vm.Updates);
    }

    [Fact]
    public void Slide_link_parses_anibel_paths()
    {
        Assert.True(HomePage.TryParseMediaLink("https://anibel.net/anime/death-note", out var slug, out var type));
        Assert.Equal("death-note", slug);
        Assert.Equal("anime", type);
        Assert.True(HomePage.TryParseMediaLink("/manga/dallae", out slug, out type));
        Assert.Equal("dallae", slug);
        Assert.Equal("manga", type);
        Assert.False(HomePage.TryParseMediaLink("/about", out _, out _));
    }

    [Fact]
    public async Task LoadAsync_surfaces_core_errors()
    {
        var vm = new HomeViewModel(new ThrowingSliderClient());
        await vm.LoadAsync();
        Assert.False(vm.IsBusy);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
        Assert.True(vm.ShowUpdatesError);
        Assert.False(vm.ShowUpdatesEmpty);
    }

    private sealed class ThrowingSliderClient : FakeCoreClient
    {
        public override Task<SlideDto[]> SliderAsync(int limit = 6, CancellationToken ct = default)
            => throw new CoreException("http_error", "down");
    }
}

public class QuickSearchTests
{
    [Fact]
    public void Build_adds_preview_rows_and_show_more()
    {
        var hits = Enumerable.Range(1, 8)
            .Select(i => new MediaCard { Slug = $"s{i}", MediaType = "anime", Title = new TitleDto { Be = $"T{i}" } })
            .ToList();
        var items = QuickSearch.Build("death", hits);
        Assert.Equal(QuickSearch.PreviewCount + 1, items.Count);
        Assert.True(items[^1].IsMore);
        Assert.Equal("death", items[^1].Subtitle);
        Assert.True(items[0].IsMedia);
        Assert.Equal("T1", items[0].Title);
        Assert.Equal("Анімэ", items[0].Subtitle);
    }

    [Fact]
    public void Build_empty_hits_still_offers_full_search()
    {
        var items = QuickSearch.Build("death", []);
        Assert.Single(items);
        Assert.True(items[0].IsMore);
        Assert.Contains("death", items[0].Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_idle_shows_history_rows()
    {
        var items = QuickSearch.Build("", [], ["lets vpn", "clash", "netflix"]);
        Assert.Equal(3, items.Count);
        Assert.True(items.All(i => i.IsHistory));
        Assert.Equal("lets vpn", items[0].Title);
        Assert.False(items[0].HasPoster);
    }

    [Fact]
    public void Build_filters_history_and_prepends_before_hits()
    {
        var hits = new List<MediaCard>
        {
            new() { Slug = "death-note", MediaType = "anime", Title = new TitleDto { Be = "Death Note" } },
        };
        var items = QuickSearch.Build("de", hits, ["death note", "clash", "demon slayer"]);
        Assert.True(items[0].IsHistory);
        Assert.Equal("death note", items[0].Title);
        Assert.True(items[1].IsHistory);
        Assert.Equal("demon slayer", items[1].Title);
        Assert.True(items[2].IsMedia);
        Assert.True(items[^1].IsMore);
        Assert.DoesNotContain(items, i => i.IsHistory && i.Title == "clash");
    }
}

public class SearchViewModelTests
{
    [Fact]
    public async Task SearchAsync_fills_items_and_stats()
    {
        var core = new FakeCoreClient
        {
            SearchResults = [new MediaCard { Slug = "death-note", MediaType = "anime" }],
        };
        var vm = new SearchViewModel(core);
        await vm.SearchAsync("death");
        Assert.Equal("death", vm.Query);
        Assert.Single(vm.Items);
        Assert.Contains("1", vm.Stats);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task SearchAsync_ignores_empty_query()
    {
        var core = new FakeCoreClient();
        var vm = new SearchViewModel(core);
        await vm.SearchAsync("   ");
        Assert.Equal(0, core.SearchCalls);
    }

    [Fact]
    public async Task SearchAsync_surfaces_core_errors()
    {
        var core = new FakeCoreClient { SearchError = new CoreException("http_error", "down") };
        var vm = new SearchViewModel(core);
        await vm.SearchAsync("death");
        Assert.True(vm.HasStatus);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
        Assert.True(vm.ShowErrorState);
        Assert.False(vm.ShowIdleState);
        Assert.False(vm.ShowNoResults);
        Assert.DoesNotContain("Памылка:", vm.Stats, StringComparison.Ordinal);
    }
}

public class CatalogViewModelTests
{
    [Fact]
    public async Task OpenAsync_loads_filters_and_page()
    {
        var core = new FakeCoreClient
        {
            Filters = new AnibelFiltersDto([2024, 2023], ["драма"], ["studio"]),
            MediaList = new PaginationDto<MediaCard>(
                [new MediaCard { Slug = "a", MediaType = "anime" }], 12, 60, 0, 1, true),
        };
        var vm = new CatalogViewModel(core);
        await vm.OpenAsync("anime", "Анімэ");
        Assert.Equal("Анімэ", vm.Title);
        Assert.Single(vm.Items);
        Assert.Contains("12", vm.Stats);
        Assert.True(vm.Years.Count >= 3);
        Assert.Single(vm.Genres);
        Assert.True(vm.HasMore);
        Assert.Equal(0, core.LastMediaListOffset);
        Assert.True(vm.ShowLanguageFilters);
    }

    [Fact]
    public async Task OpenAsync_clears_grid_before_results_arrive()
    {
        var core = new FakeCoreClient
        {
            MediaListDelayMs = 200,
            MediaList = new PaginationDto<MediaCard>(
                [new MediaCard { Slug = "a", MediaType = "anime" }], 1, 60, 0),
        };
        var vm = new CatalogViewModel(core);
        var task = vm.OpenAsync("anime", "Анімэ");
        Assert.True(vm.IsBusy);
        Assert.Empty(vm.Items);
        await task;
        Assert.False(vm.IsBusy);
        Assert.Single(vm.Items);
    }

    [Fact]
    public async Task Filter_change_clears_immediately()
    {
        var core = new FakeCoreClient
        {
            MediaList = new PaginationDto<MediaCard>(
                [new MediaCard { Slug = "a", MediaType = "anime" }], 12, 60, 0, 1, true),
        };
        var vm = new CatalogViewModel(core);
        await vm.OpenAsync("anime", "Анімэ");
        Assert.Single(vm.Items);
        core.MediaListDelayMs = 250;
        vm.SelectedStatusIndex = 1;
        Assert.True(vm.IsBusy);
        Assert.Empty(vm.Items);
        await Task.Delay(400);
        Assert.False(vm.IsBusy);
        Assert.Single(vm.Items);
    }

    [Theory]
    [InlineData("manga")]
    [InlineData("games")]
    [InlineData("books")]
    public async Task OpenAsync_hides_sub_dub_for_non_video(string type)
    {
        var vm = new CatalogViewModel(new FakeCoreClient());
        await vm.OpenAsync(type, type);
        Assert.False(vm.ShowLanguageFilters);
        Assert.False(vm.SubSelected);
        Assert.False(vm.DubSelected);
    }

    [Fact]
    public async Task OpenAsync_cinema_keeps_sub_dub()
    {
        var vm = new CatalogViewModel(new FakeCoreClient());
        await vm.OpenAsync("cinema", "Кіно");
        Assert.True(vm.ShowLanguageFilters);
    }

    [Fact]
    public async Task LoadMoreAsync_requests_next_offset()
    {
        var core = new FakeCoreClient
        {
            MediaList = new PaginationDto<MediaCard>(
                [new MediaCard { Slug = "a", MediaType = "anime" }], 120, 60, 0, 1, true),
        };
        var vm = new CatalogViewModel(core);
        await vm.OpenAsync("anime", "Анімэ");
        Assert.Equal(0, core.LastMediaListOffset);
        await vm.LoadMoreAsync();
        Assert.Equal(1, core.LastMediaListOffset);
        Assert.Equal(2, vm.Items.Count);
        Assert.True(vm.HasMore);
    }

    [Fact]
    public async Task OpenAsync_surfaces_error_state_when_list_fails()
    {
        var core = new FakeCoreClient { MediaListError = new CoreException("http_error", "down") };
        var vm = new CatalogViewModel(core);
        await vm.OpenAsync("anime", "Анімэ");
        Assert.True(vm.ShowErrorState);
        Assert.False(vm.ShowEmptyState);
        Assert.True(vm.IsEmptyVisible);
        Assert.False(string.IsNullOrEmpty(vm.StatusMessage));
    }

    [Fact]
    public void Genre_and_country_use_belarusian_labels()
    {
        Assert.Equal("баявік", Ui.Genre("боевик"));
        Assert.Equal("звышнатуральнае", Ui.Genre("сверхъестественное"));
        Assert.Equal("Японія", Ui.Country("Япония"));
        Assert.Equal("Японія", Ui.Country("Japan"));
        Assert.Equal("Анімэ", Ui.MediaType("anime"));
        Assert.Equal("Субцітры", Ui.Language("sub"));
        Assert.Equal("Дубляж", Ui.Language("dub"));
        Assert.Equal("Тэлесерыял", Ui.ContentType("tv"));
    }
}

public class MediaDetailsViewModelTests
{
    [Fact]
    public async Task LoadAsync_missing_title_shows_page_error()
    {
        var vm = new MediaDetailsViewModel(new FakeCoreClient { Media = null }, new SessionService(new FakeCoreClient(), new MemoryCredentialStore()));
        await vm.LoadAsync("missing", "anime");
        Assert.True(vm.ShowPageError);
        Assert.Equal("Няма такога тайтла.", vm.StatusMessage);
        Assert.Null(vm.Media);
    }

    [Fact]
    public async Task LoadAsync_maps_media_and_episodes()
    {
        var core = new FakeCoreClient
        {
            Media = new MediaDetailDto
            {
                MediaId = "1",
                MediaType = "anime",
                Slug = "death-note",
                Title = new TitleDto { Be = "Сшытак смерці" },
                Year = 2006,
            },
            Episodes =
            [
                new EpisodeDto { Id = "e1", Episode = 1, Type = "sub", Resource = 2, Url = "https://video.anibel.net/x" },
            ],
        };
        var vm = new MediaDetailsViewModel(core, new SessionService(new FakeCoreClient(), new MemoryCredentialStore()));
        await vm.LoadAsync("death-note", "anime");
        Assert.Equal("Сшытак смерці", vm.DisplayTitle);
        Assert.True(vm.ShowEpisodes);
        Assert.False(vm.ShowChapters);
        Assert.False(vm.ShowGameInfo);
        Assert.False(vm.ShowBookInfo);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task LoadAsync_fills_franchise_and_recommendations()
    {
        var core = new FakeCoreClient
        {
            Media = new MediaDetailDto
            {
                MediaId = "1",
                MediaType = "anime",
                Slug = "death-note",
                Franchise = "Death Note",
                Relations =
                [
                    new MediaCard { Slug = "death-note", MediaType = "anime" },
                    new MediaCard { Slug = "death-note-rewrite", MediaType = "anime", Title = new TitleDto { Be = "Rewrite" } },
                ],
                Recommendations =
                [
                    new MediaCard { Slug = "monster", MediaType = "anime", Title = new TitleDto { Be = "Monster" } },
                ],
            },
        };
        var vm = new MediaDetailsViewModel(core, new SessionService(new FakeCoreClient(), new MemoryCredentialStore()));
        await vm.LoadAsync("death-note", "anime");
        Assert.True(vm.ShowFranchise);
        Assert.Equal("Death Note", vm.FranchiseName);
        Assert.Single(vm.Relations);
        Assert.Equal("death-note-rewrite", vm.Relations[0].Slug);
        Assert.True(vm.ShowRecommendations);
        Assert.Single(vm.Recommendations);
        Assert.True(vm.EpisodesEmpty);
        Assert.Equal("dub", vm.SelectedKind);
    }

    [Fact]
    public async Task LoadAsync_hides_franchise_and_recommendations_when_empty()
    {
        var core = new FakeCoreClient
        {
            Media = new MediaDetailDto
            {
                MediaId = "1",
                MediaType = "manga",
                Slug = "solo",
            },
        };
        var vm = new MediaDetailsViewModel(core, new SessionService(new FakeCoreClient(), new MemoryCredentialStore()));
        core.KindChoice = new("chapters", ["notselected", "reading", "read"]);
        await vm.LoadAsync("solo", "manga");
        Assert.False(vm.ShowFranchise);
        Assert.False(vm.ShowRecommendations);
        Assert.Empty(vm.Relations);
        Assert.Empty(vm.Recommendations);
    }

    [Fact]
    public async Task Empty_core_episode_choices_hide_kind_bar()
    {
        var vm = new MediaDetailsViewModel(new FakeCoreClient { Media = new MediaDetailDto { MediaId="1",MediaType="anime",Slug="empty" } }, new SessionService(new FakeCoreClient(), new MemoryCredentialStore()));
        await vm.LoadAsync("empty", "anime");
        Assert.True(vm.EpisodesEmpty);
        Assert.Equal("dub", vm.SelectedKind);
        Assert.False(vm.ShowKindBar);
    }

    [Fact]
    public async Task LoadAsync_manga_shows_chapters_not_episodes()
    {
        var core = new FakeCoreClient
        {
            Media = new MediaDetailDto { MediaId = "2", MediaType = "manga", Slug = "dallae" },
            Chapters = new PaginationDto<ChapterDto>([new ChapterDto { Id = "c1", Chapter = 1, Title = "Start" }], 1, 500, 0),
        };
        var vm = new MediaDetailsViewModel(core, new SessionService(new FakeCoreClient(), new MemoryCredentialStore()));
        core.KindChoice = new("chapters", ["notselected", "reading", "read"]);
        await vm.LoadAsync("dallae", "manga");
        Assert.True(vm.ShowChapters);
        Assert.False(vm.ShowEpisodes);
        Assert.False(vm.ShowBookInfo);
        Assert.False(vm.ShowGameInfo);
        Assert.Single(vm.Chapters);
        Assert.Equal("Start", ChapterDisplay.TitleLabel(vm.Chapters[0]));
    }

    [Fact]
    public async Task ChapterAsync_returns_page_images()
    {
        var core = new FakeCoreClient
        {
            Chapter = new ChapterDto
            {
                Id = "c1",
                Chapter = 1,
                Title = "Start",
                Images = [new ChapterImageDto { Large = "https://cdn.example/1.jpg" }],
            },
        };
        var loaded = await core.ChapterAsync("dallae", 1);
        Assert.NotNull(loaded);
        Assert.Single(loaded!.Images);
        Assert.Equal("https://cdn.example/1.jpg", loaded.Images[0].Large);
    }

    [Fact]
    public async Task LoadAsync_books_are_not_manga_chapters()
    {
        var core = new FakeCoreClient
        {
            Media = new MediaDetailDto
            {
                MediaId = "3",
                MediaType = "books",
                Slug = "novel",
                Download = "https://anibel.net/file.pdf",
            },
            Chapters = new PaginationDto<ChapterDto>([new ChapterDto { Id = "c1", Chapter = 1 }], 1, 500, 0),
        };
        var vm = new MediaDetailsViewModel(core, new SessionService(new FakeCoreClient(), new MemoryCredentialStore()));
        core.KindChoice = new("book", ["notselected", "reading", "read"]);
        await vm.LoadAsync("novel", "books");
        Assert.True(vm.ShowBookInfo);
        Assert.False(vm.ShowChapters);
        Assert.False(vm.ShowEpisodes);
        Assert.False(vm.ShowGameInfo);
        Assert.Empty(vm.Chapters);
        Assert.Equal("https://anibel.net/file.pdf", vm.DownloadUrl);
    }

    [Fact]
    public async Task LoadAsync_games_show_overview_not_episodes()
    {
        var core = new FakeCoreClient
        {
            Media = new MediaDetailDto
            {
                MediaId = "4",
                MediaType = "games",
                Slug = "game",
                Trailer = "https://youtu.be/x",
                Instructions = new DescriptionDto { Be = "Распакуйце архіў" },
            },
        };
        var vm = new MediaDetailsViewModel(core, new SessionService(new FakeCoreClient(), new MemoryCredentialStore()));
        core.KindChoice = new("game", ["notselected", "playing", "played"]);
        await vm.LoadAsync("game", "games");
        Assert.True(vm.ShowGameInfo);
        Assert.False(vm.ShowEpisodes);
        Assert.False(vm.ShowChapters);
        Assert.False(vm.ShowBookInfo);
        Assert.Equal("Распакуйце архіў", vm.Instructions);
        Assert.Equal("https://youtu.be/x", vm.TrailerUrl);
    }

    [Fact]
    public async Task Episode_tabs_show_core_choices_and_send_filter_intent()
    {
        var core = new FakeCoreClient { Media = new MediaDetailDto { MediaId = "1", MediaType = "anime", Slug = "x" },
            EpisodeChoices = new([new EpisodeDto { Id = "d1" }], ["dub", "sub"], "dub", [2]) };
        var vm = new MediaDetailsViewModel(core, new SessionService(core, new MemoryCredentialStore()));
        await vm.LoadAsync("x", "anime");
        Assert.True(vm.ShowKindBar); Assert.Equal("d1", Assert.Single(vm.Episodes).Id);
        core.EpisodeChoices = new([new EpisodeDto { Id = "s1" }], ["dub", "sub"], "sub", [2]);
        vm.SelectedKind = "sub";
        Assert.Equal("s1", Assert.Single(vm.Episodes).Id);
        Assert.Equal("sub", core.Calls.Last(c => c.Op == "episodeChoices").Args.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task LoadAsync_null_media_sets_status()
    {
        var vm = new MediaDetailsViewModel(new FakeCoreClient { Media = null }, new SessionService(new FakeCoreClient(), new MemoryCredentialStore()));
        await vm.LoadAsync("missing", "anime");
        Assert.NotNull(vm.StatusMessage);
    }

    [Fact]
    public async Task AddCommentAsync_rejects_empty()
    {
        var core = new FakeCoreClient
        {
            Media = new MediaDetailDto { MediaId = "1", MediaType = "anime", Slug = "x" },
        };
        var vm = new MediaDetailsViewModel(core, new SessionService(new FakeCoreClient(), new MemoryCredentialStore()));
        await vm.LoadAsync("x", "anime");
        Assert.False(await vm.AddCommentAsync("   "));
    }

    [Fact]
    public void BeginReply_sets_chip_and_cancel_clears()
    {
        var vm = new MediaDetailsViewModel(new FakeCoreClient(), new SessionService(new FakeCoreClient(), new MemoryCredentialStore()));
        vm.BeginReply(new CommentDto
        {
            Id = "c1",
            Content = "прывітанне",
            User = new CommentUserDto { Username = "alice" },
        });
        Assert.True(vm.HasReplyTarget);
        Assert.Equal("c1", vm.ReplyTarget!.Id);
        Assert.Equal("Адказ @alice", vm.ReplyLabel);
        vm.CancelReply();
        Assert.False(vm.HasReplyTarget);
        Assert.Equal("", vm.ReplyLabel);
    }

    [Fact]
    public void BeginLoad_clears_reply_target()
    {
        var vm = new MediaDetailsViewModel(new FakeCoreClient(), new SessionService(new FakeCoreClient(), new MemoryCredentialStore()));
        vm.BeginReply(new CommentDto { Id = "c1", User = new CommentUserDto { Username = "bob" } });
        vm.BeginLoad();
        Assert.False(vm.HasReplyTarget);
    }

    [Fact]
    public async Task SetFavoriteAsync_updates_state_only_after_success()
    {
        var core = new FakeCoreClient
        {
            Media = new MediaDetailDto { MediaId = "1", MediaType = "anime", Slug = "x", Favorite = false },
        };
        var vm = new MediaDetailsViewModel(core, new SessionService(new FakeCoreClient(), new MemoryCredentialStore()));
        await vm.LoadAsync("x", "anime");
        Assert.False(vm.IsFavorite);
        Assert.False(vm.Media!.Favorite);

        core.Handler = (op, args, ct) => Task.FromResult<object?>(new MediaDetailsViewModel.SelectionResult(true));
        await vm.SetFavoriteAsync(true);
        Assert.True(vm.IsFavorite);
        Assert.True(vm.Media!.Favorite);

        core.Handler = (op, args, ct) => Task.FromResult<object?>(new MediaDetailsViewModel.SelectionResult(false));
        await vm.SetFavoriteAsync(false);
        Assert.False(vm.IsFavorite);
        Assert.False(vm.Media!.Favorite);
    }
}

public class PosterGridLayoutTests
{
    [Theory]
    [InlineData(1000)]
    [InlineData(1280)]
    [InlineData(1680)]
    [InlineData(1920)]
    public void Cells_fill_the_viewport_without_a_leftover_column(double width)
    {
        var m = PosterGridLayout.ForViewport(width);
        Assert.True(m.Columns >= 1);
        var used = m.Columns * m.ItemWidth + (m.Columns - 1) * PosterGridLayout.ColumnGap;
        Assert.True(Math.Abs(width - used) < 0.5);
        Assert.Equal(m.ItemWidth, m.SlotWidth);
    }

    [Fact]
    public void Poster_box_is_two_by_three()
    {
        var m = PosterGridLayout.ForViewport(900);
        var posterHeight = m.ItemHeight - PosterGridLayout.TitleHeight;
        Assert.Equal(m.ItemWidth * PosterGridLayout.PosterAspect, posterHeight, 3);
    }

    [Fact]
    public void Two_min_width_slots_make_two_columns()
    {
        var m = PosterGridLayout.ForViewport(PosterGridLayout.MinItemWidth * 2);
        Assert.Equal(2, m.Columns);
        var used = m.Columns * m.ItemWidth + PosterGridLayout.ColumnGap;
        Assert.True(Math.Abs(PosterGridLayout.MinItemWidth * 2 - used) < 0.5);
    }

    [Fact]
    public void Narrow_viewport_stays_one_column()
    {
        var m = PosterGridLayout.ForViewport(120);
        Assert.Equal(1, m.Columns);
        Assert.Equal(120, m.SlotWidth);
    }

    [Fact]
    public void Leftover_width_grows_cells_instead_of_leaving_a_gap()
    {
        var tight = PosterGridLayout.ForViewport(168 * 5);
        var withGap = PosterGridLayout.ForViewport(168 * 5 + 100);
        Assert.Equal(tight.Columns, withGap.Columns);
        Assert.True(withGap.ItemWidth > tight.ItemWidth);
        Assert.True(withGap.ItemHeight > tight.ItemHeight);
        Assert.True(withGap.SlotWidth > tight.SlotWidth);
    }
}

public class MediaCardDisplayTests
{
    [Fact]
    public void Catalog_card_shows_both_languages_and_status()
    {
        var card = new MediaCard
        {
            MediaType = "anime",
            Status = "ongoing",
            Language = ["sub", "dub"],
        };
        Assert.Equal("У выхадзе", CardDisplay.StatusLabel(card));
        Assert.True(CardDisplay.ShowSub(card));
        Assert.True(CardDisplay.ShowDub(card));
        Assert.Equal("Субцітры · Дубляж", CardDisplay.LanguageLabel(card));
        Assert.Contains("У выхадзе", CardDisplay.InfoLine(card));
        Assert.Contains("Субцітры", CardDisplay.InfoLine(card));
        Assert.Contains("Дубляж", CardDisplay.InfoLine(card));
    }

    [Fact]
    public void Update_records_distinguish_sub_and_dub()
    {
        var sub = new MediaCard
        {
            Slug = "same",
            MediaType = "anime",
            Language = ["sub", "dub"],
            UpdateType = "SUB",
            Num = 12,
            Status = "ongoing",
        };
        var dub = new MediaCard
        {
            Slug = "same",
            MediaType = "anime",
            Language = ["sub", "dub"],
            UpdateType = "DUB",
            Num = 12,
            Status = "ongoing",
        };
        Assert.True(CardDisplay.ShowSub(sub));
        Assert.False(CardDisplay.ShowDub(sub));
        Assert.False(CardDisplay.ShowSub(dub));
        Assert.True(CardDisplay.ShowDub(dub));
        Assert.Equal("Субцітры", CardDisplay.LanguageLabel(sub));
        Assert.Equal("Дубляж", CardDisplay.LanguageLabel(dub));
        Assert.Equal("эп. 12", CardDisplay.NumLabel(sub));
        Assert.NotEqual(CardDisplay.InfoLine(sub), CardDisplay.InfoLine(dub));
    }

    [Fact]
    public void Finished_manga_uses_chapter_label()
    {
        var card = new MediaCard { MediaType = "manga", Num = 5, Status = "finished" };
        Assert.Equal("гл. 5", CardDisplay.NumLabel(card));
        Assert.Equal("Завершана", CardDisplay.StatusLabel(card));
        Assert.False(CardDisplay.ShowSub(card));
        Assert.False(CardDisplay.ShowDub(card));
    }
}

public class ErrorMappingTests
{
    [Fact]
    public void FriendlyError_maps_login_and_graphql()
    {
        Assert.Equal("Няправільны лагін або пароль.", Ui.FriendlyError("graphql_error", "Incorrect username or password"));
        Assert.Equal("Сеанс скончыўся. Увайдзіце зноў.", Ui.FriendlyError("auth_expired", "unauthorized"));
        Assert.Equal("Няма сувязі з серверам. Праверце інтэрнэт і паспрабуйце яшчэ раз.", Ui.FriendlyError("transport_error", "connect: failed"));
        Assert.Equal("Cannot query field x", Ui.FriendlyError("graphql_error", "Cannot query field x"));
    }

    [Fact]
    public void CoreException_user_message_strips_code_prefix()
    {
        var ex = new CoreException("graphql_error", "Invalid credentials");
        Assert.Equal("Invalid credentials", ex.Message);
        Assert.Equal("Няправільны лагін або пароль.", ex.UserMessage);
    }
}
