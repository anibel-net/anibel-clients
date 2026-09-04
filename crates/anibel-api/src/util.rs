//! Small helpers shared by the API client submodules.

use anibel_domain::error::{AnibelError, Result};
use serde::de::DeserializeOwned;
use serde_json::Value;

/// Generic enum conversion — generated enums have an `Other(String)` fallback
/// variant, so arbitrary strings deserialize losslessly (serde-based).
/// Returns an error instead of panicking: bad host input must never unwind
/// across the C ABI.
pub(super) fn to_enum<T>(s: &str) -> Result<T>
where
    T: DeserializeOwned,
{
    serde_json::from_value(Value::String(s.to_string()))
        .map_err(|_| AnibelError::BadArgs(format!("invalid enum value `{s}`")))
}

pub(super) fn opt_enum<T>(s: Option<&str>) -> Result<Option<T>>
where
    T: DeserializeOwned,
{
    s.map(to_enum).transpose()
}

/// Wrap a produced GraphQL request body: `query` + `variables` +
/// `operationName` (one operation per document).
macro_rules! gql_payload {
    ($name:ident, $op_name:literal, $vars:expr,) => {
        gql_payload!($name, $op_name, $vars)
    };
    ($name:ident, $op_name:literal, $vars:expr) => {{
        let body = gql::$name::build_query($vars);
        serde_json::json!({
            "query": body.query,
            "variables": body.variables,
            "operationName": $op_name,
        })
    }};
}
pub(super) use gql_payload;

/// `.deserialize::<ResponseData>()` sugar used by every operation: the
/// generated response types live in `crate::gql` but are only reachable
/// through their module paths, so we pass through JSON.
pub(super) trait ValueDeserialize: Sized {
    fn deserialize<T: DeserializeOwned>(self) -> Result<T>;
}

impl ValueDeserialize for Value {
    fn deserialize<T: DeserializeOwned>(self) -> Result<T> {
        serde_json::from_value(self).map_err(|e| AnibelError::Decode(e.to_string()))
    }
}
