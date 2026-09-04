//! Episodes / chapters / comments, including the paginated episodes matrix.

use crate::gql;
use crate::transport::AnibelApi;
use crate::util::{ValueDeserialize, gql_payload, to_enum};
use anibel_domain::error::{AnibelError, Result};
use anibel_domain::models::{Chapter, Comment, Episode, Page};
use graphql_client::GraphQLQuery;
use serde_json::Value;
use std::future::Future;

/// Hard cap on pages fetched per sub/dub × resource combo in
/// [`AnibelApi::episodes_matrix`]. 20 pages × 100 docs = 2000 — far beyond
/// any real episode list; the cap keeps a poisoned/inflated `totalDocs`
/// from turning pagination into an unbounded loop.
pub(super) const EPISODE_PAGES_MAX: usize = 20;

/// Page size for every episodes request (site parity: the old code capped at
/// 100/combo).
pub(super) const EPISODE_PAGE_SIZE: i64 = 100;

/// Pure pagination driver: fetches page 1 at offset 0, then follows the
/// server's `totalDocs` with offset/limit pages until the docs seen cover
/// `totalDocs` or [`EPISODE_PAGES_MAX`] pages were fetched. A page with zero
/// docs also stops the loop (guards a lying `totalDocs`). `fetch(offset,
/// limit)` is injected, so this is fully offline-testable.
pub(super) async fn fetch_all_pages<T, F, Fut>(mut fetch: F) -> Result<Vec<T>>
where
    F: FnMut(i64, i64) -> Fut,
    Fut: Future<Output = Result<Page<T>>>,
{
    let mut docs: Vec<T> = Vec::new();
    for _ in 0..EPISODE_PAGES_MAX {
        let offset = docs.len() as i64;
        let page = fetch(offset, EPISODE_PAGE_SIZE).await?;
        let page_len = page.docs.len();
        let total = page.total_docs.max(docs.len() as i64);
        docs.extend(page.docs);
        if page_len == 0 || docs.len() as i64 >= total {
            break;
        }
    }
    Ok(docs)
}

impl AnibelApi {
    pub async fn episodes(
        &self,
        media_id: &str,
        type_: &str,
        resource: i64,
        limit: Option<i64>,
    ) -> Result<Page<Episode>> {
        self.episodes_page(media_id, type_, resource, 0, limit)
            .await
    }

    async fn episodes_page(
        &self,
        media_id: &str,
        type_: &str,
        resource: i64,
        offset: i64,
        limit: Option<i64>,
    ) -> Result<Page<Episode>> {
        let data = self
            .request::<Value>(gql_payload!(
                EpisodesQuery,
                "EpisodesQuery",
                gql::episodes_query::Variables {
                    media_id: media_id.to_string(),
                    type_: type_.to_string(),
                    resource,
                    // MUST be Some(offset), never null: the prod resolver
                    // computes `offset: NaN` for null and Int serialization
                    // of NaN poisons the whole response
                    // ("Int cannot represent non-integer value: NaN").
                    offset: Some(offset),
                    limit,
                },
            ))
            .await?
            .deserialize::<gql::episodes_query::ResponseData>()?;
        Ok(crate::map::page_episodes(data.episodes.ok_or_else(
            || AnibelError::Graphql("episodes: null".into()),
        )?))
    }

    /// Fetch all four sub/dub × resource 1/2 combos and merge (site parity).
    /// Each combo pages past the first 100 docs by following `totalDocs`
    /// (hard cap [`EPISODE_PAGES_MAX`] pages/combo), while the four combos
    /// still run concurrently — 4 serial round trips doubled the
    /// details-page latency.
    pub async fn episodes_matrix(&self, media_id: &str) -> Result<Vec<Episode>> {
        let sub1 = self.episodes_combo(media_id, "sub", 1);
        let sub2 = self.episodes_combo(media_id, "sub", 2);
        let dub1 = self.episodes_combo(media_id, "dub", 1);
        let dub2 = self.episodes_combo(media_id, "dub", 2);
        let (r1, r2, r3, r4) = tokio::join!(sub1, sub2, dub1, dub2);

        let mut all = Vec::new();
        for combo in [r1, r2, r3, r4] {
            match combo {
                Ok(docs) => all.extend(docs),
                // Resolver quirks on prod: empty combos can surface as
                // graphql errors / 500-on-missing — treat as "no docs".
                Err(AnibelError::Graphql(_)) | Err(AnibelError::NotFound(_)) => {}
                Err(e) => return Err(e),
            }
        }
        Ok(all)
    }

    async fn episodes_combo(
        &self,
        media_id: &str,
        type_: &str,
        resource: i64,
    ) -> Result<Vec<Episode>> {
        fetch_all_pages(|offset, limit| {
            self.episodes_page(media_id, type_, resource, offset, Some(limit))
        })
        .await
    }

    pub async fn chapters(&self, media_id: &str, limit: Option<i64>) -> Result<Page<Chapter>> {
        let data = self
            .request::<Value>(gql_payload!(
                ChaptersQuery,
                "ChaptersQuery",
                gql::chapters_query::Variables {
                    media_id: media_id.to_string(),
                    // see episodes() — null offset makes the resolver emit NaN
                    offset: Some(0),
                    limit,
                },
            ))
            .await?
            .deserialize::<gql::chapters_query::ResponseData>()?;
        Ok(crate::map::page_chapters(data.chapters.ok_or_else(
            || AnibelError::Graphql("chapters: null".into()),
        )?))
    }

    pub async fn chapter(&self, slug: &str, chapter: f64) -> Result<Chapter> {
        let data = self
            .request::<Value>(gql_payload!(
                ChapterQuery,
                "ChapterQuery",
                gql::chapter_query::Variables {
                    slug: slug.to_string(),
                    chapter,
                },
            ))
            .await?
            .deserialize::<gql::chapter_query::ResponseData>()?;
        Ok(crate::map::chapter(data.chapter.ok_or_else(|| {
            AnibelError::NotFound(format!("chapter {chapter} of `{slug}`"))
        })?))
    }

    pub async fn comments(
        &self,
        media_id: &str,
        media_type: &str,
        offset: i64,
        limit: i64,
    ) -> Result<Page<Comment>> {
        let data = self
            .request::<Value>(gql_payload!(
                CommentsQuery,
                "CommentsQuery",
                gql::comments_query::Variables {
                    media_id: media_id.to_string(),
                    media_type: to_enum::<gql::comments_query::MediaTypes>(media_type)?,
                    offset,
                    limit,
                },
            ))
            .await?
            .deserialize::<gql::comments_query::ResponseData>()?;
        Ok(crate::map::page_comments(data.comments.ok_or_else(
            || AnibelError::Graphql("comments: null".into()),
        )?))
    }

    pub async fn add_comment(
        &self,
        media_id: &str,
        media_type: &str,
        content: &str,
        reply_to: Option<&str>,
    ) -> Result<Comment> {
        // Mutation: never retried (see AnibelApi::request_no_retry).
        let data = self
            .request_no_retry::<Value>(gql_payload!(
                AddCommentMutation,
                "AddCommentMutation",
                gql::add_comment_mutation::Variables {
                    input: gql::add_comment_mutation::AddCommentInput {
                        media_id: media_id.to_string(),
                        media_type: to_enum::<gql::add_comment_mutation::MediaTypes>(media_type)?,
                        content: content.to_string(),
                        reply_to: reply_to
                            .map(str::trim)
                            .filter(|s| !s.is_empty())
                            .map(str::to_string),
                    },
                },
            ))
            .await?
            .deserialize::<gql::add_comment_mutation::ResponseData>()?;
        Ok(crate::map::comment(data.add_comment.ok_or_else(|| {
            AnibelError::Graphql("addComment: null".into())
        })?))
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use anibel_domain::models::Page;
    use std::sync::atomic::{AtomicUsize, Ordering};

    fn page(docs: Vec<u64>, total_docs: i64, offset: i64) -> Page<u64> {
        Page {
            docs,
            total_docs,
            limit: Some(EPISODE_PAGE_SIZE),
            offset: Some(offset),
        }
    }

    #[tokio::test]
    async fn fetch_all_pages_follows_total_docs() {
        let pages = vec![
            page((0..100).collect(), 250, 0),
            page((100..200).collect(), 250, 100),
            page((200..250).collect(), 250, 200),
        ];
        let calls = AtomicUsize::new(0);
        let (calls_ref, pages_ref) = (&calls, &pages);
        let all = fetch_all_pages(|offset: i64, _limit: i64| {
            calls_ref.fetch_add(1, Ordering::SeqCst);
            let p = match offset {
                0 => pages_ref[0].clone(),
                100 => pages_ref[1].clone(),
                _ => pages_ref[2].clone(),
            };
            async move { Ok(p) }
        })
        .await
        .unwrap();

        assert_eq!(all.len(), 250);
        assert_eq!(all[0], 0);
        assert_eq!(all[249], 249);
        assert_eq!(
            calls.load(Ordering::SeqCst),
            3,
            "exactly 3 pages for 250 docs"
        );
    }

    #[tokio::test]
    async fn fetch_all_pages_hard_caps_at_max_pages() {
        // Poisoned totalDocs claims 1_000_000 docs; the cap must stop the
        // loop at EPISODE_PAGES_MAX pages instead of running forever.
        let calls = AtomicUsize::new(0);
        let (calls_ref,) = (&calls,);
        let all = fetch_all_pages(|offset: i64, _limit: i64| {
            calls_ref.fetch_add(1, Ordering::SeqCst);
            let docs: Vec<u64> = (offset..offset + EPISODE_PAGE_SIZE)
                .map(|x| x as u64)
                .collect();
            async move { Ok(page(docs, 1_000_000, offset)) }
        })
        .await
        .unwrap();

        assert_eq!(calls.load(Ordering::SeqCst), EPISODE_PAGES_MAX);
        assert_eq!(all.len(), EPISODE_PAGES_MAX * EPISODE_PAGE_SIZE as usize);
    }

    #[tokio::test]
    async fn fetch_all_pages_stops_on_empty_page_even_with_big_total() {
        let calls = AtomicUsize::new(0);
        let (calls_ref,) = (&calls,);
        let all = fetch_all_pages(|offset: i64, _limit: i64| {
            calls_ref.fetch_add(1, Ordering::SeqCst);
            let docs = if offset == 0 { vec![1, 2, 3] } else { vec![] };
            async move { Ok(page(docs, 50, offset)) }
        })
        .await
        .unwrap();

        assert_eq!(all, vec![1, 2, 3]);
        assert_eq!(
            calls.load(Ordering::SeqCst),
            2,
            "empty page must stop pagination"
        );
    }
}
