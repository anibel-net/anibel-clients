use super::{
    downloads::{Downloads, Kind},
    storage::{Storage, digest},
};
use anibel_domain::error::{AnibelError, Result};
use anibel_player::{
    player::{self, PlaybackIntent},
    video::VideoService,
};
use serde::{Deserialize, Serialize};
use serde_json::{Value, json};
use std::{
    collections::HashMap,
    sync::{Arc, Mutex},
    time::{Duration, Instant},
};

#[derive(Clone, Copy, PartialEq)]
enum Phase {
    Loading,
    Playing,
    Ended,
}
#[derive(Clone)]
struct Session {
    episode: Option<String>,
    account_revision: u64,
    phase: Phase,
    last_saved: Option<Instant>,
    last_sequence: u64,
}
#[derive(Default)]
struct State {
    next_id: u64,
    sessions: HashMap<u64, Session>,
    resume: HashMap<String, f64>,
}
pub(super) struct Playback {
    storage: Arc<Storage>,
    assets: tokio::sync::RwLock<()>,
    state: Mutex<State>,
}
#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub(super) struct Open {
    #[serde(default)]
    pub url: String,
    pub episode_id: Option<String>,
    pub download_id: Option<String>,
    #[serde(default)]
    pub prefer_dub: Option<bool>,
    pub episode_type: Option<String>,
    #[serde(default)]
    pub prefer_dash: bool,
}
#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub(super) struct Report {
    pub session_id: u64,
    pub sequence: u64,
    pub event: Event,
    #[serde(default)]
    pub position: f64,
    #[serde(default)]
    pub duration: f64,
}
#[derive(Deserialize)]
#[serde(rename_all = "camelCase")]
pub(super) enum Event {
    Ready,
    Position,
    Ended,
    Close,
}
#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
pub(super) struct Action {
    pub seek_to: Option<f64>,
    #[serde(skip)]
    pub history: Option<(String, u64)>,
}

impl Playback {
    pub fn new(storage: Arc<Storage>) -> Result<Self> {
        let resume: HashMap<String, f64> = storage.load("resume.json")?;
        if resume.len() > 10000 || resume.values().any(|p| !p.is_finite() || *p < 0.0) {
            return Err(AnibelError::Internal("invalid resume data".into()));
        }
        Ok(Self {
            storage,
            assets: tokio::sync::RwLock::new(()),
            state: Mutex::new(State {
                resume,
                ..Default::default()
            }),
        })
    }
    pub async fn open(
        &self,
        args: Open,
        downloads: &Downloads,
        video: &VideoService,
        revision: u64,
    ) -> Result<Value> {
        let _assets = self.assets.read().await;
        let local = if let Some(id) = &args.download_id {
            Some(downloads.by_id(id)?)
        } else {
            args.episode_id
                .as_deref()
                .and_then(|id| downloads.find_episode(id, false))
        };
        let episode = local
            .as_ref()
            .and_then(|r| r.request.episode_id.clone())
            .or(args.episode_id);
        let episode_type = local
            .as_ref()
            .and_then(|r| r.request.episode_type.as_deref())
            .or(args.episode_type.as_deref());
        let prefer_dub = args
            .prefer_dub
            .unwrap_or_else(|| episode_type.is_some_and(|s| s.eq_ignore_ascii_case("dub")));
        let (intent, subs, config_dir) = if let Some(local) = local {
            if !downloads.available(&local) {
                return Err(AnibelError::NotFound("download assets".into()));
            }
            let source = if local.kind == Kind::Audio {
                local.assets.audio_path.as_ref()
            } else {
                local.assets.video_path.as_ref()
            }
            .ok_or_else(|| AnibelError::BadArgs("download is not playable media".into()))?;
            let video_src = downloads.absolute(source)?;
            let config = std::path::Path::new(&video_src)
                .parent()
                .unwrap()
                .to_string_lossy()
                .into_owned();
            let subs = local
                .assets
                .subtitle_paths
                .iter()
                .map(|p| downloads.absolute(p))
                .collect::<Result<Vec<_>>>()?;
            (
                PlaybackIntent {
                    kind: player::PlaybackKind::Native,
                    video_src: Some(video_src),
                    audio_src: if local.kind == Kind::Video {
                        local
                            .assets
                            .audio_path
                            .as_ref()
                            .map(|p| downloads.absolute(p))
                            .transpose()?
                    } else {
                        None
                    },
                    sub_src: subs.first().cloned(),
                    ..Default::default()
                },
                subs,
                config,
            )
        } else {
            let intent = player::resolve_intent(
                video,
                &json!({"url":args.url,"preferDash":args.prefer_dash}),
            )
            .await?;
            let folder = format!("playback/{}", digest(&args.url));
            let config = self.storage.path(&folder)?;
            std::fs::create_dir_all(config.join("fonts")).map_err(super::storage::io_error)?;
            let (subs, _) = downloads.prepare_assets(&intent, &folder).await?;
            let paths = subs
                .iter()
                .map(|p| downloads.absolute(p))
                .collect::<Result<Vec<_>>>()?;
            (intent, paths, config.to_string_lossy().into_owned())
        };
        // Keep every track available for manual selection; the player applies the initial preference.
        let mut state = self.state.lock().unwrap();
        if state.sessions.len() >= 8 {
            return Err(AnibelError::BadArgs("too many playback sessions".into()));
        }
        let id = state
            .next_id
            .checked_add(1)
            .ok_or_else(|| AnibelError::Internal("playback id capacity".into()))?;
        state.next_id = id;
        state.sessions.insert(
            id,
            Session {
                episode,
                account_revision: revision,
                phase: Phase::Loading,
                last_saved: None,
                last_sequence: 0,
            },
        );
        Ok(
            json!({"sessionId":id,"preferDub":prefer_dub,"intent":intent,"subtitlePaths":subs,"configDirectory":config_dir}),
        )
    }
    pub fn report(&self, r: Report) -> Result<Action> {
        if !r.position.is_finite()
            || r.position < 0.0
            || !r.duration.is_finite()
            || r.duration < 0.0
        {
            return Err(AnibelError::BadArgs("invalid playback position".into()));
        }
        let mut state = self.state.lock().unwrap();
        let mut action = Action {
            seek_to: None,
            history: None,
        };
        let Some(mut session) = state.sessions.get(&r.session_id).cloned() else {
            return Ok(action);
        };
        if r.sequence <= session.last_sequence {
            return Ok(action);
        }
        session.last_sequence = r.sequence;
        let mut updated_resume = None;
        match r.event {
            Event::Ready if session.phase == Phase::Loading => {
                session.phase = Phase::Playing;
                if let Some(id) = &session.episode {
                    let position = state.resume.get(id).copied().unwrap_or_default();
                    if position > 30.0 && position < r.duration - 15.0 {
                        action.seek_to = Some((position - 5.0).max(0.0));
                    }
                    action.history = Some((id.clone(), session.account_revision));
                }
            }
            Event::Ended if session.phase != Phase::Ended => {
                session.phase = Phase::Ended;
                if let Some(id) = &session.episode {
                    if state.resume.contains_key(id) {
                        let mut next = state.resume.clone();
                        next.remove(id);
                        updated_resume = Some(next);
                    }
                    action.history = Some((id.clone(), session.account_revision));
                }
            }
            Event::Position | Event::Close if session.phase != Phase::Ended => {
                let save = matches!(r.event, Event::Close)
                    || session
                        .last_saved
                        .is_none_or(|t| t.elapsed() >= Duration::from_secs(5));
                if save
                    && r.position >= 5.0
                    && let Some(id) = &session.episode
                {
                    if state.resume.len() >= 10000 && !state.resume.contains_key(id) {
                        return Err(AnibelError::Internal("resume store is full".into()));
                    }
                    let mut next = state.resume.clone();
                    next.insert(id.clone(), r.position);
                    updated_resume = Some(next);
                    session.last_saved = Some(Instant::now());
                }
            }
            _ => {}
        }
        if let Some(next) = updated_resume {
            self.storage.save("resume.json", &next)?;
            state.resume = next;
        }
        if matches!(r.event, Event::Close) {
            state.sessions.remove(&r.session_id);
        } else {
            state.sessions.insert(r.session_id, session);
        }
        Ok(action)
    }
    pub async fn clear_assets(&self) -> Result<()> {
        let _assets = self.assets.write().await;
        if !self.state.lock().unwrap().sessions.is_empty() {
            return Err(AnibelError::BadArgs(
                "close playback before clearing assets".into(),
            ));
        }
        let root = self.storage.path("playback")?;
        if root.exists() {
            std::fs::remove_dir_all(root).map_err(super::storage::io_error)?;
        }
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    fn setup(position: f64) -> Playback {
        let storage = Arc::new(Storage::new(None).unwrap());
        storage
            .save("resume.json", &HashMap::from([("ep".to_owned(), position)]))
            .unwrap();
        let p = Playback::new(storage).unwrap();
        p.state.lock().unwrap().sessions.insert(
            1,
            Session {
                episode: Some("ep".into()),
                account_revision: 7,
                phase: Phase::Loading,
                last_saved: None,
                last_sequence: 0,
            },
        );
        p
    }
    fn report(p: &Playback, sequence: u64, event: Event, position: f64) -> Result<Action> {
        p.report(Report {
            session_id: 1,
            sequence,
            event,
            position,
            duration: 600.0,
        })
    }
    #[test]
    fn resume_bounds_and_ready_once() {
        for (position, expected) in [
            (4.0, None),
            (30.0, None),
            (100.0, Some(95.0)),
            (585.0, None),
            (1000.0, None),
        ] {
            let p = setup(position);
            let a = report(&p, 1, Event::Ready, 0.0).unwrap();
            assert_eq!(a.seek_to, expected);
            assert_eq!(a.history, Some(("ep".into(), 7)));
            assert!(report(&p, 2, Event::Ready, 0.0).unwrap().history.is_none());
        }
    }
    #[test]
    fn close_forces_save_and_late_reports_cannot_restore_ended_resume() {
        let p = setup(100.0);
        report(&p, 1, Event::Position, 20.0).unwrap();
        report(&p, 2, Event::Position, 25.0).unwrap();
        assert_eq!(p.state.lock().unwrap().resume["ep"], 20.0);
        report(&p, 3, Event::Close, 25.0).unwrap();
        assert_eq!(p.state.lock().unwrap().resume["ep"], 25.0);
        assert!(p.state.lock().unwrap().sessions.is_empty());
        report(&p, 4, Event::Position, 30.0).unwrap();
        assert_eq!(p.state.lock().unwrap().resume["ep"], 25.0);
        let p = setup(100.0);
        assert!(
            report(&p, 2, Event::Ended, 600.0)
                .unwrap()
                .history
                .is_some()
        );
        assert!(
            report(&p, 3, Event::Ended, 600.0)
                .unwrap()
                .history
                .is_none()
        );
        report(&p, 1, Event::Position, 50.0).unwrap();
        report(&p, 4, Event::Close, 600.0).unwrap();
        assert!(
            Playback::new(p.storage.clone())
                .unwrap()
                .state
                .lock()
                .unwrap()
                .resume
                .is_empty()
        );
    }
    #[test]
    fn storage_failure_does_not_commit_session_or_resume() {
        let p = setup(100.0);
        std::fs::remove_file(p.storage.root.join("resume.json")).unwrap();
        std::fs::create_dir(p.storage.root.join("resume.json")).unwrap();
        assert!(report(&p, 1, Event::Close, 40.0).is_err());
        assert_eq!(p.state.lock().unwrap().resume["ep"], 100.0);
        assert_eq!(p.state.lock().unwrap().sessions[&1].last_sequence, 0);
        std::fs::remove_dir(p.storage.root.join("resume.json")).unwrap();
        report(&p, 1, Event::Close, 40.0).unwrap();
        assert_eq!(
            Playback::new(p.storage.clone())
                .unwrap()
                .state
                .lock()
                .unwrap()
                .resume["ep"],
            40.0
        );
    }
    #[test]
    fn invalid_positions_and_short_positions_do_not_change_resume() {
        let p = setup(100.0);
        assert!(report(&p, 1, Event::Position, f64::NAN).is_err());
        assert!(report(&p, 1, Event::Position, -1.0).is_err());
        report(&p, 1, Event::Close, 3.0).unwrap();
        assert_eq!(p.state.lock().unwrap().resume["ep"], 100.0);
    }
}
