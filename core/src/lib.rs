//! Shared application behaviour and C ABI over the domain/api/player crates.

pub use anibel_api::DEFAULT_BASE_URL;
pub use anibel_api::api;
pub use anibel_domain::error;
pub use anibel_domain::models;
pub use anibel_player::player;
pub use anibel_player::video;

pub mod application;
pub mod ffi;

pub use ffi::*;
