namespace EnglishMasterAI.Web.Application;

public static class SrsScheduler
{
    public static ReviewUpdate Calculate(int rating, int currentRepetition, int currentInterval, double currentEase, DateTimeOffset now)
    {
        rating = Math.Clamp(rating, 0, 3);
        var ease = currentEase;
        var repetition = currentRepetition;
        var interval = currentInterval;

        if (rating == 0)
        {
            repetition = 0;
            interval = 0;
            ease = Math.Max(1.3, ease - 0.2);
            return new ReviewUpdate(repetition, interval, ease, now.AddMinutes(10));
        }

        repetition++;
        ease = Math.Clamp(ease + (rating == 3 ? 0.15 : rating == 1 ? -0.15 : 0), 1.3, 3.0);
        interval = repetition switch
        {
            1 => 1,
            2 => rating == 1 ? 2 : 3,
            _ => Math.Max(1, (int)Math.Round(Math.Max(1, interval) * ease * (rating == 1 ? 0.7 : rating == 3 ? 1.25 : 1)))
        };

        return new ReviewUpdate(repetition, interval, ease, now.AddDays(interval));
    }
}
