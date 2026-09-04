//! GraphQL transport + mapping onto frozen domain DTOs.

pub mod api;
pub(crate) mod gql;
pub(crate) mod map;

mod auth;
mod catalog;
mod home;
mod media;
mod mutations;
mod transport;
mod user;
mod util;

pub use api::{AnibelApi, DEFAULT_BASE_URL};
pub use gql::media_list_filters_from_json;
