//! Golden contract test — anchors the C# DTO mirror.
//!
//! Every DTO that crosses the FFI boundary is serialized with
//! `serde_json::to_value` and must produce the EXACT camelCase JSON key set
//! (values included) declared in the inline `json!` literal below. If a
//! field is added/removed/renamed in a domain struct, this test pins the
//! wire shape the host must mirror.
//!
//! Uses only `anibel_core` re-exports.

use anibel_core::error::ErrorDto;
use anibel_core::models::{
    Chapter, Comment, CommentUser, Description, Episode, LoginUser, Mark, MediaCard, MediaDetail,
    Page, Title,
};
use anibel_core::player::PlaybackIntent;
use serde_json::{Value, json};

fn wire<T: serde::Serialize>(v: &T) -> Value {
    serde_json::to_value(v).expect("DTO must serialize")
}

#[test]
fn media_card_exact_key_set() {
    let card = MediaCard {
        media_id: "1".into(),
        media_type: "anime".into(),
        slug: "death-note".into(),
        title: Some(Title {
            ru: Some("Тетрадь смерти".into()),
            be: Some("Сшытак смерці".into()),
            en: Some("Death Note".into()),
            alt: Some(vec!["DN".into(), "ДН".into()]),
        }),
        poster: Some("https://cdn.anibel.net/p/death-note.jpg".into()),
        year: Some(2006),
        rating: Some(8.6),
        genres: Some(vec!["детектив".into(), "драма".into()]),
        status: Some("finished".into()),
        language: Some(vec!["sub".into(), "dub".into()]),
        update_type: Some("DUB".into()),
        num: Some(37),
    };
    assert_eq!(
        wire(&card),
        json!({
            "mediaId": "1",
            "mediaType": "anime",
            "slug": "death-note",
            "title": {
                "ru": "Тетрадь смерти",
                "be": "Сшытак смерці",
                "en": "Death Note",
                "alt": ["DN", "ДН"]
            },
            "poster": "https://cdn.anibel.net/p/death-note.jpg",
            "year": 2006,
            "rating": 8.6,
            "genres": ["детектив", "драма"],
            "status": "finished",
            "language": ["sub", "dub"],
            "updateType": "DUB",
            "num": 37
        })
    );
}

#[test]
fn media_detail_exact_key_set() {
    let detail = MediaDetail {
        media_id: "7".into(),
        media_type: "anime".into(),
        slug: "monster".into(),
        title: Some(Title {
            ru: Some("Монстр".into()),
            be: None,
            en: None,
            alt: None,
        }),
        description: Some(Description {
            ru: Some("Психологический триллер".into()),
            be: None,
            en: None,
        }),
        poster: Some("https://cdn.anibel.net/p/monster.jpg".into()),
        wallpaper: Some("https://cdn.anibel.net/w/monster.jpg".into()),
        studio: Some("Madhouse".into()),
        country: Some("JP".into()),
        status: Some("finished".into()),
        year: Some(2004),
        rating: Some(8.9),
        genres: Some(vec!["психология".into()]),
        language: Some(vec!["sub".into()]),
        mark: Some(Mark {
            status: Some("watching".into()),
        }),
        favorite: Some(true),
        trailer: Some("https://video.anibel.net/tr-1?v=abc".into()),
        download: Some("https://dl.anibel.net/monster.zip".into()),
        instructions: Some(Description {
            ru: Some("Скачайте и распакуйте".into()),
            be: None,
            en: None,
        }),
        franchise: Some("Monster".into()),
        relations: vec![MediaCard {
            media_id: "8".into(),
            media_type: "anime".into(),
            slug: "monster-rewrite".into(),
            ..Default::default()
        }],
        recommendations: vec![MediaCard {
            media_id: "9".into(),
            media_type: "anime".into(),
            slug: "buddha".into(),
            ..Default::default()
        }],
        episodes: vec![Episode {
            id: "e1".into(),
            episode: 1.0,
            ..Default::default()
        }],
        chapters: vec![Chapter {
            id: "c1".into(),
            chapter: 1.0,
            ..Default::default()
        }],
    };
    assert_eq!(
        wire(&detail),
        json!({
            "mediaId": "7",
            "mediaType": "anime",
            "slug": "monster",
            "title": { "ru": "Монстр", "be": null, "en": null },
            "description": { "ru": "Психологический триллер", "be": null, "en": null },
            "poster": "https://cdn.anibel.net/p/monster.jpg",
            "wallpaper": "https://cdn.anibel.net/w/monster.jpg",
            "studio": "Madhouse",
            "country": "JP",
            "status": "finished",
            "year": 2004,
            "rating": 8.9,
            "genres": ["психология"],
            "language": ["sub"],
            "mark": { "status": "watching" },
            "favorite": true,
            "trailer": "https://video.anibel.net/tr-1?v=abc",
            "download": "https://dl.anibel.net/monster.zip",
            "instructions": { "ru": "Скачайте и распакуйте", "be": null, "en": null },
            "franchise": "Monster",
            "relations": [{
                "mediaId": "8",
                "mediaType": "anime",
                "slug": "monster-rewrite"
            }],
            "recommendations": [{
                "mediaId": "9",
                "mediaType": "anime",
                "slug": "buddha"
            }],
            "episodes": [{
                "id": "e1",
                "episode": 1.0
            }],
            "chapters": [{
                "id": "c1",
                "chapter": 1.0,
                "images": []
            }]
        })
    );
}

#[test]
fn episode_exact_key_set() {
    let ep = Episode {
        id: "ep-13".into(),
        episode: 13.0,
        end_episode: Some(13.0),
        title: Some("Встреча".into()),
        url: Some(
            "https://video.anibel.net/8c52d132-955a-445c-8fa7-2f5739e141d8?type=anime".into(),
        ),
        r#type: Some("sub".into()),
        resource: Some(1),
        released: Some(1_700_000_000_000),
        watched: Some(false),
    };
    assert_eq!(
        wire(&ep),
        json!({
            "id": "ep-13",
            "episode": 13.0,
            "endEpisode": 13.0,
            "title": "Встреча",
            "url": "https://video.anibel.net/8c52d132-955a-445c-8fa7-2f5739e141d8?type=anime",
            "type": "sub",
            "resource": 1,
            "released": 1700000000000u64,
            "watched": false
        })
    );
}

#[test]
fn comment_exact_key_set() {
    let comment = Comment {
        id: "c-42".into(),
        content: "Отличный эпизод".into(),
        user: Some(CommentUser {
            username: "alice".into(),
            avatar: Some("https://cdn.anibel.net/u/alice.png".into()),
            display_name: Some("Алиса".into()),
        }),
        created: 1_700_000_000_000,
    };
    assert_eq!(
        wire(&comment),
        json!({
            "id": "c-42",
            "content": "Отличный эпизод",
            "user": {
                "username": "alice",
                "avatar": "https://cdn.anibel.net/u/alice.png",
                "displayName": "Алиса"
            },
            "created": 1700000000000u64
        })
    );
}

#[test]
fn login_user_exact_key_set() {
    let user = LoginUser {
        id: "u-1".into(),
        username: "alice".into(),
        email: Some("a@example.com".into()),
        role: Some("admin".into()),
        token: Some("eyJhbGciOiJIUzI1NiJ9...".into()),
        avatar: Some("https://cdn.anibel.net/u/alice.png".into()),
    };
    assert_eq!(
        wire(&user),
        json!({
            "id": "u-1",
            "username": "alice",
            "email": "a@example.com",
            "role": "admin",
            "token": "eyJhbGciOiJIUzI1NiJ9...",
            "avatar": "https://cdn.anibel.net/u/alice.png"
        })
    );
}

#[test]
fn page_exact_key_set() {
    let page = Page::<MediaCard> {
        docs: vec![MediaCard {
            media_id: "1".into(),
            media_type: "anime".into(),
            slug: "a".into(),
            ..Default::default()
        }],
        total_docs: 250,
        limit: Some(20),
        offset: Some(40),
    };
    assert_eq!(
        wire(&page),
        json!({
            "docs": [{ "mediaId": "1", "mediaType": "anime", "slug": "a" }],
            "totalDocs": 250,
            "limit": 20,
            "offset": 40
        })
    );
}

#[test]
fn playback_intent_exact_key_set() {
    // embed shape (Google Drive) — the WebView2 pipeline
    let embed = PlaybackIntent::embed("https://drive.google.com/file/d/abc/preview");
    assert_eq!(
        wire(&embed),
        json!({
            "kind": "embed",
            "pageUrl": "https://drive.google.com/file/d/abc/preview",
            "subtitles": [],
            "fonts": []
        })
    );

    // native shape (Anibel HLS/DASH + ASS) — the mpv/libass pipeline
    let native = PlaybackIntent {
        kind: "native".into(),
        page_url: Some(
            "https://video.anibel.net/8c52d132-955a-445c-8fa7-2f5739e141d8?type=anime".into(),
        ),
        video_id: Some("8c52d132-955a-445c-8fa7-2f5739e141d8".into()),
        video_src: Some("https://n3.anibel.stream/dash/8c52d132/manifest.m3u8".into()),
        audio_src: None,
        sub_src: Some("https://subtitles.anibel.net/8c52d132/ep13.ass".into()),
        subtitles: vec![anibel_core::player::SubtitleTrack {
            url: "https://subtitles.anibel.net/8c52d132/ep13.ass".into(),
            fonts: vec!["Montserrat".into()],
            label: Some("ep13".into()),
        }],
        fonts: vec![anibel_core::player::FontAsset {
            family: "Montserrat".into(),
            url: "https://fonts.anibel.net/montserrat/aaa.ttf".into(),
        }],
        duration_secs: Some(600.0),
    };
    assert_eq!(
        wire(&native),
        json!({
            "kind": "native",
            "pageUrl": "https://video.anibel.net/8c52d132-955a-445c-8fa7-2f5739e141d8?type=anime",
            "videoId": "8c52d132-955a-445c-8fa7-2f5739e141d8",
            "videoSrc": "https://n3.anibel.stream/dash/8c52d132/manifest.m3u8",
            "subSrc": "https://subtitles.anibel.net/8c52d132/ep13.ass",
            "subtitles": [{
                "url": "https://subtitles.anibel.net/8c52d132/ep13.ass",
                "fonts": ["Montserrat"],
                "label": "ep13"
            }],
            "fonts": [{
                "family": "Montserrat",
                "url": "https://fonts.anibel.net/montserrat/aaa.ttf"
            }],
            "durationSecs": 600.0
        })
    );
}

#[test]
fn error_dto_exact_key_set() {
    // Wire shape is frozen to {code, message} — the C# mirror never reads
    // anything else (the old `cause` field was removed).
    let dto = ErrorDto {
        code: "http_error".into(),
        message: "http error: 500".into(),
    };
    let value = wire(&dto);
    assert_eq!(
        value,
        json!({ "code": "http_error", "message": "http error: 500" })
    );
    let keys: Vec<&str> = value
        .as_object()
        .unwrap()
        .keys()
        .map(String::as_str)
        .collect();
    assert_eq!(keys, vec!["code", "message"]);
}
