"""Split core/graphql/queries.graphql into one file per operation."""

from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / "core" / "graphql" / "queries.graphql"
OUT = ROOT / "crates" / "anibel-api" / "graphql" / "ops"

STEM = {
    "SearchQuery": "search",
    "MediaQuery": "media",
    "MediaListQuery": "media_list",
    "EpisodesQuery": "episodes",
    "ChaptersQuery": "chapters",
    "ChapterQuery": "chapter",
    "CommentsQuery": "comments",
    "TrendsQuery": "trends",
    "UpdatesQuery": "updates",
    "RecommendationsQuery": "recommendations",
    "ScheduleQuery": "schedule",
    "SliderQuery": "slider",
    "FiltersQuery": "filters",
    "StatisticsQuery": "statistics",
    "RandomMediaQuery": "random_media",
    "UserQuery": "user",
    "FavoritesQuery": "favorites",
    "MarksQuery": "marks",
    "StatusQuery": "status",
    "LoginMutation": "login",
    "MarkAsMutation": "mark_as",
    "RemoveMarkMutation": "remove_mark",
    "AddFavoriteMutation": "add_favorite",
    "RemoveFavoriteMutation": "remove_favorite",
    "AddHistoryMutation": "add_history",
    "RemoveHistoryMutation": "remove_history",
}

MEDIA_FRAGS = ("EpisodeFields", "MediaLinkFields", "MediaFields")
EPISODE_FRAGS = ("EpisodeFields",)


def blocks(text: str) -> list[str]:
    items: list[str] = []
    current: list[str] = []
    depth = 0
    started = False
    for line in text.splitlines(keepends=True):
        stripped = line.lstrip()
        opens = stripped.startswith("fragment ") or stripped.startswith("query ") or stripped.startswith(
            "mutation "
        )
        if opens and started and depth == 0 and current:
            items.append("".join(current).strip() + "\n")
            current = []
        if opens or started:
            started = True
            current.append(line)
            depth += line.count("{") - line.count("}")
    if current:
        items.append("".join(current).strip() + "\n")
    return items


def main() -> None:
    text = SRC.read_text(encoding="utf-8")
    frags: dict[str, str] = {}
    ops: list[tuple[str, str]] = []
    for block in blocks(text):
        first = block.splitlines()[0]
        if first.startswith("fragment "):
            name = first.split()[1]
            frags[name] = block
        else:
            name = first.split()[1].split("(")[0]
            ops.append((name, block))

    OUT.mkdir(parents=True, exist_ok=True)
    for old in OUT.glob("*.graphql"):
        old.unlink()

    for name, body in ops:
        stem = STEM[name]
        needed: tuple[str, ...]
        if "...MediaFields" in body:
            needed = MEDIA_FRAGS
        elif "...EpisodeFields" in body:
            needed = EPISODE_FRAGS
        else:
            needed = ()
        parts = [frags[n] for n in needed] + [body]
        (OUT / f"{stem}.graphql").write_text("\n".join(parts).rstrip() + "\n", encoding="utf-8")
        print(stem)

    print(f"wrote {len(ops)} ops to {OUT}")


if __name__ == "__main__":
    main()
