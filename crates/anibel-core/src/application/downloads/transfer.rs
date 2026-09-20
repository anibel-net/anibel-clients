use super::*;
impl Downloads {
    pub(super) async fn run(&self, id: &str) -> Result<Assets> {
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
            row.live = LiveProgress {
                started: Some(Instant::now()),
                ..Default::default()
            };
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
                self.fetch_progress(id, url, &self.storage.path(&rel)?)
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
                    crate::application::mkv::download(
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
                        |bytes, media| {
                            if let Some(row) =
                                self.records.lock().unwrap().iter_mut().find(|r| r.id == id)
                            {
                                row.bytes = bytes;
                                row.live.media.processed =
                                    row.live.media.processed.max(media.processed);
                                row.live.media.duration = media.duration;
                            }
                        },
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
    async fn fetch_progress(&self, id: &str, url: &str, path: &Path) -> Result<u64> {
        crate::application::storage::fetch_file_with_progress(
            &self.http,
            url,
            path,
            hls::MAX_JOB_BYTES,
            |bytes, total| {
                if let Some(row) = self.records.lock().unwrap().iter_mut().find(|r| r.id == id) {
                    row.bytes = bytes;
                    row.live.total_bytes = total;
                }
            },
        )
        .await
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
            self.fetch_progress(id, src, &path).await?;
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
        if let Some(row) = self.records.lock().unwrap().iter_mut().find(|r| r.id == id) {
            row.live.total_bytes = None;
            row.bytes = 0;
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
}
