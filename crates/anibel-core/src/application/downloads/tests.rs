use super::*;
use httpmock::prelude::*;
fn setup(server: &MockServer) -> Arc<Downloads> {
    Arc::new(
        Downloads::new(
            Arc::new(Storage::new(None).unwrap()),
            AnibelApi::with_base(server.url("/graphql")),
            VideoService::with_base(server.base_url()),
            reqwest::Client::new(),
        )
        .unwrap(),
    )
}
fn request(server: &MockServer) -> Request {
    Request {
        media_id: "book1".into(),
        media_type: "books".into(),
        title: "Book".into(),
        file_url: Some(server.url("/file.pdf")),
        ..Default::default()
    }
}
#[tokio::test]
async fn snapshot_reports_real_progress_without_persisting_live_estimates() {
    let server = MockServer::start_async().await;
    let d = setup(&server);
    let mut row = Record {
        id: "progress".into(),
        kind: Kind::File,
        request: request(&server),
        status: Status::Downloading,
        assets: Assets::default(),
        error: None,
        bytes: 250,
        parts_done: 0,
        parts_total: 0,
        created: 0,
        live: LiveProgress {
            started: Some(Instant::now() - std::time::Duration::from_secs(10)),
            total_bytes: Some(1000),
            media: Default::default(),
        },
    };
    let snapshot = d.snapshot(&row);
    assert_eq!(snapshot["progress"], 0.25);
    assert_eq!(snapshot["progressKnown"], true);
    assert!(snapshot["bytesPerSecond"].as_f64().unwrap() > 0.0);
    assert!((30..=31).contains(&snapshot["remainingSeconds"].as_u64().unwrap()));
    let restored: Record = serde_json::from_value(serde_json::to_value(&row).unwrap()).unwrap();
    assert!(restored.live.started.is_none());
    row.live.total_bytes = None;
    assert_eq!(d.snapshot(&row)["progressKnown"], false);
    assert_eq!(d.snapshot(&row)["remainingSeconds"], Value::Null);
    row.live.media = super::super::mkv::Progress {
        processed: 30.0,
        duration: 120.0,
    };
    assert_eq!(d.snapshot(&row)["progress"], 0.25);
    row.live.media.processed = 121.0;
    assert_eq!(d.snapshot(&row)["progress"], 1.0);
    assert_eq!(d.snapshot(&row)["phase"], "finalizing");
}
#[tokio::test]
async fn mkv_export_is_one_file_and_does_not_overwrite() {
    let server = MockServer::start_async().await;
    let d = setup(&server);
    let folder = Downloads::folder("mkv-test");
    let video = format!("{folder}/media.mkv");
    std::fs::create_dir_all(d.storage.path(&folder).unwrap()).unwrap();
    std::fs::write(d.storage.path(&video).unwrap(), b"mkv-fixture").unwrap();
    let mut request = request(&server);
    request.title = "Title: / test".into();
    request.episode_label = "1".into();
    request.video_format = VideoFormat::Mkv;
    d.records.lock().unwrap().push(Record {
        live: LiveProgress::default(),
        id: "mkv-test".into(),
        kind: Kind::Video,
        request,
        status: Status::Completed,
        assets: Assets {
            video_path: Some(video),
            ..Default::default()
        },
        error: None,
        bytes: 11,
        parts_done: 1,
        parts_total: 1,
        created: 0,
    });
    let destination = tempfile::tempdir().unwrap();
    let exported = d.export("mkv-test", destination.path()).await.unwrap();
    let path = Path::new(exported["path"].as_str().unwrap());
    assert_eq!(path.extension().unwrap(), "mkv");
    assert_eq!(std::fs::read(path).unwrap(), b"mkv-fixture");
    assert!(d.export("mkv-test", destination.path()).await.is_err());
    assert_eq!(std::fs::read_dir(destination.path()).unwrap().count(), 1);
}
async fn finished(d: &Downloads, id: &str) -> Record {
    tokio::time::timeout(std::time::Duration::from_secs(5), async {
        loop {
            let r = d.record(id).unwrap();
            if !r.status.active() {
                return r;
            }
            tokio::time::sleep(std::time::Duration::from_millis(10)).await;
        }
    })
    .await
    .unwrap()
}
#[tokio::test]
async fn file_lifecycle_is_durable_and_missing_assets_are_not_ready() {
    let server = MockServer::start_async().await;
    let file = server
        .mock_async(|w, t| {
            w.path("/file.pdf");
            t.body("pdf-content");
        })
        .await;
    let d = setup(&server);
    let first = d.enqueue(Kind::File, request(&server)).await.unwrap();
    let id = first["id"].as_str().unwrap();
    assert_eq!(
        d.enqueue(Kind::File, request(&server)).await.unwrap()["id"],
        id
    );
    let record = finished(&d, id).await;
    assert_eq!(record.status, Status::Completed);
    assert!(d.available(&record));
    assert_eq!(record.bytes, 11);
    file.assert_hits_async(1).await;
    d.change(id, "retry").await.unwrap();
    file.assert_hits_async(1).await;
    let restored = Downloads::new(
        d.storage.clone(),
        d.api.clone(),
        d.video.clone(),
        d.http.clone(),
    )
    .unwrap();
    assert!(restored.available(&restored.record(id).unwrap()));
    let export = tempfile::tempdir().unwrap();
    let result = d.export(id, export.path()).await.unwrap();
    assert!(
        Path::new(result["path"].as_str().unwrap())
            .join("file.pdf")
            .is_file()
    );
    assert!(d.export(id, export.path()).await.is_err());
    std::fs::remove_file(
        d.storage
            .path(record.assets.file_path.as_ref().unwrap())
            .unwrap(),
    )
    .unwrap();
    assert_eq!(d.snapshot(&record)["canPlay"], false);
    assert_eq!(d.snapshot(&record)["canRetry"], true);
    assert!(d.export(id, export.path()).await.is_err());
    d.change(id, "retry").await.unwrap();
    assert_eq!(finished(&d, id).await.status, Status::Completed);
    file.assert_hits_async(2).await;
    d.change(id, "delete").await.unwrap();
    assert!(d.record(id).is_err());
    assert!(!d.storage.path(&Downloads::folder(id)).unwrap().exists());
}
#[tokio::test]
async fn cancel_stops_writes_before_delete_and_invalid_action_does_not_stop_job() {
    let server = MockServer::start_async().await;
    server
        .mock_async(|w, t| {
            w.path("/file.pdf");
            t.delay(std::time::Duration::from_millis(200))
                .body("content");
        })
        .await;
    let d = setup(&server);
    let first = d.enqueue(Kind::File, request(&server)).await.unwrap();
    let id = first["id"].as_str().unwrap();
    assert!(d.change(id, "bogus").await.is_err());
    assert!(d.record(id).unwrap().status.active());
    d.change(id, "cancel").await.unwrap();
    assert_eq!(d.record(id).unwrap().status, Status::Cancelled);
    d.change(id, "delete").await.unwrap();
    tokio::time::sleep(std::time::Duration::from_millis(250)).await;
    assert!(!d.storage.path(&Downloads::folder(id)).unwrap().exists());
}
#[tokio::test]
async fn storage_failure_does_not_enqueue_and_interrupted_jobs_restart_queued() {
    let server = MockServer::start_async().await;
    let d = setup(&server);
    std::fs::create_dir(d.storage.root.join("downloads.json")).unwrap();
    assert!(d.enqueue(Kind::File, request(&server)).await.is_err());
    assert!(d.records.lock().unwrap().is_empty());
    assert!(d.work.lock().unwrap().is_empty());
    std::fs::remove_dir(d.storage.root.join("downloads.json")).unwrap();
    let r = Record {
        live: LiveProgress::default(),
        id: "restore".into(),
        kind: Kind::File,
        request: request(&server),
        status: Status::Downloading,
        assets: Assets::default(),
        error: None,
        bytes: 9,
        parts_done: 0,
        parts_total: 1,
        created: 0,
    };
    d.save(&[r]).unwrap();
    let restored = Downloads::new(
        d.storage.clone(),
        d.api.clone(),
        d.video.clone(),
        d.http.clone(),
    )
    .unwrap();
    assert_eq!(restored.record("restore").unwrap().status, Status::Queued);
}
#[tokio::test]
async fn offline_hls_requires_every_segment_and_preserves_manifest() {
    let server = MockServer::start_async().await;
    server.mock_async(|w,t|{w.path("/list.m3u8");t.body("#EXTM3U\n#EXT-X-TARGETDURATION:6\n#EXTINF:6,\na.ts\n#EXTINF:6,\nb.ts\n#EXT-X-ENDLIST");}).await;
    for path in ["/a.ts", "/b.ts"] {
        server
            .mock_async(|w, t| {
                w.path(path);
                t.body(path);
            })
            .await;
    }
    let d = setup(&server);
    let folder = Downloads::folder("hls");
    let dir = d.storage.path(&folder).unwrap();
    std::fs::create_dir_all(&dir).unwrap();
    let paths = hls::download(&d.http, &server.url("/list.m3u8"), &dir, |_, _, _| {})
        .await
        .unwrap();
    let r = Record {
        live: LiveProgress::default(),
        id: "hls".into(),
        kind: Kind::Video,
        request: Request::default(),
        status: Status::Completed,
        assets: Assets {
            video_path: Some(format!("{folder}/playlist.m3u8")),
            required_paths: paths.iter().map(|p| format!("{folder}/{p}")).collect(),
            ..Default::default()
        },
        error: None,
        bytes: 0,
        parts_done: 1,
        parts_total: 1,
        created: 0,
    };
    assert!(d.available(&r));
    let manifest = std::fs::read_to_string(dir.join("playlist.m3u8")).unwrap();
    assert!(manifest.contains("segs/000001.ts"));
    assert!(!manifest.contains(&server.base_url()));
    std::fs::remove_file(dir.join("segs/000001.ts")).unwrap();
    assert!(!d.available(&r));
}

#[tokio::test]
async fn host_suspension_preserves_queue_and_resume_finishes_transfer() {
    let server = MockServer::start_async().await;
    let file = server
        .mock_async(|when, then| {
            when.path("/file.pdf");
            then.status(200).body("book");
        })
        .await;
    let d = setup(&server);
    d.set_execution(false).await.unwrap();
    let item = d.enqueue(Kind::File, request(&server)).await.unwrap();
    let id = item["id"].as_str().unwrap();
    assert_eq!(d.record(id).unwrap().status, Status::Queued);
    assert!(d.work.lock().unwrap().is_empty());
    file.assert_hits_async(0).await;
    d.set_execution(true).await.unwrap();
    let result = finished(&d, id).await;
    assert_eq!(result.status, Status::Completed);
    d.set_execution(false).await.unwrap();
    assert_eq!(d.record(id).unwrap().status, Status::Completed);
}

#[tokio::test]
async fn host_suspension_drains_active_transfer_without_cancelling_record() {
    let server = MockServer::start_async().await;
    let file = server
        .mock_async(|when, then| {
            when.path("/file.pdf");
            then.delay(std::time::Duration::from_millis(300))
                .body("book");
        })
        .await;
    let d = setup(&server);
    let item = d.enqueue(Kind::File, request(&server)).await.unwrap();
    let id = item["id"].as_str().unwrap();
    tokio::time::timeout(std::time::Duration::from_secs(5), async {
        while file.hits_async().await == 0 {
            tokio::time::sleep(std::time::Duration::from_millis(5)).await;
        }
    })
    .await
    .unwrap();
    d.set_execution(false).await.unwrap();
    assert_eq!(d.record(id).unwrap().status, Status::Queued);
    assert!(d.work.lock().unwrap().is_empty());
    tokio::time::sleep(std::time::Duration::from_millis(350)).await;
    assert_eq!(d.record(id).unwrap().status, Status::Queued);
    d.set_execution(true).await.unwrap();
    assert_eq!(finished(&d, id).await.status, Status::Completed);
}
