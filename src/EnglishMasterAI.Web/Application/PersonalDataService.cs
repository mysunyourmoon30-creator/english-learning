using EnglishMasterAI.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EnglishMasterAI.Web.Application;

public sealed class PersonalDataService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager)
{
    public async Task<IReadOnlyDictionary<string, object?>> ExportLearningDataAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var profile = await db.LearnerProfiles
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new
            {
                x.DisplayName,
                x.LearningGoal,
                x.CurrentCefr,
                x.TargetCefr,
                x.TargetToeicScore,
                x.DailyMinutes,
                x.CurrentStreak,
                x.TimeZoneId,
                x.CreatedAt,
                x.UpdatedAt
            })
            .SingleOrDefaultAsync(cancellationToken);
        var progress = await db.LearningProgress
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new
            {
                x.LessonId,
                x.BestScore,
                x.AttemptCount,
                x.IsCompleted,
                x.StartedAt,
                x.CompletedAt,
                x.LastActivityAt
            })
            .ToListAsync(cancellationToken);
        var assessments = await db.PlacementAttempts
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new
            {
                x.AssessmentName,
                x.OverallCefr,
                x.EstimatedToeicScore,
                x.SkillScoresJson,
                x.PrioritiesJson,
                x.CorrectAnswers,
                x.TotalQuestions,
                x.DurationSeconds,
                x.CompletedAt
            })
            .ToListAsync(cancellationToken);
        var reviews = await db.ReviewSchedules
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new
            {
                x.VocabularyItemId,
                x.Repetition,
                x.IntervalDays,
                x.EaseFactor,
                x.NextReviewAt,
                x.LastReviewedAt
            })
            .ToListAsync(cancellationToken);
        var writing = await db.WritingSubmissions
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new
            {
                x.Prompt,
                x.Content,
                x.Score,
                x.FeedbackJson,
                x.SubmittedAt
            })
            .ToListAsync(cancellationToken);
        var speaking = await db.SpeakingSubmissions
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new
            {
                x.Prompt,
                x.Transcript,
                x.DurationSeconds,
                x.Score,
                x.EvaluationMode,
                x.PronunciationScore,
                x.AccuracyScore,
                x.CompletenessScore,
                x.ProsodyScore,
                x.PronunciationProvider,
                x.FeedbackJson,
                x.SubmittedAt
            })
            .ToListAsync(cancellationToken);
        var activities = await db.LearningActivities
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new
            {
                x.Kind,
                x.Minutes,
                x.Points,
                x.OccurredAt
            })
            .ToListAsync(cancellationToken);
        var achievements = await db.LearnerAchievements
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new
            {
                x.Code,
                x.Title,
                x.Description,
                x.EarnedAt
            })
            .ToListAsync(cancellationToken);
        var aiUsage = await db.AiUsageRecords
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new
            {
                x.Operation,
                x.Provider,
                x.Model,
                x.Status,
                x.InputUnits,
                x.InputTokens,
                x.OutputTokens,
                x.DurationMilliseconds,
                x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new Dictionary<string, object?>
        {
            ["Profile"] = profile,
            ["LearningProgress"] = progress,
            ["Assessments"] = assessments,
            ["VocabularyReviews"] = reviews,
            ["WritingSubmissions"] = writing,
            ["SpeakingSubmissions"] = speaking,
            ["LearningActivities"] = activities,
            ["Achievements"] = achievements,
            ["AiUsageMetadata"] = aiUsage,
            ["AudioRetention"] = "Learner audio is processed in memory and is not stored. Generated reference audio may be cached by text hash."
        };
    }

    public async Task<IdentityResult> DeleteAccountDataAsync(
        ApplicationUser user,
        CancellationToken cancellationToken = default)
    {
        var userId = await userManager.GetUserIdAsync(user);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.LearnerProfiles
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.LearningProgress
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.PlacementAttempts
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.ReviewSchedules
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.WritingSubmissions
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.SpeakingSubmissions
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.LearningActivities
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.LearnerAchievements
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.AiUsageRecords
            .Where(x => x.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
        await db.ContentReviewAssignments
            .Where(x => x.ReviewerUserId == userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.ReviewerUserId, (string?)null)
                    .SetProperty(x => x.Status, Domain.ContentReviewStatus.Pending)
                    .SetProperty(x => x.ReviewedAt, (DateTimeOffset?)null),
                cancellationToken);

        var result = await userManager.DeleteAsync(user);
        if (result.Succeeded)
        {
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await transaction.RollbackAsync(cancellationToken);
        }

        return result;
    }
}
