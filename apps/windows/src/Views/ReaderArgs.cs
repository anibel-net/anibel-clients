namespace Anibel.App.Views;

public sealed record ReaderArgs(
    string Slug,
    double Chapter,
    string MediaTitle,
    string? ChapterTitle,
    string? ChapterId,
    double[] Chapters);
