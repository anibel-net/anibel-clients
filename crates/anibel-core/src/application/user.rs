use super::{Application, storage::Storage};
use anibel_domain::error::{AnibelError, Result};
use serde::{Deserialize, Serialize};
use serde_json::{Value, json};
use std::sync::{Arc, Mutex};

#[derive(Clone, Default, Serialize)]
#[serde(rename_all = "camelCase")]
pub(super) struct Session {
    pub revision: u64,
    pub authenticated: bool,
    pub username: Option<String>,
    pub user_id: Option<String>,
    pub avatar: Option<String>,
}
impl Session {
    pub fn changed(&mut self, profile: Option<&Value>) -> Result<()> {
        self.revision = self
            .revision
            .checked_add(1)
            .ok_or_else(|| AnibelError::Internal("session revision capacity".into()))?;
        self.authenticated = profile.is_some();
        self.username = profile
            .and_then(|p| p.get("username"))
            .and_then(Value::as_str)
            .map(str::to_owned);
        self.user_id = profile
            .and_then(|p| p.get("id"))
            .and_then(Value::as_str)
            .map(str::to_owned);
        self.avatar = profile
            .and_then(|p| p.get("avatar"))
            .and_then(Value::as_str)
            .map(str::to_owned);
        Ok(())
    }
}

pub(super) struct SearchHistory {
    storage: Arc<Storage>,
    items: Mutex<Vec<String>>,
}
impl SearchHistory {
    pub fn new(storage: Arc<Storage>) -> Result<Self> {
        let items: Vec<String> = storage.load("search-history.json")?;
        if items.len() > 8 || items.iter().any(|q| q.len() > 4096) {
            return Err(AnibelError::Internal("invalid search history".into()));
        }
        Ok(Self {
            storage,
            items: Mutex::new(items),
        })
    }
    pub fn command(&self, action: &str, query: &str) -> Result<Value> {
        let mut items = self.items.lock().unwrap();
        let mut next = items.clone();
        let q = query.trim();
        match action {
            "add" => {
                if q.chars().count() >= 2 {
                    if q.len() > 4096 {
                        return Err(AnibelError::BadArgs("query is too long".into()));
                    }
                    next.retain(|s| s.to_lowercase() != q.to_lowercase());
                    next.insert(0, q.to_owned());
                    next.truncate(8);
                }
            }
            "remove" => next.retain(|s| s.to_lowercase() != q.to_lowercase()),
            "list" => {}
            _ => return Err(AnibelError::BadArgs("invalid search history action".into())),
        }
        if *items != next {
            self.storage.save("search-history.json", &next)?;
            *items = next;
        }
        Ok(json!(*items))
    }
}

#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub(super) enum PersonalList {
    Favorites,
    Inprogress,
    Done,
    Planned,
    Dropped,
}
impl PersonalList {
    fn matches(&self, s: Option<&str>) -> bool {
        match self {
            Self::Inprogress => matches!(s, Some("watching" | "reading" | "playing")),
            Self::Done => matches!(s, Some("watched" | "read" | "played")),
            Self::Planned => s == Some("planned"),
            Self::Dropped => s == Some("dropped"),
            Self::Favorites => false,
        }
    }
}
impl Application {
    pub(super) async fn profile_hub(&self) -> Result<Value> {
        let session = self.session.lock().unwrap().clone();
        let name = session.username.ok_or(AnibelError::Unauthorized)?;
        let favorites = self.api.favorites(&name, None, 0, 1).await?.total_docs;
        let mut counts = [0_i64; 4];
        let mut failed = Vec::new();
        for kind in ["anime", "manga", "cinema", "games", "books"] {
            match self.api.status(&name, kind).await {
                Ok(s) => {
                    for (total, value) in counts
                        .iter_mut()
                        .zip([s.watching, s.watched, s.planned, s.dropped])
                    {
                        *total = total
                            .checked_add(value.unwrap_or(0).max(0))
                            .ok_or_else(|| AnibelError::Decode("counter overflow".into()))?;
                    }
                }
                Err(AnibelError::Unauthorized) => return Err(AnibelError::Unauthorized),
                Err(_) => failed.push(kind),
            }
        }
        Ok(
            json!({"favorites":favorites,"inProgress":counts[0],"done":counts[1],"planned":counts[2],"dropped":counts[3],"unavailableTypes":failed}),
        )
    }
    pub(super) async fn personal_list(&self, kind: PersonalList) -> Result<Value> {
        let name = self
            .session
            .lock()
            .unwrap()
            .username
            .clone()
            .ok_or(AnibelError::Unauthorized)?;
        let mut items = Vec::new();
        let mut offset = 0;
        for _ in 0..100 {
            let (count, total) = if matches!(kind, PersonalList::Favorites) {
                let page = self.api.favorites(&name, None, offset, 100).await?;
                let count = page.docs.len();
                items.extend(page.docs);
                (count, page.total_docs)
            } else {
                let page = self.api.marks(&name, None, offset, 100).await?;
                let count = page.docs.len();
                items.extend(
                    page.docs
                        .into_iter()
                        .filter(|m| kind.matches(m.status.as_deref()))
                        .filter_map(|m| m.media),
                );
                (count, page.total_docs)
            };
            offset += count as i64;
            if count == 0 || offset >= total {
                return Ok(json!(items));
            }
        }
        Err(AnibelError::BadArgs(
            "personal list exceeds capacity".into(),
        ))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn search_history_is_bounded_deduplicated_and_durable() {
        let storage = Arc::new(Storage::new(None).unwrap());
        let h = SearchHistory::new(storage.clone()).unwrap();
        assert_eq!(h.command("add", " x ").unwrap(), json!([]));
        h.command("add", "  Hello  ").unwrap();
        assert_eq!(h.command("add", "hello").unwrap(), json!(["hello"]));
        for i in 0..12 {
            h.command("add", &format!("query{i}")).unwrap();
        }
        let rows = SearchHistory::new(storage.clone())
            .unwrap()
            .command("list", "")
            .unwrap();
        assert_eq!(rows.as_array().unwrap().len(), 8);
        assert_eq!(rows[0], "query11");
        assert_eq!(h.command("remove", "QUERY11").unwrap()[0], "query10");
        assert!(h.command("add", &"x".repeat(4097)).is_err());
    }
    #[test]
    fn all_content_statuses_share_personal_groups() {
        for s in ["watching", "reading", "playing"] {
            assert!(PersonalList::Inprogress.matches(Some(s)));
        }
        for s in ["watched", "read", "played"] {
            assert!(PersonalList::Done.matches(Some(s)));
        }
        assert!(PersonalList::Planned.matches(Some("planned")));
        assert!(!PersonalList::Dropped.matches(Some("watching")));
        assert!(!PersonalList::Done.matches(None));
    }
}
