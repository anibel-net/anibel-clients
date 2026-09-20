use super::*;
impl Downloads {
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
