using Anibel.App.Core;
using Anibel.App.Services;
using Anibel.App.ViewModels;
using System.Text.Json;
using Xunit;

namespace Anibel.App.Tests;

public class RatingTests
{
    [Fact]
    public async Task Account_refresh_replaces_all_personal_fields_and_logout_clears_them()
    {
        var core = new FakeCoreClient { Session = new(1, true, "alice", "owner", null) };
        var session = new SessionService(core, new MemoryCredentialStore());
        await session.RefreshAsync();
        var media = new MediaDetailDto { MediaId = "m", MediaType = "anime", IRated = 4, Favorite = true, Mark = new() { Status = "watched" } };
        var vm = new MediaDetailsViewModel(core, session) { Media = media };
        core.Media = new() { IRated = 8, Favorite = false, Mark = new() { Status = "planned" } };
        await vm.RefreshPersonalAsync();
        Assert.Equal(8, media.IRated);
        Assert.False(vm.IsFavorite);
        Assert.Equal("planned", media.Mark?.Status);
        core.Session = new(2, false, null, null, null);
        await session.RefreshAsync();
        await vm.RefreshPersonalAsync();
        Assert.Null(media.IRated);
        Assert.Null(media.Favorite);
        Assert.Null(media.Mark);
        Assert.False(vm.ShowPersonal);
    }

    [Fact]
    public async Task Pending_favorite_cannot_change_another_title()
    {
        var core = new FakeCoreClient { Session = new(1, true, "alice", "owner", null) };
        var session = new SessionService(core, new MemoryCredentialStore());
        await session.RefreshAsync();
        var vm = new MediaDetailsViewModel(core, session) { Media = new() { MediaId = "first", MediaType = "anime" } };
        var pending = new TaskCompletionSource<object?>();
        core.Handler = (_, _, _) => pending.Task;
        var save = vm.SetFavoriteAsync(true);
        var second = new MediaDetailDto { MediaId = "second", Favorite = false };
        vm.Media = second;
        pending.SetResult(new MediaDetailsViewModel.SelectionResult(true));
        await save;
        Assert.False(second.Favorite);
        Assert.False(vm.IsFavorite);
    }

    [Theory]
    [InlineData(1, 0.5)]
    [InlineData(7, 3.5)]
    [InlineData(8.6, 4.5)]
    [InlineData(10, 5)]
    [InlineData(100, 5)]
    [InlineData(-1, 0)]
    [InlineData(double.NaN, 0)]
    public void Display_uses_five_stars_with_half_steps(double score, double stars) =>
        Assert.Equal(stars, RatingDisplay.Stars(score));

    [Fact]
    public async Task Rating_sends_original_scale_and_keeps_previous_value_on_failure()
    {
        var core = new FakeCoreClient { Session = new(1, true, "alice", "owner", null) };
        var session = new SessionService(core, new MemoryCredentialStore());
        await session.RefreshAsync();
        var media = new MediaDetailDto { MediaId = "m", MediaType = "anime", IRated = 4 };
        var vm = new MediaDetailsViewModel(core, session) { Media = media };
        core.Handler = (_, _, _) => Task.FromResult<object?>(JsonSerializer.SerializeToElement(new { rating = 7 }));
        await vm.SetRatingAsync(7);
        Assert.Equal(7, media.IRated);
        Assert.Equal(7, core.Calls.Last().Args.GetProperty("rating").GetInt32());
        core.Handler = (_, _, _) => throw new InvalidOperationException("Save failed");
        await vm.SetRatingAsync(9);
        Assert.Equal(7, media.IRated);
        Assert.True(vm.HasStatus);
        Assert.False(vm.RatingSaving);
    }

    [Fact]
    public async Task Pending_rating_prevents_duplicate_send()
    {
        var core = new FakeCoreClient { Session = new(1, true, "alice", "owner", null) };
        var session = new SessionService(core, new MemoryCredentialStore());
        await session.RefreshAsync();
        var vm = new MediaDetailsViewModel(core, session) { Media = new() { MediaId = "m", MediaType = "anime" } };
        var pending = new TaskCompletionSource<object?>();
        core.Handler = (_, _, _) => pending.Task;
        var save = vm.SetRatingAsync(7);
        await vm.SetRatingAsync(8);
        Assert.Single(core.Calls, c => c.Op == "setRating");
        pending.SetResult(JsonSerializer.SerializeToElement(new { rating = 7 }));
        await save;
        Assert.Equal(7, vm.Media.IRated);
    }
}
