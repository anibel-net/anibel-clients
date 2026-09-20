//! Supported offline HLS: finite, unencrypted TS or fMP4 playlists.
//! Unsupported constructs fail before downloading segments.
use super::storage::{atomic_write, extension, fetch_file, http_error};
use anibel_domain::error::{AnibelError, Result};
use futures_util::{StreamExt, stream};
use std::path::Path;

const MAX_SEGMENTS: usize = 100_000;
const MAX_PLAYLIST: usize = 4 * 1024 * 1024;
pub(super) const MAX_JOB_BYTES: u64 = 32 * 1024 * 1024 * 1024;

#[derive(Debug)]
struct Media {
    init: Option<String>,
    segments: Vec<(String, f64)>,
    target: u64,
}
fn unsupported() -> AnibelError {
    AnibelError::BadArgs("unsupported HLS playlist".into())
}

fn attributes(line: &str) -> Result<Vec<(String, String)>> {
    let raw = line.split_once(':').ok_or_else(unsupported)?.1;
    let mut parts = Vec::new();
    let mut quoted = false;
    let mut start = 0;
    for (i, c) in raw.char_indices() {
        if c == '"' {
            quoted = !quoted;
        }
        if c == ',' && !quoted {
            parts.push(&raw[start..i]);
            start = i + 1;
        }
    }
    if quoted {
        return Err(unsupported());
    }
    parts.push(&raw[start..]);
    parts
        .into_iter()
        .map(|p| {
            let (k, v) = p.trim().split_once('=').ok_or_else(unsupported)?;
            Ok((k.to_owned(), v.trim_matches('"').to_owned()))
        })
        .collect()
}
fn attr(line: &str, key: &str) -> Result<Option<String>> {
    Ok(attributes(line)?
        .into_iter()
        .find(|(k, _)| k == key)
        .map(|(_, v)| v))
}
fn resolve(base: &str, relative: &str) -> Result<String> {
    let url = url::Url::parse(base)
        .and_then(|u| u.join(relative))
        .map_err(|_| unsupported())?;
    if !matches!(url.scheme(), "http" | "https") {
        return Err(unsupported());
    }
    Ok(url.into())
}
fn variant(text: &str, base: &str) -> Result<Option<String>> {
    let lines: Vec<_> = text.lines().map(str::trim).collect();
    let mut best: Option<(u64, String)> = None;
    for (i, line) in lines.iter().enumerate() {
        if line.starts_with("#EXT-X-MEDIA:") && attr(line, "URI")?.is_some() {
            // A separate rendition requires a local master manifest. Do not silently lose audio.
            return Err(unsupported());
        }
        if line.starts_with("#EXT-X-STREAM-INF:") {
            let bw = attr(line, "BANDWIDTH")?
                .and_then(|s| s.parse::<u64>().ok())
                .ok_or_else(unsupported)?;
            let uri = lines
                .get(i + 1)
                .filter(|s| !s.is_empty() && !s.starts_with('#'))
                .ok_or_else(unsupported)?;
            if best.as_ref().is_none_or(|(n, _)| bw > *n) {
                best = Some((bw, resolve(base, uri)?));
            }
        }
    }
    Ok(best.map(|(_, u)| u))
}
fn parse(text: &str, base: &str) -> Result<Media> {
    if !text.trim_start().starts_with("#EXTM3U")
        || !text.lines().any(|l| l.trim() == "#EXT-X-ENDLIST")
    {
        return Err(unsupported());
    }
    let mut media = Media {
        init: None,
        segments: Vec::new(),
        target: 0,
    };
    let mut duration = None;
    for line in text.lines().map(str::trim) {
        if line.starts_with("#EXT-X-KEY:") && attr(line, "METHOD")?.as_deref() != Some("NONE") {
            return Err(unsupported());
        }
        if line.starts_with("#EXT-X-BYTERANGE")
            || line.starts_with("#EXT-X-DISCONTINUITY")
            || line.starts_with("#EXT-X-GAP")
            || line.starts_with("#EXT-X-PART")
        {
            return Err(unsupported());
        }
        if line.starts_with("#EXT-X-MAP:") {
            if media.init.is_some()
                || !media.segments.is_empty()
                || attr(line, "BYTERANGE")?.is_some()
            {
                return Err(unsupported());
            }
            media.init = Some(resolve(base, &attr(line, "URI")?.ok_or_else(unsupported)?)?);
        } else if let Some(s) = line.strip_prefix("#EXT-X-TARGETDURATION:") {
            media.target = s.parse().map_err(|_| unsupported())?;
        } else if let Some(s) = line.strip_prefix("#EXTINF:") {
            let n = s
                .split(',')
                .next()
                .unwrap_or("")
                .parse::<f64>()
                .map_err(|_| unsupported())?;
            if !n.is_finite() || n <= 0.0 || n > 86400.0 {
                return Err(unsupported());
            }
            duration = Some(n);
        } else if !line.is_empty() && !line.starts_with('#') {
            if media.segments.len() >= MAX_SEGMENTS {
                return Err(unsupported());
            }
            media.segments.push((
                resolve(base, line)?,
                duration.take().ok_or_else(unsupported)?,
            ));
        }
    }
    if media.segments.is_empty() || media.target == 0 || duration.is_some() {
        return Err(unsupported());
    }
    Ok(media)
}

pub(super) async fn download(
    http: &reqwest::Client,
    initial: &str,
    dir: &Path,
    progress: impl Fn(u64, u64, u64),
) -> Result<Vec<String>> {
    let mut url = initial.to_owned();
    let mut text = String::new();
    for depth in 0..4 {
        let mut response = http
            .get(&url)
            .send()
            .await
            .map_err(http_error)?
            .error_for_status()
            .map_err(http_error)?;
        url = response.url().to_string();
        let mut body = Vec::new();
        while let Some(chunk) = response.chunk().await.map_err(http_error)? {
            if body.len().saturating_add(chunk.len()) > MAX_PLAYLIST {
                return Err(unsupported());
            }
            body.extend_from_slice(&chunk);
        }
        text = String::from_utf8(body).map_err(|_| unsupported())?;
        if let Some(next) = variant(&text, &url)? {
            if depth == 3 {
                return Err(unsupported());
            }
            url = next;
        } else {
            break;
        }
    }
    let media = parse(&text, &url)?;
    let mut assets = Vec::new();
    let mut manifest = format!(
        "#EXTM3U\n#EXT-X-VERSION:7\n#EXT-X-TARGETDURATION:{}\n#EXT-X-MEDIA-SEQUENCE:0\n#EXT-X-PLAYLIST-TYPE:VOD\n",
        media.target
    );
    if let Some(init) = media.init {
        assets.push((init, "segs/init.mp4".to_owned()));
        manifest.push_str("#EXT-X-MAP:URI=\"segs/init.mp4\"\n");
    }
    for (i, (url, duration)) in media.segments.iter().enumerate() {
        let name = format!("segs/{i:06}{}", extension(url, ".ts"));
        assets.push((url.clone(), name.clone()));
        manifest.push_str(&format!("#EXTINF:{duration},\n{name}\n"));
    }
    manifest.push_str("#EXT-X-ENDLIST\n");
    let mut paths: Vec<String> = assets.iter().map(|(_, name)| name.clone()).collect();
    paths.push("playlist.m3u8".into());
    let total = assets.len() as u64;
    let mut pending = stream::iter(assets.into_iter().map(|(url, name)| async move {
        fetch_file(http, &url, &dir.join(name), 256 * 1024 * 1024).await
    }))
    .buffer_unordered(4);
    let mut bytes = 0_u64;
    let mut done = 0;
    while let Some(result) = pending.next().await {
        bytes = bytes
            .checked_add(result?)
            .filter(|n| *n <= MAX_JOB_BYTES)
            .ok_or_else(unsupported)?;
        done += 1;
        progress(done, total, bytes);
    }
    atomic_write(&dir.join("playlist.m3u8"), manifest.as_bytes())?;
    Ok(paths)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn rejects_lossy_playlist_rewrites() {
        for tag in [
            "#EXT-X-BYTERANGE:20@0",
            "#EXT-X-DISCONTINUITY",
            "#EXT-X-KEY:METHOD=AES-128,URI=\"key\"",
        ] {
            assert!(
                parse(
                    &format!(
                        "#EXTM3U\n#EXT-X-TARGETDURATION:6\n{tag}\n#EXTINF:6,\nx.ts\n#EXT-X-ENDLIST"
                    ),
                    "https://test/a/list.m3u8"
                )
                .is_err()
            );
        }
    }
    #[test]
    fn finite_fmp4_and_master_selection() {
        let p=parse("#EXTM3U\n#EXT-X-TARGETDURATION:6\n#EXT-X-MAP:URI=\"init.mp4\"\n#EXTINF:6,\nx.m4s\n#EXT-X-ENDLIST","https://test/a/list.m3u8").unwrap();
        assert_eq!(p.init.as_deref(), Some("https://test/a/init.mp4"));
        assert_eq!(variant("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=20\nhigh.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=10\nlow.m3u8","https://test/a/master.m3u8").unwrap().as_deref(),Some("https://test/a/high.m3u8"));
    }
}
