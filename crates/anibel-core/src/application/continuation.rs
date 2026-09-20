//! Durable title selection. Native clients keep only transient navigation state.
use super::{Application, arg, decode, presentation_data, storage::Storage};
use anibel_domain::error::{AnibelError, Result};
use serde::{Deserialize, Serialize};
use serde_json::{Value, json};
use std::{
    collections::HashMap,
    sync::{Arc, Mutex},
};

#[derive(Clone, Deserialize, Serialize)]
#[serde(tag = "kind", rename_all = "camelCase")]
enum Selection {
    Episode { id: String, track: String },
    Chapter { number: f64 },
}
pub(super) struct Continuation {
    storage: Arc<Storage>,
    values: Mutex<HashMap<String, Selection>>,
}
impl Continuation {
    pub fn new(storage: Arc<Storage>) -> Result<Self> {
        let values: HashMap<String, Selection> = storage.load("continuation.json")?;
        if values.len() > 10000
            || values.iter().any(|(k, v)| {
                k.len() > 4096
                    || match v {
                        Selection::Episode { id, track } => {
                            id.is_empty()
                                || id.len() > 4096
                                || !matches!(track.as_str(), "sub" | "dub")
                        }
                        Selection::Chapter { number } => !number.is_finite() || *number < 0.0,
                    }
            })
        {
            return Err(AnibelError::Internal("invalid continuation data".into()));
        }
        Ok(Self {
            storage,
            values: Mutex::new(values),
        })
    }
    fn save(&self, key: String, value: Selection) -> Result<()> {
        if key.len() > 4096 {
            return Err(AnibelError::BadArgs("title identity is too long".into()));
        }
        let mut values = self.values.lock().unwrap();
        if values.len() >= 10000 && !values.contains_key(&key) {
            return Err(AnibelError::BadArgs("continuation store is full".into()));
        }
        let mut next = values.clone();
        next.insert(key, value);
        self.storage.save("continuation.json", &next)?;
        *values = next;
        Ok(())
    }
    pub fn remember_episode(&self, args: &Value) -> Result<Value> {
        let id = arg(args, "episodeId")?;
        let track = arg(args, "episodeType")?;
        if id.len() > 4096 || !matches!(track, "sub" | "dub") {
            return Err(AnibelError::BadArgs("invalid episode selection".into()));
        }
        self.save(
            format!("episode:{}", arg(args, "mediaId")?),
            Selection::Episode {
                id: id.into(),
                track: track.into(),
            },
        )?;
        Ok(json!({"status":"ok"}))
    }
    pub fn remember_chapter(&self, slug: &str, number: f64) -> Result<()> {
        if !number.is_finite() || number < 0.0 {
            return Err(AnibelError::BadArgs("invalid chapter".into()));
        }
        self.save(format!("chapter:{slug}"), Selection::Chapter { number })
    }
}
impl Application {
    pub(super) async fn continue_episode(&self, args: &Value) -> Result<Value> {
        let id = arg(args, "mediaId")?;
        // Import the prior Android selection once. Never overwrite newer core state.
        let key = format!("episode:{id}");
        let saved = self.continuation.values.lock().unwrap().get(&key).cloned();
        let legacy = args.get("legacy").filter(|v| !v.is_null());
        let saved = if let (None, Some(legacy)) = (&saved, legacy) {
            let mut value = legacy.clone();
            value["mediaId"] = json!(id);
            self.continuation.remember_episode(&value)?;
            self.continuation.values.lock().unwrap().get(&key).cloned()
        } else {
            saved
        };
        let (preferred, track) = match &saved {
            Some(Selection::Episode { id, track }) => (Some(id.as_str()), track.as_str()),
            _ => (None, "dub"),
        };
        let choices = presentation_data::episodes(self.api.episodes_matrix(id).await?, track, None);
        let rows: Vec<anibel_domain::models::Episode> = decode(&choices["items"])?;
        let selected = rows
            .iter()
            .find(|r| Some(r.id.as_str()) == preferred)
            .or_else(|| rows.iter().find(|r| r.watched != Some(true)))
            .or_else(|| rows.first());
        Ok(json!({"episode":selected}))
    }
    pub(super) async fn continue_chapter(&self, args: &Value) -> Result<Value> {
        let slug = arg(args, "slug")?;
        let key = format!("chapter:{slug}");
        let saved = self.continuation.values.lock().unwrap().get(&key).cloned();
        let preferred = match saved {
            Some(Selection::Chapter { number }) => Some(number),
            _ => args
                .get("legacyChapter")
                .and_then(Value::as_f64)
                .filter(|n| n.is_finite() && *n >= 0.0),
        };
        let chapters = self.api.chapters(arg(args, "mediaId")?, Some(1000)).await?;
        let mut numbers: Vec<_> = chapters
            .docs
            .iter()
            .map(|c| c.chapter)
            .filter(|n| n.is_finite() && *n >= 0.0)
            .collect();
        numbers.sort_by(f64::total_cmp);
        numbers.dedup();
        let selected = preferred
            .filter(|n| numbers.contains(n))
            .or_else(|| numbers.first().copied());
        if let Some(number) = selected {
            self.continuation.remember_chapter(slug, number)?;
        }
        Ok(json!({"chapter":selected,"chapters":numbers}))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn selected_episode_survives_restart_and_invalid_values_do_not_replace_it() {
        let storage = Arc::new(Storage::new(None).unwrap());
        let choices = Continuation::new(storage.clone()).unwrap();
        choices
            .remember_episode(&json!({"mediaId":"title","episodeId":"ep","episodeType":"sub"}))
            .unwrap();
        assert!(
            choices
                .remember_episode(
                    &json!({"mediaId":"title","episodeId":"bad","episodeType":"unknown"})
                )
                .is_err()
        );
        let restored = Continuation::new(storage).unwrap();
        assert!(
            matches!(restored.values.lock().unwrap().get("episode:title"), Some(Selection::Episode{id,track}) if id=="ep" && track=="sub")
        );
    }
    #[test]
    fn failed_write_preserves_previous_selection() {
        let storage = Arc::new(Storage::new(None).unwrap());
        let choices = Continuation::new(storage.clone()).unwrap();
        choices.remember_chapter("title", 1.0).unwrap();
        let file = storage.root.join("continuation.json");
        std::fs::remove_file(&file).unwrap();
        std::fs::create_dir(&file).unwrap();
        assert!(choices.remember_chapter("title", 2.0).is_err());
        assert!(
            matches!(choices.values.lock().unwrap().get("chapter:title"), Some(Selection::Chapter{number}) if *number==1.0)
        );
        assert!(choices.remember_chapter("title", f64::INFINITY).is_err());
    }
}
