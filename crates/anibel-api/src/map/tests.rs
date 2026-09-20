use super::*;
use serde_json::json;

#[test]
fn malformed_media_is_an_error_not_an_empty_identity() {
    assert!(media_card(json!({"title":{"be":"Title"}})).is_err());
    assert!(page_media(json!({"docs":[{"mediaId":"x","mediaType":"anime","slug":""}]})).is_err());
    assert!(media_card(json!({"mediaId":"x","mediaType":"anime","slug":"title"})).is_ok());
}
#[test]
fn flattens_comment_date_created() {
    let c = comment_from(&json!({
        "id": "c1",
        "content": "hi",
        "user": { "username": "ann", "avatar": null, "displayName": "Ann" },
        "date": { "created": 1700000000000u64, "updated": "1700000001000" }
    }));
    assert_eq!(c.id, "c1");
    assert_eq!(c.created, 1_700_000_000_000);
    assert_eq!(c.user.as_ref().unwrap().username, "ann");
    let wire = serde_json::to_value(&c).unwrap();
    assert!(wire.get("date").is_none());
    assert_eq!(wire["created"], 1_700_000_000_000u64);
}

#[test]
fn parses_bignumber_string_on_episode() {
    let ep = episode_from(&json!({
        "id": "e1",
        "episode": 13,
        "url": "https://video.anibel.net/abc",
        "type": "sub",
        "resource": 2,
        "released": "1700000000000"
    }));
    assert_eq!(ep.released, Some(1_700_000_000_000));
}

#[test]
fn maps_studies_to_studios() {
    let f = filters_from(&json!({
        "years": [2024, "2023"],
        "genres": ["драма", null],
        "studies": ["studio a"],
        "translators": ["translator"],
        "editors": ["editor"],
        "dubbers": ["dubber"],
        "programmers": ["programmer"],
        "audioEngineers": ["engineer"],
        "typpers": ["typper"],
        "cleanners": ["cleanner"]
    }));
    assert_eq!(f.years, Some(vec![2024, 2023]));
    assert_eq!(f.genres.as_ref().unwrap()[0], "драма");
    assert_eq!(f.studios, Some(vec!["studio a".into()]));
    let wire = serde_json::to_value(&f).unwrap();
    assert!(wire.get("studies").is_none());
    assert_eq!(wire["studios"][0], "studio a");
    for (field, value) in [
        ("translators", "translator"),
        ("editors", "editor"),
        ("dubbers", "dubber"),
        ("programmers", "programmer"),
        ("audioEngineers", "engineer"),
        ("typpers", "typper"),
        ("cleanners", "cleanner"),
    ] {
        assert_eq!(wire[field][0], value);
    }
}

#[test]
fn media_card_ignores_graphql_extras() {
    let card = media_card_from(&json!({
        "mediaId": "1",
        "mediaType": "anime",
        "slug": "foo",
        "title": { "be": "Т", "ru": "Р", "en": null },
        "poster": "https://x",
        "year": 2020,
        "rating": 8.5,
        "genres": ["a"],
        "hidden": false,
        "markStats": { "total": 1 }
    }));
    assert_eq!(card.slug, "foo");
    assert_eq!(card.title.unwrap().be.as_deref(), Some("Т"));
    let wire = serde_json::to_value(media_card_from(&json!({
        "mediaId": "1", "mediaType": "anime", "slug": "foo"
    })))
    .unwrap();
    assert!(wire.get("hidden").is_none());
    assert!(wire.get("markStats").is_none());
}

#[test]
fn media_card_maps_status_language_and_update() {
    let card = media_card_from(&json!({
        "mediaId": "1",
        "mediaType": "anime",
        "slug": "foo",
        "status": "ongoing",
        "language": ["sub", "dub"],
        "updateType": "DUB",
        "num": 12,
        "year": 2024
    }));
    assert_eq!(card.status.as_deref(), Some("ongoing"));
    assert_eq!(
        card.language.as_ref().unwrap(),
        &vec!["sub".to_string(), "dub".to_string()]
    );
    assert_eq!(card.update_type.as_deref(), Some("DUB"));
    assert_eq!(card.num, Some(12));
    assert_eq!(card.year, Some(2024));
}

#[test]
fn media_detail_maps_franchise_relations_recommendations() {
    let detail = media_detail_from(&json!({
        "mediaId": "1",
        "mediaType": "anime",
        "slug": "death-note",
        "franchise": "Death Note",
        "relations": [
            { "mediaId": "1", "mediaType": "anime", "slug": "death-note" },
            { "mediaId": "2", "mediaType": "anime", "slug": "death-note-rewrite" }
        ],
        "recommendations": [
            { "mediaId": "3", "mediaType": "anime", "slug": "monster" },
            { "mediaId": "4", "mediaType": "anime", "slug": "hidden-rec", "hidden": true }
        ]
    }));
    assert_eq!(detail.franchise.as_deref(), Some("Death Note"));
    assert_eq!(detail.relations.len(), 2);
    assert_eq!(detail.relations[1].slug, "death-note-rewrite");
    assert_eq!(detail.recommendations.len(), 1);
    assert_eq!(detail.recommendations[0].slug, "monster");
}
