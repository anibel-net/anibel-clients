use super::{
    hls,
    storage::{Storage, digest, extension, fetch_file, io_error},
};
use anibel_api::AnibelApi;
use anibel_domain::error::{AnibelError, Result};
use anibel_player::{player, video::VideoService};
use serde::{Deserialize, Serialize};
use serde_json::{Value, json};
use std::{
    collections::HashMap,
    path::Path,
    sync::{Arc, Mutex},
    time::{SystemTime, UNIX_EPOCH},
};
use tokio::sync::{Mutex as AsyncMutex, Semaphore};
use tokio_util::sync::CancellationToken;

#[derive(Clone, Copy, Debug, Deserialize, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub(super) enum Kind {
    Video,
    Audio,
    Manga,
    File,
}
#[derive(Clone, Copy, Debug, Deserialize, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub(super) enum Status {
    Queued,
    Downloading,
    Completed,
    Failed,
    Cancelled,
}
impl Status {
    fn active(self) -> bool {
        matches!(self, Self::Queued | Self::Downloading)
    }
}

#[derive(Clone, Default, Deserialize, Serialize)]
#[serde(default, rename_all = "camelCase")]
pub(super) struct Request {
    pub video_format: VideoFormat,
    pub media_id: String,
    pub media_type: String,
    pub slug: String,
    pub title: String,
    pub subtitle: Option<String>,
    pub poster_url: Option<String>,
    pub episode_url: Option<String>,
    pub episode_id: Option<String>,
    pub episode_type: Option<String>,
    pub episode_label: String,
    pub chapter: Option<f64>,
    pub chapter_id: Option<String>,
    pub chapter_title: Option<String>,
    pub chapter_list: Vec<f64>,
    pub file_url: Option<String>,
}
#[derive(Clone, Copy, Default, Deserialize, Serialize, PartialEq, Eq)]
#[serde(rename_all = "camelCase")]
pub(super) enum VideoFormat {
    #[default]
    Source,
    Mkv,
}
#[derive(Clone, Default, Deserialize, Serialize)]
#[serde(default, rename_all = "camelCase")]
pub(super) struct Assets {
    pub video_path: Option<String>,
    pub audio_path: Option<String>,
    pub file_path: Option<String>,
    pub poster_path: Option<String>,
    pub subtitle_paths: Vec<String>,
    pub font_paths: Vec<String>,
    pub image_paths: Vec<String>,
    pub required_paths: Vec<String>,
}
impl Assets {
    fn all(&self) -> Vec<&str> {
        self.video_path
            .iter()
            .chain(self.audio_path.iter())
            .chain(self.file_path.iter())
            .chain(self.poster_path.iter())
            .chain(self.subtitle_paths.iter())
            .chain(self.font_paths.iter())
            .chain(self.image_paths.iter())
            .chain(self.required_paths.iter())
            .map(String::as_str)
            .collect()
    }
}
#[derive(Clone, Deserialize, Serialize)]
pub(super) struct Record {
    pub id: String,
    pub kind: Kind,
    pub request: Request,
    pub status: Status,
    pub assets: Assets,
    pub error: Option<String>,
    pub bytes: u64,
    pub parts_done: u64,
    pub parts_total: u64,
    pub created: u64,
}
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
        })
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
    pub fn snapshot(&self, r: &Record) -> Value {
        let mut value = serde_json::to_value(&r.request).unwrap();
        let obj = value.as_object_mut().unwrap();
        let ready = self.available(r);
        let absolute = |p: &Option<String>| p.as_ref().and_then(|p| self.absolute(p).ok());
        let fields = json!({"id":r.id,"kind":r.kind,"status":if r.status==Status::Completed&&!ready {Status::Failed}else{r.status},
            "subtitle":r.request.subtitle.as_ref().or(r.request.chapter_title.as_ref()).cloned().unwrap_or_else(||r.request.episode_label.clone()),
            "error":if r.status==Status::Completed&&!ready {Some("download assets are missing".to_owned())}else{r.error.clone()},
            "bytesReceived":r.bytes,"diskBytes":r.bytes,"partsDone":r.parts_done,"partsTotal":r.parts_total,
            "progress":if ready {1.0}else if r.parts_total>0 {r.parts_done as f64/r.parts_total as f64}else{0.0},
            "posterPath":absolute(&r.assets.poster_path),
            "canPlay":ready&&r.kind!=Kind::File,"canSave":ready,"canRetry":matches!(r.status,Status::Failed|Status::Cancelled)||r.status==Status::Completed&&!ready});
        obj.remove("fileUrl");
        obj.remove("episodeUrl");
        obj.extend(fields.as_object().unwrap().clone());
        value
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
        if !matches!(action, "retry" | "cancel" | "delete") {
            return Err(AnibelError::BadArgs("invalid download action".into()));
        }
        let _op = self.operations.lock().await;
        let old = self.record(id)?;
        if action == "retry" && (old.status.active() || self.available(&old)) {
            return Ok(self.snapshot(&old));
        }
        self.stop(id).await?;
        if action == "delete" || action == "retry" {
            let folder = self.storage.path(&Self::folder(id))?;
            if folder.exists() {
                std::fs::remove_dir_all(folder).map_err(io_error)?;
            }
        }
        let result = {
            let mut records = self.records.lock().unwrap();
            let mut next = records.clone();
            if action == "delete" {
                next.retain(|r| r.id != id);
            } else if let Some(r) = next.iter_mut().find(|r| r.id == id) {
                match action {
                    "retry" => {
                        r.status = Status::Queued;
                        r.assets = Assets::default();
                        r.error = None;
                        r.bytes = 0;
                        r.parts_done = 0;
                        r.parts_total = 0;
                    }
                    "cancel" => {
                        if r.status.active() {
                            r.status = Status::Cancelled;
                        }
                    }
                    _ => return Err(AnibelError::BadArgs("invalid download action".into())),
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
        if action == "retry" {
            self.spawn(id.to_owned());
        }
        Ok(result)
    }
    fn progress(&self, id: &str, done: u64, total: u64, bytes: u64) {
        if let Some(r) = self.records.lock().unwrap().iter_mut().find(|r| r.id == id) {
            r.parts_done = done;
            r.parts_total = total;
            r.bytes = bytes;
        }
    }
    async fn run(&self, id: &str) -> Result<Assets> {
        let _permit = self
            .worker
            .acquire()
            .await
            .map_err(|_| AnibelError::Cancelled)?;
        let r = self.record(id)?;
        {
            let mut records = self.records.lock().unwrap();
            let row = records.iter_mut().find(|r| r.id == id).unwrap();
            row.status = Status::Downloading;
            self.save(&records)?;
        }
        let folder = Self::folder(id);
        let dir = self.storage.path(&folder)?;
        std::fs::create_dir_all(&dir).map_err(io_error)?;
        let mut assets = Assets::default();
        let mut bytes = 0;
        match r.kind {
            Kind::Manga => {
                let chapter = self
                    .api
                    .chapter(&r.request.slug, r.request.chapter.unwrap())
                    .await?;
                if let Some(row) = self.records.lock().unwrap().iter_mut().find(|r| r.id == id) {
                    row.request.chapter_id = Some(chapter.id.clone());
                }
                if chapter.images.is_empty() || chapter.images.len() > 100_000 {
                    return Err(AnibelError::BadArgs("invalid chapter page count".into()));
                }
                for (i, image) in chapter.images.iter().enumerate() {
                    let url = if image.large.is_empty() {
                        image.thumbnail.as_deref().unwrap_or("")
                    } else {
                        &image.large
                    };
                    let rel = format!("{folder}/pages/{i:06}{}", extension(url, ".jpg"));
                    bytes +=
                        fetch_file(&self.http, url, &self.storage.path(&rel)?, 64 * 1024 * 1024)
                            .await?;
                    if bytes > hls::MAX_JOB_BYTES {
                        return Err(AnibelError::BadArgs("download exceeds size limit".into()));
                    }
                    assets.image_paths.push(rel);
                    self.progress(id, (i + 1) as u64, chapter.images.len() as u64, bytes);
                }
            }
            Kind::File => {
                let url = required(&r.request.file_url, "fileUrl")?;
                let rel = format!("{folder}/file{}", extension(url, ".bin"));
                fetch_file(
                    &self.http,
                    url,
                    &self.storage.path(&rel)?,
                    hls::MAX_JOB_BYTES,
                )
                .await?;
                assets.file_path = Some(rel);
            }
            Kind::Video | Kind::Audio => {
                let intent =
                    player::resolve_intent(&self.video, &json!({"url":r.request.episode_url}))
                        .await?;
                if intent.kind != player::PlaybackKind::Native {
                    return Err(AnibelError::BadArgs(
                        "embedded video cannot be downloaded".into(),
                    ));
                }
                let src = if r.kind == Kind::Audio {
                    intent.audio_src.as_deref().or(intent.video_src.as_deref())
                } else {
                    intent.video_src.as_deref()
                }
                .ok_or_else(|| AnibelError::NotFound("media source".into()))?;
                let (subs, fonts) = self.prepare_assets(&intent, &folder).await?;
                if r.kind == Kind::Video && r.request.video_format == VideoFormat::Mkv {
                    if subs.len() != intent.subtitles.len() || fonts.len() != intent.fonts.len() {
                        return Err(AnibelError::Transport("some subtitles or fonts could not be downloaded; retry the MKV download".into()));
                    }
                    let path = format!("{folder}/media.mkv");
                    super::mkv::download(
                        src,
                        intent.audio_src.as_deref(),
                        &subs
                            .iter()
                            .map(|p| self.absolute(p))
                            .collect::<Result<Vec<_>>>()?,
                        &fonts
                            .iter()
                            .map(|p| self.absolute(p))
                            .collect::<Result<Vec<_>>>()?,
                        &self.storage.path(&path)?,
                        &r.request.title,
                        |bytes| self.progress(id, 0, 0, bytes),
                    )
                    .await?;
                    assets.video_path = Some(path);
                    // They are now inside the MKV; offline playback must not add them twice.
                    for file in subs.iter().chain(fonts.iter()) {
                        std::fs::remove_file(self.storage.path(file)?).map_err(io_error)?;
                    }
                } else {
                    let (primary, required) = self.fetch_media(src, &folder, id).await?;
                    if r.kind == Kind::Audio {
                        if primary.ends_with(".m3u8") && intent.audio_src.is_none() {
                            return Err(AnibelError::BadArgs(
                                "source has no separate audio stream".into(),
                            ));
                        }
                        assets.audio_path = Some(primary);
                    } else {
                        assets.video_path = Some(primary);
                        if let Some(audio) = intent.audio_src.as_deref() {
                            let (path, required) = self
                                .fetch_media(audio, &format!("{folder}/audio"), id)
                                .await?;
                            assets.audio_path = Some(path);
                            assets.required_paths.extend(required);
                        }
                    }
                    assets.required_paths.extend(required);
                    assets.subtitle_paths = subs;
                    assets.font_paths = fonts;
                }
            }
        }
        if let Some(url) = r.request.poster_url.as_deref().filter(|s| !s.is_empty()) {
            let rel = format!("{folder}/poster{}", extension(url, ".jpg"));
            if fetch_file(&self.http, url, &self.storage.path(&rel)?, 16 * 1024 * 1024)
                .await
                .is_ok()
            {
                assets.poster_path = Some(rel);
            }
        }
        let mut unique = std::collections::HashSet::new();
        bytes = 0;
        for path in assets.all() {
            if unique.insert(path) {
                bytes = bytes
                    .checked_add(
                        std::fs::metadata(self.storage.path(path)?)
                            .map_err(io_error)?
                            .len(),
                    )
                    .filter(|n| *n <= hls::MAX_JOB_BYTES)
                    .ok_or_else(|| AnibelError::BadArgs("download exceeds size limit".into()))?;
            }
        }
        self.progress(id, 1, 1, bytes);
        Ok(assets)
    }
    async fn fetch_media(
        &self,
        src: &str,
        folder: &str,
        id: &str,
    ) -> Result<(String, Vec<String>)> {
        let ext = extension(src, "");
        if ext == ".mpd" {
            return Err(AnibelError::BadArgs("DASH download is unsupported".into()));
        }
        let dir = self.storage.path(folder)?;
        std::fs::create_dir_all(&dir).map_err(io_error)?;
        if ext != ".m3u8" && ext != ".m3u" {
            let relative = format!("{folder}/media{}", extension(src, ".mp4"));
            let path = self.storage.path(&relative)?;
            fetch_file(&self.http, src, &path, hls::MAX_JOB_BYTES).await?;
            use std::io::Read;
            let mut prefix = [0u8; 1024];
            let n = std::fs::File::open(&path)
                .map_err(io_error)?
                .read(&mut prefix)
                .map_err(io_error)?;
            let text = String::from_utf8_lossy(&prefix[..n]);
            if text.contains("<MPD") {
                return Err(AnibelError::BadArgs("DASH download is unsupported".into()));
            }
            if !text
                .trim_start_matches('\u{feff}')
                .trim_start()
                .starts_with("#EXTM3U")
            {
                return Ok((relative, Vec::new()));
            }
            std::fs::remove_file(path).map_err(io_error)?;
        }
        let required = hls::download(&self.http, src, &dir, |d, t, b| self.progress(id, d, t, b))
            .await?
            .into_iter()
            .map(|p| format!("{folder}/{p}"))
            .collect();
        Ok((format!("{folder}/playlist.m3u8"), required))
    }
    pub async fn prepare_assets(
        &self,
        intent: &player::PlaybackIntent,
        folder: &str,
    ) -> Result<(Vec<String>, Vec<String>)> {
        if intent.subtitles.len() > 128 || intent.fonts.len() > 256 {
            return Err(AnibelError::BadArgs("too many playback assets".into()));
        }
        let mut subs = Vec::new();
        let mut fonts = Vec::new();
        for (i, sub) in intent.subtitles.iter().enumerate() {
            let original = url::Url::parse(&sub.url)
                .ok()
                .and_then(|u| {
                    u.path_segments()
                        .and_then(|mut s| s.next_back().map(str::to_owned))
                })
                .unwrap_or_else(|| "subtitle.ass".into());
            let decoded = percent_encoding::percent_decode_str(&original).decode_utf8_lossy();
            let name: String = decoded
                .chars()
                .filter(|c| c.is_alphanumeric() || matches!(c, '.' | '_' | '-' | '&'))
                .take(120)
                .collect();
            let path = format!(
                "{folder}/sub-{i}-{name}{}",
                if name.ends_with(".ass") || name.ends_with(".srt") || name.ends_with(".ssa") {
                    String::new()
                } else {
                    extension(&sub.url, ".ass")
                }
            );
            if fetch_file(
                &self.http,
                &sub.url,
                &self.storage.path(&path)?,
                16 * 1024 * 1024,
            )
            .await
            .is_ok()
            {
                subs.push(path);
            }
        }
        for font in &intent.fonts {
            let path = format!("{folder}/fonts/{}.ttf", digest(&font.url));
            let absolute = self.storage.path(&path)?;
            if absolute.is_file()
                || fetch_file(&self.http, &font.url, &absolute, 32 * 1024 * 1024)
                    .await
                    .is_ok()
            {
                fonts.push(path);
            }
        }
        Ok((subs, fonts))
    }
    pub async fn export(&self, id: &str, destination: &Path) -> Result<Value> {
        let _op = self.operations.lock().await;
        let r = self.record(id)?;
        if !self.available(&r) {
            return Err(AnibelError::BadArgs("download is not complete".into()));
        }
        std::fs::create_dir_all(destination).map_err(io_error)?;
        let destination = destination.canonicalize().map_err(io_error)?;
        if destination.starts_with(&self.storage.root) {
            return Err(AnibelError::BadArgs(
                "export destination must be outside core storage".into(),
            ));
        }
        if let Some(video) = r.assets.video_path.as_ref().filter(|p| p.ends_with(".mkv")) {
            let name: String = format!("{} - {}", r.request.title, r.request.episode_label)
                .chars()
                .map(|c| {
                    if c.is_control() || "<>:\"/\\|?*".contains(c) {
                        '_'
                    } else {
                        c
                    }
                })
                .take(120)
                .collect();
            let target = destination.join(format!(
                "{}-{}.mkv",
                name.trim().trim_end_matches('.'),
                &digest(id)[..8]
            ));
            let temporary = tempfile::NamedTempFile::new_in(&destination).map_err(io_error)?;
            std::fs::copy(self.storage.path(video)?, temporary.path()).map_err(io_error)?;
            temporary
                .persist_noclobber(&target)
                .map_err(|e| io_error(e.error))?;
            return Ok(json!({"path":target.to_string_lossy()}));
        }
        let source = self.storage.path(&Self::folder(id))?;
        let target = destination.join(format!("anibel-{}", &digest(id)[..16]));
        if target.exists() {
            return Err(AnibelError::BadArgs(
                "export destination already exists".into(),
            ));
        }
        let temporary = tempfile::tempdir_in(&destination).map_err(io_error)?;
        copy_tree(&source, temporary.path())?;
        std::fs::rename(temporary.path(), &target).map_err(io_error)?;
        Ok(json!({"path":target.to_string_lossy()}))
    }
}
fn required<'a>(s: &'a Option<String>, name: &str) -> Result<&'a str> {
    s.as_deref()
        .filter(|s| !s.trim().is_empty())
        .ok_or_else(|| AnibelError::BadArgs(format!("{name} required")))
}
fn copy_tree(source: &Path, target: &Path) -> Result<()> {
    for entry in std::fs::read_dir(source).map_err(io_error)? {
        let entry = entry.map_err(io_error)?;
        let kind = entry.file_type().map_err(io_error)?;
        if kind.is_symlink() {
            return Err(AnibelError::BadArgs("download contains a link".into()));
        }
        let dest = target.join(entry.file_name());
        if kind.is_dir() {
            std::fs::create_dir(&dest).map_err(io_error)?;
            copy_tree(&entry.path(), &dest)?;
        } else {
            std::fs::copy(entry.path(), dest).map_err(io_error)?;
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use httpmock::prelude::*;
    fn setup(server: &MockServer) -> Arc<Downloads> {
        Arc::new(
            Downloads::new(
                Arc::new(Storage::new(None).unwrap()),
                AnibelApi::with_base(server.url("/graphql")),
                VideoService::with_base(server.base_url()),
                reqwest::Client::new(),
            )
            .unwrap(),
        )
    }
    fn request(server: &MockServer) -> Request {
        Request {
            media_id: "book1".into(),
            media_type: "books".into(),
            title: "Book".into(),
            file_url: Some(server.url("/file.pdf")),
            ..Default::default()
        }
    }
    #[tokio::test]
    async fn mkv_export_is_one_file_and_does_not_overwrite() {
        let server = MockServer::start_async().await;
        let d = setup(&server);
        let folder = Downloads::folder("mkv-test");
        let video = format!("{folder}/media.mkv");
        std::fs::create_dir_all(d.storage.path(&folder).unwrap()).unwrap();
        std::fs::write(d.storage.path(&video).unwrap(), b"mkv-fixture").unwrap();
        let mut request = request(&server);
        request.title = "Title: / test".into();
        request.episode_label = "1".into();
        request.video_format = VideoFormat::Mkv;
        d.records.lock().unwrap().push(Record {
            id: "mkv-test".into(),
            kind: Kind::Video,
            request,
            status: Status::Completed,
            assets: Assets {
                video_path: Some(video),
                ..Default::default()
            },
            error: None,
            bytes: 11,
            parts_done: 1,
            parts_total: 1,
            created: 0,
        });
        let destination = tempfile::tempdir().unwrap();
        let exported = d.export("mkv-test", destination.path()).await.unwrap();
        let path = Path::new(exported["path"].as_str().unwrap());
        assert_eq!(path.extension().unwrap(), "mkv");
        assert_eq!(std::fs::read(path).unwrap(), b"mkv-fixture");
        assert!(d.export("mkv-test", destination.path()).await.is_err());
        assert_eq!(std::fs::read_dir(destination.path()).unwrap().count(), 1);
    }
    async fn finished(d: &Downloads, id: &str) -> Record {
        tokio::time::timeout(std::time::Duration::from_secs(5), async {
            loop {
                let r = d.record(id).unwrap();
                if !r.status.active() {
                    return r;
                }
                tokio::time::sleep(std::time::Duration::from_millis(10)).await;
            }
        })
        .await
        .unwrap()
    }
    #[tokio::test]
    async fn file_lifecycle_is_durable_and_missing_assets_are_not_ready() {
        let server = MockServer::start_async().await;
        let file = server
            .mock_async(|w, t| {
                w.path("/file.pdf");
                t.body("pdf-content");
            })
            .await;
        let d = setup(&server);
        let first = d.enqueue(Kind::File, request(&server)).await.unwrap();
        let id = first["id"].as_str().unwrap();
        assert_eq!(
            d.enqueue(Kind::File, request(&server)).await.unwrap()["id"],
            id
        );
        let record = finished(&d, id).await;
        assert_eq!(record.status, Status::Completed);
        assert!(d.available(&record));
        assert_eq!(record.bytes, 11);
        file.assert_hits_async(1).await;
        d.change(id, "retry").await.unwrap();
        file.assert_hits_async(1).await;
        let restored = Downloads::new(
            d.storage.clone(),
            d.api.clone(),
            d.video.clone(),
            d.http.clone(),
        )
        .unwrap();
        assert!(restored.available(&restored.record(id).unwrap()));
        let export = tempfile::tempdir().unwrap();
        let result = d.export(id, export.path()).await.unwrap();
        assert!(
            Path::new(result["path"].as_str().unwrap())
                .join("file.pdf")
                .is_file()
        );
        assert!(d.export(id, export.path()).await.is_err());
        std::fs::remove_file(
            d.storage
                .path(record.assets.file_path.as_ref().unwrap())
                .unwrap(),
        )
        .unwrap();
        assert_eq!(d.snapshot(&record)["canPlay"], false);
        assert_eq!(d.snapshot(&record)["canRetry"], true);
        assert!(d.export(id, export.path()).await.is_err());
        d.change(id, "retry").await.unwrap();
        assert_eq!(finished(&d, id).await.status, Status::Completed);
        file.assert_hits_async(2).await;
        d.change(id, "delete").await.unwrap();
        assert!(d.record(id).is_err());
        assert!(!d.storage.path(&Downloads::folder(id)).unwrap().exists());
    }
    #[tokio::test]
    async fn cancel_stops_writes_before_delete_and_invalid_action_does_not_stop_job() {
        let server = MockServer::start_async().await;
        server
            .mock_async(|w, t| {
                w.path("/file.pdf");
                t.delay(std::time::Duration::from_millis(200))
                    .body("content");
            })
            .await;
        let d = setup(&server);
        let first = d.enqueue(Kind::File, request(&server)).await.unwrap();
        let id = first["id"].as_str().unwrap();
        assert!(d.change(id, "bogus").await.is_err());
        assert!(d.record(id).unwrap().status.active());
        d.change(id, "cancel").await.unwrap();
        assert_eq!(d.record(id).unwrap().status, Status::Cancelled);
        d.change(id, "delete").await.unwrap();
        tokio::time::sleep(std::time::Duration::from_millis(250)).await;
        assert!(!d.storage.path(&Downloads::folder(id)).unwrap().exists());
    }
    #[tokio::test]
    async fn storage_failure_does_not_enqueue_and_interrupted_jobs_restart_queued() {
        let server = MockServer::start_async().await;
        let d = setup(&server);
        std::fs::create_dir(d.storage.root.join("downloads.json")).unwrap();
        assert!(d.enqueue(Kind::File, request(&server)).await.is_err());
        assert!(d.records.lock().unwrap().is_empty());
        assert!(d.work.lock().unwrap().is_empty());
        std::fs::remove_dir(d.storage.root.join("downloads.json")).unwrap();
        let r = Record {
            id: "restore".into(),
            kind: Kind::File,
            request: request(&server),
            status: Status::Downloading,
            assets: Assets::default(),
            error: None,
            bytes: 9,
            parts_done: 0,
            parts_total: 1,
            created: 0,
        };
        d.save(&[r]).unwrap();
        let restored = Downloads::new(
            d.storage.clone(),
            d.api.clone(),
            d.video.clone(),
            d.http.clone(),
        )
        .unwrap();
        assert_eq!(restored.record("restore").unwrap().status, Status::Queued);
    }
    #[tokio::test]
    async fn offline_hls_requires_every_segment_and_preserves_manifest() {
        let server = MockServer::start_async().await;
        server.mock_async(|w,t|{w.path("/list.m3u8");t.body("#EXTM3U\n#EXT-X-TARGETDURATION:6\n#EXTINF:6,\na.ts\n#EXTINF:6,\nb.ts\n#EXT-X-ENDLIST");}).await;
        for path in ["/a.ts", "/b.ts"] {
            server
                .mock_async(|w, t| {
                    w.path(path);
                    t.body(path);
                })
                .await;
        }
        let d = setup(&server);
        let folder = Downloads::folder("hls");
        let dir = d.storage.path(&folder).unwrap();
        std::fs::create_dir_all(&dir).unwrap();
        let paths = hls::download(&d.http, &server.url("/list.m3u8"), &dir, |_, _, _| {})
            .await
            .unwrap();
        let r = Record {
            id: "hls".into(),
            kind: Kind::Video,
            request: Request::default(),
            status: Status::Completed,
            assets: Assets {
                video_path: Some(format!("{folder}/playlist.m3u8")),
                required_paths: paths.iter().map(|p| format!("{folder}/{p}")).collect(),
                ..Default::default()
            },
            error: None,
            bytes: 0,
            parts_done: 1,
            parts_total: 1,
            created: 0,
        };
        assert!(d.available(&r));
        let manifest = std::fs::read_to_string(dir.join("playlist.m3u8")).unwrap();
        assert!(manifest.contains("segs/000001.ts"));
        assert!(!manifest.contains(&server.base_url()));
        std::fs::remove_file(dir.join("segs/000001.ts")).unwrap();
        assert!(!d.available(&r));
    }
}
