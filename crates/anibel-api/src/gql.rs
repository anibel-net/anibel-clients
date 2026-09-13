//! Schema-driven GraphQL operations. One `.graphql` file per op so the
//! gateway never sees unused operations or fragments.

use graphql_client::GraphQLQuery;

pub type BigNumber = serde_json::Value;
pub type BigInt = serde_json::Value;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/search.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct SearchQuery;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/media.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct MediaQuery;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/media_safe.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct MediaSafeQuery;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/media_list.graphql",
    response_derives = "Debug, Clone, Serialize",
    variables_derives = "Clone, Deserialize"
)]
pub struct MediaListQuery;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/media_list_safe.graphql",
    response_derives = "Debug, Clone, Serialize",
    variables_derives = "Clone, Deserialize"
)]
pub struct MediaListSafeQuery;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/episodes.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct EpisodesQuery;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/chapters.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct ChaptersQuery;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/chapter.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct ChapterQuery;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/comments.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct CommentsQuery;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/add_comment.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct AddCommentMutation;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/trends.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct TrendsQuery;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/updates.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct UpdatesQuery;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/recommendations.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct RecommendationsQuery;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/schedule.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct ScheduleQuery;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/slider.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct SliderQuery;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/filters.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct FiltersQuery;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/statistics.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct StatisticsQuery;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/random_media.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct RandomMediaQuery;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/user.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct UserQuery;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/favorites.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct FavoritesQuery;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/marks.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct MarksQuery;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/status.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct StatusQuery;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/login.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct LoginMutation;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/mark_as.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct MarkAsMutation;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/remove_mark.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct RemoveMarkMutation;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/add_favorite.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct AddFavoriteMutation;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/remove_favorite.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct RemoveFavoriteMutation;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/add_history.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct AddHistoryMutation;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/remove_history.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct RemoveHistoryMutation;

// ---------------------------------------------------------------------------
// mediaList filters: FFI arg object (camelCase keys) → generated input type
// ---------------------------------------------------------------------------

pub fn media_list_filters_from_json(value: &serde_json::Value) -> media_list_query::MediaFilters {
    let get = |k: &str| value.get(k).cloned();
    let opt_bool = |k: &str| -> Option<bool> { get(k).and_then(|v| v.as_bool()) };
    let opt_i64s = |k: &str| -> Option<Vec<Option<i64>>> {
        get(k).and_then(|v| {
            v.as_array().map(|a| {
                a.iter()
                    .map(|x| x.as_i64().or_else(|| x.as_f64().map(|f| f as i64)))
                    .collect()
            })
        })
    };
    let opt_strs = |k: &str| -> Option<Vec<Option<String>>> {
        get(k).and_then(|v| {
            v.as_array()
                .map(|a| a.iter().map(|x| x.as_str().map(str::to_owned)).collect())
        })
    };

    let media_status = value
        .get("status")
        .and_then(|v| v.as_str())
        .and_then(|s| match s {
            "finished" => Some(media_list_query::MediaStatus::finished),
            "ongoing" => Some(media_list_query::MediaStatus::ongoing),
            // Unknown/typo'd host status strings must NOT be coerced to a
            // default filter (previously everything unknown became
            // `ongoing` and silently narrowed the list).
            _ => None,
        });
    let language = get("language").and_then(|v| {
        v.as_array().map(|a| {
            a.iter()
                .filter_map(|x| x.as_str())
                .map(|s| match s {
                    "sub" => Some(media_list_query::MediaLanguage::sub),
                    "dub" => Some(media_list_query::MediaLanguage::dub),
                    _ => None,
                })
                .collect::<Vec<Option<media_list_query::MediaLanguage>>>()
        })
    });

    media_list_query::MediaFilters {
        language,
        genres: opt_strs("genres"),
        studies: opt_strs("studies"),
        year: opt_i64s("year"),
        status: media_status,
        country: opt_strs("country").and_then(|v| v.into_iter().flatten().next()),
        type_: opt_strs("type"),
        translators: opt_strs("translators"),
        dubbers: opt_strs("dubbers"),
        editors: opt_strs("editors"),
        franchises: opt_strs("franchises"),
        audio_engineers: opt_strs("audioEngineers"),
        cleanners: opt_strs("cleanners"),
        programmers: opt_strs("programmers"),
        typpers: opt_strs("typpers"),
        hidden: opt_bool("hidden"),
        query: value
            .get("query")
            .and_then(|v| v.as_str())
            .map(str::to_owned),
        only_drafts: opt_bool("onlyDrafts"),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn builds_query_bodies() {
        let body = MediaListQuery::build_query(media_list_query::Variables {
            media_type: media_list_query::MediaTypes::anime,
            offset: 0,
            limit: 10,
            filters: None,
        });
        assert!(body.query.contains("getMediaList"));
        assert!(
            !body.query.contains("query SearchQuery"),
            "per-op document leaked another operation"
        );
        assert!(
            !body.query.contains("episodes"),
            "catalog list must not pull episode trees"
        );
    }

    #[test]
    fn filters_from_json_maps_camel_args() {
        let f = media_list_filters_from_json(&json!({
            "language": ["sub"],
            "type": ["tv"],
            "year": [2024],
            "query": "death"
        }));
        let lang = f.language.as_ref().and_then(|v| v.first());
        assert!(matches!(
            lang,
            Some(Some(media_list_query::MediaLanguage::sub))
        ));
        assert_eq!(f.type_, Some(vec![Some("tv".to_string())]));
        assert_eq!(f.year, Some(vec![Some(2024)]));
        assert_eq!(f.query.as_deref(), Some("death"));
    }

    #[test]
    fn filters_from_json_unknown_status_is_none_not_ongoing() {
        // Regression: an unknown/typo'd status string used to be coerced to
        // `ongoing`, silently narrowing every list to airing titles.
        let known = media_list_filters_from_json(&json!({ "status": "ongoing" }));
        assert!(matches!(
            known.status,
            Some(media_list_query::MediaStatus::ongoing)
        ));
        let finished = media_list_filters_from_json(&json!({ "status": "finished" }));
        assert!(matches!(
            finished.status,
            Some(media_list_query::MediaStatus::finished)
        ));
        let unknown = media_list_filters_from_json(&json!({ "status": "bogus" }));
        assert!(unknown.status.is_none(), "unknown status must not coerce");
        let missing = media_list_filters_from_json(&json!({}));
        assert!(missing.status.is_none());
    }

    #[test]
    fn decodes_media_page_from_response_data() {
        // spot-check that generated response structs accept the camelCase wire
        let json = json!({
            "search": [{
                "mediaId": "1", "mediaType": "anime", "slug": "s",
                "title": { "be": "Т", "ru": "Р", "en": null, "alt": null },
                "status": "ongoing", "poster": "", "year": 2020,
                "language": [], "genres": [], "relations": [],
                "episodes": [], "chapters": []
            }]
        });
        let data: search_query::ResponseData =
            serde_json::from_value(json).unwrap_or_else(|e| panic!("decode fixture failed: {e}"));
        let first = data.search[0].as_ref().unwrap();
        assert_eq!(first.media_id.as_deref(), Some("1"));
        assert_eq!(first.title.as_ref().unwrap().be, "Т");
    }
}

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/update_profile.graphql",
    response_derives = "Debug, Clone, Serialize",
    variables_derives = "Deserialize"
)]
pub struct UpdateProfileMutation;

#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "graphql/ops/add_rating.graphql",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct AddRatingMutation;
