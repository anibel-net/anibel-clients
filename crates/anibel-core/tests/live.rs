//! Live-network smoke tests against production anibel.net.
//! Run on demand (detects backend schema drift):
//!
//! ```ps1
//! cargo test -p anibel-core --test live -- --ignored --nocapture
//! ```

use anibel_core::ffi::{
    anibel_core_call, anibel_core_free, anibel_core_init, anibel_core_shutdown,
};
use serde_json::Value;
use std::ffi::CStr;
use std::ffi::CString;

fn init() -> i64 {
    unsafe { anibel_core_init(std::ptr::null()) }
}

fn call(handle: i64, req: &str) -> Value {
    let req = CString::new(req).unwrap();
    let resp = unsafe { anibel_core_call(handle, req.as_ptr()) };
    let out = unsafe { CStr::from_ptr(resp) }
        .to_string_lossy()
        .to_string();
    unsafe { anibel_core_free(resp) };
    serde_json::from_str(&out).unwrap()
}

#[test]
#[ignore = "live network"]
fn live_media_list_anime() {
    let h = init();
    let resp = call(
        h,
        r#"{"id": 1, "op": "mediaList", "args": { "mediaType": "anime", "offset": 0, "limit": 3 }}"#,
    );
    assert_eq!(resp["ok"], true, "mediaList failed: {resp}");
    unsafe { anibel_core_shutdown(h) };
}

#[test]
#[ignore = "live network"]
fn live_comment_tree_within_server_depth_limit() {
    let h = init();
    let titles = call(h, r#"{"id":1,"op":"trends","args":{"limit":3}}"#);
    assert_eq!(titles["ok"], true, "trends failed: {titles}");
    let titles = titles["value"].as_array().unwrap();
    assert!(!titles.is_empty(), "no titles to check");
    for (index, title) in titles.iter().enumerate() {
        let request = serde_json::json!({
            "id": index + 2, "op": "comments", "cache": "reload",
            "args": {"mediaId":title["mediaId"], "mediaType":title["mediaType"], "limit":20}
        });
        let response = call(h, &request.to_string());
        assert_eq!(response["ok"], true, "comment query rejected: {response}");
        for comment in response["value"]["docs"].as_array().unwrap() {
            assert!(comment["replies"].is_array());
        }
    }
    unsafe { anibel_core_shutdown(h) };
}

#[test]
#[ignore = "live network"]
fn live_resolve_episode_native() {
    let h = init();
    // Known Anibel-player episode (videoService record) — id from episode url
    // pattern https://video.anibel.net/<uuid>?type=anime
    let resp = call(
        h,
        r#"{"id": 1, "op": "resolveEpisode", "args": { "videoId": "8c52d132-955a-445c-8fa7-2f5739e141d8" }}"#,
    );
    assert_eq!(resp["ok"], true, "resolveEpisode failed: {resp}");
    assert_eq!(resp["value"]["kind"], "native");
    assert!(
        resp["value"]["videoSrc"]
            .as_str()
            .unwrap_or("")
            .starts_with("https://n"),
        "videoSrc missing: {resp}"
    );
    assert!(
        !resp["value"]["subtitles"]
            .as_array()
            .unwrap_or(&vec![])
            .is_empty(),
        "no subtitles: {resp}"
    );
    unsafe { anibel_core_shutdown(h) };
}

#[test]
#[ignore = "live network"]
fn live_player_parser_page_is_spa_noop() {
    // Regression guard: production player page is an SPA (no <source> tags).
    // Resolution must NOT parse HTML; it must hit the video service directly.
    let h = init();
    let resp = call(
        h,
        r#"{"id": 1, "op": "resolveEpisode", "args": { "url": "https://video.anibel.net/8c52d132-955a-445c-8fa7-2f5739e141d8?type=anime" }}"#,
    );
    assert_eq!(resp["ok"], true, "resolveEpisode failed: {resp}");
    assert!(resp["value"]["videoSrc"].as_str().is_some());
    unsafe { anibel_core_shutdown(h) };
}

#[test]
#[ignore = "live network"]
fn live_media_details_by_slug() {
    let h = init();
    let resp = call(
        h,
        r#"{"id": 1, "op": "media", "args": { "slug": "death-note" }}"#,
    );
    assert_eq!(resp["ok"], true, "media failed: {resp}");
    assert_eq!(resp["value"]["mediaType"], "anime");
    assert!(resp["value"]["title"]["be"].as_str().is_some());
    unsafe { anibel_core_shutdown(h) };
}

#[test]
#[ignore = "live network"]
fn live_games_catalog() {
    let h = init();
    let resp = call(
        h,
        r#"{"id": 1, "op": "mediaList", "args": { "mediaType": "games", "offset": 0, "limit": 3 }}"#,
    );
    assert_eq!(resp["ok"], true, "games mediaList failed: {resp}");
    assert!(resp["value"]["totalDocs"].as_i64().unwrap_or(0) >= 1);
    unsafe { anibel_core_shutdown(h) };
}

#[test]
#[ignore = "live network"]
fn live_chapters_manga() {
    let h = init();
    let resp = call(
        h,
        r#"{"id": 1, "op": "media", "args": { "slug": "death-note-2020" }}"#,
    );
    assert_eq!(resp["ok"], true, "manga media failed: {resp}");
    assert_eq!(resp["value"]["mediaType"], "manga");
    unsafe { anibel_core_shutdown(h) };
}

#[tokio::test]
#[ignore = "live network"]
async fn live_episodes_raw_body_diagnostic() {
    // Posts the core's EXACT request body via plain reqwest. If this succeeds
    // while the FFI op fails, the difference is transport-level (headers/cookies).
    // GraphQL types stay crate-private; ship the same prepared document the FFI uses.

    // resolve media id via raw HTTP (no FFI here — nested runtime would abort)
    let media_body = serde_json::json!({
        "query": "query MediaQuery($slug: String!, $mediaType: MediaTypes) { media(slug: $slug, mediaType: $mediaType) { mediaId } }",
        "variables": { "slug": "death-note" },
        "operationName": "MediaQuery",
    });
    let client = reqwest::Client::new();
    let media_resp: Value = client
        .post("https://anibel.net/graphql")
        .json(&media_body)
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    let media_id = media_resp["data"]["media"]["mediaId"]
        .as_str()
        .unwrap_or("89bffd5d-1efc-49c6-ba21-e1ec06c1041d")
        .to_string();

    let raw_body = serde_json::json!({
        "query": include_str!("../../anibel-api/graphql/ops/episodes.graphql"),
        "variables": {
            "mediaId": media_id,
            "type": "sub",
            "resource": 1,
            "offset": 0,
            "limit": 5,
        },
        "operationName": "EpisodesQuery",
    });
    println!(
        "request body: {}",
        serde_json::to_string_pretty(&raw_body).unwrap()
    );
    let raw_resp: Value = client
        .post("https://anibel.net/graphql")
        .json(&raw_body)
        .send()
        .await
        .unwrap()
        .json()
        .await
        .unwrap();
    println!("raw-with-core-body errors: {}", raw_resp["errors"]);
    println!(
        "raw docs: {:?}",
        raw_resp["data"]["episodes"]["docs"]
            .as_array()
            .map(|a| a.len())
    );
}

#[test]
#[ignore = "live network"]
fn live_episodes_matrix() {
    let h = init();
    let media = call(
        h,
        r#"{"id": 1, "op": "media", "args": { "slug": "death-note" }}"#,
    );
    assert_eq!(media["ok"], true, "media failed: {media}");
    println!("mediaId raw: {}", media["value"]["mediaId"]);
    let media_id = media["value"]["mediaId"].as_str().unwrap_or("1");
    for (r#type, resource) in [("sub", 1), ("sub", 2), ("dub", 1), ("dub", 2)] {
        let req = format!(
            r#"{{"id": 2, "op": "episodes", "args": {{ "mediaId": "{media_id}", "type": "{type}", "resource": {resource}, "limit": 5 }} }}"#
        );
        let resp = call(h, &req);
        println!(
            "episodes {type}/r{resource} -> ok={} docs={:?} err={:?}",
            resp["ok"],
            resp["value"]["docs"].as_array().map(|a| a.len()),
            resp["error"]["message"]
        );
    }
    let req =
        format!(r#"{{"id": 2, "op": "episodesMatrix", "args": {{ "mediaId": "{media_id}" }} }}"#);
    let resp = call(h, &req);
    assert_eq!(resp["ok"], true, "episodesMatrix failed: {resp}");
    let docs = resp["value"].as_array().cloned().unwrap_or_default();
    println!("episodesMatrix: {} docs", docs.len());
    println!(
        "first: {}",
        serde_json::to_string_pretty(docs.first().unwrap_or(&serde_json::Value::Null))
            .unwrap_or_default()
    );
    assert!(!docs.is_empty(), "no episodes returned");
    unsafe { anibel_core_shutdown(h) };
}

#[test]
#[ignore = "live network"]
fn live_chapters_docs() {
    let h = init();
    let media = call(
        h,
        r#"{"id": 1, "op": "media", "args": { "slug": "death-note-2020" }}"#,
    );
    assert_eq!(media["ok"], true, "manga media failed: {media}");
    let media_id = media["value"]["mediaId"].as_str().unwrap_or("1");
    let req = format!(
        r#"{{"id": 2, "op": "chapters", "args": {{ "mediaId": "{media_id}", "limit": 20 }} }}"#
    );
    let resp = call(h, &req);
    assert_eq!(resp["ok"], true, "chapters failed: {resp}");
    let docs = resp["value"]["docs"]
        .as_array()
        .cloned()
        .unwrap_or_default();
    println!("chapters: {} docs", docs.len());
    println!(
        "first: {}",
        serde_json::to_string_pretty(docs.first().unwrap_or(&serde_json::Value::Null))
            .unwrap_or_default()
    );
    assert!(!docs.is_empty(), "no chapters returned");
    unsafe { anibel_core_shutdown(h) };
}
