using Anibel.App.Core;
using Anibel.App.Services;
using Anibel.App.ViewModels;
using Xunit;

namespace Anibel.App.Tests;

public class ProfileCommentTests
{
    [Fact]
    public async Task Older_profile_response_cannot_replace_new_profile()
    {
        var pending = new TaskCompletionSource<object?>();
        var bob = new ProfileDto("bob", "bob", null, null, null, null);
        var alice = new ProfileDto("alice", "alice", null, null, null, null);
        var core = new FakeCoreClient { Handler = (_, _, _) => pending.Task };
        var vm = new ProfileViewModel(core, new SessionService(core, new MemoryCredentialStore()));
        var old = vm.LoadProfileAsync("alice");
        core.Handler = (_, _, _) => Task.FromResult<object?>(new ProfileViewDto(bob, false));
        await vm.LoadProfileAsync("bob");
        pending.SetResult(new ProfileViewDto(alice, false));
        await old;
        Assert.Same(bob, vm.Profile);
        Assert.False(vm.IsBusy);
    }

    private static async Task<MediaDetailsViewModel> CommentModel(FakeCoreClient core)
    {
        core.Session = new(1, true, "alice", "owner", null);
        core.Media = new MediaDetailDto { MediaId = "m", MediaType = "anime", Slug = "title" };
        var session = new SessionService(core, new MemoryCredentialStore());
        await session.RefreshAsync();
        var vm = new MediaDetailsViewModel(core, session);
        await vm.LoadAsync("title", "anime");
        return vm;
    }

    [Fact]
    public async Task Reply_uses_selected_parent_and_reloads_the_server_tree()
    {
        var parent = new CommentDto { Id = "parent", User = new() { Username = "bob" } };
        var reply = new CommentDto { Id = "new", Content = "reply" };
        var core = new FakeCoreClient { Comments = new([parent], 1, 30, 0) };
        var vm = await CommentModel(core);
        vm.BeginReply(parent);
        core.CommentHandler = () =>
        {
            core.Comments = new([new CommentDto { Id = "parent", Replies = [reply] }], 1, 30, 0);
            return Task.FromResult(reply);
        };
        Assert.True(await vm.AddCommentAsync(" reply "));
        Assert.Equal("parent", core.LastAddedCommentReplyTo);
        Assert.Equal("reply", core.LastAddedCommentContent);
        Assert.Equal("parent", Assert.Single(vm.Comments).Id);
        Assert.Equal("new", Assert.Single(vm.Comments[0].Replies).Id);
        Assert.False(vm.HasReplyTarget);
    }

    [Fact]
    public async Task Pending_reply_blocks_duplicate_send_and_target_changes()
    {
        var pending = new TaskCompletionSource<CommentDto>();
        var core = new FakeCoreClient { CommentHandler = () => pending.Task };
        var vm = await CommentModel(core);
        var parent = new CommentDto { Id = "parent" };
        vm.BeginReply(parent);
        var send = vm.AddCommentAsync("reply");
        Assert.True(vm.IsPosting);
        vm.BeginReply(new CommentDto { Id = "other" });
        Assert.Same(parent, vm.ReplyTarget);
        Assert.False(await vm.AddCommentAsync("reply"));
        Assert.Equal(1, core.AddCommentCalls);
        pending.SetResult(new() { Id = "new" });
        await send;
        Assert.False(vm.IsPosting);
    }

    [Fact]
    public async Task Failed_reply_keeps_target_and_does_not_change_comments()
    {
        var parent = new CommentDto { Id = "parent" };
        var core = new FakeCoreClient
        {
            Comments = new([parent], 1, 30, 0),
            CommentHandler = () => Task.FromException<CommentDto>(new InvalidOperationException("offline")),
        };
        var vm = await CommentModel(core);
        vm.BeginReply(parent);
        Assert.False(await vm.AddCommentAsync("draft"));
        Assert.Same(parent, vm.ReplyTarget);
        Assert.Same(parent, Assert.Single(vm.Comments));
        Assert.False(vm.IsPosting);
    }

    [Fact]
    public async Task Public_profile_uses_core_ownership_and_profile_fields()
    {
        var profile = new ProfileDto("other", "bob", "https://img/avatar", "Bob", "Bio", "https://img/banner");
        var core = new FakeCoreClient
        {
            Handler = (op, args, ct) => Task.FromResult<object?>(new ProfileViewDto(profile, false)),
        };
        var vm = new ProfileViewModel(core, new SessionService(new FakeCoreClient(), new MemoryCredentialStore()));
        await vm.LoadProfileAsync("bob");
        Assert.Same(profile, vm.Profile);
        Assert.False(vm.IsOwn);
        Assert.Equal("bob", Assert.Single(core.Calls).Args.GetProperty("username").GetString());
    }
}
