from pathlib import Path

p = Path("crates/anibel-api/src/gql.rs")
text = p.read_text(encoding="utf-8")
idx = text.find("// ---------------------------------------------------------------------------")
rest = text[idx:]
ops = [
    ("SearchQuery", "graphql/ops/search.graphql"),
    ("MediaQuery", "graphql/ops/media.graphql"),
    ("MediaListQuery", "graphql/ops/media_list.graphql"),
    ("EpisodesQuery", "graphql/ops/episodes.graphql"),
    ("ChaptersQuery", "graphql/ops/chapters.graphql"),
    ("ChapterQuery", "graphql/ops/chapter.graphql"),
    ("CommentsQuery", "graphql/ops/comments.graphql"),
    ("TrendsQuery", "graphql/ops/trends.graphql"),
    ("UpdatesQuery", "graphql/ops/updates.graphql"),
    ("RecommendationsQuery", "graphql/ops/recommendations.graphql"),
    ("ScheduleQuery", "graphql/ops/schedule.graphql"),
    ("SliderQuery", "graphql/ops/slider.graphql"),
    ("FiltersQuery", "graphql/ops/filters.graphql"),
    ("StatisticsQuery", "graphql/ops/statistics.graphql"),
    ("RandomMediaQuery", "graphql/ops/random_media.graphql"),
    ("UserQuery", "graphql/ops/user.graphql"),
    ("FavoritesQuery", "graphql/ops/favorites.graphql"),
    ("MarksQuery", "graphql/ops/marks.graphql"),
    ("StatusQuery", "graphql/ops/status.graphql"),
    ("LoginMutation", "graphql/ops/login.graphql"),
    ("MarkAsMutation", "graphql/ops/mark_as.graphql"),
    ("RemoveMarkMutation", "graphql/ops/remove_mark.graphql"),
    ("AddFavoriteMutation", "graphql/ops/add_favorite.graphql"),
    ("RemoveFavoriteMutation", "graphql/ops/remove_favorite.graphql"),
    ("AddHistoryMutation", "graphql/ops/add_history.graphql"),
    ("RemoveHistoryMutation", "graphql/ops/remove_history.graphql"),
]
head = """//! Schema-driven GraphQL operations. One `.graphql` file per op so the
//! gateway never sees unused operations or fragments.

use graphql_client::GraphQLQuery;

pub type BigNumber = serde_json::Value;
pub type BigInt = serde_json::Value;

"""
for name, path in ops:
    head += f"""#[derive(GraphQLQuery)]
#[graphql(
    schema_path = "graphql/schema.graphql",
    query_path = "{path}",
    response_derives = "Debug, Clone, Serialize"
)]
pub struct {name};

"""
p.write_text(head + rest, encoding="utf-8")
print("wrote", p)
