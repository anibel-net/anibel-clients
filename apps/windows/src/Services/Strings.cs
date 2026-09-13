namespace Anibel.App.Services;

/// <summary>
/// Fixed, UI-facing Belarusian strings once inlined into code-behind.
/// Language-aware selection of backend-provided labels lives in <see cref="Ui"/>;
/// this class holds the app's own fixed UI text so it is never duplicated.
/// </summary>
public static class Strings
{
    // --- catalog content types (nav titles, search suggestions) ---
    public const string Anime = "Анімэ";
    public const string Manga = "Манга";
    public const string Cinema = "Кіно";
    public const string Games = "Гульні";
    public const string Books = "Кнігі";

    // --- title bar / account flyout ---
    public const string Guest = "Госць";
    public const string SearchHintFull = "Пошук анімэ, мангі, кіно, гульняў…";
    public const string SearchHintShort = "Пошук…";

    // --- media details header ---
    public const string NoSuchTitle = "Няма такога тайтла.";
    public const string LoginToComment = "Каб пакінуць каментар, увайдзіце ў профіль.";
    public const string Chapters = "Главы";
    public const string WriteCommentPlaceholder = "Напісаць каментар…";
    public static string ReplyPlaceholder(string username) => $"Адказ @{username}…";
    public static string ReplyLabel(string username) => $"Адказ @{username}";
    public const string FranchiseFallback = "Іншыя часткі франшызы";
    public static string Rating(double value) => $"Рэйтынг: {RatingDisplay.Stars(value):0.0}/5";

    // --- episode kind bar ---
    public const string KindDub = "Дубляж";
    public const string KindSub = "Субцітры";
    public const string KindOther = "Іншае";

    // --- user mark choices ---
    public const string MarkNotSelected = "Не адзначана";
    public const string MarkWatching = "Гляджу";
    public const string MarkWatched = "Прагледжана";
    public const string MarkDropped = "Кінуў";
    public const string MarkPlanned = "Запланавана";
    public const string MarkReading = "Чытаю";
    public const string MarkRead = "Прачытана";
    public const string MarkPlaying = "Гуляю";
    public const string MarkPlayed = "Прайграна";

    // --- episode resource filter ---
    public const string AllSources = "Усе крыніцы";
    public const string ResourceGoogleDrive = "1 — Google Drive";
    public const string ResourceAnibelPlayer = "2 — Anibel Player";

    // --- downloads: queued notes ---
    public const string EpisodeAddedToDownloads = "Эпізод дададзены ў спампоўкі.";
    public const string AudioAddedToDownloads = "Аўдыё дададзена ў спампоўкі.";
    public const string ChapterAddedToDownloads = "Глава дададзена ў спампоўкі.";
    public const string FileAddedToDownloads = "Файл дададзены ў спампоўкі.";
    public const string NoEpisodesToDownload = "Няма эпізодаў для спампоўкі.";
    public const string NoChaptersToDownload = "Няма глаў для спампоўкі.";
    public static string QueueAdded(int count) =>
        $"У чаргу дададзена: {count}. Прагрэс — на старонцы «Спампаванае».";

    // --- downloads: item labels ---
    public const string KindVideo = "Відэа";
    public const string KindAudio = "Аўдыё";
    public const string KindManga = "Манга";
    public const string KindFile = "Файл";
    public const string StatusQueued = "У чарзе";
    public const string StatusDownloading = "Спампоўваецца…";
    public static string StatusDownloadingParts(int done, int total) => $"Спампоўваецца {done}/{total}";
    public const string StatusCompleted = "Гатова";
    public const string StatusFailed = "Памылка";
    public const string StatusCancelled = "Скасавана";
    public const string PlayChapter = "Чытаць";
    public const string PlayEpisode = "Глядзець";
    public static string OnDisk(string bytes) => $"На дыску: {bytes}";
    public const string UnitBytes = "Б";
    public const string UnitKb = "КБ";
    public const string UnitMb = "МБ";
    public const string UnitGb = "ГБ";
    public const string UnitTb = "ТБ";
    public const string MediaFileType = "Медыя";

    // --- reader ---
    public const string LoadingChapter = "Загружаем главу…";
    public const string NoPagesTitle = "Няма старонак";
    public const string NoPagesMessage = "Гэта глава яшчэ без старонак.";
    public const string Sorry = "Прабачце!";
    public const string Copy = "Капіяваць";

    // --- player ---
    public const string Episode = "Эпізод";
    public static string EpisodeSuffix(string label) => $" · эпізод {label}";
    public const string OpeningLocalFile = "Адкрываем спампаваны файл…";
    public const string ResolvingSource = "Раздагадваем крыніцу…";

    // --- catalog ---
    public const string NothingFound = "Нічога не знойдзена";
    public const string NothingFoundHint = "Нічога не знойдзена — паспрабуйце іншыя фільтры";
    public const string AllYears = "Усе гады";
    public const string AllCountries = "Усе краіны";
    public const string AllTypes = "Усе тыпы";
    public static string Total(long count) => $"Усяго: {count}";
    public static string Found(int count) => $"Знойдзена: {count}";

    // --- search suggestions ---
    public const string ShowMoreResults = "Паказаць больш вынікаў";
    public static string SearchFor(string query) => $"Шукаць «{query}»";

    // --- profile ---
    public const string Profile = "Профіль";
    public static string RecordsCount(long count) => count == 1 ? "1 запіс" : $"{count} запісаў";
    public const string TabFavorites = "Закладкі";
    public const string TabWatching = "Гляджу";
    public const string TabWatched = "Прагледжана";
    public const string TabPlanned = "Запланавана";
    public const string TabDropped = "Кінуў";
    public const string LoginToSeeList = "Увайдзіце, каб бачыць спіс.";
    public const string EmptyList = "Спіс пусты";

    // --- auth ---
    public const string EnterLoginAndPassword = "Увядзіце лагін і пароль.";
    public const string NoTokenTryAgain = "Сервер не вярнуў токен — паспрабуйце яшчэ раз.";

    // --- settings ---
    public const string LanguageRestartNote = "Мова застосуется пасля перазапуску";
    public const string UrlsSaved = "Адрасы захаваны — перазапусціце праграму";
    public const string CacheCleared = "Кэш ачышчаны";
    public const string CatalogCacheCleared = "Кэш каталога ачышчаны";
    public static string Error(string message) => $"Памылка: {message}";

    // --- app bootstrap ---
    public const string CoreErrorTitle = "Памылка ядра";
    public static string CoreInitError(string detail) =>
        "Не ўдалося ініцыялізаваць anibel_core.dll. Праверце, што DLL побач з exe " +
        "(scripts/build-core.ps1) і запусціце праграму нанова.\n\n" + detail;
    public const string Ok = "OK";

    // --- shared display composition ---
    public const string Anonymous = "Анонім";
    public const string EmptyStateDefault = "Тут пакуль пуста";
    public static string NumChapters(string number) => $"гл. {number}";
    public static string NumGame(string number) => $"№ {number}";
    public static string NumEpisode(string number) => $"эп. {number}";
    public static string EpisodeTitle(string number) => $"Эпізод {number}";
    public static string ChapterTitle(string number) => $"Глава {number}";
}
