namespace Anibel.App.Services;

public static class RatingDisplay
{
    public static double Stars(double score) => double.IsFinite(score)
        ? Math.Round(Math.Clamp(score, 0, 10), MidpointRounding.AwayFromZero) / 2
        : 0;
}
