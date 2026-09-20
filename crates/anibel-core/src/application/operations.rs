//! API operation mapping. Application policy is in the parent module.

use crate::protocol::Command;

use super::Application;
use anibel_api::media_list_filters_from_json;
use anibel_domain::error::AnibelError;
use anibel_player::player;
use serde::Serialize;
use serde_json::{Value, json};

fn field_str(args: &Value, name: &str) -> Result<String, AnibelError> {
    args.get(name)
        .and_then(Value::as_str)
        .map(str::to_owned)
        .ok_or_else(|| AnibelError::BadArgs(format!("`{name}` required")))
}

fn field_str_vec(args: &Value, name: &str) -> Result<Vec<String>, AnibelError> {
    args.get(name)
        .and_then(Value::as_array)
        .map(|a| {
            a.iter()
                .filter_map(Value::as_str)
                .map(str::to_owned)
                .collect::<Vec<_>>()
        })
        .ok_or_else(|| AnibelError::BadArgs(format!("`{name}` required (string array)")))
}

fn domain_value<T: Serialize>(v: T) -> Result<Value, AnibelError> {
    serde_json::to_value(v).map_err(|e| AnibelError::Internal(e.to_string()))
}

pub(super) async fn dispatch(
    state: &Application,
    op: Command,
    args: &Value,
) -> Result<Value, AnibelError> {
    let api = &state.api;
    match op {
        Command::ContinueEpisode => state.continue_episode(args).await,
        Command::ContinueChapter => state.continue_chapter(args).await,
        Command::SetRating => {
            if !state.session.lock().unwrap().authenticated {
                return Err(AnibelError::Unauthorized);
            }
            let id = field_str(args, "mediaId")?;
            let kind = field_str(args, "mediaType")?;
            super::presentation_data::media_kind(&kind)?;
            let rating = args
                .get("rating")
                .and_then(Value::as_f64)
                .filter(|v| v.is_finite() && (1.0..=10.0).contains(v) && v.fract() == 0.0)
                .ok_or_else(|| {
                    AnibelError::BadArgs("Rating must be a whole number from 1 to 10".into())
                })?;
            api.set_rating(&id, &kind, rating).await?;
            Ok(json!({"rating":rating}))
        }
        Command::SetFavorite => {
            let id = field_str(args, "mediaId")?;
            let kind = field_str(args, "mediaType")?;
            super::presentation_data::media_kind(&kind)?;
            let selected = args
                .get("selected")
                .and_then(Value::as_bool)
                .ok_or_else(|| AnibelError::BadArgs("selected required".into()))?;
            if selected {
                api.add_favorite(&id, &kind).await?;
            } else {
                api.remove_favorite(&id, &kind).await?;
            }
            Ok(json!({"selected":selected}))
        }
        Command::SetMark => {
            let id = field_str(args, "mediaId")?;
            let kind = field_str(args, "mediaType")?;
            let choices = super::presentation_data::media_kind(&kind)?;
            let status = field_str(args, "status")?;
            let current = args
                .get("current")
                .and_then(Value::as_str)
                .filter(|s| !s.is_empty())
                .unwrap_or("notselected");
            if !choices["marks"]
                .as_array()
                .unwrap()
                .contains(&json!(status))
            {
                return Err(AnibelError::BadArgs("invalid mark for media type".into()));
            }
            if status != current {
                if status == "notselected" {
                    api.remove_mark(&id, &kind, current).await?;
                } else {
                    api.mark_as(&id, &kind, &status).await?;
                }
            }
            Ok(json!({"mark":if status=="notselected" {Value::Null}else{json!({"status":status})}}))
        }
        Command::SetWatched => {
            let id = field_str(args, "entityId")?;
            let selected = args
                .get("selected")
                .and_then(Value::as_bool)
                .ok_or_else(|| AnibelError::BadArgs("selected required".into()))?;
            if selected {
                api.add_history_record(&id, "episode").await?;
            } else {
                api.remove_history_record(&id, "episode").await?;
            }
            Ok(json!({"selected":selected}))
        }
        Command::EpisodeChoices => {
            let id = field_str(args, "mediaId")?;
            let key = state.cache().key("episodesMatrix", &json!({"mediaId":id}));
            let hit = state
                .cache()
                .get(&key, "episodesMatrix", std::time::SystemTime::now())
                .filter(|h| !h.stale);
            let episodes = if let Some(hit) = hit {
                super::decode(&hit.value)?
            } else {
                let rows = state.api.episodes_matrix(&id).await?;
                state.store(key, json!(rows));
                rows
            };
            Ok(super::presentation_data::episodes(
                episodes,
                args.get("kind").and_then(Value::as_str).unwrap_or("dub"),
                args.get("resource").and_then(Value::as_i64),
            ))
        }
        Command::ProfileHub => state.profile_hub().await,
        Command::PersonalList => {
            state
                .personal_list(super::decode(args.get("kind").unwrap_or(&Value::Null))?)
                .await
        }
        Command::Version => {
            Ok(json!({ "name": "anibel-core", "version": env!("CARGO_PKG_VERSION") }))
        }
        Command::Health => Ok(json!({ "status": "ok" })),

        Command::Login => {
            let username: String = field_str(args, "username")?;
            let password: String = field_str(args, "password")?;
            let user = api.login(&username, &password).await?;
            domain_value(user)
        }
        Command::Logout => {
            api.logout().await;
            Ok(json!({ "status": "ok" }))
        }
        Command::SetToken => {
            let token: Option<String> =
                args.get("token").and_then(Value::as_str).map(str::to_owned);
            if token.as_ref().is_some_and(|t| t.trim().is_empty()) {
                return Err(AnibelError::BadArgs("token must not be empty".into()));
            }
            api.set_token(token).await;
            Ok(json!({ "status": "ok" }))
        }
        Command::MarkAs => {
            let media_id: String = field_str(args, "mediaId")?;
            let media_type: String = field_str(args, "mediaType")?;
            let status: String = field_str(args, "status")?;
            api.mark_as(&media_id, &media_type, &status).await?;
            Ok(json!({ "status": "ok" }))
        }
        Command::RemoveMark => {
            let media_id: String = field_str(args, "mediaId")?;
            let media_type: String = field_str(args, "mediaType")?;
            let status: String = field_str(args, "status")?;
            api.remove_mark(&media_id, &media_type, &status).await?;
            Ok(json!({ "status": "ok" }))
        }
        Command::AddFavorite => {
            let media_id: String = field_str(args, "mediaId")?;
            let media_type: String = field_str(args, "mediaType")?;
            api.add_favorite(&media_id, &media_type).await?;
            Ok(json!({ "status": "ok" }))
        }
        Command::RemoveFavorite => {
            let media_id: String = field_str(args, "mediaId")?;
            let media_type: String = field_str(args, "mediaType")?;
            api.remove_favorite(&media_id, &media_type).await?;
            Ok(json!({ "status": "ok" }))
        }
        Command::AddHistoryRecord => {
            let entity_id: String = field_str(args, "entityId")?;
            let history_type: String = args
                .get("type")
                .and_then(Value::as_str)
                .unwrap_or("episode")
                .to_string();
            api.add_history_record(&entity_id, &history_type).await?;
            Ok(json!({ "status": "ok" }))
        }
        Command::RemoveHistoryRecord => {
            let entity_id: String = field_str(args, "entityId")?;
            let history_type: String = args
                .get("type")
                .and_then(Value::as_str)
                .unwrap_or("episode")
                .to_string();
            api.remove_history_record(&entity_id, &history_type).await?;
            Ok(json!({ "status": "ok" }))
        }
        Command::Me => {
            // `me` is absent on production schema; user(username) is the fallback.
            let username = field_str(args, "username")?;
            let profile = api.user(&username).await?;
            domain_value(profile)
        }

        Command::Search => {
            let query: String = field_str(args, "query")?;
            let limit = args.get("limit").and_then(Value::as_i64);
            let out = api.search(&query, limit.unwrap_or(20)).await?;
            domain_value(out)
        }
        Command::Media => {
            let slug: String = field_str(args, "slug")?;
            let media_type = args
                .get("mediaType")
                .and_then(Value::as_str)
                .map(str::to_owned);
            let out = api.media(&slug, media_type).await?;
            domain_value(out)
        }
        Command::MediaList => {
            let args = args.clone();
            let media_type = args
                .get("mediaType")
                .and_then(Value::as_str)
                .ok_or_else(|| AnibelError::BadArgs("mediaType required".into()))?
                .to_string();
            let offset = args.get("offset").and_then(Value::as_i64).unwrap_or(0);
            let limit = args.get("limit").and_then(Value::as_i64).unwrap_or(20);
            super::presentation_data::page_cursor(offset, 0, limit, None)?;
            let filters = args.get("filters").map(media_list_filters_from_json);
            let out = api.media_list(&media_type, offset, limit, filters).await?;
            let mut value = domain_value(out)?;
            let count = value["docs"].as_array().map_or(0, Vec::len);
            let (next, more) = super::presentation_data::page_cursor(
                offset,
                count,
                limit,
                value["totalDocs"].as_i64(),
            )?;
            value["nextOffset"] = json!(next);
            value["hasMore"] = json!(more);
            Ok(value)
        }
        Command::Episodes => {
            let media_id: String = args
                .get("mediaId")
                .and_then(Value::as_str)
                .ok_or_else(|| AnibelError::BadArgs("mediaId required".into()))?
                .to_string();
            let r#type = args
                .get("type")
                .and_then(Value::as_str)
                .unwrap_or("sub")
                .to_string();
            let resource = args.get("resource").and_then(Value::as_i64).unwrap_or(1);
            // null limit => server pagination returns 0 docs; default to site behaviour (100)
            let limit = args.get("limit").and_then(Value::as_i64).unwrap_or(100);
            let out = api
                .episodes(&media_id, &r#type, resource, Some(limit))
                .await?;
            domain_value(out)
        }
        Command::EpisodesMatrix => {
            let media_id: String = args
                .get("mediaId")
                .and_then(Value::as_str)
                .ok_or_else(|| AnibelError::BadArgs("mediaId required".into()))?
                .to_string();
            let out = api.episodes_matrix(&media_id).await?;
            domain_value(out)
        }
        Command::Chapters => {
            let media_id: String = args
                .get("mediaId")
                .and_then(Value::as_str)
                .ok_or_else(|| AnibelError::BadArgs("mediaId required".into()))?
                .to_string();
            let limit = args.get("limit").and_then(Value::as_i64).unwrap_or(500);
            let out = api.chapters(&media_id, Some(limit)).await?;
            domain_value(out)
        }
        Command::Chapter => {
            let slug: String = args
                .get("slug")
                .and_then(Value::as_str)
                .ok_or_else(|| AnibelError::BadArgs("slug required".into()))?
                .to_string();
            let chapter: f64 = args
                .get("chapter")
                .and_then(Value::as_f64)
                .ok_or_else(|| AnibelError::BadArgs("chapter required".into()))?;
            let out = api.chapter(&slug, chapter).await?;
            domain_value(out)
        }
        Command::Profile => {
            state
                .profile_view(args.get("username").and_then(Value::as_str))
                .await
        }
        Command::UpdateProfile => state.update_profile(args).await,
        Command::Comments => {
            let media_id: String = args
                .get("mediaId")
                .and_then(Value::as_str)
                .ok_or_else(|| AnibelError::BadArgs("mediaId required".into()))?
                .to_string();
            let media_type = args
                .get("mediaType")
                .and_then(Value::as_str)
                .unwrap_or("anime")
                .to_string();
            let offset = args.get("offset").and_then(Value::as_i64).unwrap_or(0);
            let limit = args.get("limit").and_then(Value::as_i64).unwrap_or(20);
            let out = api.comments(&media_id, &media_type, offset, limit).await?;
            domain_value(out)
        }
        Command::AddComment => {
            let media_id: String = field_str(args, "mediaId")?;
            let media_type: String = field_str(args, "mediaType")?;
            let content: String = field_str(args, "content")?;
            let content = content.trim();
            if content.is_empty() || content.chars().count() > 2000 {
                return Err(AnibelError::BadArgs(
                    "Comment must contain 1 to 2000 characters".into(),
                ));
            }
            let reply_to = args
                .get("replyTo")
                .and_then(Value::as_str)
                .map(str::trim)
                .filter(|s| !s.is_empty())
                .map(str::to_owned);
            let out = api
                .add_comment(&media_id, &media_type, content, reply_to.as_deref())
                .await?;
            domain_value(out)
        }
        Command::Trends => {
            let r#type = args
                .get("type")
                .and_then(Value::as_str)
                .unwrap_or("all")
                .to_string();
            let date = args
                .get("date")
                .and_then(Value::as_str)
                .unwrap_or("week")
                .to_string();
            let limit = args.get("limit").and_then(Value::as_i64);
            let out = api.trends(&r#type, &date, limit).await?;
            domain_value(out)
        }
        Command::Updates | Command::UpdatesPage => {
            let r#type = args
                .get("type")
                .and_then(Value::as_str)
                .unwrap_or("ALL")
                .to_string();
            let offset = args.get("offset").and_then(Value::as_i64).unwrap_or(0);
            let limit = args.get("limit").and_then(Value::as_i64).unwrap_or(20);
            super::presentation_data::page_cursor(offset, 0, limit, None)?;
            let out = api.updates(&r#type, offset, limit).await?;
            if op == Command::UpdatesPage {
                let (next, more) =
                    super::presentation_data::page_cursor(offset, out.len(), limit, None)?;
                Ok(json!({"docs":out,"nextOffset":next,"hasMore":more}))
            } else {
                domain_value(out)
            }
        }
        Command::Recommendations => {
            let r#type = args
                .get("type")
                .and_then(Value::as_str)
                .unwrap_or("all")
                .to_string();
            let limit = args.get("limit").and_then(Value::as_i64).unwrap_or(10);
            let out = api.recommendations(&r#type, limit).await?;
            domain_value(out)
        }
        Command::Schedule => {
            let out = api.schedule().await?;
            domain_value(out)
        }
        Command::Slider => {
            let limit = args.get("limit").and_then(Value::as_i64).unwrap_or(6);
            let out = api.slider(limit).await?;
            domain_value(out)
        }
        Command::Filters => {
            let media_type = args
                .get("mediaType")
                .and_then(Value::as_str)
                .map(str::to_string);
            let out = api.filters(media_type).await?;
            domain_value(out)
        }
        Command::Statistics => {
            let out = api.statistics().await?;
            domain_value(out)
        }
        Command::Random => {
            let out = api.random_media().await?;
            domain_value(out)
        }
        Command::User => {
            let username = field_str(args, "username")?;
            let out = api.user(&username).await?;
            domain_value(out)
        }
        Command::Favorites => {
            let username: String = args
                .get("username")
                .and_then(Value::as_str)
                .ok_or_else(|| AnibelError::BadArgs("username required".into()))?
                .to_string();
            let media_type = args
                .get("mediaType")
                .and_then(Value::as_str)
                .map(str::to_string);
            let offset = args.get("offset").and_then(Value::as_i64).unwrap_or(0);
            let limit = args.get("limit").and_then(Value::as_i64).unwrap_or(20);
            let out = api.favorites(&username, media_type, offset, limit).await?;
            domain_value(out)
        }
        Command::Marks => {
            let username: String = args
                .get("username")
                .and_then(Value::as_str)
                .ok_or_else(|| AnibelError::BadArgs("username required".into()))?
                .to_string();
            let media_type = args
                .get("mediaType")
                .and_then(Value::as_str)
                .map(str::to_string);
            let offset = args.get("offset").and_then(Value::as_i64).unwrap_or(0);
            let limit = args.get("limit").and_then(Value::as_i64).unwrap_or(20);
            let out = api.marks(&username, media_type, offset, limit).await?;
            domain_value(out)
        }
        Command::Status => {
            let username: String = args
                .get("username")
                .and_then(Value::as_str)
                .ok_or_else(|| AnibelError::BadArgs("username required".into()))?
                .to_string();
            let media_type: String = args
                .get("mediaType")
                .and_then(Value::as_str)
                .ok_or_else(|| AnibelError::BadArgs("mediaType required".into()))?
                .to_string();
            let out = api.status(&username, &media_type).await?;
            domain_value(out)
        }
        Command::ResolveEpisode => {
            let intent = player::resolve_intent(&state.video, args).await?;
            domain_value(intent)
        }
        Command::VideoInfo => {
            let video_id = field_str(args, "videoId")?;
            let info = state
                .video
                .get(&video_id)
                .await
                .map_err(|e| AnibelError::Transport(format!("video service: {e}")))?;
            domain_value(info)
        }
        Command::FontAssets => {
            let names = field_str_vec(args, "names")?;
            let urls = state
                .video
                .fonts_by_names(&names)
                .await
                .map_err(|e| AnibelError::Transport(format!("video service: {e}")))?;
            domain_value(urls)
        }

        _ => Err(AnibelError::BadArgs(format!(
            "unsupported API command `{op:?}`"
        ))),
    }
}
