//! Map crate-private GraphQL types onto frozen domain DTOs.
//!
//! graphql_client generates a distinct nested type per operation even when
//! they share fragments, so we go through serde JSON (camelCase GraphQL
//! names) and pick the host fields we actually freeze.

use anibel_domain::models::{
    Chapter, ChapterImage, Comment, CommentUser, Description, Episode, Filters, LoginUser, Mark,
    MarkEntry, MediaCard, MediaDetail, Page, Profile, ScheduleDay, ScheduleEntity, Slide,
    Statistics, StatusCounters, Title, big_number,
};
use serde::Serialize;
use serde_json::Value;

use anibel_domain::error::{AnibelError, Result};

pub fn json(v: impl Serialize) -> Result<Value> {
    serde_json::to_value(v).map_err(|e| AnibelError::Internal(format!("API mapping: {e}")))
}
fn validate_media(value: &Value) -> Result<()> {
    for key in ["mediaId", "mediaType", "slug"] {
        if value
            .get(key)
            .and_then(Value::as_str)
            .is_none_or(|s| s.trim().is_empty())
        {
            return Err(AnibelError::Internal(format!("API media is missing {key}")));
        }
    }
    Ok(())
}
pub fn media_card(v: impl Serialize) -> Result<MediaCard> {
    let v = json(v)?;
    validate_media(&v)?;
    Ok(media_card_from(&v))
}
pub fn media_detail(v: impl Serialize) -> Result<MediaDetail> {
    let v = json(v)?;
    validate_media(&v)?;
    Ok(media_detail_from(&v))
}
pub fn media_cards_opt<T: Serialize>(
    items: impl IntoIterator<Item = Option<T>>,
) -> Result<Vec<MediaCard>> {
    items.into_iter().flatten().map(media_card).collect()
}
pub fn media_detail_opt<T: Serialize>(v: Option<T>) -> Result<Option<MediaDetail>> {
    v.map(media_detail).transpose()
}
pub fn page_media(v: impl Serialize) -> Result<Page<MediaCard>> {
    let v = json(v)?;
    if let Some(docs) = v.get("docs").and_then(Value::as_array) {
        for item in docs.iter().filter(|item| !item.is_null()) {
            validate_media(item)?;
        }
    }
    Ok(page_from(&v, media_card_from))
}
pub fn page_episodes(v: impl Serialize) -> Result<Page<Episode>> {
    Ok(page_from(&json(v)?, episode_from))
}
pub fn page_chapters(v: impl Serialize) -> Result<Page<Chapter>> {
    Ok(page_from(&json(v)?, chapter_from))
}
pub fn page_comments(v: impl Serialize) -> Result<Page<Comment>> {
    Ok(page_from(&json(v)?, comment_from))
}
pub fn comment(v: impl Serialize) -> Result<Comment> {
    Ok(comment_from(&json(v)?))
}
pub fn page_marks(v: impl Serialize) -> Result<Page<MarkEntry>> {
    Ok(page_from(&json(v)?, mark_entry_from))
}
pub fn chapter(v: impl Serialize) -> Result<Chapter> {
    Ok(chapter_from(&json(v)?))
}
pub fn slide(v: impl Serialize) -> Result<Slide> {
    Ok(slide_from(&json(v)?))
}
pub fn login_user(v: impl Serialize) -> Result<LoginUser> {
    Ok(login_user_from(&json(v)?))
}
pub fn profile(v: impl Serialize) -> Result<Profile> {
    Ok(profile_from(&json(v)?))
}
pub fn filters(v: impl Serialize) -> Result<Filters> {
    Ok(filters_from(&json(v)?))
}
pub fn statistics(v: impl Serialize) -> Result<Statistics> {
    Ok(statistics_from(&json(v)?))
}
pub fn status(v: impl Serialize) -> Result<StatusCounters> {
    Ok(status_from(&json(v)?))
}
pub fn schedule_day(v: impl Serialize) -> Result<ScheduleDay> {
    Ok(schedule_day_from(&json(v)?))
}
pub fn slides_opt<T: Serialize>(items: impl IntoIterator<Item = Option<T>>) -> Result<Vec<Slide>> {
    items.into_iter().flatten().map(slide).collect()
}
pub fn profile_opt<T: Serialize>(v: Option<T>) -> Result<Option<Profile>> {
    v.map(profile).transpose()
}
pub fn schedule_days_opt<T: Serialize>(
    items: impl IntoIterator<Item = Option<T>>,
) -> Result<Vec<ScheduleDay>> {
    items.into_iter().flatten().map(schedule_day).collect()
}

fn page_from<T>(v: &Value, map_item: fn(&Value) -> T) -> Page<T> {
    let docs = v
        .get("docs")
        .and_then(Value::as_array)
        .map(|a| {
            a.iter()
                .filter(|x| !x.is_null())
                .map(map_item)
                .collect::<Vec<_>>()
        })
        .unwrap_or_default();
    Page {
        total_docs: i64_field(v, "totalDocs").unwrap_or(docs.len() as i64),
        limit: i64_field(v, "limit"),
        offset: i64_field(v, "offset"),
        docs,
    }
}

fn media_cards_from_field(v: &Value, key: &str) -> Vec<MediaCard> {
    v.get(key)
        .and_then(Value::as_array)
        .map(|a| {
            a.iter()
                .filter(|x| !x.is_null())
                .filter(|x| x.get("hidden").and_then(Value::as_bool) != Some(true))
                .map(media_card_from)
                .collect()
        })
        .unwrap_or_default()
}

fn media_card_from(v: &Value) -> MediaCard {
    MediaCard {
        media_id: str_field(v, "mediaId").unwrap_or_default(),
        media_type: str_field(v, "mediaType").unwrap_or_default(),
        slug: str_field(v, "slug").unwrap_or_default(),
        title: title_field(v, "title"),
        poster: str_field(v, "poster"),
        year: i64_field(v, "year"),
        rating: f64_field(v, "rating"),
        genres: str_vec_opt(v, "genres"),
        status: str_field(v, "status"),
        language: str_vec_opt(v, "language"),
        update_type: str_field(v, "updateType"),
        num: i64_field(v, "num"),
    }
}

fn media_detail_from(v: &Value) -> MediaDetail {
    let card = media_card_from(v);
    MediaDetail {
        media_id: card.media_id,
        media_type: card.media_type,
        slug: card.slug,
        title: card.title,
        description: description_field(v, "description"),
        poster: card.poster,
        wallpaper: str_field(v, "wallpaper"),
        studio: str_field(v, "studio"),
        country: str_field(v, "country"),
        status: str_field(v, "status"),
        year: card.year,
        rating: card.rating,
        i_rated: f64_field(v, "iRated"),
        genres: card.genres,
        language: str_vec_opt(v, "language"),
        mark: v.get("mark").filter(|m| !m.is_null()).map(|m| Mark {
            status: str_field(m, "status"),
        }),
        favorite: v.get("favorite").and_then(Value::as_bool),
        trailer: str_field(v, "trailer"),
        download: str_field(v, "download"),
        instructions: description_field(v, "instructions"),
        franchise: str_field(v, "franchise"),
        relations: media_cards_from_field(v, "relations"),
        recommendations: media_cards_from_field(v, "recommendations"),
        episodes: v
            .get("episodes")
            .and_then(Value::as_array)
            .map(|a| {
                a.iter()
                    .filter(|x| !x.is_null())
                    .map(episode_from)
                    .collect()
            })
            .unwrap_or_default(),
        chapters: v
            .get("chapters")
            .and_then(Value::as_array)
            .map(|a| {
                a.iter()
                    .filter(|x| !x.is_null())
                    .map(chapter_from)
                    .collect()
            })
            .unwrap_or_default(),
    }
}

fn episode_from(v: &Value) -> Episode {
    Episode {
        id: str_field(v, "id").unwrap_or_default(),
        episode: f64_field(v, "episode").unwrap_or(0.0),
        end_episode: f64_field(v, "endEpisode"),
        title: str_field(v, "title"),
        url: str_field(v, "url"),
        r#type: str_field(v, "type"),
        resource: i64_field(v, "resource"),
        released: i64_field(v, "released"),
        watched: v.get("watched").and_then(Value::as_bool),
    }
}

fn chapter_from(v: &Value) -> Chapter {
    Chapter {
        id: str_field(v, "id").unwrap_or_default(),
        chapter: f64_field(v, "chapter").unwrap_or(0.0),
        end_chapter: f64_field(v, "endChapter"),
        title: str_field(v, "title"),
        released: i64_field(v, "released"),
        view: i64_field(v, "view"),
        read: v.get("read").and_then(Value::as_bool),
        images: v
            .get("images")
            .and_then(Value::as_array)
            .map(|a| {
                a.iter()
                    .filter(|x| !x.is_null())
                    .filter_map(chapter_image_from)
                    .collect()
            })
            .unwrap_or_default(),
    }
}

fn chapter_image_from(v: &Value) -> Option<ChapterImage> {
    let large = str_field(v, "large").filter(|s| !s.is_empty())?;
    Some(ChapterImage {
        large,
        thumbnail: str_field(v, "thumbnail"),
    })
}

fn comment_from(v: &Value) -> Comment {
    let created = i64_field(v, "created")
        .or_else(|| v.get("date").and_then(|d| i64_field(d, "created")))
        .unwrap_or(0);
    Comment {
        id: str_field(v, "id").unwrap_or_default(),
        content: str_field(v, "content").unwrap_or_default(),
        user: v.get("user").filter(|u| !u.is_null()).map(|u| CommentUser {
            username: str_field(u, "username").unwrap_or_default(),
            avatar: str_field(u, "avatar"),
            display_name: str_field(u, "displayName"),
        }),
        created,
        replies: v
            .get("replies")
            .and_then(Value::as_array)
            .map(|rows| {
                rows.iter()
                    .filter(|r| !r.is_null())
                    .map(comment_from)
                    .collect()
            })
            .unwrap_or_default(),
    }
}

fn slide_from(v: &Value) -> Slide {
    Slide {
        id: str_field(v, "id"),
        title: title_field(v, "title"),
        content: description_field(v, "content"),
        link: str_field(v, "link"),
        img: str_field(v, "img"),
    }
}

fn login_user_from(v: &Value) -> LoginUser {
    LoginUser {
        id: str_field(v, "id").unwrap_or_default(),
        username: str_field(v, "username").unwrap_or_default(),
        email: str_field(v, "email"),
        role: str_field(v, "role"),
        token: str_field(v, "token"),
        avatar: str_field(v, "avatar"),
    }
}

fn profile_from(v: &Value) -> Profile {
    Profile {
        id: str_field(v, "id").unwrap_or_default(),
        username: str_field(v, "username").unwrap_or_default(),
        avatar: str_field(v, "avatar"),
        display_name: str_field(v, "displayName"),
        bio: str_field(v, "bio"),
        wallpaper: str_field(v, "wallpaper"),
    }
}

fn mark_entry_from(v: &Value) -> MarkEntry {
    MarkEntry {
        mark_id: str_field(v, "markId"),
        status: str_field(v, "status"),
        media: v.get("media").filter(|m| !m.is_null()).map(media_card_from),
    }
}

fn filters_from(v: &Value) -> Filters {
    Filters {
        years: i64_vec_opt(v, "years"),
        genres: str_vec_opt(v, "genres"),
        studios: str_vec_opt(v, "studios").or_else(|| str_vec_opt(v, "studies")),
        types: str_vec_opt(v, "types"),
        translators: str_vec_opt(v, "translators"),
        dubbers: str_vec_opt(v, "dubbers"),
        editors: str_vec_opt(v, "editors"),
        programmers: str_vec_opt(v, "programmers"),
        audio_engineers: str_vec_opt(v, "audioEngineers"),
        typpers: str_vec_opt(v, "typpers"),
        cleanners: str_vec_opt(v, "cleanners"),
    }
}

fn statistics_from(v: &Value) -> Statistics {
    Statistics {
        anime: i64_field(v, "anime"),
        manga: i64_field(v, "manga"),
        cinema: i64_field(v, "cinema"),
        games: i64_field(v, "games"),
        users: i64_field(v, "users"),
    }
}

fn status_from(v: &Value) -> StatusCounters {
    StatusCounters {
        watching: i64_field(v, "watching"),
        watched: i64_field(v, "watched"),
        dropped: i64_field(v, "dropped"),
        planned: i64_field(v, "planned"),
    }
}

fn schedule_day_from(v: &Value) -> ScheduleDay {
    ScheduleDay {
        day: str_field(v, "day"),
        entities: v
            .get("entities")
            .and_then(Value::as_array)
            .map(|a| {
                a.iter()
                    .filter(|x| !x.is_null())
                    .map(|e| ScheduleEntity {
                        id: str_field(e, "id"),
                        title: str_field(e, "title"),
                        episode: f64_field(e, "episode"),
                        chapter: f64_field(e, "chapter"),
                        media_id: str_field(e, "mediaId"),
                    })
                    .collect()
            })
            .unwrap_or_default(),
    }
}

fn title_field(v: &Value, key: &str) -> Option<Title> {
    let t = v.get(key).filter(|x| x.is_object())?;
    Some(Title {
        ru: str_field(t, "ru"),
        be: str_field(t, "be"),
        en: str_field(t, "en"),
        alt: str_vec_opt(t, "alt"),
    })
}

fn description_field(v: &Value, key: &str) -> Option<Description> {
    let t = v.get(key).filter(|x| x.is_object())?;
    Some(Description {
        ru: str_field(t, "ru"),
        be: str_field(t, "be"),
        en: str_field(t, "en"),
    })
}

fn str_field(v: &Value, key: &str) -> Option<String> {
    v.get(key).and_then(as_plain_str)
}

fn i64_field(v: &Value, key: &str) -> Option<i64> {
    v.get(key).and_then(big_number)
}

fn f64_field(v: &Value, key: &str) -> Option<f64> {
    v.get(key).and_then(|x| match x {
        Value::Number(n) => n.as_f64(),
        Value::String(s) => s.parse().ok(),
        _ => None,
    })
}

fn str_vec_opt(v: &Value, key: &str) -> Option<Vec<String>> {
    v.get(key).and_then(Value::as_array).map(|a| {
        a.iter()
            .filter_map(as_plain_str)
            .filter(|s| !s.is_empty())
            .collect()
    })
}

fn i64_vec_opt(v: &Value, key: &str) -> Option<Vec<i64>> {
    v.get(key)
        .and_then(Value::as_array)
        .map(|a| a.iter().filter_map(big_number).collect())
}

fn as_plain_str(v: &Value) -> Option<String> {
    match v {
        Value::String(s) => Some(s.clone()),
        Value::Number(n) => Some(n.to_string()),
        Value::Object(o) => o
            .get("Other")
            .and_then(Value::as_str)
            .map(str::to_owned)
            .or_else(|| {
                o.iter().next().and_then(|(k, val)| {
                    val.as_str().map(str::to_owned).or_else(|| Some(k.clone()))
                })
            }),
        _ => None,
    }
}

#[cfg(test)]
mod tests;
