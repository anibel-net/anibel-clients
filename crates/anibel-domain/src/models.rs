//! Frozen host-facing domain DTOs. These are the only JSON shapes that
//! cross the FFI boundary. GraphQL generated types stay crate-private.

use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Title {
    #[serde(default)]
    pub ru: Option<String>,
    #[serde(default)]
    pub be: Option<String>,
    #[serde(default)]
    pub en: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub alt: Option<Vec<String>>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Description {
    #[serde(default)]
    pub ru: Option<String>,
    #[serde(default)]
    pub be: Option<String>,
    #[serde(default)]
    pub en: Option<String>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MediaCard {
    #[serde(default)]
    pub media_id: String,
    #[serde(default)]
    pub media_type: String,
    #[serde(default)]
    pub slug: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub title: Option<Title>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub poster: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub year: Option<i64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub rating: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub genres: Option<Vec<String>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub status: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub language: Option<Vec<String>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub update_type: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub num: Option<i64>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Mark {
    #[serde(skip_serializing_if = "Option::is_none")]
    pub status: Option<String>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Episode {
    #[serde(default)]
    pub id: String,
    #[serde(default)]
    pub episode: f64,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub end_episode: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub title: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub url: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub r#type: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub resource: Option<i64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub released: Option<i64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub watched: Option<bool>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ChapterImage {
    #[serde(default)]
    pub large: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub thumbnail: Option<String>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Chapter {
    #[serde(default)]
    pub id: String,
    #[serde(default)]
    pub chapter: f64,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub end_chapter: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub title: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub released: Option<i64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub view: Option<i64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub read: Option<bool>,
    #[serde(default)]
    pub images: Vec<ChapterImage>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MediaDetail {
    #[serde(default)]
    pub media_id: String,
    #[serde(default)]
    pub media_type: String,
    #[serde(default)]
    pub slug: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub title: Option<Title>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub description: Option<Description>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub poster: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub wallpaper: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub studio: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub country: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub status: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub year: Option<i64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub rating: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub genres: Option<Vec<String>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub language: Option<Vec<String>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub mark: Option<Mark>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub favorite: Option<bool>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub trailer: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub download: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub instructions: Option<Description>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub franchise: Option<String>,
    #[serde(default)]
    pub relations: Vec<MediaCard>,
    #[serde(default)]
    pub recommendations: Vec<MediaCard>,
    #[serde(default)]
    pub episodes: Vec<Episode>,
    #[serde(default)]
    pub chapters: Vec<Chapter>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Page<T> {
    #[serde(default)]
    pub docs: Vec<T>,
    #[serde(default)]
    pub total_docs: i64,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub limit: Option<i64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub offset: Option<i64>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct CommentUser {
    #[serde(default)]
    pub username: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub avatar: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub display_name: Option<String>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Comment {
    #[serde(default)]
    pub id: String,
    #[serde(default)]
    pub content: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub user: Option<CommentUser>,
    /// Epoch milliseconds. Flattened from GraphQL `date.created` (BigNumber).
    #[serde(default)]
    pub created: i64,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Slide {
    #[serde(skip_serializing_if = "Option::is_none")]
    pub id: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub title: Option<Title>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub content: Option<Description>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub link: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub img: Option<String>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct LoginUser {
    #[serde(default)]
    pub id: String,
    #[serde(default)]
    pub username: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub email: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub role: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub token: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub avatar: Option<String>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Profile {
    #[serde(default)]
    pub id: String,
    #[serde(default)]
    pub username: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub avatar: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub display_name: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub bio: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub wallpaper: Option<String>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct MarkEntry {
    #[serde(skip_serializing_if = "Option::is_none")]
    pub mark_id: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub status: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub media: Option<MediaCard>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct StatusCounters {
    #[serde(skip_serializing_if = "Option::is_none")]
    pub watching: Option<i64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub watched: Option<i64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub dropped: Option<i64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub planned: Option<i64>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Filters {
    #[serde(skip_serializing_if = "Option::is_none")]
    pub years: Option<Vec<i64>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub genres: Option<Vec<String>>,
    /// GraphQL field is `studies`; hosts see `studios`.
    #[serde(skip_serializing_if = "Option::is_none")]
    pub studios: Option<Vec<String>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub types: Option<Vec<String>>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct Statistics {
    #[serde(skip_serializing_if = "Option::is_none")]
    pub anime: Option<i64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub manga: Option<i64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub cinema: Option<i64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub games: Option<i64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub users: Option<i64>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ScheduleEntity {
    #[serde(skip_serializing_if = "Option::is_none")]
    pub id: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub title: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub episode: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub chapter: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub media_id: Option<String>,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ScheduleDay {
    #[serde(skip_serializing_if = "Option::is_none")]
    pub day: Option<String>,
    #[serde(default)]
    pub entities: Vec<ScheduleEntity>,
}

/// Parse a GraphQL BigNumber/BigInt (JSON number or decimal string) to i64.
pub fn big_number(value: &serde_json::Value) -> Option<i64> {
    match value {
        serde_json::Value::Null => None,
        serde_json::Value::Number(n) => n.as_i64().or_else(|| n.as_f64().map(|f| f as i64)),
        serde_json::Value::String(s) => s
            .parse::<i64>()
            .ok()
            .or_else(|| s.parse::<f64>().ok().map(|f| f as i64)),
        _ => None,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn big_number_accepts_int_float_and_string() {
        assert_eq!(
            big_number(&json!(1700000000000u64)),
            Some(1_700_000_000_000)
        );
        assert_eq!(big_number(&json!(1700000000000.0)), Some(1_700_000_000_000));
        assert_eq!(big_number(&json!("1700000000000")), Some(1_700_000_000_000));
        assert_eq!(big_number(&json!(null)), None);
    }
}
