//! Catalog queries: search, media detail, media list (with the resilient
//! poster fallback) and the bad-media-URL sniffer.

use crate::gql;
use crate::transport::{AnibelApi, SNIFF_MSG_INVALID_URL, SNIFF_MSG_NULL_NON_NULLABLE};
use crate::util::{ValueDeserialize, gql_payload, opt_enum, to_enum};
use anibel_domain::error::{AnibelError, Result};
use anibel_domain::models::{MediaCard, MediaDetail, Page};
use graphql_client::GraphQLQuery;
use serde_json::Value;

impl AnibelApi {
    pub async fn search(&self, query: &str, limit: i64) -> Result<Vec<MediaCard>> {
        let data = self
            .request::<Value>(gql_payload!(
                SearchQuery,
                "SearchQuery",
                gql::search_query::Variables {
                    query: query.to_string(),
                    limit: Some(limit),
                },
            ))
            .await?
            .deserialize::<gql::search_query::ResponseData>()?;
        crate::map::media_cards_opt(data.search)
    }

    pub async fn media(
        &self,
        slug: &str,
        media_type: Option<String>,
    ) -> Result<Option<MediaDetail>> {
        match self.media_once(slug, media_type.clone(), true).await {
            Ok(v) => Ok(v),
            Err(e) if is_bad_media_url(&e) => self.media_once(slug, media_type, false).await,
            Err(e) => Err(e),
        }
    }

    async fn media_once(
        &self,
        slug: &str,
        media_type: Option<String>,
        with_urls: bool,
    ) -> Result<Option<MediaDetail>> {
        if with_urls {
            let media_type = opt_enum::<gql::media_query::MediaTypes>(media_type.as_deref())?;
            let data = self
                .request::<Value>(gql_payload!(
                    MediaQuery,
                    "MediaQuery",
                    gql::media_query::Variables {
                        slug: slug.to_string(),
                        media_type,
                    }
                ))
                .await?
                .deserialize::<gql::media_query::ResponseData>()?;
            return crate::map::media_detail_opt(data.media);
        }
        let media_type = opt_enum::<gql::media_safe_query::MediaTypes>(media_type.as_deref())?;
        let data = self
            .request::<Value>(gql_payload!(
                MediaSafeQuery,
                "MediaSafeQuery",
                gql::media_safe_query::Variables {
                    slug: slug.to_string(),
                    media_type,
                }
            ))
            .await?
            .deserialize::<gql::media_safe_query::ResponseData>()?;
        crate::map::media_detail_opt(data.media)
    }

    pub async fn media_list(
        &self,
        media_type: &str,
        offset: i64,
        limit: i64,
        filters: Option<gql::media_list_query::MediaFilters>,
    ) -> Result<Page<MediaCard>> {
        self.media_list_resilient(media_type, offset, limit, filters, true)
            .await
    }

    async fn media_list_resilient(
        &self,
        media_type: &str,
        offset: i64,
        limit: i64,
        filters: Option<gql::media_list_query::MediaFilters>,
        with_poster: bool,
    ) -> Result<Page<MediaCard>> {
        match self
            .media_list_once(media_type, offset, limit, filters.clone(), with_poster)
            .await
        {
            Ok(page) => Ok(page),
            Err(e) if is_bad_media_url(&e) => {
                if limit <= 1 {
                    if with_poster {
                        return self
                            .media_list_once(media_type, offset, limit, filters, false)
                            .await;
                    }
                    return Ok(Page {
                        docs: Vec::new(),
                        total_docs: 0,
                        limit: Some(limit),
                        offset: Some(offset),
                    });
                }
                let left_n = (limit / 2).max(1);
                let right_n = limit - left_n;
                let (left, right) = tokio::join!(
                    Box::pin(self.media_list_resilient(
                        media_type,
                        offset,
                        left_n,
                        filters.clone(),
                        with_poster
                    )),
                    Box::pin(self.media_list_resilient(
                        media_type,
                        offset + left_n,
                        right_n,
                        filters,
                        with_poster
                    )),
                );
                Ok(merge_media_pages(left?, right?, offset, limit))
            }
            Err(e) => Err(e),
        }
    }

    async fn media_list_once(
        &self,
        media_type: &str,
        offset: i64,
        limit: i64,
        filters: Option<gql::media_list_query::MediaFilters>,
        with_poster: bool,
    ) -> Result<Page<MediaCard>> {
        if with_poster {
            let data = self
                .request::<Value>(gql_payload!(
                    MediaListQuery,
                    "MediaListQuery",
                    gql::media_list_query::Variables {
                        media_type: to_enum::<gql::media_list_query::MediaTypes>(media_type)?,
                        offset,
                        limit,
                        filters,
                    },
                ))
                .await?
                .deserialize::<gql::media_list_query::ResponseData>()?;
            return crate::map::page_media(
                data.get_media_list
                    .ok_or_else(|| AnibelError::Graphql("getMediaList: null".into()))?,
            );
        }

        let safe_filters = filters.and_then(|f| {
            serde_json::to_value(f)
                .ok()
                .and_then(|v| serde_json::from_value(v).ok())
        });
        let data = self
            .request::<Value>(gql_payload!(
                MediaListSafeQuery,
                "MediaListSafeQuery",
                gql::media_list_safe_query::Variables {
                    media_type: to_enum::<gql::media_list_safe_query::MediaTypes>(media_type)?,
                    offset,
                    limit,
                    filters: safe_filters,
                },
            ))
            .await?
            .deserialize::<gql::media_list_safe_query::ResponseData>()?;
        crate::map::page_media(
            data.get_media_list
                .ok_or_else(|| AnibelError::Graphql("getMediaList: null".into()))?,
        )
    }
}

/// True when the resolver poisoned a response through a bad media URL —
/// either a malformed URL (`Invalid URL`) or a null under a non-nullable
/// field (e.g. a poster resolving to null). The catalog fallbacks then
/// retry without poster/url fields or halve the page to isolate the bad row.
fn is_bad_media_url(err: &AnibelError) -> bool {
    match err {
        AnibelError::Graphql(msg) => {
            let m = msg.to_ascii_lowercase();
            m.contains(SNIFF_MSG_INVALID_URL) || m.contains(SNIFF_MSG_NULL_NON_NULLABLE)
        }
        _ => false,
    }
}

fn merge_media_pages(
    mut left: Page<MediaCard>,
    right: Page<MediaCard>,
    offset: i64,
    limit: i64,
) -> Page<MediaCard> {
    let total = left.total_docs.max(right.total_docs);
    left.docs.extend(right.docs);
    Page {
        docs: left.docs,
        total_docs: total,
        limit: Some(limit),
        offset: Some(offset),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn is_bad_media_url_sniffs_both_backend_wordings() {
        // "Invalid URL" — malformed/undownloadable poster URL.
        assert!(is_bad_media_url(&AnibelError::Graphql(
            "Invalid URL".into()
        )));
        // null under non-nullable field — poster URL resolved to null.
        assert!(is_bad_media_url(&AnibelError::Graphql(
            "Cannot return null for non-nullable field Episode.poster".into()
        )));
        // Genuine resolver/validation errors are NOT treated as bad media.
        assert!(!is_bad_media_url(&AnibelError::Graphql(
            "Cannot query field x".into()
        )));
        // Only Graphql errors carry resolver wording; transport/http/decoding
        // failures are never misclassified.
        assert!(!is_bad_media_url(&AnibelError::Http(500, "raw".into())));
        assert!(!is_bad_media_url(&AnibelError::Transport(
            "connect refused".into()
        )));
        assert!(!is_bad_media_url(&AnibelError::Unauthorized));
    }
}
