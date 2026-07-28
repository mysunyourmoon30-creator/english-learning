namespace EnglishMasterAI.Web.Application;

public static class AssessmentScorer
{
    public static string ToCefrLevel(int accuracyPercent) => accuracyPercent switch
    {
        < 25 => "Pre-A1",
        < 40 => "A1",
        < 55 => "A2",
        < 70 => "B1",
        < 85 => "B2",
        < 95 => "C1",
        _ => "C2"
    };

    public static int EstimateToeicScore(int accuracyPercent)
    {
        var raw = 10 + (int)Math.Round(accuracyPercent / 100d * 980, MidpointRounding.AwayFromZero);
        return Math.Clamp((int)Math.Round(raw / 5d) * 5, 10, 990);
    }
}
