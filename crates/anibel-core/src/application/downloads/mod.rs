mod export;
mod model;
mod progress;
#[cfg(test)]
mod tests;
mod transfer;
use super::{
    hls,
    storage::{Storage, digest, extension, fetch_file, io_error},
};
use anibel_api::AnibelApi;
use anibel_domain::error::{AnibelError, Result};
use anibel_player::{player, video::VideoService};
pub(super) use model::{Assets, Kind, Record, Request, Status, VideoFormat};
use model::{Change, LiveProgress};

use serde_json::{Value, json};
use std::{
    collections::HashMap,
    path::Path,
    sync::{Arc, Mutex},
    time::{Instant, SystemTime, UNIX_EPOCH},
};
use tokio::sync::{Mutex as AsyncMutex, Semaphore};
use tokio_util::sync::CancellationToken;

struct Work {
    cancel: CancellationToken,
    task: AsyncMutex<Option<tokio::task::JoinHandle<()>>>,
}
pub(super) struct Downloads {
    pub storage: Arc<Storage>,
    api: AnibelApi,
    video: VideoService,
    http: reqwest::Client,
    records: Mutex<Vec<Record>>,
    work: Mutex<HashMap<String, Arc<Work>>>,
    operations: AsyncMutex<()>,
    worker: Semaphore,
    execution_enabled: Mutex<bool>,
}
impl Downloads {
    pub fn new(
        storage: Arc<Storage>,
        api: AnibelApi,
        video: VideoService,
        http: reqwest::Client,
    ) -> Result<Self> {
        let mut records: Vec<Record> = storage.load("downloads.json")?;
        if records.len() > 2048 {
            return Err(AnibelError::Internal(
                "download library exceeds capacity".into(),
            ));
        }
        let mut ids = std::collections::HashSet::new();
        for r in &mut records {
            Self::identity(r.kind, &r.request)?;
            if !ids.insert(r.id.clone()) {
                return Err(AnibelError::Internal("duplicate download id".into()));
            }
            if r.status.active() {
                r.status = Status::Queued;
            }
            for path in r.assets.all() {
                if !Path::new(path).starts_with(Self::folder(&r.id)) {
                    return Err(AnibelError::Internal(
                        "asset outside download folder".into(),
                    ));
                }
                storage.path(path)?;
            }
        }
        Ok(Self {
            storage,
            api,
            video,
            http,
            records: Mutex::new(records),
            work: Mutex::new(HashMap::new()),
            operations: AsyncMutex::new(()),
            worker: Semaphore::new(1),
            execution_enabled: Mutex::new(!cfg!(target_os = "android")),
        })
    }
    /// The host grants execution time. Suspension drains transfers and preserves queued work.
    pub async fn set_execution(self: &Arc<Self>, enabled: bool) -> Result<()> {
        let _op = self.operations.lock().await;
        *self.execution_enabled.lock().unwrap() = enabled;
        let ids: Vec<_> = self
            .records
            .lock()
            .unwrap()
            .iter()
            .filter(|r| r.status.active())
            .map(|r| r.id.clone())
            .collect();
        if enabled {
            for id in ids {
                self.spawn(id);
            }
        } else {
            for id in &ids {
                self.stop(id).await?;
            }
            let mut records = self.records.lock().unwrap();
            let mut next = records.clone();
            for r in &mut next {
                if ids.contains(&r.id) && (r.status.active() || r.status == Status::Cancelled) {
                    r.status = Status::Queued;
                    r.live = LiveProgress::default();
                }
            }
            self.save(&next)?;
            *records = next;
        }
        Ok(())
    }
    pub async fn start_pending(self: &Arc<Self>) {
        let _op = self.operations.lock().await;
        let ids: Vec<_> = self
            .records
            .lock()
            .unwrap()
            .iter()
            .filter(|r| r.status == Status::Queued)
            .map(|r| r.id.clone())
            .collect();
        for id in ids {
            self.spawn(id);
        }
    }
    fn folder(id: &str) -> String {
        format!("downloads/{}", digest(id))
    }
    pub fn root(&self) -> String {
        self.storage.root.join("downloads").to_string_lossy().into()
    }
    fn save(&self, records: &[Record]) -> Result<()> {
        self.storage.save("downloads.json", &records)
    }
    pub fn list(&self) -> Value {
        json!({"root":self.root(),"items":self.records.lock().unwrap().iter().map(|r|self.snapshot(r)).collect::<Vec<_>>()})
    }
    pub fn find_episode(&self, id: &str, audio: bool) -> Option<Record> {
        self.records
            .lock()
            .unwrap()
            .iter()
            .find(|r| {
                r.request.episode_id.as_deref() == Some(id)
                    && r.kind == if audio { Kind::Audio } else { Kind::Video }
                    && self.available(r)
            })
            .cloned()
    }
    pub fn find_chapter(&self, slug: &str, chapter: f64) -> Option<Record> {
        self.records
            .lock()
            .unwrap()
            .iter()
            .find(|r| {
                r.kind == Kind::Manga
                    && r.request.slug == slug
                    && r.request.chapter == Some(chapter)
                    && self.available(r)
            })
            .cloned()
    }
    pub fn available(&self, r: &Record) -> bool {
        r.status == Status::Completed
            && !r.assets.all().is_empty()
            && r.assets
                .all()
                .iter()
                .all(|p| self.storage.path(p).is_ok_and(|p| p.is_file()))
            && match r.kind {
                Kind::Video => r.assets.video_path.is_some(),
                Kind::Audio => r.assets.audio_path.is_some(),
                Kind::Manga => !r.assets.image_paths.is_empty(),
                Kind::File => r.assets.file_path.is_some(),
            }
    }
    pub fn absolute(&self, path: &str) -> Result<String> {
        Ok(self.storage.path(path)?.to_string_lossy().into())
    }
    fn identity(kind: Kind, request: &Request) -> Result<String> {
        if request.title.len() > 4096
            || request.chapter_list.len() > 100_000
            || serde_json::to_vec(request)
                .map_err(|e| AnibelError::BadArgs(e.to_string()))?
                .len()
                > 1024 * 1024
        {
            return Err(AnibelError::BadArgs(
                "download metadata exceeds bounds".into(),
            ));
        }
        let identity = match kind {
            Kind::Video | Kind::Audio => {
                required(&request.episode_url, "episodeUrl")?;
                format!(
                    "{:?}:{}{}",
                    kind,
                    required(&request.episode_id, "episodeId")?,
                    if kind == Kind::Video && request.video_format == VideoFormat::Mkv {
                        ":mkv"
                    } else {
                        ""
                    }
                )
            }
            Kind::Manga => {
                let ch = request
                    .chapter
                    .filter(|c| c.is_finite() && *c >= 0.0)
                    .ok_or_else(|| AnibelError::BadArgs("chapter required".into()))?;
                if request.slug.is_empty() {
                    return Err(AnibelError::BadArgs("slug required".into()));
                }
                format!("chapter:{}:{ch}", request.slug)
            }
            Kind::File => {
                required(&request.file_url, "fileUrl")?;
                if request.media_id.is_empty() {
                    return Err(AnibelError::BadArgs("mediaId required".into()));
                }
                format!("file:{}:{}", request.media_type, request.media_id)
            }
        };
        Ok(identity)
    }
    pub async fn enqueue(self: &Arc<Self>, kind: Kind, request: Request) -> Result<Value> {
        if cfg!(target_os = "android")
            && kind == Kind::Video
            && request.video_format == VideoFormat::Mkv
        {
            return Err(AnibelError::BadArgs(
                "MKV downloads are not supported on this host".into(),
            ));
        }
        let identity = Self::identity(kind, &request)?;
        let _op = self.operations.lock().await;
        let id = digest(&identity);
        {
            let mut records = self.records.lock().unwrap();
            if let Some(r) = records.iter().find(|r| r.id == id) {
                return Ok(self.snapshot(r));
            }
            if records.len() >= 2048 {
                return Err(AnibelError::BadArgs("download library is full".into()));
            }
            let mut next = records.clone();
            next.insert(
                0,
                Record {
                    live: LiveProgress::default(),
                    id: id.clone(),
                    kind,
                    request,
                    status: Status::Queued,
                    assets: Assets::default(),
                    error: None,
                    bytes: 0,
                    parts_done: 0,
                    parts_total: 0,
                    created: SystemTime::now()
                        .duration_since(UNIX_EPOCH)
                        .unwrap_or_default()
                        .as_secs(),
                },
            );
            self.save(&next)?;
            *records = next;
        }
        self.spawn(id.clone());
        Ok(self.snapshot(&self.record(&id)?))
    }
    fn record(&self, id: &str) -> Result<Record> {
        self.records
            .lock()
            .unwrap()
            .iter()
            .find(|r| r.id == id)
            .cloned()
            .ok_or_else(|| AnibelError::NotFound("download".into()))
    }
    pub fn by_id(&self, id: &str) -> Result<Record> {
        self.record(id)
    }
    fn spawn(self: &Arc<Self>, id: String) {
        if !*self.execution_enabled.lock().unwrap() {
            return;
        }
        let mut work = self.work.lock().unwrap();
        if work.contains_key(&id) {
            return;
        }
        let cancel = CancellationToken::new();
        let ct = cancel.clone();
        let owner = self.clone();
        let key = id.clone();
        let task = tokio::spawn(async move {
            let result = tokio::select! { biased; _=ct.cancelled()=>Err(AnibelError::Cancelled), result=owner.run(&key)=>result };
            let mut records = owner.records.lock().unwrap();
            if let Some(r) = records.iter_mut().find(|r| r.id == key) {
                match result {
                    Ok(assets) => {
                        r.assets = assets;
                        r.status = Status::Completed;
                        r.error = None;
                    }
                    Err(AnibelError::Cancelled) => r.status = Status::Cancelled,
                    Err(e) => {
                        r.status = Status::Failed;
                        r.error = Some(e.to_string());
                    }
                }
                if let Err(e) = owner.save(&records)
                    && let Some(r) = records.iter_mut().find(|r| r.id == key)
                {
                    r.status = Status::Failed;
                    r.error = Some(e.to_string());
                }
            }
        });
        work.insert(
            id,
            Arc::new(Work {
                cancel,
                task: AsyncMutex::new(Some(task)),
            }),
        );
    }
    async fn stop(&self, id: &str) -> Result<()> {
        let work = self.work.lock().unwrap().get(id).cloned();
        if let Some(work) = work {
            work.cancel.cancel();
            let mut handle = work.task.lock().await;
            if let Some(task) = handle.as_mut() {
                task.await
                    .map_err(|e| AnibelError::Internal(e.to_string()))?;
            }
            *handle = None;
            self.work.lock().unwrap().remove(id);
        }
        Ok(())
    }
    pub async fn change(self: &Arc<Self>, id: &str, action: &str) -> Result<Value> {
        let action: Change = serde_json::from_value(json!(action))
            .map_err(|_| AnibelError::BadArgs("invalid download action".into()))?;
        let _op = self.operations.lock().await;
        let old = self.record(id)?;
        if action == Change::Retry && (old.status.active() || self.available(&old)) {
            return Ok(self.snapshot(&old));
        }
        self.stop(id).await?;
        if action == Change::Delete || action == Change::Retry {
            let folder = self.storage.path(&Self::folder(id))?;
            if folder.exists() {
                std::fs::remove_dir_all(folder).map_err(io_error)?;
            }
        }
        let result = {
            let mut records = self.records.lock().unwrap();
            let mut next = records.clone();
            if action == Change::Delete {
                next.retain(|r| r.id != id);
            } else if let Some(r) = next.iter_mut().find(|r| r.id == id) {
                match action {
                    Change::Retry => {
                        r.status = Status::Queued;
                        r.live = LiveProgress::default();
                        r.assets = Assets::default();
                        r.error = None;
                        r.bytes = 0;
                        r.parts_done = 0;
                        r.parts_total = 0;
                    }
                    Change::Cancel => {
                        if r.status.active() {
                            r.status = Status::Cancelled;
                        }
                    }
                    Change::Delete => unreachable!("delete removes the record above"),
                }
            }
            self.save(&next)?;
            *records = next;
            records
                .iter()
                .find(|r| r.id == id)
                .map(|r| self.snapshot(r))
                .unwrap_or(Value::Null)
        };
        if action == Change::Retry {
            self.spawn(id.to_owned());
        }
        Ok(result)
    }
}
fn required<'a>(s: &'a Option<String>, name: &str) -> Result<&'a str> {
    s.as_deref()
        .filter(|s| !s.trim().is_empty())
        .ok_or_else(|| AnibelError::BadArgs(format!("{name} required")))
}
