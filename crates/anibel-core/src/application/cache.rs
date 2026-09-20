//! Bounded, account-scoped response storage. All access is under Application's mutex.

use serde::{Deserialize, Serialize};
use serde_json::Value;
use sha2::{Digest, Sha256};
use std::collections::{HashMap, HashSet};
use std::io::Write;
use std::path::PathBuf;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

const MAX_ENTRIES: usize = 512;
const MAX_BYTES: usize = 16 * 1024 * 1024;

#[derive(Clone, Serialize, Deserialize)]
struct Entry {
    value: Value,
    stored_at: u64,
}

#[derive(Default, Serialize, Deserialize)]
struct Snapshot {
    version: u32,
    entries: HashMap<String, Entry>,
}

pub(super) struct Cache {
    path: Option<PathBuf>,
    entries: HashMap<String, Entry>,
    refreshing: HashSet<String>,
    endpoint: String,
    scope: String,
}

pub(super) struct Hit {
    pub value: Value,
    pub stale: bool,
}

impl Cache {
    pub fn new(data_dir: Option<PathBuf>, base: &str, video_base: &str) -> Self {
        let path = data_dir.map(|dir| dir.join("api-cache-v1.json"));
        let entries = path
            .as_ref()
            .and_then(|path| {
                // Cache data is disposable. Never load an unbounded or incompatible snapshot.
                let size = std::fs::metadata(path).ok()?.len();
                if size > MAX_BYTES as u64 {
                    return None;
                }
                let snapshot: Snapshot = serde_json::from_slice(&std::fs::read(path).ok()?).ok()?;
                (snapshot.version == 1 && snapshot.entries.len() <= MAX_ENTRIES)
                    .then_some(snapshot.entries)
            })
            .unwrap_or_default();
        Self {
            path,
            entries,
            refreshing: HashSet::new(),
            endpoint: serde_json::to_string(&(base, video_base)).expect("string tuple"),
            scope: hash("anonymous"),
        }
    }

    pub fn set_token(&mut self, token: Option<&str>) {
        // Neither the token nor a reversible representation is stored on disk.
        self.scope = token.map_or_else(|| hash("anonymous"), |t| hash(&format!("token:{t}")));
    }

    pub fn key(&self, op: &str, args: &Value) -> String {
        // Old cached comment pages did not include the reply tree.
        let op = match op {
            "comments" => "comments-tree-v1",
            "media" => "media-rating-v1",
            _ => op,
        };
        hash(&serde_json::to_string(&(&self.endpoint, &self.scope, op, args)).expect("JSON value"))
    }

    pub fn get(&self, key: &str, op: &str, now: SystemTime) -> Option<Hit> {
        let entry = self.entries.get(key)?;
        let stored = UNIX_EPOCH.checked_add(Duration::from_secs(entry.stored_at))?;
        // A clock rollback must not make old data fresh indefinitely.
        let age = now.duration_since(stored).ok()?;
        let (fresh, keep) = ttl(op)?;
        (age <= keep).then(|| Hit {
            value: entry.value.clone(),
            stale: age > fresh,
        })
    }

    pub fn begin_refresh(&mut self, key: &str) -> bool {
        self.refreshing.insert(key.to_owned())
    }

    pub fn end_refresh(&mut self, key: &str) {
        self.refreshing.remove(key);
    }

    pub fn set(&mut self, key: String, value: Value, now: SystemTime) -> std::io::Result<()> {
        let stored_at = now.duration_since(UNIX_EPOCH).unwrap_or_default().as_secs();
        // Leave large responses uncached. The API result itself is still returned.
        if serde_json::to_vec(&value)?.len() > MAX_BYTES / 2 {
            self.entries.remove(&key);
            return self.persist(&serde_json::to_vec(&Snapshot {
                version: 1,
                entries: self.entries.clone(),
            })?);
        }
        self.entries.insert(key, Entry { value, stored_at });
        loop {
            let bytes = serde_json::to_vec(&Snapshot {
                version: 1,
                entries: self.entries.clone(),
            })?;
            if self.entries.len() <= MAX_ENTRIES && bytes.len() <= MAX_BYTES {
                return self.persist(&bytes);
            }
            if let Some(oldest) = self
                .entries
                .iter()
                .min_by_key(|(_, e)| e.stored_at)
                .map(|(k, _)| k.clone())
            {
                self.entries.remove(&oldest);
            } else {
                return Ok(());
            }
        }
    }

    pub fn clear(&mut self) -> std::io::Result<()> {
        self.entries.clear();
        self.persist(br#"{"version":1,"entries":{}}"#)
    }

    fn persist(&self, bytes: &[u8]) -> std::io::Result<()> {
        let Some(path) = &self.path else {
            return Ok(());
        };
        let parent = path.parent().expect("cache path has a parent");
        std::fs::create_dir_all(parent)?;
        let mut temp = tempfile::NamedTempFile::new_in(parent)?;
        temp.write_all(bytes)?;
        temp.as_file().sync_all()?;
        temp.persist(path).map_err(|e| e.error)?;
        Ok(())
    }
}

fn hash(text: &str) -> String {
    format!("{:x}", Sha256::digest(text.as_bytes()))
}

/// Explicit read allowlist: new commands cannot become cacheable by accident.
pub(super) fn ttl(op: &str) -> Option<(Duration, Duration)> {
    crate::protocol::Command::parse(op).ok()?.ttl()
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn cache_is_scoped_to_token_and_endpoints_and_survives_restart() {
        let dir = tempfile::tempdir().unwrap();
        let mut cache = Cache::new(Some(dir.path().to_owned()), "api", "video");
        cache.set_token(Some("secret-a"));
        let key = cache.key("episodes", &json!({"mediaId":"1"}));
        cache
            .set(key.clone(), json!({"watched":true}), SystemTime::now())
            .unwrap();
        cache.set_token(Some("secret-b"));
        assert_ne!(key, cache.key("episodes", &json!({"mediaId":"1"})));
        let mut restored = Cache::new(Some(dir.path().to_owned()), "api", "video");
        restored.set_token(Some("secret-a"));
        assert_eq!(key, restored.key("episodes", &json!({"mediaId":"1"})));
        assert!(restored.get(&key, "episodes", SystemTime::now()).is_some());
        let mut other = Cache::new(None, "another-api", "video");
        other.set_token(Some("secret-a"));
        assert_ne!(key, other.key("episodes", &json!({"mediaId":"1"})));
        let disk = std::fs::read_to_string(dir.path().join("api-cache-v1.json")).unwrap();
        assert!(!disk.contains("secret-a"));
        restored.clear().unwrap();
        assert!(
            Cache::new(Some(dir.path().to_owned()), "api", "video")
                .entries
                .is_empty()
        );
    }

    #[test]
    fn freshness_expiry_and_clock_rollback() {
        let mut cache = Cache::new(None, "api", "video");
        let now = UNIX_EPOCH + Duration::from_secs(1000);
        cache.set("key".into(), json!(1), now).unwrap();
        assert!(!cache.get("key", "comments", now).unwrap().stale);
        assert!(
            cache
                .get("key", "comments", now + Duration::from_secs(901))
                .unwrap()
                .stale
        );
        assert!(
            cache
                .get("key", "comments", now + Duration::from_secs(2 * 86400 + 1))
                .is_none()
        );
        assert!(
            cache
                .get("key", "comments", now - Duration::from_secs(1))
                .is_none()
        );
    }

    #[test]
    fn unsafe_and_unknown_operations_are_never_cached() {
        for op in [
            "login",
            "setToken",
            "markAs",
            "addComment",
            "resolveEpisode",
            "random",
            "videoInfo",
            "futureCommand",
        ] {
            assert!(ttl(op).is_none(), "{op}");
        }
    }

    #[test]
    fn corrupt_cache_is_disposable() {
        let dir = tempfile::tempdir().unwrap();
        std::fs::write(dir.path().join("api-cache-v1.json"), b"{partial").unwrap();
        assert!(
            Cache::new(Some(dir.path().to_owned()), "api", "video")
                .entries
                .is_empty()
        );
    }

    #[test]
    fn oversized_refresh_removes_previous_value_and_entry_count_is_bounded() {
        let mut cache = Cache::new(None, "api", "video");
        for i in 0..=MAX_ENTRIES {
            cache
                .set(i.to_string(), serde_json::json!(i), SystemTime::now())
                .unwrap();
        }
        assert_eq!(cache.entries.len(), MAX_ENTRIES);
        cache
            .set("large".into(), serde_json::json!("old"), SystemTime::now())
            .unwrap();
        cache
            .set(
                "large".into(),
                serde_json::json!("x".repeat(MAX_BYTES / 2)),
                SystemTime::now(),
            )
            .unwrap();
        assert!(cache.get("large", "search", SystemTime::now()).is_none());
    }
}
