//! Playback intent & source resolution.
//!
//! An Anibel-player episode carries a page URL of the form:
//!
//! ```text
//! https://video.anibel.net/<videoId-uuid>?type=anime
//! ```
//!
//! The `<uuid>` is the **video service record id** — resolution sequence:
//!
//! 1. extract uuid from `episode.url`
//! 2. `POST api.anibel.stream/video` (form `videoId`, public) → `VideoInfo`
//!    (HLS master `host+hls`, DASH `host+stream`, `.ass` subtitle tracks
//!     + required font family names)
//! 3. `POST /fonts-by-names` (public) → direct `.ttf` URLs for libass
//! 4. assemble [`PlaybackIntent`] — native engines consume it.
//!
//! Google Drive urls (`resource: 1`) classify as `embed` (WebView2 pipeline).

use crate::video::VideoService;
use anibel_domain::models::Episode;
use regex::Regex;
use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::sync::LazyLock;

static VIDEO_ID_RE: LazyLock<Regex> = LazyLock::new(|| {
    Regex::new(
        r"(?i)video\.anibel\.net/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})",
    )
    .unwrap()
});

/// Direct uuid in a plain `videoId` argument.
static UUID_RE: LazyLock<Regex> = LazyLock::new(|| {
    Regex::new(r"(?i)[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}").unwrap()
});

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SubtitleTrack {
    /// Absolute `.ass` (or `.srt`) URL.
    pub url: String,
    /// Font family names referenced by the ASS styles.
    pub fonts: Vec<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub label: Option<String>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct FontAsset {
    pub family: String,
    pub url: String,
}

#[derive(Debug, Clone, Copy, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub enum PlaybackKind {
    Native,
    Embed,
    #[default]
    PendingNative,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct PlaybackIntent {
    /// `embed` — external iframe (Google Drive) — WebView2 pipeline
    /// `native` — Anibel-hosted HLS/DASH + ASS subtitles — native playback/libass pipeline
    pub kind: PlaybackKind,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub page_url: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub video_id: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub video_src: Option<String>,
    /// Separate audio track — reserved (dub comes as its own video record).
    #[serde(skip_serializing_if = "Option::is_none")]
    pub audio_src: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub sub_src: Option<String>,
    #[serde(default)]
    pub subtitles: Vec<SubtitleTrack>,
    /// Resolved font assets (family → .ttf URL) for libass.
    #[serde(default)]
    pub fonts: Vec<FontAsset>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub duration_secs: Option<f64>,
}

impl PlaybackIntent {
    pub fn embed(url: &str) -> Self {
        PlaybackIntent {
            kind: PlaybackKind::Embed,
            page_url: Some(url.to_string()),
            ..Default::default()
        }
    }

    pub fn is_native(&self) -> bool {
        self.kind == PlaybackKind::Native && self.video_src.is_some()
    }
}

pub fn extract_video_id(url: &str) -> Option<String> {
    VIDEO_ID_RE
        .captures(url)
        .and_then(|c| c.get(1))
        .map(|m| m.as_str().to_string())
        .or_else(|| UUID_RE.find(url).map(|m| m.as_str().to_string()))
}

/// Classify an episode by URL only (no network).
pub fn classify(ep: &Episode) -> PlaybackIntent {
    let url = ep.url.clone().unwrap_or_default();
    if url.contains("drive.google") {
        PlaybackIntent::embed(&url)
    } else {
        PlaybackIntent {
            kind: PlaybackKind::PendingNative,
            page_url: Some(url),
            ..Default::default()
        }
    }
}

/// Full resolution: video service → playback intent with stream + subs + fonts.
pub async fn resolve_intent(
    video_service: &VideoService,
    args: &Value,
) -> Result<PlaybackIntent, anibel_domain::error::AnibelError> {
    use anibel_domain::error::AnibelError;

    let page_url = args.get("url").and_then(Value::as_str).or_else(|| {
        args.get("episode")
            .and_then(|e| e.get("url"))
            .and_then(Value::as_str)
    });

    let mut video_id = args
        .get("videoId")
        .and_then(Value::as_str)
        .map(str::to_string);

    if video_id.is_none() {
        if let Some(url) = page_url.filter(|u| u.contains("drive.google")) {
            return Ok(PlaybackIntent::embed(url));
        }
        if let Some(url) = page_url {
            video_id = extract_video_id(url);
        }
    }

    let video_id = video_id
        .ok_or_else(|| AnibelError::BadArgs("resolveEpisode: no videoId / anibel url".into()))?;

    let info = video_service
        .get(&video_id)
        .await
        .map_err(|e| AnibelError::Transport(format!("video service: {e}")))?;

    let video_src = info
        .primary_stream(
            args.get("preferDash")
                .and_then(Value::as_bool)
                .unwrap_or(false),
        )
        .ok_or_else(|| AnibelError::Transport(format!("video {video_id}: no hls/stream")))?;

    let subtitles: Vec<SubtitleTrack> = info
        .subtitles
        .iter()
        .map(|s| SubtitleTrack {
            url: crate::video::join_host(info.host.as_deref(), &s.path),
            fonts: s.fonts.clone(),
            label: subtitle_label(&s.path),
        })
        .collect();

    // resolve ASS fonts once, from the union of required family names
    let mut families: Vec<String> = Vec::new();
    for sub in &info.subtitles {
        for f in &sub.fonts {
            if !families.contains(f) {
                families.push(f.clone());
            }
        }
    }

    let fonts = match video_service.fonts_by_names(&families).await {
        Ok(urls) => families
            .iter()
            .cloned()
            .zip(urls.into_iter().chain(std::iter::repeat_with(String::new)))
            .filter(|(_, u)| !u.is_empty())
            .map(|(family, url)| FontAsset { family, url })
            .collect(),
        Err(_) => Vec::new(), // fonts optional — libass falls back to system fonts
    };

    let sub_src = subtitles.first().map(|t| t.url.clone());

    Ok(PlaybackIntent {
        kind: PlaybackKind::Native,
        page_url: page_url.map(str::to_string),
        video_id: Some(video_id),
        video_src: Some(video_src),
        audio_src: None,
        sub_src,
        subtitles,
        fonts,
        duration_secs: info.duration_secs(),
    })
}

fn subtitle_label(path: &str) -> Option<String> {
    let name = path.rsplit(['/', '\\']).next().unwrap_or(path);
    let stem = name.rsplit_once('.').map(|(s, _)| s).unwrap_or(name);
    let stem = stem.trim();
    if stem.is_empty() {
        None
    } else {
        Some(stem.to_string())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn extracts_uuid_from_player_url() {
        let url = "https://video.anibel.net/8c52d132-955a-445c-8fa7-2f5739e141d8?type=anime";
        assert_eq!(
            extract_video_id(url).as_deref(),
            Some("8c52d132-955a-445c-8fa7-2f5739e141d8")
        );
    }

    #[test]
    fn extracts_uuid_from_plain_argument() {
        assert_eq!(
            extract_video_id("8c52d132-955a-445c-8fa7-2f5739e141d8").as_deref(),
            Some("8c52d132-955a-445c-8fa7-2f5739e141d8")
        );
    }

    #[test]
    fn classifies_drive_as_embed() {
        let ep = Episode {
            url: Some("https://drive.google.com/file/d/abc/preview".into()),
            ..Default::default()
        };
        assert_eq!(classify(&ep).kind, PlaybackKind::Embed);
    }

    #[test]
    fn classifies_anibel_as_pending_native() {
        let ep = Episode {
            url: Some(
                "https://video.anibel.net/8c52d132-955a-445c-8fa7-2f5739e141d8?type=anime".into(),
            ),
            ..Default::default()
        };
        assert_eq!(classify(&ep).kind, PlaybackKind::PendingNative);
        assert!(extract_video_id(ep.url.as_deref().unwrap()).is_some());
    }

    #[tokio::test]
    async fn resolve_with_mock_service() {
        use httpmock::prelude::*;
        let server = MockServer::start_async().await;

        let m1 = server
            .mock_async(|when, then| {
                when.method(POST).path("/video");
                then.status(200).json_body(serde_json::json!({
                    "videoId": "8c52d132-955a-445c-8fa7-2f5739e141d8",
                    "title": "ep13",
                    "processing": false,
                    "meta": { "width": 1920, "height": 1080, "duraction": 600.0 },
                    "subtitles": [
                        { "path": "https://subtitles.anibel.net/8c52d132/ep13.ass", "fonts": ["Montserrat"] },
                        { "path": "/subtitles/8c52d132/субцітры.ass", "fonts": [] }
                    ],
                    "hls": "/dash/8c52d132/manifest.m3u8",
                    "stream": "/dash/8c52d132/manifest.mpd",
                    "host": "https://n3.anibel.stream"
                }));
            })
            .await;

        let m2 = server
            .mock_async(|when, then| {
                when.method(POST).path("/fonts-by-names");
                then.status(200).json_body(serde_json::json!([
                    "https://fonts.anibel.net/montserrat/aaa.ttf"
                ]));
            })
            .await;

        let svc = VideoService::with_base(server.base_url());
        let intent = resolve_intent(
            &svc,
            &serde_json::json!({ "videoId": "8c52d132-955a-445c-8fa7-2f5739e141d8" }),
        )
        .await
        .unwrap();

        assert_eq!(intent.kind, PlaybackKind::Native);
        assert_eq!(
            intent.video_src.as_deref(),
            Some("https://n3.anibel.stream/dash/8c52d132/manifest.m3u8")
        );
        assert_eq!(intent.subtitles.len(), 2);
        assert_eq!(
            intent.subtitles[0].url,
            "https://subtitles.anibel.net/8c52d132/ep13.ass"
        );
        assert_eq!(
            intent.subtitles[1].url,
            "https://n3.anibel.stream/subtitles/8c52d132/субцітры.ass"
        );
        assert_eq!(intent.subtitles[0].label.as_deref(), Some("ep13"));
        assert_eq!(intent.fonts[0].family, "Montserrat");
        assert_eq!(intent.duration_secs, Some(600.0));

        let dash = resolve_intent(
            &svc,
            &serde_json::json!({
                "videoId":"8c52d132-955a-445c-8fa7-2f5739e141d8", "preferDash":true
            }),
        )
        .await
        .unwrap();
        assert_eq!(
            dash.video_src.as_deref(),
            Some("https://n3.anibel.stream/dash/8c52d132/manifest.mpd")
        );
        m1.assert_hits_async(2).await;
        m2.assert_hits_async(2).await;
    }
}
