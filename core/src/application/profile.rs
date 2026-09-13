use super::Application;
use anibel_domain::error::{AnibelError, Result};
use serde::Deserialize;
use serde_json::{Value, json};
use std::io::Read;

#[derive(Default, Deserialize)]
#[serde(rename_all = "camelCase", deny_unknown_fields)]
struct ProfilePatch {
    display_name: Option<String>,
    bio: Option<String>,
    avatar: Option<String>,
    wallpaper: Option<String>,
    avatar_path: Option<String>,
    wallpaper_path: Option<String>,
}

fn image_file(path: &str) -> Result<(Vec<u8>, &'static str, &'static str)> {
    const LIMIT: u64 = 10 * 1024 * 1024;
    let file =
        std::fs::File::open(path).map_err(|e| AnibelError::BadArgs(format!("Image: {e}")))?;
    if !file
        .metadata()
        .map_err(|e| AnibelError::BadArgs(e.to_string()))?
        .is_file()
    {
        return Err(AnibelError::BadArgs("Select an image file".into()));
    }
    let mut bytes = Vec::new();
    file.take(LIMIT + 1)
        .read_to_end(&mut bytes)
        .map_err(|e| AnibelError::BadArgs(e.to_string()))?;
    if bytes.len() as u64 > LIMIT {
        return Err(AnibelError::BadArgs("Image must be at most 10 MiB".into()));
    }
    let (mime, name) = if bytes.starts_with(b"\x89PNG\r\n\x1a\n") {
        ("image/png", "profile.png")
    } else if bytes.starts_with(&[0xff, 0xd8, 0xff]) {
        ("image/jpeg", "profile.jpg")
    } else if bytes.starts_with(b"GIF87a") || bytes.starts_with(b"GIF89a") {
        ("image/gif", "profile.gif")
    } else if bytes.starts_with(b"RIFF") && bytes.get(8..12) == Some(b"WEBP") {
        ("image/webp", "profile.webp")
    } else {
        return Err(AnibelError::BadArgs(
            "Select a PNG, JPEG, GIF, or WebP image".into(),
        ));
    };
    Ok((bytes, mime, name))
}

fn image_url(value: &str) -> Result<()> {
    if value.is_empty() {
        return Ok(());
    }
    let url =
        url::Url::parse(value).map_err(|_| AnibelError::BadArgs("Invalid image URL".into()))?;
    if value.len() > 2048
        || !matches!(url.scheme(), "http" | "https")
        || !url.username().is_empty()
        || url.password().is_some()
    {
        return Err(AnibelError::BadArgs(
            "Use an HTTP or HTTPS image URL".into(),
        ));
    }
    Ok(())
}

impl Application {
    pub(super) async fn profile_view(&self, username: Option<&str>) -> Result<Value> {
        let session = self.session.lock().unwrap().clone();
        let username = username
            .filter(|s| !s.trim().is_empty())
            .map(str::to_owned)
            .or(session.username)
            .ok_or(AnibelError::Unauthorized)?;
        let profile = self
            .api
            .user(&username)
            .await?
            .ok_or_else(|| AnibelError::BadArgs("Profile not found".into()))?;
        let own = session.authenticated && session.user_id.as_deref() == Some(profile.id.as_str());
        Ok(json!({"profile": profile, "isOwn": own}))
    }

    pub(super) async fn update_profile(&self, args: &Value) -> Result<Value> {
        let session = self.session.lock().unwrap().clone();
        if !session.authenticated {
            return Err(AnibelError::Unauthorized);
        }
        let id = session.user_id.ok_or(AnibelError::Unauthorized)?;
        let patch: ProfilePatch = serde_json::from_value(args.clone())
            .map_err(|e| AnibelError::BadArgs(e.to_string()))?;
        if patch.bio.as_ref().is_some_and(|s| s.chars().count() > 2000)
            || patch
                .display_name
                .as_ref()
                .is_some_and(|s| s.chars().count() > 100)
        {
            return Err(AnibelError::BadArgs("Profile text is too long".into()));
        }
        let mut input = json!({"id": id});
        if let Some(value) = patch.display_name {
            input["displayName"] = json!(value.trim());
        }
        if let Some(value) = patch.bio {
            input["bio"] = json!(value.trim());
        }
        for (field, value) in [("avatar", patch.avatar), ("wallpaper", patch.wallpaper)] {
            if let Some(value) = value {
                image_url(&value)?;
                input[field] = json!(value);
            }
        }
        // Validate both selected files before the first upload.
        let avatar = patch.avatar_path.as_deref().map(image_file).transpose()?;
        let wallpaper = patch
            .wallpaper_path
            .as_deref()
            .map(image_file)
            .transpose()?;
        for (field, image) in [("avatar", avatar), ("wallpaper", wallpaper)] {
            if let Some((bytes, mime, name)) = image {
                input[field] = json!(self.api.upload_profile_image(bytes, mime, name).await?);
            }
        }
        let acknowledged = self.api.update_profile(input).await?;
        if acknowledged.id != id {
            return Err(AnibelError::Decode(
                "Profile identity changed unexpectedly".into(),
            ));
        }
        // The backend returns the pre-update document. Invalidate before readback
        // so a readback error cannot leave a successful write hidden by cache.
        let saved = self.cache().clear();
        self.report_storage(saved);
        let profile = self
            .api
            .user(&session.username.ok_or(AnibelError::Unauthorized)?)
            .await?
            .ok_or_else(|| AnibelError::Decode("Saved profile could not be reloaded".into()))?;
        if profile.id != id {
            return Err(AnibelError::Decode(
                "Profile identity changed unexpectedly".into(),
            ));
        }
        Ok(json!(profile))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn image_validation_checks_content_and_size() {
        let file = tempfile::NamedTempFile::new().unwrap();
        std::fs::write(file.path(), b"not an image").unwrap();
        assert!(image_file(file.path().to_str().unwrap()).is_err());
        file.as_file().set_len(10 * 1024 * 1024 + 1).unwrap();
        assert!(image_file(file.path().to_str().unwrap()).is_err());
        assert!(image_url("javascript:alert(1)").is_err());
        assert!(image_url("https://user:password@example.org/a.png").is_err());
        assert!(image_url("").is_ok());
    }
}
