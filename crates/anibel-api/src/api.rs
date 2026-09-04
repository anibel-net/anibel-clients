//! Typed API client over the anibel.net GraphQL endpoint.
//!
//! Responses decode into crate-private `graphql_client` types then map to
//! frozen domain DTOs before leaving this crate. Thread-safe: every call
//! is `&self`; the token lives in a lock.
//!
//! The implementation is split into domain-area modules; this facade keeps
//! the public paths stable — [`AnibelApi`], [`DEFAULT_BASE_URL`] and the
//! crate-root `media_list_filters_from_json` re-export.
//!
//! - `transport`: client construction, retry policy, error sniffing
//! - `auth`: login / logout
//! - `catalog`: search, media, media list (+ poster-failure resilience)
//! - `media`: episodes (+ paginated matrix), chapters, comments
//! - `home`: trends, updates, recommendations, schedule, slider, filters,
//!   statistics, random media
//! - `user`: user profile, favorites, marks, status counters
//! - `mutations`: markAs / removeMark / addFavorite / removeFavorite /
//!   addHistoryRecord / removeHistoryRecord

pub use crate::transport::{AnibelApi, DEFAULT_BASE_URL};
