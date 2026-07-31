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
