//! Protocol 2 command identities and execution rules. Wire names remain stable.
use serde::{Deserialize, Serialize};
use std::time::Duration;

#[derive(Clone, Copy, Debug, Deserialize, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub enum Command {
    ContinueEpisode,
    ContinueChapter,
    RememberEpisode,
    AddComment,
    AddFavorite,
    AddHistoryRecord,
    Capabilities,
    Chapter,
    Chapters,
    ClearCache,
    ClearPlaybackAssets,
    Comments,
    DownloadChange,
    DownloadEnqueue,
    DownloadExport,
    Downloads,
    DownloadsResume,
    DownloadsSuspend,
    EpisodeChoices,
    Episodes,
    EpisodesMatrix,
    Favorites,
    Filters,
    FontAssets,
    Health,
    Login,
    Logout,
    MarkAs,
    Marks,
    Me,
    Media,
    MediaKind,
    MediaList,
    PersonalList,
    PlaybackOpen,
    PlaybackReport,
    Profile,
    ProfileHub,
    Random,
    ReaderOpen,
    Recommendations,
    RemoveFavorite,
    RemoveHistoryRecord,
    RemoveMark,
    ResolveEpisode,
    Schedule,
    Search,
    SearchHistory,
    SelectTracks,
    Session,
    SetFavorite,
    SetMark,
    SetRating,
    SetToken,
    SetWatched,
    Slider,
    Statistics,
    Status,
    Trends,
    UpdateProfile,
    Updates,
    UpdatesPage,
    User,
    Version,
    VideoInfo,
    VideoQualities,
}

impl Command {
    pub fn parse(value: &str) -> Result<Self, anibel_domain::error::AnibelError> {
        serde_json::from_value(serde_json::Value::String(value.into())).map_err(|_| {
            anibel_domain::error::AnibelError::BadArgs(format!("unknown op `{value}`"))
        })
    }
    pub fn must_complete(self) -> bool {
        matches!(
            self,
            Self::RememberEpisode
                | Self::DownloadsSuspend
                | Self::DownloadsResume
                | Self::Login
                | Self::SetToken
                | Self::Logout
                | Self::MarkAs
                | Self::RemoveMark
                | Self::AddFavorite
                | Self::RemoveFavorite
                | Self::AddHistoryRecord
                | Self::RemoveHistoryRecord
                | Self::AddComment
                | Self::UpdateProfile
                | Self::SetRating
                | Self::SetFavorite
                | Self::SetMark
                | Self::SetWatched
        )
    }
    pub fn is_write(self) -> bool {
        self.must_complete() || self == Self::ClearCache
    }
    pub fn ttl(self) -> Option<(Duration, Duration)> {
        let (fresh, keep) = match self {
            Self::Comments => (15 * 60, 2 * 86400),
            Self::Profile
            | Self::User
            | Self::Me
            | Self::Favorites
            | Self::Marks
            | Self::Status
            | Self::ProfileHub
            | Self::PersonalList => (10 * 60, 2 * 86400),
            Self::Updates | Self::UpdatesPage | Self::Slider | Self::Trends => {
                (6 * 3600, 7 * 86400)
            }
            Self::EpisodeChoices
            | Self::Search
            | Self::Media
            | Self::MediaList
            | Self::Episodes
            | Self::EpisodesMatrix
            | Self::Chapters
            | Self::Chapter
            | Self::Recommendations
            | Self::Schedule
            | Self::Filters
            | Self::Statistics => (12 * 3600, 14 * 86400),
            _ => return None,
        };
        Some((Duration::from_secs(fresh), Duration::from_secs(keep)))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn shared_wire_names_round_trip() {
        let names: Vec<String> =
            serde_json::from_str(include_str!("../tests/fixtures/commands.json")).unwrap();
        let mut unique = std::collections::HashSet::new();
        for name in names {
            assert!(unique.insert(name.clone()));
            let command = Command::parse(&name).unwrap();
            assert_eq!(serde_json::to_value(command).unwrap(), name);
        }
    }
    #[test]
    fn execution_rules_are_explicit() {
        assert!(Command::SetRating.must_complete());
        assert!(Command::SetRating.is_write());
        assert!(Command::SetRating.ttl().is_none());
        assert!(!Command::VideoInfo.is_write());
        assert!(Command::VideoInfo.ttl().is_none());
        assert!(!Command::PlaybackReport.must_complete());
        assert!(Command::Media.ttl().is_some());
        assert!(Command::parse("notAnOperation").is_err());
        assert_eq!(
            serde_json::to_string(&Command::VideoQualities).unwrap(),
            "\"videoQualities\""
        );
    }
}
