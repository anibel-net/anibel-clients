use serde::{Deserialize, Serialize};
use std::time::Instant;
#[derive(Clone, Copy, Debug, Deserialize, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub(crate) enum Kind {
    Video,
    Audio,
    Manga,
    File,
}
#[derive(Clone, Copy, Debug, Deserialize, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub(crate) enum Status {
    Queued,
    Downloading,
    Completed,
    Failed,
    Cancelled,
}
impl Status {
    pub(super) fn active(self) -> bool {
        matches!(self, Self::Queued | Self::Downloading)
    }
}

#[derive(Clone, Default, Deserialize, Serialize)]
#[serde(default, rename_all = "camelCase")]
pub(crate) struct Request {
    pub video_format: VideoFormat,
    pub media_id: String,
    pub media_type: String,
    pub slug: String,
    pub title: String,
    pub subtitle: Option<String>,
    pub poster_url: Option<String>,
    pub episode_url: Option<String>,
    pub episode_id: Option<String>,
    pub episode_type: Option<String>,
    pub episode_label: String,
    pub chapter: Option<f64>,
    pub chapter_id: Option<String>,
    pub chapter_title: Option<String>,
    pub chapter_list: Vec<f64>,
    pub file_url: Option<String>,
}
#[derive(Clone, Copy, Default, Deserialize, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub(crate) enum VideoFormat {
    #[default]
    Source,
    Mkv,
}
#[derive(Clone, Default, Deserialize, Serialize)]
#[serde(default, rename_all = "camelCase")]
pub(crate) struct Assets {
    pub video_path: Option<String>,
    pub audio_path: Option<String>,
    pub file_path: Option<String>,
    pub poster_path: Option<String>,
    pub subtitle_paths: Vec<String>,
    pub font_paths: Vec<String>,
    pub image_paths: Vec<String>,
    pub required_paths: Vec<String>,
}
impl Assets {
    pub(super) fn all(&self) -> Vec<&str> {
        self.video_path
            .iter()
            .chain(self.audio_path.iter())
            .chain(self.file_path.iter())
            .chain(self.poster_path.iter())
            .chain(self.subtitle_paths.iter())
            .chain(self.font_paths.iter())
            .chain(self.image_paths.iter())
            .chain(self.required_paths.iter())
            .map(String::as_str)
            .collect()
    }
}
#[derive(Clone, Deserialize, Serialize)]
pub(crate) struct Record {
    #[serde(skip)]
    pub(super) live: LiveProgress,
    pub id: String,
    pub kind: Kind,
    pub request: Request,
    pub status: Status,
    pub assets: Assets,
    pub error: Option<String>,
    pub bytes: u64,
    pub parts_done: u64,
    pub parts_total: u64,
    pub created: u64,
}
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub(super) enum Phase {
    Queued,
    Preparing,
    Downloading,
    Finalizing,
}

#[derive(Clone, Default)]
pub(super) struct LiveProgress {
    pub(super) started: Option<Instant>,
    pub(super) total_bytes: Option<u64>,
    pub(super) media: crate::application::mkv::Progress,
}

#[derive(Clone, Copy, Deserialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub(super) enum Change {
    Retry,
    Cancel,
    Delete,
}
