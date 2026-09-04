//! HTTP transport: client construction, retry policy and error sniffing.
//!
//! Responses decode into crate-private `graphql_client` types in the
//! domain-area modules; this module owns the wire-level request lifecycle.

use anibel_domain::error::{AnibelError, Result};
use serde::de::DeserializeOwned;
use serde_json::Value;
use std::sync::Arc;
use std::time::Duration;
use tokio::sync::RwLock;

pub const DEFAULT_BASE_URL: &str = "https://anibel.net/graphql";

#[derive(Clone)]
pub struct AnibelApi {
    pub(crate) http: reqwest::Client,
    pub(crate) base_url: String,
    pub(crate) token: Arc<RwLock<Option<String>>>,
}

impl Default for AnibelApi {
    fn default() -> Self {
        Self::new()
    }
}

fn reqwest_err(e: reqwest::Error) -> AnibelError {
    if e.is_timeout() {
        AnibelError::Transport("timeout".into())
    } else if e.is_connect() {
        AnibelError::Transport(format!("connect: {e}"))
    } else {
        AnibelError::Transport(e.to_string())
    }
}

// ---------------------------------------------------------------------------
// backend error sniffing
// ---------------------------------------------------------------------------
//
// `extensions.code` is the typed, backend-stable discriminator; the message
// strings are the backend's own wording and may drift — they are matched
// case-insensitively as a fallback.

/// `extensions.code` the backend attaches to expired/rejected JWTs.
const EXT_CODE_UNAUTHENTICATED: &str = "UNAUTHENTICATED";
/// Backend wording for resolver errors requiring a logged-in caller.
const SNIFF_MSG_MUST_BE_LOGGED_IN: &str = "must be logged in";
/// Backend wording for permission-denied resolver errors.
const SNIFF_MSG_UNAUTHORIZED: &str = "unauthorized";
/// Backend wording for expired JWTs.
const SNIFF_MSG_JWT_EXPIRED: &str = "jwt expired";
/// Backend wording for rejected/malformed JWTs.
const SNIFF_MSG_INVALID_TOKEN: &str = "invalid token";
/// Backend wording for malformed/undownloadable media URLs (e.g. a poster
/// the resolver cannot fetch). Recognised by the catalog fallback paths.
pub(super) const SNIFF_MSG_INVALID_URL: &str = "invalid url";
/// Backend wording when a resolver returns `null` under a non-nullable
/// schema field (a poster URL resolving to null surfaces like this).
pub(super) const SNIFF_MSG_NULL_NON_NULLABLE: &str = "cannot return null for non-nullable field";

pub(super) fn graphql_errors(errors: &[Value]) -> AnibelError {
    let mut messages = Vec::new();
    let mut unauthorized = false;
    for err in errors {
        let msg = err
            .get("message")
            .and_then(Value::as_str)
            .unwrap_or("unknown graphql error");
        let ext_code = err
            .get("extensions")
            .and_then(|x| x.get("code"))
            .and_then(Value::as_str)
            .unwrap_or("");
        let hay = format!("{ext_code} {msg}").to_ascii_lowercase();
        // Prefer the typed `extensions.code`; only fall back to message
        // sniffing when the code is missing or unfamiliar.
        if ext_code.eq_ignore_ascii_case(EXT_CODE_UNAUTHENTICATED)
            || hay.contains(SNIFF_MSG_MUST_BE_LOGGED_IN)
            || hay.contains(SNIFF_MSG_UNAUTHORIZED)
            || hay.contains(SNIFF_MSG_JWT_EXPIRED)
            || hay.contains(SNIFF_MSG_INVALID_TOKEN)
        {
            unauthorized = true;
        }
        messages.push(msg.to_string());
    }
    if unauthorized {
        AnibelError::Unauthorized
    } else {
        AnibelError::Graphql(messages.join(" · "))
    }
}

/// Error classes that may be transient: Cloudflare challenge pages
/// (Http(403) or non-JSON bodies → Decode), rate limits (429), server
/// errors (5xx), network/transport failures. Genuine GraphQL resolver
/// errors are never retried — that would just double latency.
fn is_transient(e: &AnibelError) -> bool {
    matches!(
        e,
        AnibelError::Http(403 | 429, _)
            | AnibelError::Http(500..=599, _)
            | AnibelError::Transport(_)
            | AnibelError::Decode(_)
    )
}

impl AnibelApi {
    pub fn new() -> Self {
        Self::with_base(DEFAULT_BASE_URL.to_string())
    }

    /// Test/dev constructor: point at a mock base URL.
    pub fn with_base(base_url: String) -> Self {
        const UA: &str = concat!(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 ",
            "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
        );
        let headers = reqwest::header::HeaderMap::from_iter([
            (
                reqwest::header::ACCEPT,
                reqwest::header::HeaderValue::from_static("application/json, text/plain, */*"),
            ),
            (
                reqwest::header::ACCEPT_LANGUAGE,
                reqwest::header::HeaderValue::from_static("ru,be;q=0.9,en;q=0.8"),
            ),
            (
                reqwest::header::REFERER,
                reqwest::header::HeaderValue::from_static("https://anibel.net/"),
            ),
        ]);
        let http = reqwest::Client::builder()
            .gzip(true)
            .cookie_store(true)
            .default_headers(headers)
            .user_agent(UA)
            .timeout(Duration::from_secs(30))
            .build()
            .expect("reqwest client");
        AnibelApi {
            http,
            base_url,
            token: Arc::new(RwLock::new(None)),
        }
    }

    pub fn override_base_url(&mut self, url: &str) {
        self.base_url = url.to_string();
    }

    pub async fn set_token(&self, token: Option<String>) {
        *self.token.write().await = token;
    }

    pub async fn has_token(&self) -> bool {
        self.token.read().await.is_some()
    }

    // ------------------------------------------------------------------
    // transport
    // ------------------------------------------------------------------

    /// Read-only query: retries once (700ms backoff) on transient failures.
    pub(crate) async fn request<T>(&self, body: Value) -> Result<T>
    where
        T: DeserializeOwned,
    {
        self.request_with_retry_policy(body, true).await
    }

    /// Mutation: NEVER retried. These ops are not idempotent — a server 500
    /// *after* persisting would replay the write on retry and duplicate the
    /// comment/mark/favorite/history record. The caller sees the error and
    /// decides what to do.
    pub(crate) async fn request_no_retry<T>(&self, body: Value) -> Result<T>
    where
        T: DeserializeOwned,
    {
        self.request_with_retry_policy(body, false).await
    }

    pub(crate) async fn request_with_retry_policy<T>(
        &self,
        body: Value,
        retryable: bool,
    ) -> Result<T>
    where
        T: DeserializeOwned,
    {
        // Cloudflare occasionally returns 403 challenge pages under rapid
        // fire — one short retry with backoff is enough in practice.
        let token = self.token.read().await.clone();

        let mut last_err: Option<AnibelError> = None;
        for attempt in 0..2 {
            match self.try_request(body.clone(), token.clone()).await {
                Ok(value) => {
                    return serde_json::from_value(value)
                        .map_err(|e| AnibelError::Decode(e.to_string()));
                }
                Err(e) => {
                    last_err = Some(e.clone());
                    if retryable && is_transient(&e) && attempt == 0 {
                        tokio::time::sleep(std::time::Duration::from_millis(700)).await;
                        continue;
                    }
                    break;
                }
            }
        }
        Err(last_err.unwrap_or_else(|| AnibelError::Transport("empty".into())))
    }

    pub(crate) async fn try_request(&self, body: Value, token: Option<String>) -> Result<Value> {
        let mut req = self.http.post(&self.base_url).json(&body);

        if let Some(token) = token {
            req = req.bearer_auth(token);
        }

        let response = req.send().await.map_err(reqwest_err)?;
        let status = response.status();
        let text = response
            .text()
            .await
            .map_err(|e| AnibelError::Decode(format!("body read: {e}")))?;
        let body: Value = serde_json::from_str(&text).map_err(|e| {
            let preview = text.chars().take(200).collect::<String>();
            AnibelError::Decode(format!("status={status} body-start=`{preview}` err={e}"))
        })?;

        if let Some(errors) = body.get("errors").and_then(Value::as_array)
            && !errors.is_empty()
        {
            return Err(graphql_errors(errors));
        }

        if !status.is_success() {
            return Err(AnibelError::Http(status.as_u16(), body.to_string()));
        }

        let data = body
            .get("data")
            .ok_or_else(|| AnibelError::Graphql("no data in response".into()))?;

        serde_json::from_value(data.clone()).map_err(|e| AnibelError::Decode(e.to_string()))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn graphql_errors_join_messages_and_detect_auth() {
        let errors = serde_json::json!([
            { "message": "Cannot query field x", "extensions": { "code": "GRAPHQL_VALIDATION_FAILED" } }
        ]);
        let err = graphql_errors(errors.as_array().unwrap());
        assert!(matches!(err, AnibelError::Graphql(m) if m.contains("Cannot query field")));

        let auth = serde_json::json!([
            { "message": "You must be logged in", "extensions": { "code": "UNAUTHENTICATED" } }
        ]);
        assert!(matches!(
            graphql_errors(auth.as_array().unwrap()),
            AnibelError::Unauthorized
        ));
    }

    #[test]
    fn graphql_errors_trusts_extensions_code_when_message_is_opaque() {
        // The typed `extensions.code` must win even when the message text
        // carries none of the sniffed phrases (backend wording may change).
        let errors = serde_json::json!([
            { "message": "token rejected by federation", "extensions": { "code": "UNAUTHENTICATED" } }
        ]);
        assert!(matches!(
            graphql_errors(errors.as_array().unwrap()),
            AnibelError::Unauthorized
        ));
    }

    #[test]
    fn graphql_errors_sniffs_message_as_fallback_without_code() {
        for msg in [
            "must be logged in",
            "Unauthorized",
            "jwt expired",
            "invalid token",
        ] {
            let errors = serde_json::json!([{ "message": msg }]);
            assert!(
                matches!(
                    graphql_errors(errors.as_array().unwrap()),
                    AnibelError::Unauthorized
                ),
                "message `{msg}` should sniff as unauthorized"
            );
        }
    }

    #[test]
    fn transient_classes_match_retry_whitelist() {
        assert!(is_transient(&AnibelError::Http(403, "".into())));
        assert!(is_transient(&AnibelError::Http(429, "".into())));
        assert!(is_transient(&AnibelError::Http(500, "".into())));
        assert!(is_transient(&AnibelError::Http(599, "".into())));
        assert!(is_transient(&AnibelError::Transport("down".into())));
        assert!(is_transient(&AnibelError::Decode("bad json".into())));
        // GraphQL resolver errors are genuine, not transient.
        assert!(!is_transient(&AnibelError::Graphql("resolver boom".into())));
        assert!(!is_transient(&AnibelError::Http(400, "".into())));
        assert!(!is_transient(&AnibelError::Unauthorized));
    }
}
