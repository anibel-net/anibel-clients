# GraphQL API adapter

Root AGENTS.md also applies.

Keep one operation per graphql/ops file. Use generated query types and map API responses into domain models. Treat optional fields and server errors explicitly. Do not put UI or durable application state here. Never commit authentication tokens or log authorization headers. Keep server filter keys and pagination values intact. Update schema.graphql only from the intended endpoint and review the diff. Use local HTTP fixtures for tests; live tests are separate. Run cargo test -p anibel-api and affected core tests from the root.
