//! Home-page queries: trends, updates, recommendations, schedule, slider,
//! filters, statistics, random media.

use crate::gql;
use crate::transport::AnibelApi;
use crate::util::{ValueDeserialize, gql_payload, opt_enum, to_enum};
use anibel_domain::error::{AnibelError, Result};
use anibel_domain::models::{Filters, MediaCard, MediaDetail, ScheduleDay, Slide, Statistics};
use graphql_client::GraphQLQuery;
use serde_json::Value;

impl AnibelApi {
    pub async fn trends(
        &self,
        r#type: &str,
        date: &str,
        limit: Option<i64>,
    ) -> Result<Vec<MediaCard>> {
        let date_enum = match date {
            "today" => gql::trends_query::TrendDates::today,
            "yestarday" => gql::trends_query::TrendDates::yestarday,
            "month" => gql::trends_query::TrendDates::month,
            "year" => gql::trends_query::TrendDates::year,
            _ => gql::trends_query::TrendDates::week,
        };
        let data = self
            .request::<Value>(gql_payload!(
                TrendsQuery,
                "TrendsQuery",
                gql::trends_query::Variables {
                    type_: to_enum::<gql::trends_query::RecommendationTypes>(r#type)?,
                    date: date_enum,
                    limit,
                },
            ))
            .await?
            .deserialize::<gql::trends_query::ResponseData>()?;
        Ok(crate::map::media_cards_opt(data.get_trends))
    }

    pub async fn updates(&self, r#type: &str, offset: i64, limit: i64) -> Result<Vec<MediaCard>> {
        let type_enum = match r#type {
            "CINEMA" => gql::updates_query::UpdateType::CINEMA,
            "SUB" => gql::updates_query::UpdateType::SUB,
            "DUB" => gql::updates_query::UpdateType::DUB,
            "MANGA" => gql::updates_query::UpdateType::MANGA,
            "GAMES" => gql::updates_query::UpdateType::GAMES,
            _ => gql::updates_query::UpdateType::ALL,
        };
        let data = self
            .request::<Value>(gql_payload!(
                UpdatesQuery,
                "UpdatesQuery",
                gql::updates_query::Variables {
                    type_: Some(type_enum),
                    offset,
                    limit,
                },
            ))
            .await?
            .deserialize::<gql::updates_query::ResponseData>()?;
        Ok(crate::map::media_cards_opt(data.get_updates_list.docs))
    }

    pub async fn recommendations(&self, r#type: &str, limit: i64) -> Result<Vec<MediaCard>> {
        let data = self
            .request::<Value>(gql_payload!(
                RecommendationsQuery,
                "RecommendationsQuery",
                gql::recommendations_query::Variables {
                    type_: to_enum::<gql::recommendations_query::RecommendationTypes>(r#type)?,
                    limit: Some(limit),
                },
            ))
            .await?
            .deserialize::<gql::recommendations_query::ResponseData>()?;
        Ok(crate::map::media_cards_opt(data.get_recommendations))
    }

    pub async fn schedule(&self) -> Result<Vec<ScheduleDay>> {
        let data = self
            .request::<Value>(gql_payload!(
                ScheduleQuery,
                "ScheduleQuery",
                gql::schedule_query::Variables {},
            ))
            .await?
            .deserialize::<gql::schedule_query::ResponseData>()?;
        Ok(crate::map::schedule_days_opt(data.get_schedule))
    }

    pub async fn slider(&self, limit: i64) -> Result<Vec<Slide>> {
        let data = self
            .request::<Value>(gql_payload!(
                SliderQuery,
                "SliderQuery",
                gql::slider_query::Variables { offset: 0, limit },
            ))
            .await?
            .deserialize::<gql::slider_query::ResponseData>()?;
        Ok(crate::map::slides_opt(data.get_slider))
    }

    pub async fn filters(&self, media_type: Option<String>) -> Result<Filters> {
        let media_type = opt_enum::<gql::filters_query::MediaTypes>(media_type.as_deref())?;
        let data = self
            .request::<Value>(gql_payload!(
                FiltersQuery,
                "FiltersQuery",
                gql::filters_query::Variables { media_type },
            ))
            .await?
            .deserialize::<gql::filters_query::ResponseData>()?;
        Ok(crate::map::filters(data.get_filters.ok_or_else(|| {
            AnibelError::Graphql("getFilters: null".into())
        })?))
    }

    pub async fn statistics(&self) -> Result<Statistics> {
        let data = self
            .request::<Value>(gql_payload!(
                StatisticsQuery,
                "StatisticsQuery",
                gql::statistics_query::Variables {},
            ))
            .await?
            .deserialize::<gql::statistics_query::ResponseData>()?;
        Ok(crate::map::statistics(data.get_statistics.ok_or_else(
            || AnibelError::Graphql("getStatistics: null".into()),
        )?))
    }

    pub async fn random_media(&self) -> Result<MediaDetail> {
        let data = self
            .request::<Value>(gql_payload!(
                RandomMediaQuery,
                "RandomMediaQuery",
                gql::random_media_query::Variables {},
            ))
            .await?
            .deserialize::<gql::random_media_query::ResponseData>()?;
        Ok(crate::map::media_detail(data.random_media.ok_or_else(
            || AnibelError::NotFound("random media".into()),
        )?))
    }
}

#[cfg(test)]
mod tests {
    use graphql_client::GraphQLQuery;

    #[test]
    fn slider_document_is_single_op() {
        let body = crate::gql::SliderQuery::build_query(crate::gql::slider_query::Variables {
            offset: 0,
            limit: 6,
        });
        assert!(body.query.contains("query SliderQuery"));
        assert!(!body.query.contains("EpisodeFields"));
        assert!(!body.query.contains("AddFavorite"));
    }
}
