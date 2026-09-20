//! C ABI surface for host apps (C#, Kotlin, Swift…).
//!
//! Contract — JSON in, JSON out, one blocking call + pollable event queue:
//!
//! ```c
//! int64_t anibel_core_init(const char* config_json);
//! int32_t anibel_core_request_begin(int64_t handle, int64_t id);
//! void    anibel_core_cancel(int64_t handle, int64_t id);
//! char*   anibel_core_call(int64_t handle, const char* req_json);
//! char*   anibel_core_events(int64_t handle);   // drained JSON array or "[]"
//! void    anibel_core_free(char* ptr);
//! void    anibel_core_shutdown(int64_t handle);
//! ```
//!
//! Request: `{ "id": 1, "op": "mediaList", "args": { … } }`
//! Response: `{ "id": 1, "ok": true, "value": { … } }`
//!        or `{ "id": 1, "ok": false, "error": { "code": "…", "message": "…" } }`

use crate::application::{Application, CacheMode};
use anibel_domain::error::{AnibelError, ErrorDto};
use serde::Serialize;
use serde_json::{Value, json};
use std::collections::HashMap;
use std::ffi::{CStr, CString, c_char};
use std::panic::AssertUnwindSafe;
use std::sync::{Arc, Mutex, OnceLock, RwLock};
use tokio_util::sync::CancellationToken;

struct CoreState {
    application: Arc<Application>,
    runtime: tokio::runtime::Runtime,
    requests: Mutex<HashMap<i64, (CancellationToken, bool)>>,
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

    let video_base = config
        .get("videoBaseUrl")
        .and_then(Value::as_str)
        .unwrap_or(anibel_player::video::DEFAULT_VIDEO_API);
    let data_dir = config
        .get("dataDir")
        .and_then(Value::as_str)
        .map(std::path::PathBuf::from);
    let state = Arc::new(CoreState {
        requests: Mutex::new(HashMap::new()),
        application: match Application::new(base_url, video_base, data_dir) {
            Ok(app) => Arc::new(app),
            Err(_) => return -1,
        },
        runtime,
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
pub extern "C" fn anibel_core_request_begin(handle: i64, id: i64) -> i32 {
    let states = handles()
        .read()
        .unwrap_or_else(std::sync::PoisonError::into_inner);
    let Some(state) = states.get(&handle) else {
        return 0;
    };
    let mut requests = state
        .requests
        .lock()
        .unwrap_or_else(std::sync::PoisonError::into_inner);
    if requests.len() >= 1024 || requests.contains_key(&id) {
        return 0;
    }
    requests.insert(id, (CancellationToken::new(), false));
    1
}

#[unsafe(no_mangle)]
pub extern "C" fn anibel_core_cancel(handle: i64, id: i64) {
    let states = handles()
        .read()
        .unwrap_or_else(std::sync::PoisonError::into_inner);
    if let Some(state) = states.get(&handle)
        && let Some((token, _)) = state
            .requests
            .lock()
            .unwrap_or_else(std::sync::PoisonError::into_inner)
            .get(&id)
    {
        token.cancel();
    }
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

    let events = state.application.drain_events();

    to_json_string(&events)
}

// ---------------------------------------------------------------------------
// op dispatch
// ---------------------------------------------------------------------------

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

    let Some(request_id) = id.as_i64() else {
        return to_json_string(
            &json!({"id":id,"ok":false,"error":{"code":"bad_args","message":"request id must be a signed 64-bit integer"}}),
        );
    };
    let token = {
        let mut requests = state
            .requests
            .lock()
            .unwrap_or_else(std::sync::PoisonError::into_inner);
        if requests
            .get(&request_id)
            .is_some_and(|(_, running)| *running)
            || requests.len() >= 1024 && !requests.contains_key(&request_id)
        {
            return to_json_string(
                &json!({"id":id,"ok":false,"error":{"code":"bad_args","message":"request id already active or capacity exceeded"}}),
            );
        }
        let (token, running) = requests
            .entry(request_id)
            .or_insert_with(|| (CancellationToken::new(), false));
        *running = true;
        token.clone()
    };
    // Panic containment: a Rust panic must never unwind across the C ABI
    // (that aborts the host process). Convert to a machine-readable error.
    let result: Result<Value, AnibelError> = std::panic::catch_unwind(AssertUnwindSafe(|| {
        let mode: CacheMode =
            serde_json::from_value(request.get("cache").cloned().unwrap_or(json!("default")))
                .map_err(|_| AnibelError::BadArgs("cache must be default or reload".into()))?;
        state.runtime.block_on(async {
            if token.is_cancelled() {return Err(AnibelError::Cancelled);}
            if crate::application::must_complete(op) {return state.application.call(op,&args,mode).await;}
            tokio::select! {biased; _=token.cancelled()=>Err(AnibelError::Cancelled), result=state.application.call(op,&args,mode)=>result}
        })
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

    state
        .requests
        .lock()
        .unwrap_or_else(std::sync::PoisonError::into_inner)
        .remove(&request_id);
    let response = match result {
        Ok(value) => json!({ "id": id, "ok": true, "value": value }),
        Err(e) => {
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
    fn cancellation_does_not_drop_an_accepted_mutation() {
        use std::io::{Read, Write};
        use std::time::Duration;
        let listener = std::net::TcpListener::bind("127.0.0.1:0").unwrap();
        let address = listener.local_addr().unwrap();
        let (ready_send, ready) = std::sync::mpsc::channel();
        let (release, release_receive) = std::sync::mpsc::channel();
        let server = std::thread::spawn(move || {
            let (mut stream, _) = listener.accept().unwrap();
            stream
                .set_read_timeout(Some(Duration::from_secs(3)))
                .unwrap();
            let mut request = Vec::new();
            let mut buffer = [0u8; 4096];
            loop {
                let n = stream.read(&mut buffer).unwrap();
                assert!(n > 0);
                request.extend_from_slice(&buffer[..n]);
                if let Some(end) = request.windows(4).position(|w| w == b"\r\n\r\n") {
                    let header = String::from_utf8_lossy(&request[..end]).to_lowercase();
                    let length = header
                        .lines()
                        .find_map(|l| l.strip_prefix("content-length:").map(str::trim))
                        .unwrap()
                        .parse::<usize>()
                        .unwrap();
                    if request.len() >= end + 4 + length {
                        break;
                    }
                }
            }
            ready_send.send(()).unwrap();
            release_receive
                .recv_timeout(Duration::from_secs(3))
                .unwrap();
            let body = r#"{"data":{"addFavorite":{"mediaId":"1"}}}"#;
            write!(stream,"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{}",body.len(),body).unwrap();
        });
        let config =
            CString::new(json!({"baseUrl":format!("http://{address}/graphql")}).to_string())
                .unwrap();
        let h = unsafe { anibel_core_init(config.as_ptr()) };
        assert_eq!(anibel_core_request_begin(h, 17), 1);
        let (result_send, result) = std::sync::mpsc::channel();
        let caller = std::thread::spawn(move || {
            result_send.send(call(h,r#"{"id":17,"op":"setFavorite","args":{"mediaId":"1","mediaType":"anime","selected":true}}"#)).unwrap()
        });
        ready.recv_timeout(Duration::from_secs(3)).unwrap();
        anibel_core_cancel(h, 17);
        assert!(matches!(
            result.recv_timeout(Duration::from_millis(50)),
            Err(std::sync::mpsc::RecvTimeoutError::Timeout)
        ));
        release.send(()).unwrap();
        assert_eq!(
            result.recv_timeout(Duration::from_secs(3)).unwrap()["ok"],
            true
        );
        caller.join().unwrap();
        server.join().unwrap();
        unsafe {
            anibel_core_shutdown(h);
        }
    }

    #[test]
    fn cancellation_interrupts_an_active_http_request() {
        let server = httpmock::MockServer::start();
        server.mock(|w, t| {
            w.method("POST");
            t.delay(std::time::Duration::from_secs(5))
                .json_body(json!({"data":{"search":[]}}));
        });
        let config = CString::new(json!({"baseUrl":server.url("/graphql")}).to_string()).unwrap();
        let h = unsafe { anibel_core_init(config.as_ptr()) };
        assert_eq!(anibel_core_request_begin(h, 9), 1);
        let (send, receive) = std::sync::mpsc::channel();
        let thread = std::thread::spawn(move || {
            send.send(call(h, r#"{"id":9,"op":"search","args":{"query":"test"}}"#))
                .unwrap();
        });
        let start = std::time::Instant::now();
        loop {
            let state = handles().read().unwrap().get(&h).unwrap().clone();
            if state
                .requests
                .lock()
                .unwrap()
                .get(&9)
                .is_some_and(|(_, running)| *running)
            {
                break;
            }
            assert!(start.elapsed() < std::time::Duration::from_secs(2));
            std::thread::sleep(std::time::Duration::from_millis(5));
        }
        std::thread::sleep(std::time::Duration::from_millis(50));
        anibel_core_cancel(h, 9);
        let result = receive
            .recv_timeout(std::time::Duration::from_secs(2))
            .unwrap();
        assert_eq!(result["error"]["code"], "cancelled");
        thread.join().unwrap();
        unsafe {
            anibel_core_shutdown(h);
        }
    }

    #[test]
    fn cancellation_before_call_is_not_lost_and_reservations_are_released() {
        let h = init();
        assert_eq!(anibel_core_request_begin(h, 42), 1);
        assert_eq!(anibel_core_request_begin(h, 42), 0);
        anibel_core_cancel(h, 42);
        let result = call(h, r#"{"id":42,"op":"health"}"#);
        assert_eq!(result["error"]["code"], "cancelled");
        assert_eq!(call(h, r#"{"id":42,"op":"health"}"#)["ok"], true);
        unsafe {
            anibel_core_shutdown(h);
        }
    }

    #[test]
    fn cache_mode_and_clear_contract() {
        let h = init();
        let resp = call(h, r#"{"id": 1, "op": "clearCache", "args": {}}"#);
        assert_eq!(resp, json!({"id":1,"ok":true,"value":{"status":"ok"}}));
        let resp = call(h, r#"{"id": 2, "op": "health", "cache": "reload"}"#);
        assert_eq!(resp["ok"], true);
        let resp = call(h, r#"{"id": 3, "op": "health", "cache": "unknown"}"#);
        assert_eq!(resp["error"]["code"], "bad_args");
        unsafe {
            anibel_core_shutdown(h);
        }
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
