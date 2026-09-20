use super::*;
use model::Phase;
impl Downloads {
    pub fn snapshot(&self, r: &Record) -> Value {
        let mut value = serde_json::to_value(&r.request).unwrap();
        let obj = value.as_object_mut().unwrap();
        let ready = self.available(r);
        let absolute = |p: &Option<String>| p.as_ref().and_then(|p| self.absolute(p).ok());
        let progress = if ready {
            1.0
        } else if r.live.media.duration > 0.0 {
            r.live.media.processed / r.live.media.duration
        } else if r.live.total_bytes.is_some_and(|n| n > 0) {
            r.bytes as f64 / r.live.total_bytes.unwrap() as f64
        } else if r.parts_total > 0 {
            r.parts_done as f64 / r.parts_total as f64
        } else {
            0.0
        };
        let progress = progress.clamp(0.0, 1.0);
        let elapsed = r
            .live
            .started
            .map(|t| t.elapsed().as_secs_f64())
            .unwrap_or(0.0);
        let speed = if r.status.active() && elapsed >= 1.0 {
            r.bytes as f64 / elapsed
        } else {
            0.0
        };
        let remaining = if r.status.active() && elapsed >= 1.0 && progress > 0.0 && progress < 1.0 {
            Some((elapsed * (1.0 - progress) / progress).ceil().min(604800.0) as u64)
        } else {
            None
        };
        let phase = if !r.status.active() {
            None
        } else if r.status == Status::Queued {
            Some(Phase::Queued)
        } else if progress >= 1.0 {
            Some(Phase::Finalizing)
        } else if r.bytes > 0 || r.parts_done > 0 {
            Some(Phase::Downloading)
        } else {
            Some(Phase::Preparing)
        };
        let fields = json!({"id":r.id,"kind":r.kind,"status":if r.status==Status::Completed&&!ready {Status::Failed}else{r.status},
            "subtitle":r.request.subtitle.as_ref().or(r.request.chapter_title.as_ref()).cloned().unwrap_or_else(||r.request.episode_label.clone()),
            "error":if r.status==Status::Completed&&!ready {Some("download assets are missing".to_owned())}else{r.error.clone()},
            "bytesReceived":r.bytes,"diskBytes":r.bytes,"partsDone":r.parts_done,"partsTotal":r.parts_total,
            "progress":progress,"progressKnown":ready||r.live.media.duration>0.0||r.live.total_bytes.is_some_and(|n| n>0)||r.parts_total>0,
            "bytesTotal":r.live.total_bytes,"bytesPerSecond":speed,"remainingSeconds":remaining,"phase":phase,
            "posterPath":absolute(&r.assets.poster_path),
            "canPlay":ready&&r.kind!=Kind::File,"canSave":ready,"canRetry":matches!(r.status,Status::Failed|Status::Cancelled)||r.status==Status::Completed&&!ready});
        obj.remove("fileUrl");
        obj.remove("episodeUrl");
        obj.extend(fields.as_object().unwrap().clone());
        value
    }
    pub(super) fn progress(&self, id: &str, done: u64, total: u64, bytes: u64) {
        if let Some(r) = self.records.lock().unwrap().iter_mut().find(|r| r.id == id) {
            r.parts_done = done;
            r.parts_total = total;
            r.bytes = bytes;
        }
    }
}
