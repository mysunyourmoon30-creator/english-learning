using System.Text.Json;
using EnglishMasterAI.Web.Data;
using EnglishMasterAI.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnglishMasterAI.Web.Application;

public sealed class LearningService(
    ApplicationDbContext db,
    ReviewService reviewService,
    LearningEngagementService engagement)
{
    public async Task<LearnerProfile> GetOrCreateProfileAsync(string userId, string? email = null)
    {
        var profile = await db.LearnerProfiles.SingleOrDefaultAsync(x => x.UserId == userId);
        if (profile is not null)
        {
            return profile;
        }

        profile = new LearnerProfile
        {
            UserId = userId,
            DisplayName = string.IsNullOrWhiteSpace(email) ? "ผู้เรียน" : email.Split('@')[0]
        };
        db.LearnerProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    public async Task<LearnerProfile> SaveOnboardingAsync(
        string userId,
        string displayName,
        string learningGoal,
        string targetCefr,
        int targetToeicScore,
        int dailyMinutes)
    {
        var profile = await GetOrCreateProfileAsync(userId);
        profile.DisplayName = displayName.Trim();
        profile.LearningGoal = learningGoal.Trim();
        profile.TargetCefr = targetCefr;
        profile.TargetToeicScore = Math.Clamp(targetToeicScore, 10, 990);
        profile.DailyMinutes = Math.Clamp(dailyMinutes, 15, 180);
        profile.OnboardingCompleted = true;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return profile;
    }

    public async Task<IReadOnlyList<ModuleProgress>> GetModulesAsync(string userId, LearningCategory? category = null)
    {
        var modulesQuery = db.CourseModules
            .AsNoTracking()
            .Where(x => x.IsPublished)
            .Include(x => x.Lessons.Where(l => l.Status == ContentStatus.Published))
            .OrderBy(x => x.SortOrder)
            .AsQueryable();

        if (category is not null)
        {
            modulesQuery = modulesQuery.Where(x => x.Category == category);
        }

        var modules = await modulesQuery.ToListAsync();
        var lessonIds = modules.SelectMany(x => x.Lessons).Select(x => x.Id).ToList();
        var completed = await db.LearningProgress
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.IsCompleted && lessonIds.Contains(x.LessonId))
            .Select(x => x.LessonId)
            .ToListAsync();
        var completedSet = completed.ToHashSet();

        return modules.Select(module =>
        {
            var total = module.Lessons.Count;
            var done = module.Lessons.Count(x => completedSet.Contains(x.Id));
            return new ModuleProgress(
                module.Id,
                module.Code,
                module.Title,
                module.TitleThai,
                module.Summary,
                module.Phase,
                module.CefrLevel,
                module.Category,
                module.EstimatedMinutes,
                done,
                total,
                total == 0 ? 0 : (int)Math.Round(done * 100d / total),
                module.Lessons.OrderBy(x => x.SortOrder).FirstOrDefault()?.Slug);
        }).ToList();
    }

    public async Task<DashboardData> GetDashboardAsync(string userId)
    {
        var profile = await GetOrCreateProfileAsync(userId);
        var modules = await GetModulesAsync(userId);
        var totalLessons = modules.Sum(x => x.TotalLessons);
        var completedLessons = modules.Sum(x => x.CompletedLessons);
        var now = DateTimeOffset.UtcNow;
        // SQLite cannot translate DateTimeOffset comparisons, so it filters
        // after materialising; PostgreSQL can, so it does the work in the
        // database. Same split as AiUsageService and LearningEngagementService.
        var reviewSchedules = db.ReviewSchedules
            .AsNoTracking()
            .Where(x => x.UserId == userId);
        var dueReviews = db.Database.IsSqlite()
            ? (await reviewSchedules.Select(x => x.NextReviewAt).ToListAsync())
                .Count(x => x <= now)
            : await reviewSchedules.CountAsync(x => x.NextReviewAt <= now);

        var placementAttempts = db.PlacementAttempts
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.AssessmentName == "Placement");
        var lastPlacement = db.Database.IsSqlite()
            ? (await placementAttempts.ToListAsync())
                .OrderByDescending(x => x.CompletedAt)
                .FirstOrDefault()
            : await placementAttempts
                .OrderByDescending(x => x.CompletedAt)
                .FirstOrDefaultAsync();

        var weakAreas = lastPlacement is null
            ? new List<string> { "Placement test ยังไม่เสร็จ", "Core vocabulary", "Sentence structure" }
            : JsonSerializer.Deserialize<List<string>>(lastPlacement.PrioritiesJson) ?? [];

        var nextFoundation = modules.FirstOrDefault(x =>
            (x.Category is LearningCategory.Foundation or LearningCategory.Grammar) && x.ProgressPercent < 100);
        var nextAi = modules.FirstOrDefault(x => x.Category == LearningCategory.AiEnglish && x.ProgressPercent < 100);
        var minutes = profile.DailyMinutes;
        var plan = new List<DailyTask>
        {
            new("Vocabulary Review", dueReviews == 0 ? "เตรียมคำใหม่สำหรับรอบถัดไป" : $"{dueReviews} คำถึงเวลาทบทวน", Math.Min(10, minutes), "/review", "review"),
            new("English Core", nextFoundation?.Title ?? "Sentence building practice", Math.Min(20, Math.Max(10, minutes / 3)), nextFoundation?.FirstLessonSlug is null ? "/learn" : $"/lessons/{nextFoundation.FirstLessonSlug}", "core"),
            new("Listening & Shadowing", "ฟัง → ตอบ → เปิด transcript → พูดตาม", Math.Min(15, Math.Max(10, minutes / 4)), "/practice/listening", "listening"),
            new("AI English", nextAi?.Title ?? "Technical English practice", Math.Min(20, Math.Max(10, minutes / 3)), nextAi?.FirstLessonSlug is null ? "/ai-english" : $"/lessons/{nextAi.FirstLessonSlug}", "ai")
        };
        var weeklyProgress = await engagement.GetWeeklyProgressAsync(userId);
        var achievements = await engagement.GetAchievementsAsync(userId);

        return new DashboardData(
            profile,
            totalLessons == 0 ? 0 : (int)Math.Round(completedLessons * 100d / totalLessons),
            dueReviews,
            completedLessons,
            totalLessons,
            weakAreas,
            plan,
            modules.Where(x => x.ProgressPercent < 100).Take(4).ToList(),
            weeklyProgress,
            achievements);
    }

    public Task<Lesson?> GetLessonAsync(string slug) =>
        db.Lessons
            .AsNoTracking()
            .Include(x => x.Module)
            .Include(x => x.Vocabulary)
            .Include(x => x.Questions.Where(q => q.Kind == AssessmentKind.LessonQuiz && q.IsPublished))
            .SingleOrDefaultAsync(x => x.Slug == slug && x.Status == ContentStatus.Published);

    public async Task<QuizResult> SubmitLessonQuizAsync(string userId, Guid lessonId, IReadOnlyDictionary<Guid, int> answers)
    {
        var lesson = await db.Lessons
            .Include(x => x.Questions.Where(q => q.Kind == AssessmentKind.LessonQuiz && q.IsPublished))
            .Include(x => x.Vocabulary)
            .SingleOrDefaultAsync(x => x.Id == lessonId)
            ?? throw new InvalidOperationException("Lesson not found.");

        var correctness = lesson.Questions.ToDictionary(
            question => question.Id,
            question => answers.TryGetValue(question.Id, out var answer) && answer == question.CorrectOptionIndex);
        var explanations = lesson.Questions.ToDictionary(x => x.Id, x => x.Explanation);
        var correct = correctness.Count(x => x.Value);
        var total = lesson.Questions.Count;
        var score = total == 0 ? 100 : (int)Math.Round(correct * 100d / total);
        var passed = score >= 80;

        var progress = await db.LearningProgress.SingleOrDefaultAsync(x => x.UserId == userId && x.LessonId == lessonId);
        if (progress is null)
        {
            progress = new LearningProgress
            {
                UserId = userId,
                LessonId = lessonId,
                StartedAt = DateTimeOffset.UtcNow
            };
            db.LearningProgress.Add(progress);
        }

        progress.AttemptCount++;
        progress.BestScore = Math.Max(progress.BestScore, score);
        progress.IsCompleted |= passed;
        progress.CompletedAt = passed ? DateTimeOffset.UtcNow : progress.CompletedAt;
        progress.LastActivityAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();
        await reviewService.ScheduleLessonVocabularyAsync(userId, lesson.Id);
        await engagement.RecordAsync(
            userId,
            passed ? "lesson" : "quiz",
            $"lesson:{lessonId}:attempt:{progress.AttemptCount}",
            lesson.EstimatedMinutes,
            passed ? 100 : score);

        return new QuizResult(score, correct, total, passed, correctness, explanations);
    }
}
