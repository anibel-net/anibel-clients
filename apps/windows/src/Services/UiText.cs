using Anibel.App.Core;
using Windows.ApplicationModel.DataTransfer;

namespace Anibel.App.Services;

/// <summary>
/// UI-language-aware text helpers. Language is read from SettingsService at
/// boot ("be" default → «Беларуская»; "ru" → рэчаісносць русская).
/// </summary>
public static class Ui
{
    public static string Lang { get; set; } = "be";

    public static void Load(SettingsService settings) => Lang = settings.Language;

    public static string Title(TitleDto? title) =>
        Lang switch
        {
            "ru" => First(title?.Ru, title?.Be),
            _ => First(title?.Be, title?.Ru),
        };

    public static string Text(string? be, string? ru) =>
        Lang == "ru" ? First(ru, be) : First(be, ru);

    public static string Language(string code) => code.Trim().ToLowerInvariant() switch
    {
        "sub" => Lang == "ru" ? "Субтитры" : "Субцітры",
        "dub" => Lang == "ru" ? "Дубляж" : "Дубляж",
        _ => code,
    };

    public static string FriendlyError(string? code, string? message)
    {
        var raw = (message ?? "").Trim();
        var key = $"{code} {raw}".ToLowerInvariant();
        if (key.Contains("incorrect username") || key.Contains("invalid credentials")
            || key.Contains("wrong password") || key.Contains("invalid password")
            || key.Contains("user not found") || key.Contains("authentication failed"))
        {
            return "Няправільны лагін або пароль.";
        }
        if (code is "auth_expired" || key.Contains("must be logged in") || key.Contains("unauthorized")
            || key.Contains("jwt"))
        {
            return "Сеанс скончыўся. Увайдзіце зноў.";
        }
        if (code is "transport_error" or "http_error" || key.Contains("timeout") || key.Contains("connect"))
        {
            return "Няма сувязі з серверам. Праверце інтэрнэт і паспрабуйце яшчэ раз.";
        }
        if (code is "http_404" or "not_found")
        {
            return "Нічога не знойдзена.";
        }
        if (key.Contains("cannot query field") || key.Contains("syntax error") || key.Contains("graphql"))
        {
            return string.IsNullOrEmpty(raw) || raw.Length > 180
                ? "Не ўдалося выканаць запыт да сервера."
                : raw;
        }
        if (string.IsNullOrEmpty(raw))
        {
            return "Нешта пайшло не так.";
        }
        return raw.StartsWith("graphql error:", StringComparison.OrdinalIgnoreCase)
            ? raw["graphql error:".Length..].Trim()
            : raw;
    }

    public static string DisplayMessage(Exception ex) =>
        ex is Core.CoreException core ? core.UserMessage : ex.Message;

    public static bool CopyToClipboard(string? text)
    {
        var value = (text ?? "").Trim();
        if (value.Length == 0)
        {
            return false;
        }
        try
        {
            var data = new DataPackage();
            data.SetText(value);
            Clipboard.SetContent(data);
            Clipboard.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string MediaStatus(string? status) => (status ?? "").Trim().ToLowerInvariant() switch
    {
        "finished" or "released" or "complete" or "completed" => "Завершана",
        "ongoing" or "airing" or "releasing" => "Выпускаецца",
        "anons" or "announced" or "upcoming" or "not_yet_aired" => "Анонс",
        "paused" or "hiatus" => "Паўза",
        _ => string.IsNullOrWhiteSpace(status) ? "" : status.Trim(),
    };

    public static string MediaType(string type) => type.Trim().ToLowerInvariant() switch
    {
        "anime" => "Анімэ",
        "manga" => "Манга",
        "cinema" => "Кіно",
        "games" => "Гульні",
        "books" => "Кнігі",
        _ => type,
    };

    public static string ContentType(string type)
    {
        var key = type.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        if (Lang != "ru" && ContentTypeBe.TryGetValue(key, out var be))
        {
            return be;
        }
        return ContentTypeBe.TryGetValue(key, out var label) ? label : type;
    }

    public static string Country(string country)
    {
        var key = country.Trim();
        if (CountryBe.TryGetValue(key, out var be))
        {
            return Lang == "ru" ? key : be;
        }
        foreach (var (api, label) in Countries)
        {
            if (string.Equals(api, key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(label, key, StringComparison.OrdinalIgnoreCase))
            {
                return Lang == "ru" ? api : label;
            }
        }
        return key;
    }

    public static IReadOnlyList<(string Value, string Label)> Countries { get; } =
    [
        ("Япония", "Японія"),
        ("Китай", "Кітай"),
        ("Южная Корея", "Паўднёвая Карэя"),
        ("Корея", "Карэя"),
        ("США", "ЗША"),
        ("Беларусь", "Беларусь"),
        ("Россия", "Расія"),
        ("Украина", "Украіна"),
        ("Польша", "Польшча"),
        ("Франция", "Францыя"),
        ("Великобритания", "Вялікабрытанія"),
        ("Германия", "Германія"),
        ("Италия", "Італія"),
        ("Канада", "Канада"),
        ("Тайвань", "Тайвань"),
        ("Гонконг", "Ганконг"),
        ("Таиланд", "Тайланд"),
        ("Испания", "Іспанія"),
    ];

    // Server returns RU (or EN) genre names — map to Belarusian for display.
    private static readonly Dictionary<string, string> GenreMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dementia"] = "вар'яцтва",
        ["martial-arts"] = "баявыя мастацтвы",
        ["shoujo-ai"] = "сёдзэ-ай",
        ["shounen-ai"] = "сёнэн-ай",
        ["slice-of-life"] = "штодзённасць",
        ["super-power"] = "звыш сілы",
        ["yaoi"] = "яой",
        ["yuri"] = "юры",
        ["detective"] = "дэтэктыў",
        ["job"] = "праца",
        ["erotica"] = "эротыка",
        ["боевик"] = "баявік",
        ["action"] = "экшн",
        ["боевые искусства"] = "баявыя мастацтвы",
        ["martial arts"] = "баявыя мастацтвы",
        ["приключения"] = "прыгоды",
        ["adventure"] = "прыгоды",
        ["фэнтези"] = "фэнтэзі",
        ["fantasy"] = "фэнтэзі",
        ["фантастика"] = "фантастыка",
        ["sci-fi"] = "фантастыка",
        ["sci fi"] = "навуковая фантастыка",
        ["научная фантастика"] = "навуковая фантастыка",
        ["драма"] = "драма",
        ["drama"] = "драма",
        ["комедия"] = "камедыя",
        ["comedy"] = "камедыя",
        ["романтика"] = "рамантыка",
        ["romance"] = "рамантыка",
        ["ужасы"] = "жахі",
        ["horror"] = "жахі",
        ["детектив"] = "дэтэктыў",
        ["mystery"] = "містыка",
        ["мистика"] = "містыка",
        ["триллер"] = "трылер",
        ["thriller"] = "трылер",
        ["спорт"] = "спорт",
        ["sports"] = "спорт",
        ["музыка"] = "музыка",
        ["music"] = "музыка",
        ["школа"] = "школа",
        ["school"] = "школа",
        ["повседневность"] = "паўсядзённасць",
        ["slice of life"] = "паўсядзённасць",
        ["психологический"] = "псіхалагічны",
        ["psychological"] = "псіхалагічнае",
        ["исторический"] = "гістарычны",
        ["historical"] = "гістарычнае",
        ["военный"] = "ваенны",
        ["military"] = "вайсковае",
        ["меха"] = "меха",
        ["mecha"] = "меха",
        ["магия"] = "магія",
        ["magic"] = "магія",
        ["сэйнэн"] = "сэйнэн",
        ["сейнен"] = "сэйнэн",
        ["seinen"] = "сэйнэн",
        ["сёнэн"] = "сёнэн",
        ["сёнен"] = "сёнэн",
        ["shounen"] = "сёнэн",
        ["shonen"] = "сёнэн",
        ["сёдзё"] = "сёдзё",
        ["сёдзе"] = "сёдзё",
        ["shoujo"] = "сёдзэ",
        ["shojo"] = "сёдзё",
        ["дзёсэй"] = "дзёсэй",
        ["josei"] = "дзёсэй",
        ["драма для взрослых"] = "драма для дарослых",
        ["этти"] = "эты",
        ["ecchi"] = "эці",
        ["хентай"] = "хентай",
        ["hentai"] = "хентай",
        ["исекай"] = "ісэкай",
        ["isekai"] = "ісэкай",
        ["гарем"] = "гарэм",
        ["harem"] = "гарэм",
        ["постапокалипсис"] = "постапакаліпсіс",
        ["post-apocalyptic"] = "постапакаліпсіс",
        ["киберпанк"] = "кібэрпанк",
        ["cyberpunk"] = "кібэрпанк",
        ["космос"] = "космас",
        ["space"] = "космас",
        ["сверхъестественное"] = "звышнатуральнае",
        ["supernatural"] = "звышнатуральнае",
        ["вампиры"] = "вампіры",
        ["vampire"] = "вампіры",
        ["вампир"] = "вампіры",
        ["демоны"] = "дэманы",
        ["demons"] = "дэманы",
        ["демон"] = "дэманы",
        ["самураи"] = "самураі",
        ["samurai"] = "самураі",
        ["ниндзя"] = "ніндзя",
        ["полиция"] = "паліцыя",
        ["police"] = "паліцыя",
        ["пародия"] = "пародыя",
        ["parody"] = "пародыя",
        ["детское"] = "дзіцячае",
        ["kids"] = "дзеці",
        ["кулинария"] = "кулінарыя",
        ["gourmet"] = "гурман",
        ["медицина"] = "медыцына",
        ["medical"] = "медыцына",
        ["автомобили"] = "аўтамабілі",
        ["cars"] = "аўтамабілі",
        ["игры"] = "гульні",
        ["game"] = "гульні",
        ["безумие"] = "вар'яцтва",
        ["супер сила"] = "суперсіла",
        ["super power"] = "суперсіла",
        ["роботы"] = "робаты",
        ["стратегия"] = "стратэгія",
        ["ролевая игра"] = "ролевая гульня",
        ["головоломка"] = "галаваломка",
        ["аркада"] = "аркада",
        ["гонки"] = "гонкі",
        ["симулятор"] = "сімулятар",
        ["выживание"] = "выжыванне",
        ["от первого лица"] = "ад першай асобы",
        ["онлайн"] = "онлайн",
        ["сёнен-ай"] = "сёнэн-ай",
        ["сёдзе-ай"] = "сёдзё-ай",
        ["музыкальный"] = "музычны",
        ["сёнэн-ай"] = "сёнэн-ай",
        ["сёдзё-ай"] = "сёдзё-ай",
        ["яой"] = "яой",
        ["юри"] = "юры",
    };

    private static readonly Dictionary<string, string> CountryBe = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Япония"] = "Японія",
        ["Japan"] = "Японія",
        ["JP"] = "Японія",
        ["Китай"] = "Кітай",
        ["China"] = "Кітай",
        ["CN"] = "Кітай",
        ["Южная Корея"] = "Паўднёвая Карэя",
        ["Корея"] = "Карэя",
        ["South Korea"] = "Паўднёвая Карэя",
        ["Korea"] = "Карэя",
        ["KR"] = "Карэя",
        ["США"] = "ЗША",
        ["USA"] = "ЗША",
        ["US"] = "ЗША",
        ["United States"] = "ЗША",
        ["Беларусь"] = "Беларусь",
        ["Belarus"] = "Беларусь",
        ["BY"] = "Беларусь",
        ["Россия"] = "Расія",
        ["Russia"] = "Расія",
        ["RU"] = "Расія",
        ["Украина"] = "Украіна",
        ["Ukraine"] = "Украіна",
        ["UA"] = "Украіна",
        ["Польша"] = "Польшча",
        ["Poland"] = "Польшча",
        ["Франция"] = "Францыя",
        ["France"] = "Францыя",
        ["Великобритания"] = "Вялікабрытанія",
        ["United Kingdom"] = "Вялікабрытанія",
        ["UK"] = "Вялікабрытанія",
        ["Германия"] = "Германія",
        ["Germany"] = "Германія",
        ["Италия"] = "Італія",
        ["Italy"] = "Італія",
        ["Канада"] = "Канада",
        ["Canada"] = "Канада",
        ["Тайвань"] = "Тайвань",
        ["Taiwan"] = "Тайвань",
        ["Гонконг"] = "Ганконг",
        ["Hong Kong"] = "Ганконг",
        ["Таиланд"] = "Тайланд",
        ["Thailand"] = "Тайланд",
        ["Испания"] = "Іспанія",
        ["Spain"] = "Іспанія",
    };

    private static readonly Dictionary<string, string> ContentTypeBe = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cinema"] = "Кіно",
        ["anime"] = "Анімэ",
        ["cartoon"] = "Мульцік",
        ["serial"] = "Серыял",
        ["film"] = "Фільм",
        ["games"] = "Гульня",
        ["manga"] = "Манга",
        ["manhwa"] = "Манхва",
        ["manhva"] = "Манхва",
        ["manhua"] = "Манхуа",
        ["oad"] = "OAD",
        ["classic"] = "Класіка",
        ["novel"] = "Навэла",
        ["tv"] = "tv-серыял",
        ["movie"] = "Фільм",
        ["ova"] = "OVA",
        ["ona"] = "ONA",
        ["special"] = "Спецвыпуск",
        ["tv_special"] = "ТВ-спэшал",
        ["music"] = "музыка",
        ["web"] = "Вэб",
        ["picture"] = "Карціна",
        ["cm"] = "Рэклама",
        ["pv"] = "Тызэр",
        ["short"] = "Кароткі метр",
        ["movie_short"] = "Кароткі метр",
    };

    public static string Genre(string genre) =>
        Lang == "ru" ? genre : GenreMap.TryGetValue(genre.Trim(), out var be) ? be : genre;

    public static string Genres(IEnumerable<string>? genres) =>
        genres is null || !genres.Any() ? "" : string.Join(" · ", genres.Select(Genre));

    private static string First(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) ? a : (b ?? "");
}
