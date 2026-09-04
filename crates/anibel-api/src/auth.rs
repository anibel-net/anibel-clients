//! Auth operations: `login` / `logout`.

use crate::gql;
use crate::transport::AnibelApi;
use crate::util::{ValueDeserialize, gql_payload};
use anibel_domain::error::{AnibelError, Result};
use anibel_domain::models::LoginUser;
use graphql_client::GraphQLQuery;
use serde_json::Value;

impl AnibelApi {
    /// `login` intentionally stays on the retryable transport: it creates no
    /// user-visible records — the only side effect is a fresh session token,
    /// which we overwrite on success — so a retried login cannot duplicate
    /// anything the way comments/marks/favorites can. The other mutations
    /// use [`AnibelApi::request_no_retry`] instead.
    pub async fn login(&self, username: &str, password: &str) -> Result<LoginUser> {
        let data = self
            .request::<Value>(gql_payload!(
                LoginMutation,
                "LoginMutation",
                gql::login_mutation::Variables {
                    username: username.to_string(),
                    password: password.to_string(),
                },
            ))
            .await?
            .deserialize::<gql::login_mutation::ResponseData>()?;
        let user = data
            .login
            .ok_or_else(|| AnibelError::Graphql("login returned null".into()))?;
        if !user.token.is_empty() {
            self.set_token(Some(user.token.clone())).await;
        }
        Ok(crate::map::login_user(user))
    }

    pub async fn logout(&self) {
        self.set_token(None).await;
    }
}
