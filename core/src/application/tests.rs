use super::*;
use httpmock::prelude::*;
use std::time::Duration;

fn app(server: &MockServer) -> Arc<Application> {
    Arc::new(Application::new(&server.url("/graphql"), &server.base_url(), None).unwrap())
}

#[test]
fn quality_choices_use_real_video_tracks_and_reject_stale_selection() {
    let args = json!({"tracks":[
        {"id":2,"height":360,"width":640,"bitrate":3_000_000,"codec":"h264"},
        {"id":1,"height":1080,"width":1920,"bitrate":15_000_000,"codec":"h264","selected":true},
        {"id":3,"height":720,"width":1280,"bitrate":8_000_000,"codec":"h264"},
        {"id":4,"height":2000,"image":true}
    ],"select":3});
    let result = presentation_data::video_qualities(&args).unwrap();
    assert_eq!(result["video"], 3);
    assert_eq!(result["choices"].as_array().unwrap().len(), 3);
    assert_eq!(result["choices"][0]["label"], "1080p · h264");
    assert_eq!(result["choices"][0]["selected"], true);
    assert_eq!(result["choices"][1]["id"], 3);
    let unknown = presentation_data::video_qualities(&json!({"tracks":[{"id":2400000,"bitrate":2400000}]})).unwrap();
    assert_eq!(unknown["choices"][0]["label"], "2.4 Mbit/s");
    for selected in [json!(99), json!(4), json!("3")] {
        let mut invalid = args.clone();
        invalid["select"] = selected;
        assert!(presentation_data::video_qualities(&invalid).is_err());
    }
    assert!(presentation_data::video_qualities(&json!({"tracks":[{"id":1},{"id":1}]})).is_err());
    assert!(
        presentation_data::video_qualities(&json!({"tracks":[{"id":1,"height":u32::MAX}]}))
            .is_err()
    );
    assert_eq!(
        presentation_data::video_qualities(&json!({"tracks":[]})).unwrap()["choices"],
        json!([])
    );
}

#[tokio::test]
async fn ratings_require_auth_and_send_ten_point_values_without_retry() {
    let server = MockServer::start();
    let success = server.mock(|when, then| {
        when.method(POST)
            .header("authorization", "Bearer test-token")
            .body_contains("AddRatingMutation")
            .body_contains(r#""rating":7.0"#);
        then.json_body(json!({"data":{"addRating":"OK"}}));
    });
    let rejected = server.mock(|when, then| {
        when.method(POST)
            .body_contains("AddRatingMutation")
            .body_contains(r#""rating":8.0"#);
        then.json_body(json!({"data":{"addRating":"ERROR"}}));
    });
    let app = app(&server);
    let args = json!({"mediaId":"m","mediaType":"anime","rating":7});
    assert!(matches!(
        app.call("setRating", &args, CacheMode::Default).await,
        Err(AnibelError::Unauthorized)
    ));
    app.call(
        "setToken",
        &json!({"token":"test-token","id":"owner","username":"alice"}),
        CacheMode::Default,
    )
    .await
    .unwrap();
    for rating in [json!(0), json!(11), json!(3.5), Value::Null, json!("7")] {
        let mut invalid = args.clone();
        invalid["rating"] = rating;
        assert!(matches!(
            app.call("setRating", &invalid, CacheMode::Default).await,
            Err(AnibelError::BadArgs(_))
        ));
    }
    assert_eq!(
        app.call("setRating", &args, CacheMode::Default)
            .await
            .unwrap()["rating"],
        7.0
    );
    let mut args = args;
    args["rating"] = json!(8);
    assert!(
        app.call("setRating", &args, CacheMode::Default)
            .await
            .is_err()
    );
    success.assert_hits(1);
    rejected.assert_hits(1);
}

#[test]
fn notification_overflow_keeps_pending_auth_expiry() {
    let app = Application::new("http://127.0.0.1", "http://127.0.0.1", None).unwrap();
    for _ in 0..2 {
        app.push_event(json!({"e":"session.changed"}));
    }
    for _ in 0..2048 {
        app.push_event(json!({"e":"cache.refreshed","op":"search"}));
    }
    let events = app.drain_events();
    assert_eq!(events.len(), 1024);
    assert_eq!(
        events
            .iter()
            .filter(|e| e["e"] == "session.changed")
            .count(),
        1
    );
    assert!(app.drain_events().is_empty());
}

#[tokio::test]
async fn fresh_reads_reload_and_mutation_invalidation() {
    let server = MockServer::start_async().await;
    let search = server
        .mock_async(|when, then| {
            when.method(POST)
                .path("/graphql")
                .body_contains("SearchQuery");
            then.json_body(json!({"data":{"search":[]}}));
        })
        .await;
    let mutation = server
        .mock_async(|when, then| {
            when.method(POST)
                .path("/graphql")
                .body_contains("RemoveFavoriteMutation");
            then.json_body(json!({"data":{"removeFavorite":"SUCCESS"}}));
        })
        .await;
    let app = app(&server);
    let args = json!({"query":"test"});
    for _ in 0..2 {
        assert_eq!(
            app.call("search", &args, CacheMode::Default).await.unwrap(),
            json!([])
        );
    }
    search.assert_hits_async(1).await;
    app.call("search", &args, CacheMode::Reload).await.unwrap();
    search.assert_hits_async(2).await;
    app.call(
        "removeFavorite",
        &json!({"mediaId":"1","mediaType":"anime"}),
        CacheMode::Default,
    )
    .await
    .unwrap();
    mutation.assert_hits_async(1).await;
    app.call("search", &args, CacheMode::Default).await.unwrap();
    search.assert_hits_async(3).await;
}

#[tokio::test]
async fn account_changes_do_not_reuse_watched_state() {
    let server = MockServer::start_async().await;
    let a = server.mock_async(|when, then| {
        when.method(POST).header("authorization", "Bearer account-a");
        then.json_body(json!({"data":{"episodes":{"docs":[{"id":"ep","episode":1.0,"type":"sub","url":"https://example.test/ep","resource":2,"watched":true}],"totalDocs":1,"limit":100,"offset":0}}}));
    }).await;
    let b = server.mock_async(|when, then| {
        when.method(POST).header("authorization", "Bearer account-b");
        then.json_body(json!({"data":{"episodes":{"docs":[{"id":"ep","episode":1.0,"type":"sub","url":"https://example.test/ep","resource":2,"watched":false}],"totalDocs":1,"limit":100,"offset":0}}}));
    }).await;
    let app = app(&server);
    let args = json!({"mediaId":"1"});
    app.call(
        "setToken",
        &json!({"token":"account-a"}),
        CacheMode::Default,
    )
    .await
    .unwrap();
    let first = app
        .call("episodes", &args, CacheMode::Default)
        .await
        .unwrap();
    app.call(
        "setToken",
        &json!({"token":"account-b"}),
        CacheMode::Default,
    )
    .await
    .unwrap();
    let second = app
        .call("episodes", &args, CacheMode::Default)
        .await
        .unwrap();
    assert_eq!(first["docs"][0]["watched"], true);
    assert_eq!(second["docs"][0]["watched"], false);
    a.assert_hits_async(1).await;
    b.assert_hits_async(1).await;
}

#[tokio::test]
async fn clear_waits_for_stale_refresh_and_cannot_be_undone_by_it() {
    let server = MockServer::start_async().await;
    let search = server
        .mock_async(|when, then| {
            when.method(POST).body_contains("SearchQuery");
            then.delay(Duration::from_millis(50))
                .json_body(json!({"data":{"search":[]}}));
        })
        .await;
    let app = app(&server);
    let args = json!({"query":"test"});
    let key = app.cache().key("search", &args);
    app.cache()
        .set(
            key.clone(),
            json!(["old"]),
            SystemTime::now() - Duration::from_secs(13 * 3600),
        )
        .unwrap();
    assert_eq!(
        app.call("search", &args, CacheMode::Default).await.unwrap(),
        json!(["old"])
    );
    // The refresh owns a read permit before call returns. Clear must run after it.
    app.call("clearCache", &json!({}), CacheMode::Default)
        .await
        .unwrap();
    search.assert_hits_async(1).await;
    assert!(app.cache().get(&key, "search", SystemTime::now()).is_none());
    app.call("search", &args, CacheMode::Default).await.unwrap();
    search.assert_hits_async(2).await;
}

#[tokio::test]
async fn failed_mutation_does_not_discard_cached_reads() {
    let server = MockServer::start_async().await;
    let app = app(&server);
    let args = json!({"query":"test"});
    let key = app.cache().key("search", &args);
    app.cache().set(key, json!([]), SystemTime::now()).unwrap();
    assert!(
        app.call(
            "markAs",
            &json!({"mediaId":"1","mediaType":"anime","status":"bad"}),
            CacheMode::Default
        )
        .await
        .is_err()
    );
    assert_eq!(
        app.call("search", &args, CacheMode::Default).await.unwrap(),
        json!([])
    );
}

#[tokio::test]
async fn session_restore_can_read_its_disk_cache_without_network() {
    let server = MockServer::start_async().await;
    let search = server
        .mock_async(|when, then| {
            when.method(POST)
                .header("authorization", "Bearer saved-token");
            then.json_body(json!({"data":{"search":[]}}));
        })
        .await;
    let dir = tempfile::tempdir().unwrap();
    let create = || {
        Arc::new(
            Application::new(
                &server.url("/graphql"),
                &server.base_url(),
                Some(dir.path().to_owned()),
            )
            .unwrap(),
        )
    };
    let args = json!({"query":"test"});
    let token = json!({"token":"saved-token"});
    let first = create();
    first
        .call("setToken", &token, CacheMode::Default)
        .await
        .unwrap();
    first
        .call("search", &args, CacheMode::Default)
        .await
        .unwrap();
    search.assert_hits_async(1).await;
    drop(first);
    search.delete_async().await;
    let restored = create();
    restored
        .call("setToken", &token, CacheMode::Default)
        .await
        .unwrap();
    assert_eq!(
        restored
            .call("search", &args, CacheMode::Default)
            .await
            .unwrap(),
        json!([])
    );
}

#[tokio::test]
async fn cache_storage_failure_does_not_turn_a_read_into_failure() {
    let server = MockServer::start_async().await;
    let search = server
        .mock_async(|when, then| {
            when.method(POST);
            then.json_body(json!({"data":{"search":[]}}));
        })
        .await;
    let file = tempfile::tempdir().unwrap();
    let app = Arc::new(
        Application::new(
            &server.url("/graphql"),
            &server.base_url(),
            Some(file.path().to_owned()),
        )
        .unwrap(),
    );
    std::fs::create_dir(file.path().join("api-cache-v1.json")).unwrap();
    assert_eq!(
        app.call("search", &json!({"query":"test"}), CacheMode::Default)
            .await
            .unwrap(),
        json!([])
    );
    search.assert_hits_async(1).await;
    assert!(
        app.drain_events()
            .iter()
            .any(|e| e["e"] == "cache.storageError")
    );
}

#[tokio::test]
async fn expiry_clears_only_the_account_that_made_the_request() {
    let server = MockServer::start_async().await;
    server.mock_async(|w,t|{w.method(POST).path("/graphql");t.json_body(json!({"errors":[{"message":"jwt expired","extensions":{"code":"UNAUTHENTICATED"}}]}));}).await;
    let app = app(&server);
    app.call(
        "setToken",
        &json!({"token":"old","username":"old"}),
        CacheMode::Default,
    )
    .await
    .unwrap();
    let old = app.session.lock().unwrap().revision;
    app.call(
        "setToken",
        &json!({"token":"new","username":"new"}),
        CacheMode::Default,
    )
    .await
    .unwrap();
    app.expire_session(old).await.unwrap();
    assert!(app.session.lock().unwrap().authenticated);
    assert!(matches!(
        app.call("search", &json!({"query":"test"}), CacheMode::Default)
            .await,
        Err(AnibelError::Unauthorized)
    ));
    assert!(!app.session.lock().unwrap().authenticated);
    assert!(app.session.lock().unwrap().username.is_none());
    let revision = app.session.lock().unwrap().revision;
    assert!(
        app.call("setToken", &json!({"token":" "}), CacheMode::Default)
            .await
            .is_err()
    );
    assert_eq!(app.session.lock().unwrap().revision, revision);
}

#[tokio::test]
async fn queued_read_uses_revision_after_waiting_for_session_write() {
    let server = MockServer::start_async().await;
    server
        .mock_async(|w, t| {
            w.method(POST);
            t.json_body(json!({"errors":[{"message":"jwt expired"}]}));
        })
        .await;
    let app = app(&server);
    let write = app.requests.write().await;
    let reader = {
        let app = app.clone();
        tokio::spawn(async move {
            app.call("search", &json!({"query":"test"}), CacheMode::Default)
                .await
        })
    };
    tokio::task::yield_now().await;
    app.api.set_token(Some("new".into())).await;
    app.session
        .lock()
        .unwrap()
        .changed(Some(&json!({"username":"new"})))
        .unwrap();
    drop(write);
    assert!(reader.await.unwrap().is_err());
    assert!(!app.session.lock().unwrap().authenticated);
}

#[tokio::test]
async fn shared_mutations_choose_backend_operation_and_return_display_state() {
    let server = MockServer::start_async().await;
    let add = server
        .mock_async(|w, t| {
            w.body_contains("AddFavoriteMutation");
            t.json_body(json!({"data":{"addFavorite":{"mediaId":"1"}}}));
        })
        .await;
    let remove = server
        .mock_async(|w, t| {
            w.body_contains("RemoveMarkMutation");
            t.json_body(json!({"data":{"removeMark":"SUCCESS"}}));
        })
        .await;
    let app = app(&server);
    assert_eq!(
        app.call(
            "setFavorite",
            &json!({"mediaId":"1","mediaType":"anime","selected":true}),
            CacheMode::Default
        )
        .await
        .unwrap(),
        json!({"selected":true})
    );
    add.assert_hits_async(1).await;
    assert_eq!(
        app.call(
            "setMark",
            &json!({"mediaId":"1","mediaType":"manga","status":"notselected","current":"reading"}),
            CacheMode::Default
        )
        .await
        .unwrap(),
        json!({"mark":null})
    );
    remove.assert_hits_async(1).await;
    assert!(
        app.call(
            "setMark",
            &json!({"mediaId":"1","mediaType":"manga","status":"watching"}),
            CacheMode::Default
        )
        .await
        .is_err()
    );
}

#[tokio::test]
async fn reader_uses_complete_local_pages_and_core_navigation() {
    let server = MockServer::start_async().await;
    let chapter=server.mock_async(|w,t|{w.method(POST).body_contains("ChapterQuery");t.json_body(json!({"data":{"chapter":{"id":"c1","chapter":1,"images":[{"large":server.url("/page.jpg")}],"view":0}}}));}).await;
    server
        .mock_async(|w, t| {
            w.path("/page.jpg");
            t.body("image");
        })
        .await;
    let app = app(&server);
    let download = app
        .call(
            "downloadEnqueue",
            &json!({"kind":"manga","request":{"slug":"book","chapter":1,"title":"Book"}}),
            CacheMode::Default,
        )
        .await
        .unwrap();
    let id = download["id"].as_str().unwrap();
    tokio::time::timeout(Duration::from_secs(3), async {
        loop {
            let r = app.downloads.by_id(id).unwrap();
            if r.status == downloads::Status::Completed {
                break;
            }
            assert!(r.error.is_none(), "{:?}", r.error);
            tokio::time::sleep(Duration::from_millis(10)).await;
        }
    })
    .await
    .unwrap();
    let result = app
        .call(
            "readerOpen",
            &json!({"slug":"book","chapter":1,"chapters":[1,2]}),
            CacheMode::Default,
        )
        .await
        .unwrap();
    assert_eq!(result["chapter"]["id"], "c1");
    assert!(
        result["chapter"]["images"][0]["large"]
            .as_str()
            .unwrap()
            .starts_with("file:")
    );
    assert_eq!(result["next"], 2.0);
    chapter.assert_hits_async(1).await;
}

#[tokio::test]
async fn profile_updates_use_the_session_owner_and_refresh_identity() {
    let server = MockServer::start();
    let update = server.mock(|when, then| {
        when.method(POST).path("/graphql").header("authorization", "Bearer test-token")
            .body_contains("UpdateProfileMutation").body_contains(r#""id":"owner""#);
        then.json_body(json!({"data":{"updateUser":{"id":"owner","username":"alice",
            "displayName":"Alice","bio":"Old bio","avatar":"https://images.example/old.png","wallpaper":null}}}));
    });
    let readback = server.mock(|when, then| {
        when.method(POST).body_contains("UserQuery");
        then.json_body(json!({"data":{"user":{"id":"owner","username":"alice","role":"USER",
            "displayName":"Alice","bio":"Hello","avatar":"https://images.example/new.png","wallpaper":null}}}));
    });
    let app = app(&server);
    assert!(matches!(
        app.call("updateProfile", &json!({"bio":"Hello"}), CacheMode::Default)
            .await,
        Err(AnibelError::Unauthorized)
    ));
    app.call(
        "setToken",
        &json!({"token":"test-token","id":"owner","username":"alice"}),
        CacheMode::Default,
    )
    .await
    .unwrap();
    for patch in [
        json!({"id":"other","bio":"Hello"}),
        json!({"role":"admin"}),
        json!({"avatar":"file:///private"}),
    ] {
        assert!(matches!(
            app.call("updateProfile", &patch, CacheMode::Default).await,
            Err(AnibelError::BadArgs(_))
        ));
    }
    let result = app
        .call(
            "updateProfile",
            &json!({"displayName":"Alice","bio":"Hello"}),
            CacheMode::Default,
        )
        .await
        .unwrap();
    assert_eq!(result["bio"], "Hello");
    let session = app
        .call("session", &json!({}), CacheMode::Default)
        .await
        .unwrap();
    assert_eq!(session["avatar"], "https://images.example/new.png");
    assert_eq!(session["userId"], "owner");
    assert_eq!(session["revision"], 2);
    readback.assert_hits(1);
    update.assert_hits(1);
}

#[tokio::test]
async fn public_profile_is_not_editable_and_contains_bio_and_wallpaper() {
    let server = MockServer::start();
    let user = server.mock(|when, then| {
        when.method(POST).body_contains("UserQuery");
        then.json_body(
            json!({"data":{"user":{"id":"other","username":"bob","role":"USER",
            "bio":"About Bob","wallpaper":"https://images.example/banner.png"}}}),
        );
    });
    let app = app(&server);
    let value = app
        .call("profile", &json!({"username":"bob"}), CacheMode::Default)
        .await
        .unwrap();
    assert_eq!(value["isOwn"], false);
    assert_eq!(value["profile"]["bio"], "About Bob");
    assert_eq!(
        value["profile"]["wallpaper"],
        "https://images.example/banner.png"
    );
    user.assert_hits(1);
}

#[tokio::test]
async fn profile_upload_uses_multipart_and_only_then_updates_the_profile() {
    let server = MockServer::start();
    let upload = server.mock(|when, then| {
        when.method(POST)
            .header("authorization", "Bearer test-token")
            .body_contains("UploadProfileImage")
            .body_contains("variables.file")
            .body_contains("image/png");
        then.json_body(json!({"data":{"upload":{"path":"/images/avatar.png"}}}));
    });
    let update = server.mock(|when, then| {
        when.method(POST).body_contains("UpdateProfileMutation").body_contains(server.url("/images/avatar.png"));
        then.json_body(json!({"data":{"updateUser":{"id":"owner","username":"alice","avatar":server.url("/images/avatar.png")}}}));
    });
    server.mock(|when, then| {
        when.method(POST).body_contains("UserQuery");
        then.json_body(json!({"data":{"user":{"id":"owner","username":"alice","role":"USER", "avatar":server.url("/images/avatar.png")}}}));
    });
    let image = tempfile::NamedTempFile::new().unwrap();
    std::fs::write(image.path(), b"\x89PNG\r\n\x1a\nfixture").unwrap();
    let app = app(&server);
    app.call(
        "setToken",
        &json!({"token":"test-token","id":"owner","username":"alice"}),
        CacheMode::Default,
    )
    .await
    .unwrap();
    app.call(
        "updateProfile",
        &json!({"avatarPath":image.path()}),
        CacheMode::Default,
    )
    .await
    .unwrap();
    upload.assert_hits(1);
    update.assert_hits(1);
}

#[tokio::test]
async fn comments_keep_replies_under_their_parent() {
    let server = MockServer::start();
    let comment = |id: &str| json!({"id":id,"content":id,"date":{"created":1},"related":{"mediaId":"m","mediaType":"ANIME"}});
    let mut root = comment("root");
    let mut reply = comment("reply");
    reply["replies"] = json!([comment("nested")]);
    root["replies"] = json!([reply]);
    let mock = server.mock(|when, then| {
        when.method(POST).body_contains("replies");
        then.json_body(
            json!({"data":{"comments":{"docs":[root],"totalDocs":1,"limit":30,"offset":0}}}),
        );
    });
    let app = app(&server);
    let value = app
        .call(
            "comments",
            &json!({"mediaId":"m","mediaType":"anime"}),
            CacheMode::Default,
        )
        .await
        .unwrap();
    assert_eq!(value["docs"].as_array().unwrap().len(), 1);
    assert_eq!(value["docs"][0]["replies"][0]["replies"][0]["id"], "nested");
    mock.assert_hits(1);
}

#[tokio::test]
async fn root_comment_omits_reply_to_and_replies_keep_the_exact_parent() {
    let server = MockServer::start();
    let root = server.mock(|when, then| {
        when.method(POST)
            .json_body_partial(
                r#"{"variables":{"input":{"mediaId":"m","mediaType":"anime","content":"root"}}}"#,
            )
            .body_contains(r#""input":{"content":"root","mediaId":"m","mediaType":"anime"}"#);
        then.json_body(
            json!({"data":{"addComment":{"id":"c1","content":"root","date":{"created":1}}}}),
        );
    });
    let reply = server.mock(|when, then| {
        when.method(POST)
            .json_body_partial(r#"{"variables":{"input":{"replyTo":"c1","content":"reply"}}}"#);
        then.json_body(
            json!({"data":{"addComment":{"id":"c2","content":"reply","date":{"created":2}}}}),
        );
    });
    let app = app(&server);
    app.call(
        "addComment",
        &json!({"mediaId":"m","mediaType":"anime","content":"root"}),
        CacheMode::Default,
    )
    .await
    .unwrap();
    app.call(
        "addComment",
        &json!({"mediaId":"m","mediaType":"anime","content":"reply","replyTo":"c1"}),
        CacheMode::Default,
    )
    .await
    .unwrap();
    root.assert_hits(1);
    reply.assert_hits(1);
}
