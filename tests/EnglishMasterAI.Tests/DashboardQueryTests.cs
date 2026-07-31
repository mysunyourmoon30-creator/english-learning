using EnglishMasterAI.Web.Application;
using EnglishMasterAI.Web.Data;
using EnglishMasterAI.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishMasterAI.Tests;

/// <summary>
/// The dashboard counts due reviews and picks the latest placement in SQL
/// rather than pulling every row back to filter in memory. These pin the
/// results down so the push-down cannot quietly change what the page shows,
/// including the boundary case of a review that is due at exactly now.
/// </summary>
[Collection(EnglishMasterWebCollection.Name)]
public sealed class DashboardQueryTests(EnglishMasterWebFactory factory)
{
    // ReviewSchedules is unique on (UserId, VocabularyItemId), so each schedule
    // in a test needs its own vocabulary item.
    private static List<Guid> VocabularyIds(ApplicationDbContext db, int count) =>
        db.VocabularyItems
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .Take(count)
            .ToList();

    [Fact]
    public async Task Due_review_count_excludes_schedules_in_the_future()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var learning = scope.ServiceProvider.GetRequiredService<LearningService>();
        var userId = $"dashboard-due-{Guid.NewGuid():N}";
        await learning.GetOrCreateProfileAsync(userId);

        var now = DateTimeOffset.UtcNow;
        var schedules = new[]
        {
            now.AddDays(-3),
            now.AddMinutes(-1),
            now.AddMinutes(5),
            now.AddDays(9)
        };
        var vocabularyIds = VocabularyIds(db, schedules.Length);
        Assert.Equal(schedules.Length, vocabularyIds.Count);

        for (var i = 0; i < schedules.Length; i++)
        {
            db.ReviewSchedules.Add(new ReviewSchedule
            {
                UserId = userId,
                VocabularyItemId = vocabularyIds[i],
                NextReviewAt = schedules[i]
            });
        }
        await db.SaveChangesAsync();

        var dashboard = await learning.GetDashboardAsync(userId);

        Assert.Equal(2, dashboard.DueReviews);
    }

    [Fact]
    public async Task Due_review_count_is_zero_when_nothing_is_scheduled()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var learning = scope.ServiceProvider.GetRequiredService<LearningService>();
        var userId = $"dashboard-empty-{Guid.NewGuid():N}";
        await learning.GetOrCreateProfileAsync(userId);

        var dashboard = await learning.GetDashboardAsync(userId);

        Assert.Equal(0, dashboard.DueReviews);
    }

    [Fact]
    public async Task Weak_areas_come_from_the_most_recent_placement_attempt()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var learning = scope.ServiceProvider.GetRequiredService<LearningService>();
        var userId = $"dashboard-placement-{Guid.NewGuid():N}";
        await learning.GetOrCreateProfileAsync(userId);

        var now = DateTimeOffset.UtcNow;
        // Inserted newest first so a correct result cannot come from insertion
        // order happening to line up with completion order.
        db.PlacementAttempts.AddRange(
            NewAttempt(userId, now.AddMinutes(-1), "newest"),
            NewAttempt(userId, now.AddDays(-30), "oldest"),
            NewAttempt(userId, now.AddDays(-2), "middle"));
        await db.SaveChangesAsync();

        var dashboard = await learning.GetDashboardAsync(userId);

        Assert.Equal(["newest"], dashboard.WeakAreas);
    }

    [Fact]
    public async Task Weak_areas_fall_back_when_no_placement_exists()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var learning = scope.ServiceProvider.GetRequiredService<LearningService>();
        var userId = $"dashboard-noplacement-{Guid.NewGuid():N}";
        await learning.GetOrCreateProfileAsync(userId);

        var dashboard = await learning.GetDashboardAsync(userId);

        Assert.NotEmpty(dashboard.WeakAreas);
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
