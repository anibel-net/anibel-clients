//! Shared application behaviour, independent of the C ABI and native UI.

mod cache;
mod downloads;
mod hls;
mod operations;
mod playback;
mod presentation_data;
mod profile;
mod storage;
#[cfg(test)]
mod tests;
mod user;

use anibel_api::AnibelApi;
use anibel_domain::error::{AnibelError, Result};
use anibel_player::video::VideoService;
use cache::{Cache, ttl};
use serde::{Deserialize, Serialize};
use serde_json::{Value, json};
use std::collections::VecDeque;
use std::path::PathBuf;
use std::sync::{Arc, Mutex, MutexGuard};
use std::time::SystemTime;
use tokio::sync::RwLock;

/// Host intent, not host policy. Rust decides freshness and retention.
#[derive(Clone, Copy, Default, Deserialize, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub enum CacheMode {
    #[default]
    Default,
    Reload,
}

pub struct Application {
    api: AnibelApi,
    storage: Arc<storage::Storage>,
    downloads: Arc<downloads::Downloads>,
    playback: playback::Playback,
    session: Mutex<user::Session>,
    search_history: user::SearchHistory,
    started: tokio::sync::OnceCell<()>,
    video: VideoService,
    cache: Mutex<Cache>,
    /// Reads can run together. Writes wait for all reads and refreshes.
    /// This prevents an old response from restoring data after invalidation.
    requests: Arc<RwLock<()>>,
    events: Mutex<VecDeque<Value>>,
}

/// These operations must observe their backend result before releasing the
/// write gate. Dropping them after server acceptance could leave auth or caches
/// inconsistent. Cancellation still prevents them from starting.
pub(crate) fn must_complete(op: &str) -> bool {
    matches!(
        op,
        "login"
            | "setToken"
            | "logout"
            | "markAs"
            | "removeMark"
            | "addFavorite"
            | "removeFavorite"
            | "addHistoryRecord"
            | "removeHistoryRecord"
            | "addComment"
            | "updateProfile"
            | "setRating"
            | "setFavorite"
            | "setMark"
            | "setWatched"
    )
}

impl Application {
    /// Use one application per storage directory. Omit data_dir for memory-only use.
    pub fn new(base_url: &str, video_base: &str, data_dir: Option<PathBuf>) -> Result<Self> {
        let storage = Arc::new(storage::Storage::new(data_dir.clone())?);
        let api = AnibelApi::with_base(base_url.to_owned());
        let video = VideoService::with_base(video_base.to_owned());
        let mut headers = reqwest::header::HeaderMap::new();
        headers.insert(
            reqwest::header::REFERER,
            reqwest::header::HeaderValue::from_static("https://video.anibel.net/"),
        );
        let http = reqwest::Client::builder()
            .default_headers(headers)
            .timeout(std::time::Duration::from_secs(300))
            .user_agent("Anibel/1.0")
            .build()
            .map_err(storage::http_error)?;
        Ok(Self {
            downloads: Arc::new(downloads::Downloads::new(
                storage.clone(),
                api.clone(),
                video.clone(),
                http,
            )?),
            playback: playback::Playback::new(storage.clone())?,
            search_history: user::SearchHistory::new(storage.clone())?,
            storage,
            api,
            video,
            session: Mutex::new(user::Session::default()),
            started: tokio::sync::OnceCell::new(),
            cache: Mutex::new(Cache::new(data_dir, base_url, video_base)),
            requests: Arc::new(RwLock::new(())),
            events: Mutex::new(VecDeque::new()),
        })
    }

    pub async fn call(self: &Arc<Self>, op: &str, args: &Value, mode: CacheMode) -> Result<Value> {
        self.started
            .get_or_init(|| self.downloads.start_pending())
            .await;
        let mut revision = self.session.lock().unwrap().revision;
        let result = self.execute(op, args, mode, &mut revision).await;
        if matches!(result, Err(AnibelError::Unauthorized)) && op != "login" {
            self.expire_session(revision).await?;
        }
        if let Err(error) = &result {
            self.report_error(error);
        }
        result
    }

    async fn execute(
        self: &Arc<Self>,
        op: &str,
        args: &Value,
        mode: CacheMode,
        revision: &mut u64,
    ) -> Result<Value> {
        if let Some(result) = self.local_operation(op, args).await {
            return result;
        }
        let args = if args.is_null() {
            json!({})
        } else {
            args.clone()
        };
        if ttl(op).is_none() || mode == CacheMode::Reload {
            // Includes session changes, mutations, explicit reload and cache clear.
            // No network await holds the synchronous cache mutex.
            let _write = self.requests.write().await;
            *revision = self.session.lock().unwrap().revision;
            if op == "clearCache" {
                self.cache()
                    .clear()
                    .map_err(|e| AnibelError::Internal(format!("cache clear: {e}")))?;
                return Ok(json!({"status":"ok"}));
            }
            let result = operations::dispatch(self, op, &args).await?;
            match op {
                "login" | "setToken" | "logout" => {
                    let profile = if op == "logout" {
                        None
                    } else if op == "login" {
                        Some(&result)
                    } else {
                        Some(&args)
                    };
                    let token = profile
                        .and_then(|p| p.get("token"))
                        .and_then(Value::as_str)
                        .filter(|t| !t.is_empty());
                    self.cache().set_token(token);
                    let snapshot = {
                        let mut session = self.session.lock().unwrap();
                        session.changed(profile.filter(|_| token.is_some()))?;
                        json!(*session)
                    };
                    self.push_event(json!({"e":"session.changed","session":snapshot}));
                }
                "updateProfile" => {
                    let snapshot = {
                        let mut session = self.session.lock().unwrap();
                        session.changed(Some(&result))?;
                        json!(*session)
                    };
                    self.push_event(json!({"e":"session.changed", "session":snapshot}));
                }
                _ if must_complete(op) => {
                    // A mutation can affect cards, counts, lists and episode state.
                    // One invalidation rule is safer than a partial dependency list.
                    let saved = self.cache().clear();
                    self.report_storage(saved);
                }
                _ => {}
            }
            if ttl(op).is_some() {
                let key = self.cache().key(op, &args);
                self.store(key, result.clone());
            }
            return Ok(result);
        }

        let read = self.requests.clone().read_owned().await;
        *revision = self.session.lock().unwrap().revision;
        let (key, hit) = {
            let cache = self.cache();
            let key = cache.key(op, &args);
            let hit = cache.get(&key, op, SystemTime::now());
            (key, hit)
        };
        if let Some(hit) = hit {
            if hit.stale && self.cache().begin_refresh(&key) {
                let app = self.clone();
                let op = op.to_owned();
                // The owned read guard crosses into the task. A mutation cannot
                // slip between serving stale data and starting its refresh.
                tokio::spawn(async move {
                    let revision = app.session.lock().unwrap().revision;
                    let result = operations::dispatch(&app, &op, &args).await;
                    let unauthorized = matches!(result, Err(AnibelError::Unauthorized));
                    match result {
                        Ok(value) => {
                            app.store(key.clone(), value);
                            app.push_event(json!({"e":"cache.refreshed","op":op}));
                        }
                        Err(e) => app.report_error(&e),
                    }
                    app.cache().end_refresh(&key);
                    drop(read);
                    if unauthorized && let Err(e) = app.expire_session(revision).await {
                        app.report_error(&e);
                    }
                });
            }
            return Ok(hit.value);
        }
        let result = operations::dispatch(self, op, &args).await?;
        self.store(key, result.clone());
        drop(read);
        Ok(result)
    }

    async fn expire_session(&self, revision: u64) -> Result<()> {
        let _write = self.requests.write().await;
        let current = self.session.lock().unwrap().clone();
        if current.revision != revision || !current.authenticated {
            return Ok(());
        }
        self.api.logout().await;
        self.cache().set_token(None);
        let snapshot = {
            let mut session = self.session.lock().unwrap();
            session.changed(None)?;
            json!(*session)
        };
        self.push_event(json!({"e":"session.changed","session":snapshot}));
        Ok(())
    }

    fn cache(&self) -> MutexGuard<'_, Cache> {
        self.cache
            .lock()
            .unwrap_or_else(std::sync::PoisonError::into_inner)
    }

    async fn local_operation(self: &Arc<Self>, op: &str, args: &Value) -> Option<Result<Value>> {
        let result: Result<Value> = match op {
            "selectTracks" => presentation_data::tracks(args),
            "videoQualities" => presentation_data::video_qualities(args),
            "mediaKind" => arg(args, "mediaType").and_then(presentation_data::media_kind),
            "capabilities" => Ok(
                json!({"protocolVersion":2,"features":["downloads","playback","reader","session","cancellation","searchHistory"]}),
            ),
            "session" => Ok(json!(*self.session.lock().unwrap())),
            "downloads" => Ok(self.downloads.list()),
            "downloadEnqueue" => match (
                decode(args.get("kind").unwrap_or(&Value::Null)),
                decode(args.get("request").unwrap_or(&Value::Null)),
            ) {
                (Ok(kind), Ok(request)) => self.downloads.enqueue(kind, request).await,
                (Err(e), _) | (_, Err(e)) => Err(e),
            },
            "downloadChange" => match (arg(args, "id"), arg(args, "action")) {
                (Ok(id), Ok(action)) => self.downloads.change(id, action).await,
                (Err(e), _) | (_, Err(e)) => Err(e),
            },
            "downloadExport" => match (arg(args, "id"), arg(args, "destination")) {
                (Ok(id), Ok(dest)) => self.downloads.export(id, std::path::Path::new(dest)).await,
                (Err(e), _) | (_, Err(e)) => Err(e),
            },
            "playbackOpen" => match decode(args) {
                Ok(open) => {
                    let revision = self.session.lock().unwrap().revision;
                    self.playback
                        .open(open, &self.downloads, &self.video, revision)
                        .await
                }
                Err(e) => Err(e),
            },
            "playbackReport" => match decode(args).and_then(|r| self.playback.report(r)) {
                Ok(action) => {
                    if let Some((id, revision)) = action.history.clone() {
                        let app = self.clone();
                        tokio::spawn(async move {
                            app.submit_history(&id, "episode", revision).await;
                        });
                    }
                    Ok(json!(action))
                }
                Err(e) => Err(e),
            },
            "clearPlaybackAssets" => self
                .playback
                .clear_assets()
                .await
                .map(|_| json!({"status":"ok"})),
            "readerOpen" => self.open_reader(args).await,
            "searchHistory" => self.search_history.command(
                args.get("action").and_then(Value::as_str).unwrap_or("list"),
                args.get("query").and_then(Value::as_str).unwrap_or(""),
            ),
            _ => return None,
        };
        Some(result)
    }

    async fn submit_history(&self, id: &str, kind: &str, revision: u64) {
        let _read = self.requests.write().await;
        let current = self.session.lock().unwrap().clone();
        if !current.authenticated || current.revision != revision {
            return;
        }
        match self.api.add_history_record(id, kind).await {
            Ok(()) => {
                let saved = self.cache().clear();
                self.report_storage(saved);
            }
            Err(e) => {
                self.report_error(&e);
                drop(_read);
                if matches!(e, AnibelError::Unauthorized) {
                    let _ = self.expire_session(revision).await;
                }
            }
        }
    }

    async fn open_reader(self: &Arc<Self>, args: &Value) -> Result<Value> {
        let slug = arg(args, "slug")?;
        let number = args
            .get("chapter")
            .and_then(Value::as_f64)
            .filter(|n| n.is_finite() && *n >= 0.0)
            .ok_or_else(|| AnibelError::BadArgs("chapter required".into()))?;
        let revision = self.session.lock().unwrap().revision;
        let (previous, next) = presentation_data::chapter_neighbors(args, number)?;
        let chapter = if let Some(local) = self.downloads.find_chapter(slug, number) {
            let images = local
                .assets
                .image_paths
                .iter()
                .map(|p| {
                    let absolute = self.storage.path(p)?;
                    let url = url::Url::from_file_path(absolute)
                        .map_err(|_| AnibelError::Internal("invalid local image path".into()))?;
                    Ok(anibel_domain::models::ChapterImage {
                        large: url.into(),
                        thumbnail: None,
                    })
                })
                .collect::<Result<Vec<_>>>()?;
            anibel_domain::models::Chapter {
                id: local.request.chapter_id.unwrap_or_default(),
                chapter: number,
                title: local.request.chapter_title,
                images,
                ..Default::default()
            }
        } else {
            let read = self.requests.read().await;
            let revision = self.session.lock().unwrap().revision;
            let result = self.api.chapter(slug, number).await;
            drop(read);
            if matches!(result, Err(AnibelError::Unauthorized)) {
                self.expire_session(revision).await?;
            }
            result?
        };
        if chapter.images.is_empty() {
            return Err(AnibelError::NotFound("chapter pages".into()));
        }
        if !chapter.id.is_empty() {
            let app = self.clone();
            let id = chapter.id.clone();
            tokio::spawn(async move {
                app.submit_history(&id, "chapter", revision).await;
            });
        }
        Ok(json!({"chapter":chapter,"previous":previous,"next":next}))
    }

    fn store(&self, key: String, value: Value) {
        let result = self.cache().set(key, value, SystemTime::now());
        self.report_storage(result);
    }

    fn report_storage(&self, result: std::io::Result<()>) {
        if let Err(error) = result {
            self.push_event(json!({"e":"cache.storageError", "message":error.to_string()}));
        }
    }

    fn report_error(&self, error: &AnibelError) {
        self.push_event(json!({"e":"error", "detail":error.to_dto()}));
    }

    pub fn push_event(&self, event: Value) {
        let mut events = self
            .events
            .lock()
            .unwrap_or_else(std::sync::PoisonError::into_inner);
        // Notifications are bounded. Hosts recover state from snapshots.
        if event["e"] == "session.changed" {
            events.retain(|e| e["e"] != "session.changed");
        }
        if events.len() == 1024 {
            let oldest = events
                .iter()
                .position(|e| e["e"] != "session.changed")
                .unwrap_or(0);
            events.remove(oldest);
        }
        events.push_back(event);
    }

    pub fn drain_events(&self) -> Vec<Value> {
        self.events
            .lock()
            .unwrap_or_else(std::sync::PoisonError::into_inner)
            .drain(..)
            .collect()
    }
}

fn decode<T: serde::de::DeserializeOwned>(value: &Value) -> Result<T> {
    serde_json::from_value(value.clone()).map_err(|e| AnibelError::BadArgs(e.to_string()))
}
fn arg<'a>(value: &'a Value, key: &str) -> Result<&'a str> {
    value
        .get(key)
        .and_then(Value::as_str)
        .filter(|s| !s.is_empty())
        .ok_or_else(|| AnibelError::BadArgs(format!("{key} required")))
}
