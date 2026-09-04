//! C ABI surface for host apps (C#, Kotlin, Swift…).
//!
//! Contract — JSON in, JSON out, one blocking call + pollable event queue:
//!
//! ```c
//! int64_t anibel_core_init(const char* config_json);
//! char*   anibel_core_call(int64_t handle, const char* req_json);
//! char*   anibel_core_events(int64_t handle);   // drained JSON array or "[]"
//! void    anibel_core_free(char* ptr);
//! void    anibel_core_shutdown(int64_t handle);
//! ```
//!
//! Request: `{ "id": 1, "op": "mediaList", "args": { … } }`
//! Response: `{ "id": 1, "ok": true, "value": { … } }`
//!        or `{ "id": 1, "ok": false, "error": { "code": "…", "message": "…" } }`

use anibel_api::{AnibelApi, media_list_filters_from_json};
use anibel_domain::error::{AnibelError, ErrorDto};
use anibel_player::player;
use anibel_player::video::VideoService;
use serde::Serialize;
use serde_json::{Value, json};
use std::collections::HashMap;
use std::collections::VecDeque;
use std::ffi::{CStr, CString, c_char};
use std::panic::AssertUnwindSafe;
use std::sync::{Arc, Mutex, OnceLock, RwLock};

struct CoreState {
    api: AnibelApi,
    video: VideoService,
    runtime: tokio::runtime::Runtime,
    #[allow(dead_code)] // config surfaced via `config` op later
    config: Value,
    /// std Mutex: drained synchronously by the host poll; pushes are quick and
    /// never cross an await, so blocking is bounded and no events are dropped.
    events: Mutex<VecDeque<Value>>,
}

static HANDLES: OnceLock<RwLock<HashMap<i64, Arc<CoreState>>>> = OnceLock::new();
static NEXT_HANDLE: OnceLock<std::sync::atomic::AtomicI64> = OnceLock::new();

fn handles() -> &'static RwLock<HashMap<i64, Arc<CoreState>>> {
    HANDLES.get_or_init(|| RwLock::new(HashMap::new()))
}

/// Lock helpers that recover from poisoning instead of panicking — a panic in
/// one request must not take down every subsequent FFI call.
fn map_handles() -> std::sync::RwLockWriteGuard<'static, HashMap<i64, Arc<CoreState>>> {
    handles()
        .write()
        .unwrap_or_else(std::sync::PoisonError::into_inner)
}

fn next_handle() -> i64 {
    NEXT_HANDLE
        .get_or_init(|| std::sync::atomic::AtomicI64::new(1))
        .fetch_add(1, std::sync::atomic::Ordering::SeqCst)
}

// ---------------------------------------------------------------------------
// JSON helpers
// ---------------------------------------------------------------------------

fn to_json_string<T: Serialize>(value: &T) -> *mut c_char {
    CString::new(serde_json::to_string(value).unwrap_or_else(|_| "{}".into()))
        .map(|s| s.into_raw())
        .unwrap_or(std::ptr::null_mut())
}

fn push_event(state: &CoreState, event: Value) {
    let mut e = state
        .events
        .lock()
        .unwrap_or_else(std::sync::PoisonError::into_inner);
    e.push_back(event);
}

// ---------------------------------------------------------------------------
// FFI
// ---------------------------------------------------------------------------

#[unsafe(no_mangle)]
/// # Safety
/// `config_json` must be a valid UTF-8 C string or NULL; the returned
/// handle is owned by the caller and must end with `anibel_core_shutdown`.
pub unsafe extern "C" fn anibel_core_init(config_json: *const c_char) -> i64 {
    let config: Value = unsafe {
        if config_json.is_null() {
            json!({})
        } else {
            match CStr::from_ptr(config_json).to_str() {
                Ok(s) => serde_json::from_str(s).unwrap_or(json!({})),
                Err(_) => json!({}),
            }
        }
    };

    let base_url = config
        .get("baseUrl")
        .and_then(Value::as_str)
        .unwrap_or(anibel_api::DEFAULT_BASE_URL);

    let runtime = match tokio::runtime::Builder::new_multi_thread()
        .worker_threads(2)
        .enable_all()
        .build()
    {
        Ok(rt) => rt,
        Err(_) => return -1,
    };

    let api = AnibelApi::with_base(base_url.to_string());
    let video_base = config
        .get("videoBaseUrl")
        .and_then(Value::as_str)
        .unwrap_or(anibel_player::video::DEFAULT_VIDEO_API);
    let video = VideoService::with_base(video_base.to_string());
    let state = Arc::new(CoreState {
        api,
        video,
        runtime,
        config,
        events: Mutex::new(VecDeque::new()),
    });

    let handle = next_handle();
    map_handles().insert(handle, state);
    handle
}

#[unsafe(no_mangle)]
/// # Safety
/// `handle` must come from `anibel_core_init` (a second shutdown is a no-op).
pub unsafe extern "C" fn anibel_core_shutdown(handle: i64) {
    map_handles().remove(&handle);
}

#[unsafe(no_mangle)]
/// # Safety
/// `ptr` must be a pointer previously returned by this dll (or NULL).
pub unsafe extern "C" fn anibel_core_free(ptr: *mut c_char) {
    unsafe {
        if !ptr.is_null() {
            drop(CString::from_raw(ptr));
        }
    }
}

#[unsafe(no_mangle)]
/// # Safety
/// `handle` must be a live handle from `anibel_core_init`.
pub unsafe extern "C" fn anibel_core_events(handle: i64) -> *mut c_char {
    let state = match handles()
        .read()
        .unwrap_or_else(std::sync::PoisonError::into_inner)
        .get(&handle)
    {
        Some(s) => s.clone(),
        None => return to_json_string(&json!([])),
    };

    let events: Vec<Value> = state
        .events
        .lock()
        .unwrap_or_else(std::sync::PoisonError::into_inner)
        .drain(..)
        .collect();

    to_json_string(&events)
}

// ---------------------------------------------------------------------------
// op dispatch
// ---------------------------------------------------------------------------

fn field_str(args: &Value, name: &str) -> Result<String, AnibelError> {
    args.get(name)
        .and_then(Value::as_str)
        .map(str::to_owned)
        .ok_or_else(|| AnibelError::BadArgs(format!("`{name}` required")))
}

fn field_str_vec(args: &Value, name: &str) -> Result<Vec<String>, AnibelError> {
    args.get(name)
        .and_then(Value::as_array)
        .map(|a| {
            a.iter()
                .filter_map(Value::as_str)
                .map(str::to_owned)
                .collect::<Vec<_>>()
        })
        .ok_or_else(|| AnibelError::BadArgs(format!("`{name}` required (string array)")))
}

fn domain_value<T: Serialize>(v: T) -> Result<Value, AnibelError> {
    serde_json::to_value(v).map_err(|e| AnibelError::Internal(e.to_string()))
}

async fn dispatch(state: &CoreState, op: &str, args: &Value) -> Result<Value, AnibelError> {
    let api = &state.api;
    match op {
        "version" => Ok(json!({ "name": "anibel-core", "version": env!("CARGO_PKG_VERSION") })),
        "health" => Ok(json!({ "status": "ok" })),

        "login" => {
            let username: String = field_str(args, "username")?;
            let password: String = field_str(args, "password")?;
            let user = api.login(&username, &password).await?;
            domain_value(user)
        }
        "logout" => {
            api.logout().await;
            Ok(json!({ "status": "ok" }))
        }
        "setToken" => {
            let token: Option<String> =
                args.get("token").and_then(Value::as_str).map(str::to_owned);
            api.set_token(token).await;
            Ok(json!({ "status": "ok" }))
        }
        "markAs" => {
            let media_id: String = field_str(args, "mediaId")?;
            let media_type: String = field_str(args, "mediaType")?;
            let status: String = field_str(args, "status")?;
            api.mark_as(&media_id, &media_type, &status).await?;
            Ok(json!({ "status": "ok" }))
        }
        "removeMark" => {
            let media_id: String = field_str(args, "mediaId")?;
            let media_type: String = field_str(args, "mediaType")?;
            let status: String = field_str(args, "status")?;
            api.remove_mark(&media_id, &media_type, &status).await?;
            Ok(json!({ "status": "ok" }))
        }
        "addFavorite" => {
            let media_id: String = field_str(args, "mediaId")?;
            let media_type: String = field_str(args, "mediaType")?;
            api.add_favorite(&media_id, &media_type).await?;
            Ok(json!({ "status": "ok" }))
        }
        "removeFavorite" => {
            let media_id: String = field_str(args, "mediaId")?;
            let media_type: String = field_str(args, "mediaType")?;
            api.remove_favorite(&media_id, &media_type).await?;
            Ok(json!({ "status": "ok" }))
        }
        "addHistoryRecord" => {
            let entity_id: String = field_str(args, "entityId")?;
            let history_type: String = args
                .get("type")
                .and_then(Value::as_str)
                .unwrap_or("episode")
                .to_string();
            api.add_history_record(&entity_id, &history_type).await?;
            Ok(json!({ "status": "ok" }))
        }
        "removeHistoryRecord" => {
            let entity_id: String = field_str(args, "entityId")?;
            let history_type: String = args
                .get("type")
                .and_then(Value::as_str)
                .unwrap_or("episode")
                .to_string();
            api.remove_history_record(&entity_id, &history_type).await?;
            Ok(json!({ "status": "ok" }))
        }
        "me" => {
            // `me` is absent on production schema; user(username) is the fallback.
            let username = field_str(args, "username")?;
            let profile = api.user(&username).await?;
            domain_value(profile)
        }

        "search" => {
            let query: String = field_str(args, "query")?;
            let limit = args.get("limit").and_then(Value::as_i64);
            let out = api.search(&query, limit.unwrap_or(20)).await?;
            domain_value(out)
        }
        "media" => {
            let slug: String = field_str(args, "slug")?;
            let media_type = args
                .get("mediaType")
                .and_then(Value::as_str)
                .map(str::to_owned);
            let out = api.media(&slug, media_type).await?;
            domain_value(out)
        }
        "mediaList" => {
            let args = args.clone();
            let media_type = args
                .get("mediaType")
                .and_then(Value::as_str)
                .ok_or_else(|| AnibelError::BadArgs("mediaType required".into()))?
                .to_string();
            let offset = args.get("offset").and_then(Value::as_i64).unwrap_or(0);
            let limit = args.get("limit").and_then(Value::as_i64).unwrap_or(20);
            let filters = args.get("filters").map(media_list_filters_from_json);
            let out = api.media_list(&media_type, offset, limit, filters).await?;
            domain_value(out)
        }
        "episodes" => {
            let media_id: String = args
                .get("mediaId")
                .and_then(Value::as_str)
                .ok_or_else(|| AnibelError::BadArgs("mediaId required".into()))?
                .to_string();
            let r#type = args
                .get("type")
                .and_then(Value::as_str)
                .unwrap_or("sub")
                .to_string();
            let resource = args.get("resource").and_then(Value::as_i64).unwrap_or(1);
            // null limit => server pagination returns 0 docs; default to site behaviour (100)
            let limit = args.get("limit").and_then(Value::as_i64).unwrap_or(100);
            let out = api
                .episodes(&media_id, &r#type, resource, Some(limit))
                .await?;
            domain_value(out)
        }
        "episodesMatrix" => {
            let media_id: String = args
                .get("mediaId")
                .and_then(Value::as_str)
                .ok_or_else(|| AnibelError::BadArgs("mediaId required".into()))?
                .to_string();
            let out = api.episodes_matrix(&media_id).await?;
            domain_value(out)
        }
        "chapters" => {
            let media_id: String = args
                .get("mediaId")
                .and_then(Value::as_str)
                .ok_or_else(|| AnibelError::BadArgs("mediaId required".into()))?
                .to_string();
            let limit = args.get("limit").and_then(Value::as_i64).unwrap_or(500);
            let out = api.chapters(&media_id, Some(limit)).await?;
            domain_value(out)
        }
        "chapter" => {
            let slug: String = args
                .get("slug")
                .and_then(Value::as_str)
                .ok_or_else(|| AnibelError::BadArgs("slug required".into()))?
                .to_string();
            let chapter: f64 = args
                .get("chapter")
                .and_then(Value::as_f64)
                .ok_or_else(|| AnibelError::BadArgs("chapter required".into()))?;
            let out = api.chapter(&slug, chapter).await?;
            domain_value(out)
        }
        "comments" => {
            let media_id: String = args
                .get("mediaId")
                .and_then(Value::as_str)
                .ok_or_else(|| AnibelError::BadArgs("mediaId required".into()))?
                .to_string();
            let media_type = args
                .get("mediaType")
                .and_then(Value::as_str)
                .unwrap_or("anime")
                .to_string();
            let offset = args.get("offset").and_then(Value::as_i64).unwrap_or(0);
            let limit = args.get("limit").and_then(Value::as_i64).unwrap_or(20);
            let out = api.comments(&media_id, &media_type, offset, limit).await?;
            domain_value(out)
        }
        "addComment" => {
            let media_id: String = field_str(args, "mediaId")?;
            let media_type: String = field_str(args, "mediaType")?;
            let content: String = field_str(args, "content")?;
            let reply_to = args
                .get("replyTo")
                .and_then(Value::as_str)
                .map(str::trim)
                .filter(|s| !s.is_empty())
                .map(str::to_owned);
            let out = api
                .add_comment(&media_id, &media_type, &content, reply_to.as_deref())
                .await?;
            domain_value(out)
        }
        "trends" => {
            let r#type = args
                .get("type")
                .and_then(Value::as_str)
                .unwrap_or("all")
                .to_string();
            let date = args
                .get("date")
                .and_then(Value::as_str)
                .unwrap_or("week")
                .to_string();
            let limit = args.get("limit").and_then(Value::as_i64);
            let out = api.trends(&r#type, &date, limit).await?;
            domain_value(out)
        }
        "updates" => {
            let r#type = args
                .get("type")
                .and_then(Value::as_str)
                .unwrap_or("ALL")
                .to_string();
            let offset = args.get("offset").and_then(Value::as_i64).unwrap_or(0);
            let limit = args.get("limit").and_then(Value::as_i64).unwrap_or(20);
            let out = api.updates(&r#type, offset, limit).await?;
            domain_value(out)
        }
        "recommendations" => {
            let r#type = args
                .get("type")
                .and_then(Value::as_str)
                .unwrap_or("all")
                .to_string();
            let limit = args.get("limit").and_then(Value::as_i64).unwrap_or(10);
            let out = api.recommendations(&r#type, limit).await?;
            domain_value(out)
        }
        "schedule" => {
            let out = api.schedule().await?;
            domain_value(out)
        }
        "slider" => {
            let limit = args.get("limit").and_then(Value::as_i64).unwrap_or(6);
            let out = api.slider(limit).await?;
            domain_value(out)
        }
        "filters" => {
            let media_type = args
                .get("mediaType")
                .and_then(Value::as_str)
                .map(str::to_string);
            let out = api.filters(media_type).await?;
            domain_value(out)
        }
        "statistics" => {
            let out = api.statistics().await?;
            domain_value(out)
        }
        "random" => {
            let out = api.random_media().await?;
            domain_value(out)
        }
        "user" => {
            let username = field_str(args, "username")?;
            let out = api.user(&username).await?;
            domain_value(out)
        }
        "favorites" => {
            let username: String = args
                .get("username")
                .and_then(Value::as_str)
                .ok_or_else(|| AnibelError::BadArgs("username required".into()))?
                .to_string();
            let media_type = args
                .get("mediaType")
                .and_then(Value::as_str)
                .map(str::to_string);
            let offset = args.get("offset").and_then(Value::as_i64).unwrap_or(0);
            let limit = args.get("limit").and_then(Value::as_i64).unwrap_or(20);
            let out = api.favorites(&username, media_type, offset, limit).await?;
            domain_value(out)
        }
        "marks" => {
            let username: String = args
                .get("username")
                .and_then(Value::as_str)
                .ok_or_else(|| AnibelError::BadArgs("username required".into()))?
                .to_string();
            let media_type = args
                .get("mediaType")
                .and_then(Value::as_str)
                .map(str::to_string);
            let offset = args.get("offset").and_then(Value::as_i64).unwrap_or(0);
            let limit = args.get("limit").and_then(Value::as_i64).unwrap_or(20);
            let out = api.marks(&username, media_type, offset, limit).await?;
            domain_value(out)
        }
        "status" => {
            let username: String = args
                .get("username")
                .and_then(Value::as_str)
                .ok_or_else(|| AnibelError::BadArgs("username required".into()))?
                .to_string();
            let media_type: String = args
                .get("mediaType")
                .and_then(Value::as_str)
                .ok_or_else(|| AnibelError::BadArgs("mediaType required".into()))?
                .to_string();
            let out = api.status(&username, &media_type).await?;
            domain_value(out)
        }
        "resolveEpisode" => {
            let intent = player::resolve_intent(&state.video, args).await?;
            domain_value(intent)
        }
        "videoInfo" => {
            let video_id = field_str(args, "videoId")?;
            let info = state
                .video
                .get(&video_id)
                .await
                .map_err(|e| AnibelError::Transport(format!("video service: {e}")))?;
            domain_value(info)
        }
        "fontAssets" => {
            let names = field_str_vec(args, "names")?;
            let urls = state
                .video
                .fonts_by_names(&names)
                .await
                .map_err(|e| AnibelError::Transport(format!("video service: {e}")))?;
            domain_value(urls)
        }

        _ => Err(AnibelError::BadArgs(format!("unknown op `{op}`"))),
    }
}

#[unsafe(no_mangle)]
/// # Safety
/// `handle` must be live; `req_json` must be a valid UTF-8 C string.
pub unsafe extern "C" fn anibel_core_call(handle: i64, req_json: *const c_char) -> *mut c_char {
    let request: Value = unsafe {
        if req_json.is_null() {
            json!({})
        } else {
            match CStr::from_ptr(req_json).to_str() {
                Ok(s) => serde_json::from_str(s).unwrap_or_else(|_| json!({ "id": 0, "op": "" })),
                Err(_) => json!({ "id": 0, "op": "" }),
            }
        }
    };

    let id = request.get("id").cloned().unwrap_or(json!(0));
    let op = request.get("op").and_then(Value::as_str).unwrap_or("");
    let args = request.get("args").cloned().unwrap_or(json!({}));

    let state = match handles()
        .read()
        .unwrap_or_else(std::sync::PoisonError::into_inner)
        .get(&handle)
    {
        Some(s) => s.clone(),
        None => {
            let err = ErrorDto {
                code: "no_instance".into(),
                message: "anibel_core_init was not called (unknown handle)".into(),
            };
            return to_json_string(&json!({ "id": id, "ok": false, "error": err }));
        }
    };

    // Panic containment: a Rust panic must never unwind across the C ABI
    // (that aborts the host process). Convert to a machine-readable error.
    let result: Result<Value, AnibelError> = std::panic::catch_unwind(AssertUnwindSafe(|| {
        state.runtime.block_on(dispatch(&state, op, &args))
    }))
    .unwrap_or_else(|payload| {
        let msg = if let Some(s) = payload.downcast_ref::<&str>() {
            (*s).to_string()
        } else if let Some(s) = payload.downcast_ref::<String>() {
            s.clone()
        } else {
            "unknown panic".to_string()
        };
        Err(AnibelError::Internal(format!("panic: {msg}")))
    });

    let response = match result {
        Ok(value) => json!({ "id": id, "ok": true, "value": value }),
        Err(e) => {
            if matches!(e, AnibelError::Unauthorized) {
                push_event(&state, json!({ "e": "auth.expired" }));
            }
            push_event(&state, json!({ "e": "error", "detail": e.to_dto() }));
            json!({ "id": id, "ok": false, "error": e.to_dto() })
        }
    };

    to_json_string(&response)
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::ffi::CString;

    fn init() -> i64 {
        let cfg = CString::new("{}").unwrap();
        unsafe { anibel_core_init(cfg.as_ptr()) }
    }

    fn call(handle: i64, req: &str) -> Value {
        let req = CString::new(req).unwrap();
        let resp = unsafe { anibel_core_call(handle, req.as_ptr()) };
        let out = unsafe { CStr::from_ptr(resp) }
            .to_string_lossy()
            .to_string();
        unsafe {
            anibel_core_free(resp);
        }
        serde_json::from_str(&out).unwrap()
    }

    #[test]
    fn version_op() {
        let h = init();
        let resp = call(h, r#"{"id": 1, "op": "version", "args": {}}"#);
        assert_eq!(resp["id"], 1);
        assert_eq!(resp["ok"], true);
        assert_eq!(resp["value"]["name"], "anibel-core");
        assert_eq!(resp["value"]["version"], env!("CARGO_PKG_VERSION"));
        unsafe {
            anibel_core_shutdown(h);
        }
    }

    #[test]
    fn unknown_op_is_error() {
        let h = init();
        let resp = call(h, r#"{"id": 7, "op": "nope", "args": {}}"#);
        assert_eq!(resp["ok"], false);
        assert_eq!(resp["error"]["code"], "bad_args");
        unsafe {
            anibel_core_shutdown(h);
        }
    }

    #[test]
    fn bad_mark_status_is_error_not_panic() {
        // Regression: invalid inputs must surface as `bad_args` errors, never
        // as a panic unwinding across the C ABI. Offline-safe: validation
        // fails before any network I/O.
        let h = init();
        let resp = call(
            h,
            r#"{"id": 4, "op": "markAs", "args": { "mediaId": "1", "mediaType": "anime", "status": "bogus" }}"#,
        );
        assert_eq!(resp["ok"], false);
        assert_eq!(resp["error"]["code"], "bad_args");
        let resp = call(
            h,
            r#"{"id": 5, "op": "addHistoryRecord", "args": { "entityId": "1", "type": "bogus" }}"#,
        );
        assert_eq!(resp["ok"], false);
        assert_eq!(resp["error"]["code"], "bad_args");
        let resp = call(
            h,
            r#"{"id": 6, "op": "removeMark", "args": { "mediaId": "1", "mediaType": "anime", "status": "notselected" }}"#,
        );
        assert_eq!(resp["ok"], false);
        assert_eq!(resp["error"]["code"], "bad_args");
        unsafe {
            anibel_core_shutdown(h);
        }
    }

    #[test]
    fn ops_require_named_object_args() {
        let h = init();
        for req in [
            r#"{"id": 1, "op": "user", "args": {}}"#,
            r#"{"id": 2, "op": "user", "args": "alice"}"#,
            r#"{"id": 3, "op": "me", "args": {}}"#,
            r#"{"id": 4, "op": "me", "args": "alice"}"#,
            r#"{"id": 5, "op": "videoInfo", "args": {}}"#,
            r#"{"id": 6, "op": "videoInfo", "args": "abc"}"#,
            r#"{"id": 7, "op": "fontAssets", "args": []}"#,
        ] {
            let resp = call(h, req);
            assert_eq!(resp["ok"], false, "expected bad_args for {req}: {resp}");
            assert_eq!(resp["error"]["code"], "bad_args", "{req}");
        }
        let empty_fonts = call(
            h,
            r#"{"id": 8, "op": "fontAssets", "args": { "names": [] }}"#,
        );
        assert_eq!(empty_fonts["ok"], true, "{empty_fonts}");
        assert_eq!(empty_fonts["value"], json!([]));
        unsafe {
            anibel_core_shutdown(h);
        }
    }
}
