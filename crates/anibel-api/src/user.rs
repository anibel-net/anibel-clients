//! User/profile queries: user, favorites, marks, status counters.

use crate::gql;
use crate::transport::AnibelApi;
use crate::util::{ValueDeserialize, gql_payload, opt_enum, to_enum};
use anibel_domain::error::{AnibelError, Result};
use anibel_domain::models::{MarkEntry, MediaCard, Page, Profile, StatusCounters};
use graphql_client::GraphQLQuery;
use serde_json::Value;

impl AnibelApi {
    pub async fn update_profile(&self, input: Value) -> Result<Profile> {
        let variables: gql::update_profile_mutation::Variables =
            serde_json::from_value(serde_json::json!({"input": input.clone()}))
                .map_err(|e| AnibelError::BadArgs(e.to_string()))?;
        let mut body = gql_payload!(UpdateProfileMutation, "UpdateProfileMutation", variables);
        // Unspecified optional fields must be omitted, not written as null.
        body["variables"]["input"] = input;
        let data = self
            .request_no_retry::<Value>(body)
            .await?
            .deserialize::<gql::update_profile_mutation::ResponseData>()?;
        crate::map::profile_opt(data.update_user)
            .ok_or_else(|| AnibelError::Graphql("updateUser: null".into()))
    }

    pub async fn upload_profile_image(
        &self,
        bytes: Vec<u8>,
        mime: &str,
        name: &str,
    ) -> Result<String> {
        let form = reqwest::multipart::Form::new()
            .text(
                "operations",
                serde_json::json!({
                    "query": include_str!("../graphql/ops/upload_profile_image.graphql"),
                    "variables": {"file": null, "type": "MEDIA"}
                })
                .to_string(),
            )
            .text("map", r#"{"0":["variables.file"]}"#)
            .part(
                "0",
                reqwest::multipart::Part::bytes(bytes)
                    .file_name(name.to_owned())
                    .mime_str(mime)
                    .map_err(|e| AnibelError::BadArgs(e.to_string()))?,
            );
        let token = self
            .token
            .read()
            .await
            .clone()
            .ok_or(AnibelError::Unauthorized)?;
        let response = self
            .http
            .post(&self.base_url)
            .bearer_auth(token)
            .header("Apollo-Require-Preflight", "true")
            .multipart(form)
            .send()
            .await
            .map_err(|e| AnibelError::Transport(e.to_string()))?;
        let value = crate::transport::decode_response(response).await?;
        let path = value
            .pointer("/upload/path")
            .and_then(Value::as_str)
            .filter(|p| !p.is_empty())
            .ok_or_else(|| AnibelError::Decode("Upload returned no image URL".into()))?;
        let base =
            reqwest::Url::parse(&self.base_url).map_err(|e| AnibelError::Decode(e.to_string()))?;
        let url = if path.starts_with('/') {
            base.join(path)
        } else {
            reqwest::Url::parse(path)
        }
        .map_err(|_| AnibelError::Decode("Upload returned an invalid image URL".into()))?;
        if !matches!(url.scheme(), "http" | "https") {
            return Err(AnibelError::Decode(
                "Upload returned an invalid image URL".into(),
            ));
        }
        Ok(url.to_string())
    }

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
