using System.Collections.ObjectModel;
using Anibel.App.Core;
using Anibel.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Anibel.App.ViewModels;

/// <summary>Owns comment loading, submission and reply selection for a title.</summary>
public partial class MediaCommentsViewModel(ICoreClient _core, SessionService _session,
    Func<MediaDetailDto?> currentMedia, Action<string?> status) : ObservableObject
{
    private MediaDetailDto? Media => currentMedia();
    private string? StatusMessage { set => status(value); }
    public ObservableCollection<CommentDto> Comments { get; } = [];
    public bool CommentsEmpty => Comments.Count == 0;
    [ObservableProperty] private bool commentsLoading;
    [ObservableProperty] private bool isPosting;
    [ObservableProperty] private CommentDto? replyTarget;
    public async Task<bool> AddCommentAsync(string content)
    {
        if (IsPosting) return false;
        if (Media is null || !_session.HasSession)
        {
            StatusMessage = Strings.LoginToComment;
            return false;
        }
        var text = content.Trim();
        if (text.Length == 0)
        {
            return false;
        }
        var media = Media;
        var target = ReplyTarget;
        IsPosting = true;
        try
        {
            await _core.AddCommentAsync(media.MediaId, media.MediaType, text, target?.Id);
            if (!ReferenceEquals(Media, media)) return true;
            if (ReferenceEquals(ReplyTarget, target)) CancelReply();
            // The server owns thread placement. Never insert a reply as a new root.
            await LoadCommentsAsync(media.MediaId, media.MediaType);
            return true;
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(Media, media)) StatusMessage = Ui.DisplayMessage(ex);
            return false;
        }
        finally { IsPosting = false; }
    }

    public void BeginReply(CommentDto comment)
    {
        if (IsPosting || string.IsNullOrWhiteSpace(comment.Id))
        {
            return;
        }
        ReplyTarget = comment;
    }

    public void CancelReply() => ReplyTarget = null;

    public async Task LoadCommentsAsync(string mediaId, string mediaType)
    {
        CommentsLoading = true;
        try
        {
            var comments = await _core.CommentsAsync(mediaId, mediaType, 0, 30);
            if (Media?.MediaId != mediaId || Media.MediaType != mediaType) return;
            Comments.Clear();
            foreach (var c in comments.Docs)
            {
                Comments.Add(c);
            }
            OnPropertyChanged(nameof(CommentsEmpty));
        }
        catch (Exception ex)
        {
            Diag.Log($"MediaDetailsViewModel: comments FAILED {ex}");
            if (Media?.MediaId == mediaId && Media.MediaType == mediaType)
            {
                OnPropertyChanged(nameof(CommentsEmpty));
                StatusMessage = Ui.DisplayMessage(ex);
            }
        }
        finally
        {
            CommentsLoading = false;
        }
    }

}
