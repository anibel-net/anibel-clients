use super::*;

impl Application {
    pub(super) async fn open_reader(self: &Arc<Self>, args: &Value) -> Result<Value> {
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
        self.continuation.remember_chapter(slug, number)?;
        if !chapter.id.is_empty() {
            let app = self.clone();
            let id = chapter.id.clone();
            tokio::spawn(async move {
                app.submit_history(&id, "chapter", revision).await;
            });
        }
        Ok(json!({"chapter":chapter,"previous":previous,"next":next}))
    }
}
