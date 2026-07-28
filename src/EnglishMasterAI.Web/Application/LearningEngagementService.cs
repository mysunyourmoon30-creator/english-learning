using EnglishMasterAI.Web.Data;
using EnglishMasterAI.Web.Domain;
using EnglishMasterAI.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EnglishMasterAI.Web.Application;

public sealed class LearningEngagementService(
    ApplicationDbContext db,
    LearningTelemetry telemetry)
{
    private static readonly AchievementRule[] Rules =
    [
        new("first-step", "First Step", "ทำกิจกรรมการเรียนครั้งแรก", 1, "all"),
        new("lesson-hero", "Lesson Hero", "ผ่านบทเรียนแรก", 1, "lesson"),
        new("lesson-10", "Core Builder", "ผ่านบทเรียนครบ 10 บท", 10, "lesson"),
        new("review-25", "Memory Keeper", "ทบทวนคำศัพท์ครบ 25 ครั้ง", 25, "review"),
        new("writing-first", "Clear Writer", "ส่ง Writing practice ครั้งแรก", 1, "writing"),
        new("speaking-first", "Confident Speaker", "ส่ง Speaking practice ครั้งแรก", 1, "speaking"),
        new("toeic-first", "TOEIC Starter", "ทำ TOEIC assessment ครั้งแรก", 1, "toeic"),
        new("streak-3", "Three-Day Streak", "เรียนต่อเนื่อง 3 วัน", 3, "streak"),
        new("streak-7", "Seven-Day Streak", "เรียนต่อเนื่อง 7 วัน", 7, "streak")
    ];

    public async Task RecordAsync(
        string userId,
        string kind,
        string deduplicationKey,
        int minutes,
        int points,
        CancellationToken cancellationToken = default)
    {
        var exists = await db.LearningActivities.AnyAsync(
            x => x.UserId == userId && x.DeduplicationKey == deduplicationKey,
            cancellationToken);
        if (exists)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        db.LearningActivities.Add(new LearningActivity
        {
            UserId = userId,
            Kind = NormalizeKind(kind),
            DeduplicationKey = deduplicationKey[..Math.Min(
                deduplicationKey.Length,
                160)],
            Minutes = Math.Clamp(minutes, 0, 720),
            Points = Math.Clamp(points, 0, 10_000),
            OccurredAt = now
        });

        var profile = await db.LearnerProfiles.SingleOrDefaultAsync(
            x => x.UserId == userId,
            cancellationToken);
        if (profile is not null)
        {
            UpdateStreak(profile, now);
        }

        await db.SaveChangesAsync(cancellationToken);
        await EvaluateAchievementsAsync(userId, profile, cancellationToken);
        telemetry.RecordLearningActivity(kind);
    }

    public async Task<WeeklyProgress> GetWeeklyProgressAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var timeZoneId = await db.LearnerProfiles
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => x.TimeZoneId)
            .SingleOrDefaultAsync(cancellationToken);
        var zone = ResolveTimeZone(timeZoneId);
        var localToday = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
        var localStart = localToday.AddDays(-6);
        var startUtc = new DateTimeOffset(
            TimeZoneInfo.ConvertTimeToUtc(
                localStart.ToDateTime(TimeOnly.MinValue),
                zone));
        var query = db.LearningActivities
            .AsNoTracking()
            .Where(x => x.UserId == userId);
        var activities = db.Database.IsSqlite()
            ? (await query.ToListAsync(cancellationToken))
                .Where(x => x.OccurredAt >= startUtc)
                .OrderBy(x => x.OccurredAt)
                .ToList()
            : await query
                .Where(x => x.OccurredAt >= startUtc)
                .OrderBy(x => x.OccurredAt)
                .ToListAsync(cancellationToken);
        var days = Enumerable.Range(0, 7)
            .Select(offset => localStart.AddDays(offset))
            .Select(day => new WeeklyProgressDay(
                day,
                activities
                    .Where(x => ActivityDate(x.OccurredAt, zone) == day)
                    .Sum(x => x.Minutes),
                activities.Count(
                    x => ActivityDate(x.OccurredAt, zone) == day)))
            .ToList();

        return new WeeklyProgress(
            activities.Sum(x => x.Minutes),
            activities.Sum(x => x.Points),
            activities.Count,
            activities.Count(x => x.Kind == "lesson"),
            activities.Count(x => x.Kind == "review"),
            activities.Count(x => x.Kind == "assessment" || x.Kind == "toeic"),
            days);
    }

    public async Task<IReadOnlyList<LearnerAchievement>> GetAchievementsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var achievements = await db.LearnerAchievements
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);
        return achievements
            .OrderByDescending(x => x.EarnedAt)
            .ToList();
    }

    private async Task EvaluateAchievementsAsync(
        string userId,
        LearnerProfile? profile,
        CancellationToken cancellationToken)
    {
        var activityCounts = await db.LearningActivities
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .GroupBy(x => x.Kind)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var total = activityCounts.Values.Sum();
        var existing = await db.LearnerAchievements
            .Where(x => x.UserId == userId)
            .Select(x => x.Code)
            .ToListAsync(cancellationToken);
        var existingSet = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in Rules)
        {
            var value = rule.Metric switch
            {
                "all" => total,
                "streak" => profile?.CurrentStreak ?? 0,
                _ => activityCounts.GetValueOrDefault(rule.Metric)
            };
            if (value < rule.Threshold || existingSet.Contains(rule.Code))
            {
                continue;
            }

            db.LearnerAchievements.Add(new LearnerAchievement
            {
                UserId = userId,
                Code = rule.Code,
                Title = rule.Title,
                Description = rule.Description
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void UpdateStreak(LearnerProfile profile, DateTimeOffset now)
    {
        var zone = ResolveTimeZone(profile.TimeZoneId);
        var localDate = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(now, zone).DateTime);
        if (profile.LastLearningDate == localDate)
        {
            return;
        }

        profile.CurrentStreak =
            profile.LastLearningDate == localDate.AddDays(-1)
                ? profile.CurrentStreak + 1
                : 1;
        profile.LastLearningDate = localDate;
        profile.UpdatedAt = now;
    }

    private static DateOnly ActivityDate(
        DateTimeOffset timestamp,
        TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timestamp, zone).DateTime);

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        try
        {
            return string.IsNullOrWhiteSpace(timeZoneId)
                ? TimeZoneInfo.Utc
                : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static string NormalizeKind(string kind) =>
        string.IsNullOrWhiteSpace(kind)
            ? "activity"
            : kind.Trim().ToLowerInvariant()[..Math.Min(kind.Trim().Length, 60)];

    private sealed record AchievementRule(
        string Code,
        string Title,
        string Description,
        int Threshold,
        string Metric);
}
