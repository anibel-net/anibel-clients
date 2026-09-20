//! Shared content choices. Native clients translate keys and render the result.
use anibel_domain::{
    error::{AnibelError, Result},
    models::Episode,
};
use serde::Deserialize;
use serde_json::{Value, json};

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct VideoTrack {
    id: i64,
    #[serde(default)]
    width: u32,
    #[serde(default)]
    height: u32,
    #[serde(default)]
    bitrate: u64,
    #[serde(default)]
    codec: String,
    #[serde(default)]
    image: bool,
    #[serde(default)]
    selected: bool,
}

pub(super) fn video_qualities(args: &Value) -> Result<Value> {
    let mut tracks: Vec<VideoTrack> = super::decode(args.get("tracks").unwrap_or(&Value::Null))?;
    if tracks.len() > 1024 {
        return Err(AnibelError::BadArgs("track count exceeds bounds".into()));
    }
    tracks.retain(|t| t.id > 0 && !t.image);
    let mut ids = std::collections::HashSet::new();
    if tracks
        .iter()
        .any(|t| !ids.insert(t.id) || t.height > 32768 || t.width > 32768 || t.codec.len() > 128)
    {
        return Err(AnibelError::BadArgs("invalid video track metadata".into()));
    }
    tracks.sort_by_key(|t| std::cmp::Reverse((t.height, t.width, t.bitrate)));
    let selection = match args.get("select") {
        None | Some(Value::Null) => None,
        Some(value) => Some(
            value
                .as_i64()
                .filter(|id| ids.contains(id))
                .ok_or_else(|| {
                    AnibelError::BadArgs("video quality is no longer available".into())
                })?,
        ),
    };
    let choices: Vec<_> = tracks
        .iter()
        .map(|t| {
            let label = if t.height > 0 {
                format!("{}p", t.height)
            } else if t.bitrate > 0 {
                format!("{:.1} Mbit/s", t.bitrate as f64 / 1_000_000.0)
            } else {
                format!("#{}", t.id)
            };
            let label = if t.codec.is_empty() {
                label
            } else {
                format!("{label} · {}", t.codec)
            };
            json!({"id":t.id,"label":label,"selected":t.selected})
        })
        .collect();
    Ok(json!({"choices":choices,"video":selection}))
}

pub(super) fn signs(name: &str) -> bool {
    let lower = name.to_lowercase();
    if ["s&s", "signsong", "sign_song", "signsongs"]
        .iter()
        .any(|s| lower.contains(s))
    {
        return true;
    }
    lower.split(|c: char| !c.is_alphanumeric()).any(|s| {
        matches!(
            s,
            "sign"
                | "signs"
                | "forced"
                | "надпісы"
                | "надпіс"
                | "надписи"
                | "надпис"
                | "знаки"
                | "знак"
        )
    })
}
#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Track {
    id: i64,
    #[serde(default)]
    lang: String,
    #[serde(default)]
    title: String,
    #[serde(default)]
    file_name: String,
}
#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
struct Tracks {
    #[serde(default)]
    audio: Vec<Track>,
    #[serde(default)]
    subtitles: Vec<Track>,
    #[serde(default)]
    prefer_dub: bool,
}
fn score(t: &Track, dub: bool) -> i32 {
    let lang = t.lang.trim().to_lowercase();
    let title = t.title.to_lowercase();
    if dub {
        if matches!(lang.as_str(), "be" | "bel" | "be-by" | "be_by") {
            100
        } else if ["belarus", "беларус", "бел."]
            .iter()
            .any(|s| title.contains(s))
        {
            95
        } else if ["dub", "дуб"].iter().any(|s| title.contains(s)) {
            70
        } else if matches!(lang.as_str(), "ru" | "rus" | "ru-ru") {
            20
        } else {
            0
        }
    } else if matches!(lang.as_str(), "jpn" | "ja" | "jp")
        || title.contains("japanese")
        || title.contains("япон")
    {
        100
    } else {
        0
    }
}
pub(super) fn tracks(args: &Value) -> Result<Value> {
    let t: Tracks = super::decode(args)?;
    if t.audio.len() > 1024 || t.subtitles.len() > 1024 {
        return Err(AnibelError::BadArgs("track count exceeds bounds".into()));
    }
    let audio = t
        .audio
        .iter()
        .enumerate()
        .max_by_key(|(i, t2)| (score(t2, t.prefer_dub), std::cmp::Reverse(*i)));
    let audio = audio.and_then(|(_, a)| {
        if score(a, t.prefer_dub) > 0 {
            Some(a.id)
        } else {
            if t.prefer_dub {
                t.audio.last()
            } else {
                t.audio.first()
            }
            .map(|a| a.id)
        }
    });
    let sub = t
        .subtitles
        .iter()
        .find(|s| signs(&format!("{} {} {}", s.file_name, s.title, s.lang)) == t.prefer_dub)
        .or_else(|| {
            if t.prefer_dub {
                None
            } else {
                t.subtitles.first()
            }
        })
        .map(|s| s.id);
    Ok(json!({"audio":audio,"subtitle":sub}))
}
pub(super) fn media_kind(kind: &str) -> Result<Value> {
    let (source, progress, done) = match kind {
        "anime" | "cinema" => ("episodes", "watching", "watched"),
        "manga" => ("chapters", "reading", "read"),
        "books" => ("book", "reading", "read"),
        "games" => ("game", "playing", "played"),
        _ => return Err(AnibelError::BadArgs("unknown media type".into())),
    };
    Ok(json!({"content":source,"marks":["notselected",progress,done,"dropped","planned"]}))
}
pub(super) fn episodes(mut rows: Vec<Episode>, selected: &str, resource: Option<i64>) -> Value {
    rows.retain(|e| e.url.as_ref().is_some_and(|u| !u.trim().is_empty()));
    let kind = |e: &Episode| match e.r#type.as_deref().map(str::to_lowercase).as_deref() {
        Some("dub") => "dub",
        Some("sub") => "sub",
        _ => "other",
    };
    let kinds: Vec<_> = ["dub", "sub", "other"]
        .into_iter()
        .filter(|k| rows.iter().any(|e| kind(e) == *k))
        .collect();
    let selected = if kinds.contains(&selected) {
        selected
    } else {
        kinds.first().copied().unwrap_or("dub")
    };
    let mut resources: Vec<_> = rows
        .iter()
        .filter_map(|e| e.resource)
        .filter(|r| *r > 0)
        .collect();
    resources.sort();
    resources.dedup();
    rows.retain(|e| kind(e) == selected && resource.is_none_or(|r| e.resource == Some(r)));
    rows.sort_by(|a, b| a.episode.total_cmp(&b.episode));
    json!({"items":rows,"kinds":kinds,"selectedKind":selected,"resources":resources})
}
pub(super) fn chapter_neighbors(args: &Value, current: f64) -> Result<(Option<f64>, Option<f64>)> {
    let chapters: Vec<f64> = args
        .get("chapters")
        .filter(|v| !v.is_null())
        .map(super::decode)
        .transpose()?
        .unwrap_or_default();
    if chapters.len() > 100_000 || chapters.iter().any(|n| !n.is_finite() || *n < 0.0) {
        return Err(AnibelError::BadArgs("invalid chapter list".into()));
    }
    Ok(chapters
        .iter()
        .position(|n| (*n - current).abs() < 0.001)
        .map(|i| {
            (
                i.checked_sub(1).map(|p| chapters[p]),
                chapters.get(i + 1).copied(),
            )
        })
        .unwrap_or((None, None)))
}
pub(super) fn page_cursor(
    offset: i64,
    count: usize,
    limit: i64,
    total: Option<i64>,
) -> Result<(i64, bool)> {
    if offset < 0 || !(1..=1000).contains(&limit) {
        return Err(AnibelError::BadArgs("invalid page bounds".into()));
    }
    let count =
        i64::try_from(count).map_err(|_| AnibelError::Decode("page size overflow".into()))?;
    let next = offset
        .checked_add(count)
        .ok_or_else(|| AnibelError::Decode("page offset overflow".into()))?;
    Ok((
        next,
        count > 0 && total.map_or(count >= limit, |n| next < n),
    ))
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn paging_and_reader_navigation_are_bounded() {
        assert_eq!(page_cursor(60, 20, 20, Some(80)).unwrap(), (80, false));
        assert_eq!(page_cursor(60, 0, 20, Some(90)).unwrap(), (60, false));
        assert_eq!(page_cursor(0, 20, 20, None).unwrap(), (20, true));
        assert!(page_cursor(i64::MAX, 1, 20, None).is_err());
        assert!(page_cursor(-1, 1, 20, None).is_err());
        assert_eq!(
            chapter_neighbors(&json!({"chapters":[1,1.5,2]}), 1.5).unwrap(),
            (Some(1.0), Some(2.0))
        );
        assert_eq!(
            chapter_neighbors(&json!({"chapters":[1,2]}), 1.0).unwrap(),
            (None, Some(2.0))
        );
        assert_eq!(
            chapter_neighbors(&json!({"chapters":[1,2]}), 3.0).unwrap(),
            (None, None)
        );
    }
    #[test]
    fn episode_choices_filter_sources_and_sort_without_native_rules() {
        let rows = vec![
            Episode {
                id: "sub".into(),
                episode: 3.0,
                r#type: Some("sub".into()),
                url: Some("https://sub".into()),
                resource: Some(2),
                ..Default::default()
            },
            Episode {
                id: "dub2".into(),
                episode: 2.0,
                r#type: Some("dub".into()),
                url: Some("https://dub".into()),
                resource: Some(1),
                ..Default::default()
            },
            Episode {
                id: "dub1".into(),
                episode: 1.0,
                r#type: Some("dub".into()),
                url: Some("https://dub".into()),
                resource: Some(2),
                ..Default::default()
            },
            Episode {
                id: "missing".into(),
                ..Default::default()
            },
        ];
        let value = episodes(rows.clone(), "dub", None);
        assert_eq!(value["items"][0]["id"], "dub1");
        assert_eq!(value["items"].as_array().unwrap().len(), 2);
        assert_eq!(value["kinds"], json!(["dub", "sub"]));
        assert_eq!(episodes(rows, "dub", Some(1))["items"][0]["id"], "dub2");
        assert_eq!(media_kind("games").unwrap()["marks"][1], "playing");
        assert!(media_kind("bad").is_err());
    }
    #[test]
    fn track_rules_are_shared() {
        assert!(signs("hash_надпісы.ass"));
        assert!(!signs("design.ass"));
        let result=tracks(&json!({"preferDub":true,"audio":[{"id":1,"lang":"jpn"},{"id":2,"lang":"bel"}],"subtitles":[{"id":3,"title":"Dialogue"},{"id":4,"fileName":"signs.ass"}]})).unwrap();
        assert_eq!(result, json!({"audio":2,"subtitle":4}));
        assert_eq!(
            tracks(&json!({"preferDub":true,"subtitles":[{"id":1,"title":"Dialogue"}]})).unwrap()["subtitle"],
            Value::Null
        );
    }
}
