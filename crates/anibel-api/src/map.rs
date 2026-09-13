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

pub fn json(v: impl Serialize) -> Value {
    serde_json::to_value(v).unwrap_or(Value::Null)
}

pub fn media_card(v: impl Serialize) -> MediaCard {
    media_card_from(&json(v))
}

pub fn media_cards_opt<T: Serialize>(items: impl IntoIterator<Item = Option<T>>) -> Vec<MediaCard> {
    items.into_iter().flatten().map(media_card).collect()
}

pub fn media_detail(v: impl Serialize) -> MediaDetail {
    media_detail_from(&json(v))
}

pub fn media_detail_opt<T: Serialize>(v: Option<T>) -> Option<MediaDetail> {
    v.map(media_detail)
}

pub fn page_media(v: impl Serialize) -> Page<MediaCard> {
    page_from(&json(v), media_card_from)
}

pub fn page_episodes(v: impl Serialize) -> Page<Episode> {
    page_from(&json(v), episode_from)
}

pub fn page_chapters(v: impl Serialize) -> Page<Chapter> {
    page_from(&json(v), chapter_from)
}

pub fn page_comments(v: impl Serialize) -> Page<Comment> {
    page_from(&json(v), comment_from)
}

pub fn comment(v: impl Serialize) -> Comment {
    comment_from(&json(v))
}

pub fn page_marks(v: impl Serialize) -> Page<MarkEntry> {
    page_from(&json(v), mark_entry_from)
}

pub fn chapter(v: impl Serialize) -> Chapter {
    chapter_from(&json(v))
}

pub fn slide(v: impl Serialize) -> Slide {
    slide_from(&json(v))
}

pub fn slides_opt<T: Serialize>(items: impl IntoIterator<Item = Option<T>>) -> Vec<Slide> {
    items.into_iter().flatten().map(slide).collect()
}

pub fn login_user(v: impl Serialize) -> LoginUser {
    login_user_from(&json(v))
}

pub fn profile(v: impl Serialize) -> Profile {
    profile_from(&json(v))
}

pub fn profile_opt<T: Serialize>(v: Option<T>) -> Option<Profile> {
    v.map(profile)
}

pub fn filters(v: impl Serialize) -> Filters {
    filters_from(&json(v))
}

pub fn statistics(v: impl Serialize) -> Statistics {
    statistics_from(&json(v))
}

pub fn status(v: impl Serialize) -> StatusCounters {
    status_from(&json(v))
}

pub fn schedule_days_opt<T: Serialize>(
    items: impl IntoIterator<Item = Option<T>>,
) -> Vec<ScheduleDay> {
    items.into_iter().flatten().map(schedule_day).collect()
}

pub fn schedule_day(v: impl Serialize) -> ScheduleDay {
    schedule_day_from(&json(v))
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
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn flattens_comment_date_created() {
        let c = comment_from(&json!({
            "id": "c1",
            "content": "hi",
            "user": { "username": "ann", "avatar": null, "displayName": "Ann" },
            "date": { "created": 1700000000000u64, "updated": "1700000001000" }
        }));
        assert_eq!(c.id, "c1");
        assert_eq!(c.created, 1_700_000_000_000);
        assert_eq!(c.user.as_ref().unwrap().username, "ann");
        let wire = serde_json::to_value(&c).unwrap();
        assert!(wire.get("date").is_none());
        assert_eq!(wire["created"], 1_700_000_000_000u64);
    }

    #[test]
    fn parses_bignumber_string_on_episode() {
        let ep = episode_from(&json!({
            "id": "e1",
            "episode": 13,
            "url": "https://video.anibel.net/abc",
            "type": "sub",
            "resource": 2,
            "released": "1700000000000"
        }));
        assert_eq!(ep.released, Some(1_700_000_000_000));
    }

    #[test]
    fn maps_studies_to_studios() {
        let f = filters_from(&json!({
            "years": [2024, "2023"],
            "genres": ["драма", null],
            "studies": ["studio a"]
        }));
        assert_eq!(f.years, Some(vec![2024, 2023]));
        assert_eq!(f.genres.as_ref().unwrap()[0], "драма");
        assert_eq!(f.studios, Some(vec!["studio a".into()]));
        let wire = serde_json::to_value(&f).unwrap();
        assert!(wire.get("studies").is_none());
        assert_eq!(wire["studios"][0], "studio a");
    }

    #[test]
    fn media_card_ignores_graphql_extras() {
        let card = media_card_from(&json!({
            "mediaId": "1",
            "mediaType": "anime",
            "slug": "foo",
            "title": { "be": "Т", "ru": "Р", "en": null },
            "poster": "https://x",
            "year": 2020,
            "rating": 8.5,
            "genres": ["a"],
            "hidden": false,
            "markStats": { "total": 1 }
        }));
        assert_eq!(card.slug, "foo");
        assert_eq!(card.title.unwrap().be.as_deref(), Some("Т"));
        let wire = serde_json::to_value(media_card_from(&json!({
            "mediaId": "1", "mediaType": "anime", "slug": "foo"
        })))
        .unwrap();
        assert!(wire.get("hidden").is_none());
        assert!(wire.get("markStats").is_none());
    }

    #[test]
    fn media_card_maps_status_language_and_update() {
        let card = media_card_from(&json!({
            "mediaId": "1",
            "mediaType": "anime",
            "slug": "foo",
            "status": "ongoing",
            "language": ["sub", "dub"],
            "updateType": "DUB",
            "num": 12,
            "year": 2024
        }));
        assert_eq!(card.status.as_deref(), Some("ongoing"));
        assert_eq!(
            card.language.as_ref().unwrap(),
            &vec!["sub".to_string(), "dub".to_string()]
        );
        assert_eq!(card.update_type.as_deref(), Some("DUB"));
        assert_eq!(card.num, Some(12));
        assert_eq!(card.year, Some(2024));
    }

    #[test]
    fn media_detail_maps_franchise_relations_recommendations() {
        let detail = media_detail_from(&json!({
            "mediaId": "1",
            "mediaType": "anime",
            "slug": "death-note",
            "franchise": "Death Note",
            "relations": [
                { "mediaId": "1", "mediaType": "anime", "slug": "death-note" },
                { "mediaId": "2", "mediaType": "anime", "slug": "death-note-rewrite" }
            ],
            "recommendations": [
                { "mediaId": "3", "mediaType": "anime", "slug": "monster" },
                { "mediaId": "4", "mediaType": "anime", "slug": "hidden-rec", "hidden": true }
            ]
        }));
        assert_eq!(detail.franchise.as_deref(), Some("Death Note"));
        assert_eq!(detail.relations.len(), 2);
        assert_eq!(detail.relations[1].slug, "death-note-rewrite");
        assert_eq!(detail.recommendations.len(), 1);
        assert_eq!(detail.recommendations[0].slug, "monster");
    }
}
