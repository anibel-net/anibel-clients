//! Anibel Video Service client (playback sources).
//!
//! Endpoint: `POST https://api.anibel.stream/video` — multipart `videoId`,
//! **public** (no apiKey — verified against production):
//!
//! ```json
//! {
//!   "videoId": "…", "title": "…", "processing": false,
//!   "meta": { "width":1920, "height":1080, "codec":"h264", "aspectRatio":"16:9",
//!             "duraction":1415.047, "size":"1.43 GB", "bitRate":"8.1 MB",
//!             "format":"matroska,webm", "fps":24 },
//!   "subtitles": [ { "path":"https://subtitles.anibel.net/<id>/<name>.ass",
//!                    "fonts":["Montserrat","Trebuchet MS"] } ],
//!   "hls":    "/dash/<id>/manifest.m3u8",
//!   "stream": "/dash/<id>/manifest.mpd",
//!   "host":   "https://n3.anibel.stream",     // CDN node owning hls/stream
//!   "support": { "dub":false, "sub":false },
//!   "screenshots": ["https://…/screenshots/<id>/1.png"], …
//! }
//! ```
//!
//! Fonts: `POST /fonts-by-names` `{ "fontNames": ["Montserrat"] }` is also
//! public and returns direct `.ttf` URLs (https://fonts.anibel.net/…) used to
//! satisfy ASS `font: …` attributes via libass.

use serde::de::DeserializeOwned;
use serde::{Deserialize, Serialize};
use std::time::Duration;

pub const DEFAULT_VIDEO_API: &str = "https://api.anibel.stream";

#[derive(Debug, Clone)]
pub struct VideoService {
    http: reqwest::Client,
    base_url: String,
}

impl Default for VideoService {
    fn default() -> Self {
        Self::new()
    }
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct VideoMeta {
    #[serde(default)]
    pub width: Option<i64>,
    #[serde(default)]
    pub height: Option<i64>,
    #[serde(default)]
    pub codec: Option<String>,
    #[serde(default)]
    pub aspect_ratio: Option<String>,
    /// Duration in seconds.
    #[serde(default)]
    pub duraction: Option<f64>,
    #[serde(default)]
    pub size: Option<String>,
    #[serde(default)]
    pub bit_rate: Option<String>,
    #[serde(default)]
    pub format: Option<String>,
    #[serde(default)]
    pub fps: Option<f64>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct VideoSubtitle {
    pub path: String,
    #[serde(default)]
    pub fonts: Vec<String>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct VideoSupport {
    #[serde(default)]
    pub dub: bool,
    #[serde(default)]
    pub sub: bool,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct VideoInfo {
    pub video_id: String,
    #[serde(default)]
    pub title: Option<String>,
    #[serde(default)]
    pub processing: bool,
    #[serde(default)]
    pub meta: Option<VideoMeta>,
    #[serde(default)]
    pub subtitles: Vec<VideoSubtitle>,
    #[serde(default)]
    pub screenshots: Vec<String>,
    #[serde(default)]
    pub hls: Option<String>,
    #[serde(default)]
    pub stream: Option<String>,
    #[serde(default)]
    pub support: Option<VideoSupport>,
    #[serde(default)]
    pub host: Option<String>,
    #[serde(default)]
    pub episode: Option<i64>,
    #[serde(default)]
    pub season: Option<i64>,
    #[serde(default)]
    pub group_by: Option<String>,
    #[serde(default)]
    pub media_type: Option<String>,
    #[serde(default)]
    pub views: Option<i64>,
    #[serde(default)]
    pub created_at: Option<i64>,
    #[serde(default)]
    pub processed_at: Option<i64>,
}

impl VideoInfo {
    /// Use the host's preferred format, with the other format as fallback.
    pub fn primary_stream(&self, prefer_dash: bool) -> Option<String> {
        let (preferred, fallback) = if prefer_dash {
            (&self.stream, &self.hls)
        } else {
            (&self.hls, &self.stream)
        };
        let path = preferred
            .as_deref()
            .filter(|s| !s.is_empty())
            .or_else(|| fallback.as_deref().filter(|s| !s.is_empty()))?;
        Some(join_host(self.host.as_deref(), path))
    }

    pub fn duration_secs(&self) -> Option<f64> {
        self.meta.as_ref().and_then(|m| m.duraction)
    }
}

impl VideoService {
    pub fn new() -> Self {
        Self::with_base(DEFAULT_VIDEO_API.to_string())
    }
    pub fn with_base(base_url: String) -> Self {
        let http = reqwest::Client::builder()
            .gzip(true)
            .user_agent(concat!("anibel-core/", env!("CARGO_PKG_VERSION")))
            .timeout(Duration::from_secs(30))
            .build()
            .expect("reqwest client");
        VideoService { http, base_url }
    }

    async fn json<T: DeserializeOwned>(
        &self,
        request: reqwest::RequestBuilder,
    ) -> Result<T, String> {
        let response = request
            .send()
            .await
            .map_err(|e| format!("transport: {e}"))?;
        if !response.status().is_success() {
            return Err(format!("http {}", response.status()));
        }
        response
            .json::<T>()
            .await
            .map_err(|e| format!("decode: {e}"))
    }

    /// Public playback data for a video record.
    pub async fn get(&self, video_id: &str) -> Result<VideoInfo, String> {
        let form = reqwest::multipart::Form::new().text("videoId", video_id.to_string());
        self.json::<VideoInfo>(
            self.http
                .post(format!("{}/video", self.base_url))
                .multipart(form),
        )
        .await
    }

    /// Public font asset resolution — family names → direct `.ttf` URLs.
    pub async fn fonts_by_names(&self, names: &[String]) -> Result<Vec<String>, String> {
        if names.is_empty() {
            return Ok(Vec::new());
        }
        self.json::<Vec<String>>(
            self.http
                .post(format!("{}/fonts-by-names", self.base_url))
                .json(&serde_json::json!({ "fontNames": names })),
        )
        .await
    }
}

pub fn join_host(host: Option<&str>, path: &str) -> String {
    if path.starts_with("http") {
        return path.to_string();
    }
    let base = host
        .map(str::to_owned)
        .unwrap_or_else(|| DEFAULT_VIDEO_API.to_string());
    format!("{}{}", base.trim_end_matches('/'), path)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn stream_preference_preserves_download_default_and_falls_back() {
        let mut info = VideoInfo {
            host: Some("https://example.test".into()),
            hls: Some("/master.m3u8".into()),
            stream: Some("/manifest.mpd".into()),
            ..Default::default()
        };
        assert_eq!(
            info.primary_stream(false).as_deref(),
            Some("https://example.test/master.m3u8")
        );
        assert_eq!(
            info.primary_stream(true).as_deref(),
            Some("https://example.test/manifest.mpd")
        );
        info.stream = Some(String::new());
        assert_eq!(info.primary_stream(true), info.primary_stream(false));
        info.hls = None;
        assert_eq!(info.primary_stream(true), None);
    }

    #[test]
    fn join_host_absolute_url_passthrough() {
        assert_eq!(
            join_host(Some("https://n3.anibel.stream"), "https://x/y"),
            "https://x/y"
        );
    }

    #[test]
    fn join_host_relative() {
        assert_eq!(
            join_host(Some("https://n3.anibel.stream"), "/dash/abc/manifest.m3u8"),
            "https://n3.anibel.stream/dash/abc/manifest.m3u8"
        );
    }
}
