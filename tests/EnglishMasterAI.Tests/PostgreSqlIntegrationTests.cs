using EnglishMasterAI.Web.Application;
using EnglishMasterAI.Web.Data;
using EnglishMasterAI.Web.Domain;
using EnglishMasterAI.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EnglishMasterAI.Tests;

public sealed class PostgreSqlIntegrationTests
{
    private static ApplicationDbContext? CreateContext()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "POSTGRES_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                connectionString,
                postgres => postgres.MigrationsAssembly(
                    "EnglishMasterAI.Migrations.PostgreSql"))
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    [Trait("Category", "PostgreSql")]
    public async Task Migrations_and_core_write_path_work_on_postgresql()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "POSTGRES_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                connectionString,
                postgres => postgres.MigrationsAssembly(
                    "EnglishMasterAI.Migrations.PostgreSql"))
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.MigrateAsync();
        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
        var userId = $"postgres-integration-{Guid.NewGuid():N}";

        db.LearnerProfiles.Add(new LearnerProfile
        {
            UserId = userId,
            DisplayName = "PostgreSQL integration",
            TimeZoneId = "Asia/Bangkok"
        });
        await db.SaveChangesAsync();
        var profile = await db.LearnerProfiles
            .AsNoTracking()
            .SingleAsync(x => x.UserId == userId);

        Assert.NotEmpty(appliedMigrations);
        Assert.Equal("PostgreSQL integration", profile.DisplayName);

        db.LearnerProfiles.Remove(
            await db.LearnerProfiles.SingleAsync(x => x.UserId == userId));
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The dashboard counts due reviews and picks the latest placement in SQL
    /// on PostgreSQL and in memory on SQLite, because SQLite cannot translate
    /// DateTimeOffset comparisons. The rest of the suite runs on SQLite, so
    /// this is the only place the PostgreSQL branch is executed at all.
    /// </summary>
    [Fact]
    [Trait("Category", "PostgreSql")]
    public async Task Dashboard_filters_reviews_and_placements_in_the_database()
    {
        await using var db = CreateContext();
        if (db is null)
        {
            return;
        }

        await db.Database.MigrateAsync();
        Assert.False(db.Database.IsSqlite());

        var telemetry = new LearningTelemetry();
        var engagement = new LearningEngagementService(db, telemetry);
        var reviews = new ReviewService(db, engagement);
        var learning = new LearningService(db, reviews, engagement);

        var userId = $"postgres-dashboard-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var dueTimes = new[]
        {
            now.AddDays(-3),
            now.AddMinutes(-1),
            now.AddMinutes(5),
            now.AddDays(9)
        };

        try
        {
            await learning.GetOrCreateProfileAsync(userId);

            // ReviewSchedules is unique on (UserId, VocabularyItemId), so each
            // schedule needs its own word. Seeded content supplies them.
            var vocabularyIds = await db.VocabularyItems
                .AsNoTracking()
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .Take(dueTimes.Length)
                .ToListAsync();
            Assert.Equal(dueTimes.Length, vocabularyIds.Count);

            for (var i = 0; i < dueTimes.Length; i++)
            {
                db.ReviewSchedules.Add(new ReviewSchedule
                {
                    UserId = userId,
                    VocabularyItemId = vocabularyIds[i],
                    NextReviewAt = dueTimes[i]
                });
            }

            // Newest inserted first, so a result that came from insertion order
            // rather than CompletedAt would still fail.
            db.PlacementAttempts.AddRange(
                NewAttempt(userId, now.AddMinutes(-1), "newest"),
                NewAttempt(userId, now.AddDays(-30), "oldest"),
                NewAttempt(userId, now.AddDays(-2), "middle"));
            await db.SaveChangesAsync();

            var dashboard = await learning.GetDashboardAsync(userId);

            Assert.Equal(2, dashboard.DueReviews);
            Assert.Equal(["newest"], dashboard.WeakAreas);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.ReviewSchedules.Where(x => x.UserId == userId)
                .ExecuteDeleteAsync();
            await db.PlacementAttempts.Where(x => x.UserId == userId)
                .ExecuteDeleteAsync();
            await db.LearnerAchievements.Where(x => x.UserId == userId)
                .ExecuteDeleteAsync();
            await db.LearningActivities.Where(x => x.UserId == userId)
                .ExecuteDeleteAsync();
            await db.LearnerProfiles.Where(x => x.UserId == userId)
                .ExecuteDeleteAsync();
            telemetry.Dispose();
        }
    }

    /// <summary>
    /// GetWeeklyProgressAsync carries the same IsSqlite() split as the
    /// dashboard: PostgreSQL filters activities by OccurredAt in SQL, SQLite
    /// materialises first. Covers the PostgreSQL side.
    /// </summary>
    [Fact]
    [Trait("Category", "PostgreSql")]
    public async Task Weekly_progress_windows_activities_in_the_database()
    {
        await using var db = CreateContext();
        if (db is null)
        {
            return;
        }

        await db.Database.MigrateAsync();
        Assert.False(db.Database.IsSqlite());

        var telemetry = new LearningTelemetry();
        var engagement = new LearningEngagementService(db, telemetry);
        var userId = $"postgres-weekly-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;

        try
        {
            db.LearnerProfiles.Add(new LearnerProfile
            {
                UserId = userId,
                DisplayName = "Weekly window",
                TimeZoneId = "Asia/Bangkok"
            });
            db.LearningActivities.AddRange(
                NewActivity(userId, now.AddHours(-2), "lesson", 20, 30),
                NewActivity(userId, now.AddDays(-1), "review", 15, 10),
                // Far outside the seven day window, so it must not be counted.
                NewActivity(userId, now.AddDays(-30), "lesson", 90, 90));
            await db.SaveChangesAsync();

            var weekly = await engagement.GetWeeklyProgressAsync(userId);

            Assert.Equal(2, weekly.ActivityCount);
            Assert.Equal(35, weekly.Minutes);
            Assert.Equal(40, weekly.Points);
            Assert.Equal(1, weekly.LessonsCompleted);
            Assert.Equal(1, weekly.ReviewsCompleted);
            Assert.Equal(7, weekly.Days.Count);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.LearningActivities.Where(x => x.UserId == userId)
                .ExecuteDeleteAsync();
            await db.LearnerProfiles.Where(x => x.UserId == userId)
                .ExecuteDeleteAsync();
            telemetry.Dispose();
        }
    }

    /// <summary>
    /// GetSummaryAsync carries the same split. It reports across every user
    /// rather than one, so this asserts the delta it contributes rather than
    /// an absolute total.
    /// </summary>
    [Fact]
    [Trait("Category", "PostgreSql")]
    public async Task Ai_usage_summary_windows_records_in_the_database()
    {
        await using var db = CreateContext();
        if (db is null)
        {
            return;
        }

        await db.Database.MigrateAsync();
        Assert.False(db.Database.IsSqlite());

        var service = new AiUsageService(
            db,
            Microsoft.Extensions.Options.Options.Create(new Web.Configuration.AiOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AiUsageService>.Instance);
        var userId = $"postgres-aiusage-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;

        try
        {
            var before = await service.GetSummaryAsync(days: 1);

            db.AiUsageRecords.AddRange(
                NewUsage(userId, now.AddHours(-1), "Succeeded"),
                NewUsage(userId, now.AddHours(-2), "Failed"),
                // Older than the one day window, so it must not be counted.
                NewUsage(userId, now.AddDays(-5), "Succeeded"));
            await db.SaveChangesAsync();

            var after = await service.GetSummaryAsync(days: 1);

            Assert.Equal(before.TotalRequests + 2, after.TotalRequests);
            Assert.Equal(before.Succeeded + 1, after.Succeeded);
            Assert.Equal(before.Failed + 1, after.Failed);
        }
        finally
        {
            db.ChangeTracker.Clear();
            await db.AiUsageRecords.Where(x => x.UserId == userId)
                .ExecuteDeleteAsync();
        }
    }

    private static LearningActivity NewActivity(
        string userId,
        DateTimeOffset occurredAt,
        string kind,
        int minutes,
        int points) =>
        new()
        {
            UserId = userId,
            Kind = kind,
            DeduplicationKey = $"{kind}-{Guid.NewGuid():N}",
            Minutes = minutes,
            Points = points,
            OccurredAt = occurredAt
        };

    private static AiUsageRecord NewUsage(
        string userId,
        DateTimeOffset createdAt,
        string status) =>
        new()
        {
            UserId = userId,
            Operation = "writing-feedback",
            Provider = "OpenAI",
            Model = "test-model",
            Status = status,
            InputUnits = 1,
            InputTokens = 100,
            OutputTokens = 50,
            DurationMilliseconds = 250,
            CreatedAt = createdAt
        };

    private static PlacementAttempt NewAttempt(
        string userId,
        DateTimeOffset completedAt,
        string priority) =>
        new()
        {
            UserId = userId,
            AssessmentName = "Placement",
            OverallCefr = "A2",
            EstimatedToeicScore = 500,
            SkillScoresJson = "{}",
            PrioritiesJson = $"[\"{priority}\"]",
            CorrectAnswers = 6,
            TotalQuestions = 12,
            DurationSeconds = 300,
            CompletedAt = completedAt
        };
}
