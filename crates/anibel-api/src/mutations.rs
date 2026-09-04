//! Personal mutations (M2): markAs / removeMark / addFavorite /
//! removeFavorite / addHistoryRecord / removeHistoryRecord.
//!
//! Every mutation goes through [`AnibelApi::request_no_retry`]: none of them
//! are idempotent, and a retried write after a server-side 500 would create
//! duplicates (comments, marks, favorites, history entries).

use crate::gql;
use crate::transport::AnibelApi;
use crate::util::{ValueDeserialize, gql_payload, to_enum};
use anibel_domain::error::{AnibelError, Result};
use graphql_client::GraphQLQuery;
use serde_json::Value;

macro_rules! expect_removal {
    ($status:expr, $field:literal, $module:ident) => {{
        match $status {
            Some(gql::$module::StatusRemoval::SUCCESS) => Ok(()),
            Some(gql::$module::StatusRemoval::ERROR) => {
                Err(AnibelError::Graphql(format!("{} returned ERROR", $field)))
            }
            Some(_) => Err(AnibelError::Graphql(format!(
                "{}: unexpected status",
                $field
            ))),
            None => Err(AnibelError::Graphql(format!("{}: null status", $field))),
        }
    }};
}

macro_rules! expect_history {
    ($status:expr, $field:literal, $module:ident) => {{
        match $status {
            Some(gql::$module::OperationStatus::SUCCESS) => Ok(()),
            Some(gql::$module::OperationStatus::ERROR) => {
                Err(AnibelError::Graphql(format!("{} returned ERROR", $field)))
            }
            Some(_) => Err(AnibelError::Graphql(format!(
                "{}: unexpected status",
                $field
            ))),
            None => Err(AnibelError::Graphql(format!("{}: null status", $field))),
        }
    }};
}

fn mark_status_from_str(s: &str) -> Result<gql::mark_as_mutation::MarkStatus> {
    use gql::mark_as_mutation::MarkStatus as MS;
    Ok(match s {
        "notselected" => MS::notselected,
        "watching" => MS::watching,
        "watched" => MS::watched,
        "dropped" => MS::dropped,
        "planned" => MS::planned,
        "played" => MS::played,
        "read" => MS::read,
        "reading" => MS::reading,
        "playing" => MS::playing,
        other => {
            return Err(AnibelError::BadArgs(format!(
                "invalid mark status `{other}`"
            )));
        }
    })
}

impl AnibelApi {
    pub async fn mark_as(&self, media_id: &str, media_type: &str, status: &str) -> Result<()> {
        let data = self
            .request_no_retry::<Value>(gql_payload!(
                MarkAsMutation,
                "MarkAsMutation",
                gql::mark_as_mutation::Variables {
                    input: gql::mark_as_mutation::MarkAsInput {
                        media_id: media_id.to_string(),
                        media_type: to_enum::<gql::mark_as_mutation::MediaTypes>(media_type)?,
                        status: mark_status_from_str(status)?,
                    },
                },
            ))
            .await?
            .deserialize::<gql::mark_as_mutation::ResponseData>()?;
        expect_removal!(data.mark_as, "markAs", mark_as_mutation)
    }

    pub async fn remove_mark(&self, media_id: &str, media_type: &str, status: &str) -> Result<()> {
        // MarkAsInput.status is required. The live API keys the row by the
        // current mark (watching/read/…), not `notselected` — sending that
        // enum yields StatusRemoval::ERROR.
        let status = status.trim();
        if status.is_empty() || status == "notselected" {
            return Err(AnibelError::BadArgs(
                "removeMark needs the current mark status, not notselected".into(),
            ));
        }
        let data = self
            .request_no_retry::<Value>(gql_payload!(
                RemoveMarkMutation,
                "RemoveMarkMutation",
                gql::remove_mark_mutation::Variables {
                    input: gql::remove_mark_mutation::MarkAsInput {
                        media_id: media_id.to_string(),
                        media_type: to_enum::<gql::remove_mark_mutation::MediaTypes>(media_type)?,
                        status: to_enum::<gql::remove_mark_mutation::MarkStatus>(status)?,
                    },
                },
            ))
            .await?
            .deserialize::<gql::remove_mark_mutation::ResponseData>()?;
        expect_removal!(data.remove_mark, "removeMark", remove_mark_mutation)
    }

    pub async fn add_favorite(&self, media_id: &str, media_type: &str) -> Result<()> {
        let data = self
            .request_no_retry::<Value>(gql_payload!(
                AddFavoriteMutation,
                "AddFavoriteMutation",
                gql::add_favorite_mutation::Variables {
                    input: gql::add_favorite_mutation::FavoriteInput {
                        media_id: media_id.to_string(),
                        media_type: to_enum::<gql::add_favorite_mutation::MediaTypes>(media_type)?,
                    },
                },
            ))
            .await?
            .deserialize::<gql::add_favorite_mutation::ResponseData>()?;
        match data.add_favorite {
            Some(_) => Ok(()),
            None => Err(AnibelError::Graphql("addFavorite: null".into())),
        }
    }

    pub async fn remove_favorite(&self, media_id: &str, media_type: &str) -> Result<()> {
        let data = self
            .request_no_retry::<Value>(gql_payload!(
                RemoveFavoriteMutation,
                "RemoveFavoriteMutation",
                gql::remove_favorite_mutation::Variables {
                    input: gql::remove_favorite_mutation::FavoriteInput {
                        media_id: media_id.to_string(),
                        media_type: to_enum::<gql::remove_favorite_mutation::MediaTypes>(
                            media_type,
                        )?,
                    },
                },
            ))
            .await?
            .deserialize::<gql::remove_favorite_mutation::ResponseData>()?;
        expect_removal!(
            data.remove_favorite,
            "removeFavorite",
            remove_favorite_mutation
        )
    }

    pub async fn add_history_record(&self, entity_id: &str, history_type: &str) -> Result<()> {
        let type_ = match history_type {
            "chapter" => gql::add_history_mutation::HistoryType::chapter,
            "episode" => gql::add_history_mutation::HistoryType::episode,
            other => {
                return Err(AnibelError::BadArgs(format!(
                    "invalid history type `{other}`"
                )));
            }
        };
        let data = self
            .request_no_retry::<Value>(gql_payload!(
                AddHistoryMutation,
                "AddHistoryMutation",
                gql::add_history_mutation::Variables {
                    input: gql::add_history_mutation::HistoryRecordInput {
                        type_,
                        entity_id: entity_id.to_string(),
                    },
                },
            ))
            .await?
            .deserialize::<gql::add_history_mutation::ResponseData>()?;
        expect_history!(
            data.add_history_record,
            "addHistoryRecord",
            add_history_mutation
        )
    }

    pub async fn remove_history_record(&self, entity_id: &str, history_type: &str) -> Result<()> {
        let type_ = match history_type {
            "chapter" => gql::remove_history_mutation::HistoryType::chapter,
            "episode" => gql::remove_history_mutation::HistoryType::episode,
            other => {
                return Err(AnibelError::BadArgs(format!(
                    "invalid history type `{other}`"
                )));
            }
        };
        let data = self
            .request_no_retry::<Value>(gql_payload!(
                RemoveHistoryMutation,
                "RemoveHistoryMutation",
                gql::remove_history_mutation::Variables {
                    input: gql::remove_history_mutation::HistoryRecordInput {
                        type_,
                        entity_id: entity_id.to_string(),
                    },
                },
            ))
            .await?
            .deserialize::<gql::remove_history_mutation::ResponseData>()?;
        expect_history!(
            data.remove_history_record,
            "removeHistoryRecord",
            remove_history_mutation
        )
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn mark_status_from_str_accepts_all_schema_values() {
        for s in [
            "notselected",
            "watching",
            "watched",
            "dropped",
            "planned",
            "played",
            "read",
            "reading",
            "playing",
        ] {
            assert!(mark_status_from_str(s).is_ok(), "`{s}` must be accepted");
        }
    }

    #[test]
    fn mark_status_from_str_rejects_unknown_values() {
        assert!(matches!(
            mark_status_from_str("bogus"),
            Err(AnibelError::BadArgs(_))
        ));
        assert!(matches!(
            mark_status_from_str(""),
            Err(AnibelError::BadArgs(_))
        ));
    }
}
