use anibel_domain::error::{AnibelError, Result};
use serde::{Serialize, de::DeserializeOwned};
use sha2::{Digest, Sha256};
use std::{
    io::{Read, Write},
    path::{Component, Path, PathBuf},
};

pub(super) struct Storage {
    pub root: PathBuf,
    _temporary: Option<tempfile::TempDir>,
}

impl Storage {
    pub fn new(root: Option<PathBuf>) -> Result<Self> {
        let temporary = if root.is_none() {
            Some(tempfile::tempdir().map_err(io_error)?)
        } else {
            None
        };
        let root = root.unwrap_or_else(|| temporary.as_ref().unwrap().path().to_owned());
        std::fs::create_dir_all(&root).map_err(io_error)?;
        let root = root.canonicalize().map_err(io_error)?;
        Ok(Self {
            root,
            _temporary: temporary,
        })
    }
    pub fn load<T: DeserializeOwned + Default>(&self, name: &str) -> Result<T> {
        let path = self.root.join(name);
        match std::fs::File::open(&path) {
            Ok(file) => {
                let mut data = Vec::new();
                file.take(64 * 1024 * 1024 + 1)
                    .read_to_end(&mut data)
                    .map_err(io_error)?;
                if data.len() > 64 * 1024 * 1024 {
                    return Err(AnibelError::Internal("state file exceeds capacity".into()));
                }
                serde_json::from_slice(&data)
                    .map_err(|e| AnibelError::Internal(format!("invalid {name}: {e}")))
            }
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(T::default()),
            Err(e) => Err(io_error(e)),
        }
    }
    pub fn save<T: Serialize>(&self, name: &str, value: &T) -> Result<()> {
        let bytes = serde_json::to_vec(value).map_err(|e| AnibelError::Internal(e.to_string()))?;
        if bytes.len() > 64 * 1024 * 1024 {
            return Err(AnibelError::Internal("state file exceeds capacity".into()));
        }
        atomic_write(&self.root.join(name), &bytes)
    }
    pub fn path(&self, relative: &str) -> Result<PathBuf> {
        let path = Path::new(relative);
        if path.as_os_str().is_empty()
            || path
                .components()
                .any(|p| !matches!(p, Component::Normal(_)))
        {
            return Err(AnibelError::BadArgs("invalid asset path".into()));
        }
        let full = self.root.join(path);
        // Reject symlinks in managed paths, including parent directories.
        let mut current = self.root.clone();
        for part in path.components() {
            current.push(part);
            if let Ok(meta) = std::fs::symlink_metadata(&current)
                && meta.file_type().is_symlink()
            {
                return Err(AnibelError::BadArgs("asset path contains a link".into()));
            }
        }
        Ok(full)
    }
}

pub(super) fn atomic_write(path: &Path, bytes: &[u8]) -> Result<()> {
    let parent = path
        .parent()
        .ok_or_else(|| AnibelError::BadArgs("destination has no parent".into()))?;
    std::fs::create_dir_all(parent).map_err(io_error)?;
    let mut file = tempfile::NamedTempFile::new_in(parent).map_err(io_error)?;
    file.write_all(bytes).map_err(io_error)?;
    file.as_file().sync_all().map_err(io_error)?;
    file.persist(path).map_err(|e| io_error(e.error))?;
    Ok(())
}

pub(super) fn io_error(e: std::io::Error) -> AnibelError {
    AnibelError::Internal(format!("storage: {e}"))
}
pub(super) fn digest(value: &str) -> String {
    format!("{:x}", Sha256::digest(value.as_bytes()))
}
pub(super) fn extension(url: &str, fallback: &str) -> String {
    url::Url::parse(url)
        .ok()
        .and_then(|u| Path::new(u.path()).extension()?.to_str().map(str::to_owned))
        .filter(|s| s.len() <= 6 && s.chars().all(|c| c.is_ascii_alphanumeric()))
        .map(|s| format!(".{}", s.to_lowercase()))
        .unwrap_or_else(|| fallback.into())
}
pub(super) fn http_error(e: reqwest::Error) -> AnibelError {
    AnibelError::Transport(e.to_string())
}

/// A file is published only after a complete bounded transfer. Dropping this
/// future closes and removes the temporary file, so cancellation stops writes.
pub(super) async fn fetch_file(
    http: &reqwest::Client,
    url: &str,
    path: &Path,
    limit: u64,
) -> Result<u64> {
    fetch_file_with_progress(http, url, path, limit, |_, _| {}).await
}

pub(super) async fn fetch_file_with_progress(
    http: &reqwest::Client,
    url: &str,
    path: &Path,
    limit: u64,
    progress: impl Fn(u64, Option<u64>),
) -> Result<u64> {
    let mut response = http
        .get(url)
        .send()
        .await
        .map_err(http_error)?
        .error_for_status()
        .map_err(http_error)?;
    if response.content_length().is_some_and(|n| n > limit) {
        return Err(AnibelError::BadArgs("asset exceeds size limit".into()));
    }
    let parent = path
        .parent()
        .ok_or_else(|| AnibelError::BadArgs("invalid asset path".into()))?;
    std::fs::create_dir_all(parent).map_err(io_error)?;
    let mut file = tempfile::NamedTempFile::new_in(parent).map_err(io_error)?;
    let total = response.content_length();
    progress(0, total);
    let mut last_report = std::time::Instant::now();
    let mut bytes = 0_u64;
    while let Some(chunk) = response.chunk().await.map_err(http_error)? {
        bytes = bytes
            .checked_add(chunk.len() as u64)
            .filter(|n| *n <= limit)
            .ok_or_else(|| AnibelError::BadArgs("asset exceeds size limit".into()))?;
        file.write_all(&chunk).map_err(io_error)?;
        if last_report.elapsed() >= std::time::Duration::from_millis(200) {
            progress(bytes, total);
            last_report = std::time::Instant::now();
        }
    }
    progress(bytes, total);
    if bytes == 0 {
        return Err(AnibelError::Transport("empty asset".into()));
    }
    file.as_file().sync_all().map_err(io_error)?;
    file.persist(path).map_err(|e| io_error(e.error))?;
    Ok(bytes)
}
