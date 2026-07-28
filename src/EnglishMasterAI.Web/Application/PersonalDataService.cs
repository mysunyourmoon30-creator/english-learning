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
                x.FeedbackJson,
                x.SubmittedAt
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
            ["AudioRetention"] = "Raw audio is processed in memory and is not stored."
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
