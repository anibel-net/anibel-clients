//! User/profile queries: user, favorites, marks, status counters.

use crate::gql;
use crate::transport::AnibelApi;
use crate::util::{ValueDeserialize, gql_payload, opt_enum, to_enum};
use anibel_domain::error::{AnibelError, Result};
use anibel_domain::models::{MarkEntry, MediaCard, Page, Profile, StatusCounters};
use graphql_client::GraphQLQuery;
use serde_json::Value;

impl AnibelApi {
    pub async fn user(&self, username: &str) -> Result<Option<Profile>> {
        let data = self
            .request::<Value>(gql_payload!(
                UserQuery,
                "UserQuery",
                gql::user_query::Variables {
                    username: username.to_string(),
                }
            ))
            .await?
            .deserialize::<gql::user_query::ResponseData>()?;
        Ok(crate::map::profile_opt(data.user))
    }

    pub async fn favorites(
        &self,
        username: &str,
        media_type: Option<String>,
        offset: i64,
        limit: i64,
    ) -> Result<Page<MediaCard>> {
        let media_type = opt_enum::<gql::favorites_query::MediaTypes>(media_type.as_deref())?;
        let data = self
            .request::<Value>(gql_payload!(
                FavoritesQuery,
                "FavoritesQuery",
                gql::favorites_query::Variables {
                    username: username.to_string(),
                    media_type,
                    offset: Some(offset),
                    limit: Some(limit),
                },
            ))
            .await?
            .deserialize::<gql::favorites_query::ResponseData>()?;
        Ok(crate::map::page_media(data.favorites.ok_or_else(|| {
            AnibelError::Graphql("favorites: null".into())
        })?))
    }

    pub async fn marks(
        &self,
        username: &str,
        media_type: Option<String>,
        offset: i64,
        limit: i64,
    ) -> Result<Page<MarkEntry>> {
        let media_type = opt_enum::<gql::marks_query::MediaTypes>(media_type.as_deref())?;
        let data = self
            .request::<Value>(gql_payload!(
                MarksQuery,
                "MarksQuery",
                gql::marks_query::Variables {
                    username: username.to_string(),
                    media_type,
                    offset: Some(offset),
                    limit: Some(limit),
                }
            ))
            .await?
            .deserialize::<gql::marks_query::ResponseData>()?;
        Ok(crate::map::page_marks(data.marks.ok_or_else(|| {
            AnibelError::Graphql("marks: null".into())
        })?))
    }

    pub async fn status(&self, username: &str, media_type: &str) -> Result<StatusCounters> {
        let data = self
            .request::<Value>(gql_payload!(
                StatusQuery,
                "StatusQuery",
                gql::status_query::Variables {
                    username: username.to_string(),
                    media_type: to_enum::<gql::status_query::MediaTypes>(media_type)?,
                },
            ))
            .await?
            .deserialize::<gql::status_query::ResponseData>()?;
        Ok(crate::map::status(data.status))
    }
}
