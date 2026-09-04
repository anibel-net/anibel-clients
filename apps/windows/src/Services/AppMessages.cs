namespace Anibel.App.Services;

/// <summary>Cross-page app messages (title-bar global search, account open,
/// session expiry). Delivered via CommunityToolkit.Mvvm.Messaging.</summary>

/// <summary>Raised after the session token/profile was saved or cleared.</summary>
public sealed record SessionChangedMessage { }

/// <summary>Raised after the core reports auth expiry and the session was cleared.</summary>
public sealed record SessionExpiredMessage { }

/// <summary>Request to open the full search page with the given query.</summary>
public sealed record GlobalSearchMessage(string Query);

/// <summary>Request to open the media details page for a slug.</summary>
public sealed record OpenMediaMessage(string Slug, string MediaType);

/// <summary>Request to navigate the main shell to a nav tag (home, profile, downloads, settings, catalog type).</summary>
public sealed record NavigateMessage(string Tag);

/// <summary>Request to start playing an episode in the shell player.</summary>
public sealed record PlayEpisodeMessage(Views.PlayerArgs Args);

/// <summary>Request to open a manga chapter in the shell reader.</summary>
public sealed record ReadChapterMessage(Views.ReaderArgs Args);

/// <summary>Request to go back (shell back button behavior).</summary>
public sealed record GoBackMessage { }
