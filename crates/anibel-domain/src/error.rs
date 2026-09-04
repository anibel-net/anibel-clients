//! Machine-readable error model. Errors cross the FFI boundary as
//! `{ code, message }` — the wire shape is frozen; the host (C#) never
//! reads any other key.

use serde::Serialize;

#[derive(Debug, Clone, Serialize)]
pub struct ErrorDto {
    pub code: String,
    pub message: String,
}

#[derive(Debug, Clone, thiserror::Error)]
pub enum AnibelError {
    #[error("http error: {1}")]
    Http(u16, String),

    #[error("graphql error: {0}")]
    Graphql(String),

    #[error("unauthorized (token expired or rejected)")]
    Unauthorized,

    #[error("transport error: {0}")]
    Transport(String),

    #[error("deserialization error: {0}")]
    Decode(String),

    #[error("invalid arguments: {0}")]
    BadArgs(String),

    #[error("not found: {0}")]
    NotFound(String),

    #[error("internal error: {0}")]
    Internal(String),
}

impl AnibelError {
    pub fn code(&self) -> &'static str {
        match self {
            AnibelError::Http(code, _) => match *code {
                401 => "http_401",
                404 => "http_404",
                _ => "http_error",
            },
            AnibelError::Graphql(_) => "graphql_error",
            AnibelError::Unauthorized => "auth_expired",
            AnibelError::Transport(_) => "transport_error",
            AnibelError::Decode(_) => "decode_error",
            AnibelError::BadArgs(_) => "bad_args",
            AnibelError::NotFound(_) => "not_found",
            AnibelError::Internal(_) => "internal",
        }
    }

    pub fn to_dto(&self) -> ErrorDto {
        ErrorDto {
            code: self.code().to_string(),
            message: self.to_string(),
        }
    }
}

pub type Result<T> = std::result::Result<T, AnibelError>;
