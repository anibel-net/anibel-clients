//! Portable downloads use the host's FFmpeg tools; playback remains host-side.
use super::{hls::MAX_JOB_BYTES, storage::io_error};
use anibel_domain::error::{AnibelError, Result};
use serde::Deserialize;
use std::{
    path::{Path, PathBuf},
    process::{Child, Command, Stdio},
    time::Duration,
};

// Dropping a cancelled job must stop writes before Downloads deletes its folder.
struct Process(Child);
impl Drop for Process {
    fn drop(&mut self) {
        let _ = self.0.kill();
        let _ = self.0.wait();
    }
}

fn command(tool: &str) -> Command {
    let name = format!("{tool}{}", std::env::consts::EXE_SUFFIX);
    let sibling = std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(|p| p.join(&name)));
    let mut command = Command::new(
        sibling
            .filter(|p| p.is_file())
            .unwrap_or_else(|| PathBuf::from(name)),
    );
    #[cfg(windows)]
    {
        use std::os::windows::process::CommandExt;
        command.creation_flags(0x08000000); // CREATE_NO_WINDOW
    }
    command.stdin(Stdio::null());
    command
}

async fn run(
    mut command: Command,
    observed: &Path,
    limit: u64,
    progress: &impl Fn(u64),
) -> Result<()> {
    let errors = tempfile::NamedTempFile::new_in(observed.parent().unwrap()).map_err(io_error)?;
    command.stderr(errors.reopen().map_err(io_error)?);
    let mut child = Process(command.spawn().map_err(|e| {
        AnibelError::Internal(format!(
            "FFmpeg/ffprobe could not start. Install FFmpeg or place its tools beside the app: {e}"
        ))
    })?);
    loop {
        let bytes = observed.metadata().map(|m| m.len()).unwrap_or(0);
        if bytes > limit {
            return Err(AnibelError::BadArgs("download exceeds size limit".into()));
        }
        if errors.as_file().metadata().map_err(io_error)?.len() > 1024 * 1024 {
            return Err(AnibelError::Transport(
                "FFmpeg produced too many errors".into(),
            ));
        }
        progress(bytes);
        if let Some(status) = child.0.try_wait().map_err(io_error)? {
            return if status.success() {
                Ok(())
            } else {
                let details = std::fs::read_to_string(errors.path()).unwrap_or_default();
                Err(AnibelError::Transport(format!(
                    "FFmpeg could not finish the MKV ({status}): {}",
                    details.chars().take(2000).collect::<String>()
                )))
            };
        }
        tokio::time::sleep(Duration::from_millis(250)).await;
    }
}

#[derive(Deserialize)]
struct Probe {
    streams: Vec<Stream>,
}
#[derive(Deserialize)]
struct Stream {
    index: u32,
    codec_type: String,
    #[serde(default)]
    codec_name: String,
    #[serde(default)]
    width: u32,
    #[serde(default)]
    height: u32,
}

fn video_index(probe: &Probe) -> Result<u32> {
    probe
        .streams
        .iter()
        .filter(|s| s.codec_type == "video")
        .max_by_key(|s| u64::from(s.width) * u64::from(s.height))
        .map(|s| s.index)
        .ok_or_else(|| AnibelError::NotFound("video track".into()))
}

pub(super) async fn download(
    source: &str,
    audio: Option<&str>,
    subtitles: &[String],
    fonts: &[String],
    output: &Path,
    title: &str,
    progress: impl Fn(u64),
) -> Result<()> {
    let parent = output
        .parent()
        .ok_or_else(|| AnibelError::BadArgs("MKV output folder".into()))?;
    let probe_file = tempfile::NamedTempFile::new_in(parent).map_err(io_error)?;
    let mut probe = command("ffprobe");
    probe
        .args([
            "-v",
            "error",
            "-rw_timeout",
            "15000000",
            "-show_entries",
            "stream=index,codec_type,codec_name,width,height",
            "-of",
            "json",
            source,
        ])
        .stdout(probe_file.reopen().map_err(io_error)?)
        .stderr(Stdio::null());
    run(probe, probe_file.path(), 4 * 1024 * 1024, &|_| {}).await?;
    let data: Probe = serde_json::from_slice(&std::fs::read(probe_file.path()).map_err(io_error)?)
        .map_err(|e| AnibelError::Transport(format!("invalid media tracks: {e}")))?;
    let video = video_index(&data)?;
    let temporary = tempfile::Builder::new()
        .suffix(".mkv")
        .tempfile_in(parent)
        .map_err(io_error)?;
    let mut mux = command("ffmpeg");
    mux.args([
        "-nostdin",
        "-v",
        "error",
        "-y",
        "-rw_timeout",
        "15000000",
        "-i",
        source,
    ]);
    if let Some(audio) = audio.filter(|a| *a != source) {
        mux.args(["-rw_timeout", "15000000", "-i", audio]);
    }
    for sub in subtitles {
        mux.arg("-i").arg(sub);
    }
    mux.args([
        "-map",
        &format!("0:{video}"),
        "-map",
        "0:a?",
        "-map",
        "0:s?",
        "-map",
        "0:t?",
    ]);
    let audio_input = usize::from(audio.is_some_and(|a| a != source));
    if audio_input > 0 {
        mux.args(["-map", "1:a?"]);
    }
    for i in 0..subtitles.len() {
        mux.args(["-map", &format!("{}:s:0", i + 1 + audio_input)]);
    }
    mux.args(["-c", "copy", "-metadata", &format!("title={title}")]);
    let embedded_subs: Vec<_> = data
        .streams
        .iter()
        .filter(|s| s.codec_type == "subtitle")
        .collect();
    for (i, sub) in embedded_subs.iter().enumerate() {
        if sub.codec_name == "mov_text" {
            mux.args([&format!("-c:s:{i}"), "srt"]);
        }
    }
    for (i, sub) in subtitles.iter().enumerate() {
        let label = Path::new(sub)
            .file_stem()
            .unwrap_or_default()
            .to_string_lossy();
        let mut parts = label.splitn(3, '-');
        let label = if parts.next() == Some("sub")
            && parts
                .next()
                .is_some_and(|index| index.parse::<usize>().is_ok())
        {
            parts.next().unwrap_or(&label)
        } else {
            &label
        };
        mux.args([
            &format!("-metadata:s:s:{}", embedded_subs.len() + i),
            &format!("title={label}"),
        ]);
    }
    let embedded_fonts = data
        .streams
        .iter()
        .filter(|s| s.codec_type == "attachment")
        .count();
    for (i, font) in fonts.iter().enumerate() {
        let path = Path::new(font);
        let mime = if path
            .extension()
            .is_some_and(|e| e.eq_ignore_ascii_case("otf"))
        {
            "application/vnd.ms-opentype"
        } else {
            "application/x-truetype-font"
        };
        mux.arg("-attach").arg(path).args([
            &format!("-metadata:s:t:{}", embedded_fonts + i),
            &format!("mimetype={mime}"),
        ]);
    }
    mux.args(["-f", "matroska"])
        .arg(temporary.path())
        .stdout(Stdio::null())
        .stderr(Stdio::null());
    run(mux, temporary.path(), MAX_JOB_BYTES, &progress).await?;
    if temporary.as_file().metadata().map_err(io_error)?.len() == 0 {
        return Err(AnibelError::Transport("empty MKV output".into()));
    }
    temporary.persist(output).map_err(|e| io_error(e.error))?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    // Fixture generation needs encoders that the shipped remux-only build omits.
    fn fixture_ffmpeg() -> Command {
        std::env::var_os("ANIBEL_TEST_FFMPEG")
            .map(Command::new)
            .unwrap_or_else(|| command("ffmpeg"))
    }
    #[tokio::test]
    #[ignore = "requires host ffmpeg"]
    async fn cancellation_stops_output_before_returning() {
        let dir = tempfile::tempdir().unwrap();
        let output = dir.path().join("cancel.mkv");
        let input = dir.path().join("input.mkv");
        assert!(fixture_ffmpeg()
            .args([
                "-v",
                "error",
                "-y",
                "-f",
                "lavfi",
                "-i",
                "testsrc=size=64x64:rate=30",
                "-t",
                "1",
                "-c:v",
                "libx264",
                "-pix_fmt",
                "yuv420p"
            ])
            .arg(&input)
            .status()
            .unwrap()
            .success());
        let mut cmd = command("ffmpeg");
        cmd.args(["-v", "error", "-y", "-re", "-stream_loop", "-1", "-i"])
            .arg(&input)
            .args(["-c", "copy", "-flush_packets", "1"])
            .arg(&output)
            .stdout(Stdio::null())
            .stderr(Stdio::null());
        assert!(tokio::time::timeout(
            Duration::from_secs(2),
            run(cmd, &output, MAX_JOB_BYTES, &|_| {})
        )
        .await
        .is_err());
        let length = output.metadata().unwrap().len();
        assert!(length > 0);
        tokio::time::sleep(Duration::from_secs(1)).await;
        assert_eq!(output.metadata().unwrap().len(), length);
        std::fs::remove_file(output).unwrap();
    }

    #[tokio::test]
    #[ignore = "requires host ffmpeg and ffprobe"]
    async fn mkv_contains_all_audio_subtitles_and_fonts() {
        let dir = tempfile::tempdir().unwrap();
        let input = dir.path().join("input.mkv");
        let status = fixture_ffmpeg()
            .args([
                "-v",
                "error",
                "-y",
                "-f",
                "lavfi",
                "-i",
                "color=size=64x64:rate=1",
                "-f",
                "lavfi",
                "-i",
                "sine=frequency=440",
                "-f",
                "lavfi",
                "-i",
                "sine=frequency=880",
                "-map",
                "0:v",
                "-map",
                "1:a",
                "-map",
                "2:a",
                "-t",
                "1",
                "-c:v",
                "libx264",
                "-c:a",
                "aac",
                "-metadata:s:a:0",
                "language=jpn",
                "-metadata:s:a:1",
                "language=bel",
            ])
            .arg(&input)
            .status()
            .unwrap();
        assert!(status.success());
        let mut subtitles: Vec<String> = ["dialogue.srt", "signs.srt"]
            .iter()
            .map(|name| {
                let path = dir.path().join(name);
                std::fs::write(&path, "1\n00:00:00,000 --> 00:00:01,000\nTest\n").unwrap();
                path.to_string_lossy().into_owned()
            })
            .collect();
        let ass = dir.path().join("sub-2-Знакі.ass");
        std::fs::write(&ass, concat!(
            "[Script Info]\nScriptType: v4.00+\nPlayResX: 640\nPlayResY: 360\n",
            "[V4+ Styles]\nFormat: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding\n",
            "Style: Default,FixtureFont,24,&H00FFFFFF,&H000000FF,&H00000000,&H00000000,0,0,0,0,100,100,0,0,1,2,0,2,10,10,10,1\n",
            "[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\n",
            "Dialogue: 0,0:00:00.00,0:00:01.00,Default,,0,0,0,,{\\pos(120,80)}Знакі\n"
        )).unwrap();
        subtitles.push(ass.to_string_lossy().into_owned());
        let mp4 = dir.path().join("subtitled.mp4");
        assert!(fixture_ffmpeg()
            .args(["-v", "error", "-y", "-i"])
            .arg(&input)
            .arg("-i")
            .arg(&subtitles[0])
            .args(["-map", "0", "-map", "1", "-c", "copy", "-c:s", "mov_text"])
            .arg(&mp4)
            .status()
            .unwrap()
            .success());
        let font = dir.path().join("font.ttf");
        std::fs::write(&font, b"attachment fixture").unwrap();
        let hls = dir.path().join("stream.m3u8");
        let dash = dir.path().join("stream.mpd");
        for target in [&hls, &dash] {
            let status = fixture_ffmpeg()
                .current_dir(dir.path())
                .args(["-v", "error", "-y", "-i"])
                .arg(&input)
                .args(["-map", "0:v", "-map", "0:a", "-c", "copy"])
                .arg(target.file_name().unwrap())
                .status()
                .unwrap();
            assert!(status.success());
        }
        let server = httpmock::MockServer::start_async().await;
        for entry in std::fs::read_dir(dir.path()).unwrap() {
            let path = entry.unwrap().path();
            if path.is_file() {
                let route = format!("/{}", path.file_name().unwrap().to_string_lossy());
                let bytes = std::fs::read(path).unwrap();
                server
                    .mock_async(|when, then| {
                        when.path(route);
                        then.body(bytes);
                    })
                    .await;
            }
        }
        let sources = [
            input.to_string_lossy().into_owned(),
            server.url("/stream.m3u8"),
            server.url("/stream.mpd"),
            mp4.to_string_lossy().into_owned(),
        ];
        for (index, source) in sources.iter().enumerate() {
            let output = dir.path().join(format!("output-{index}.mkv"));
            download(
                source,
                None,
                &subtitles,
                &[font.to_string_lossy().into_owned()],
                &output,
                "Test title",
                |_| {},
            )
            .await
            .unwrap();
            let result = command("ffprobe")
                .args(["-v", "error", "-show_streams", "-of", "json"])
                .arg(&output)
                .output()
                .unwrap();
            assert!(result.status.success());
            let result: serde_json::Value = serde_json::from_slice(&result.stdout).unwrap();
            let streams = result["streams"].as_array().unwrap();
            for (kind, expected) in [
                ("video", 1),
                ("audio", 2),
                ("subtitle", if index == 3 { 4 } else { 3 }),
                ("attachment", 1),
            ] {
                assert_eq!(
                    streams.iter().filter(|s| s["codec_type"] == kind).count(),
                    expected
                );
            }
            let languages: Vec<_> = streams
                .iter()
                .filter(|s| s["codec_type"] == "audio")
                .map(|s| s["tags"]["language"].as_str().unwrap())
                .collect();
            assert_eq!(languages, ["jpn", "bel"]);
            assert!(streams
                .iter()
                .any(|s| s["codec_name"] == "ass" && s["tags"]["title"] == "Знакі"));
            if index == 3 {
                assert_eq!(
                    streams
                        .iter()
                        .filter(|s| s["codec_name"] == "mov_text")
                        .count(),
                    0
                );
            }
        }
    }

    #[test]
    fn picks_highest_resolution_without_overflow() {
        let probe: Probe = serde_json::from_value(serde_json::json!({"streams":[
            {"index":0,"codec_type":"audio"},
            {"index":1,"codec_type":"video","width":640,"height":360},
            {"index":2,"codec_type":"video","width":1920,"height":1080},
            {"index":3,"codec_type":"video","width":4294967295u32,"height":4294967295u32}
        ]}))
        .unwrap();
        assert_eq!(video_index(&probe).unwrap(), 3);
        assert!(video_index(&Probe { streams: vec![] }).is_err());
    }
}
