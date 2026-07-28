using EnglishMasterAI.Web.Data;
using EnglishMasterAI.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnglishMasterAI.Web.Application;

public sealed class ReviewService(ApplicationDbContext db)
{
    public async Task ScheduleLessonVocabularyAsync(string userId, Guid lessonId)
    {
        var vocabularyIds = await db.VocabularyItems
            .Where(x => x.LessonId == lessonId)
            .Select(x => x.Id)
            .ToListAsync();
        var existing = await db.ReviewSchedules
            .Where(x => x.UserId == userId && vocabularyIds.Contains(x.VocabularyItemId))
            .Select(x => x.VocabularyItemId)
            .ToListAsync();
        var existingSet = existing.ToHashSet();

        foreach (var vocabularyId in vocabularyIds.Where(x => !existingSet.Contains(x)))
        {
            db.ReviewSchedules.Add(new ReviewSchedule
            {
                UserId = userId,
                VocabularyItemId = vocabularyId,
                NextReviewAt = DateTimeOffset.UtcNow
            });
        }
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<ReviewSchedule>> GetDueAsync(string userId, int take = 20)
    {
        var schedules = await db.ReviewSchedules
            .Include(x => x.VocabularyItem)
            .ThenInclude(x => x.Lesson)
            .Where(x => x.UserId == userId)
            .AsNoTracking()
            .ToListAsync();
        var now = DateTimeOffset.UtcNow;
        return schedules
            .Where(x => x.NextReviewAt <= now)
            .OrderBy(x => x.NextReviewAt)
            .Take(Math.Clamp(take, 1, 50))
            .ToList();
    }

    public async Task<ReviewUpdate> RateAsync(string userId, Guid scheduleId, int rating)
    {
        var schedule = await db.ReviewSchedules.SingleOrDefaultAsync(x => x.Id == scheduleId && x.UserId == userId)
            ?? throw new InvalidOperationException("Review card not found.");
        var now = DateTimeOffset.UtcNow;
        var update = SrsScheduler.Calculate(rating, schedule.Repetition, schedule.IntervalDays, schedule.EaseFactor, now);
        schedule.Repetition = update.Repetition;
        schedule.IntervalDays = update.IntervalDays;
        schedule.EaseFactor = update.EaseFactor;
        schedule.NextReviewAt = update.NextReviewAt;
        schedule.LastReviewedAt = now;
        await db.SaveChangesAsync();
        return update;
    }
}
