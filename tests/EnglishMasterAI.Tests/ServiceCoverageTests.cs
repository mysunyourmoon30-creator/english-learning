using EnglishMasterAI.Web.Application;
using EnglishMasterAI.Web.Data;
using EnglishMasterAI.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishMasterAI.Tests;

[Collection(EnglishMasterWebCollection.Name)]
public sealed class ServiceCoverageTests(EnglishMasterWebFactory factory)
{
    [Fact]
    public async Task Assessment_submission_updates_profile_history_and_toeic_result()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<AssessmentService>();
        var userId = $"assessment-{Guid.NewGuid():N}";
        var placement = await db.AssessmentQuestions
            .AsNoTracking()
            .Where(x => x.Kind == AssessmentKind.Placement && x.LessonId == null)
            .ToListAsync();
        var correctAnswers = placement.ToDictionary(
            question => question.Id,
            question => question.CorrectOptionIndex);

        var placementResult = await service.SubmitAsync(
            userId,
            AssessmentKind.Placement,
            correctAnswers,
            -10);
        var diagnosticResult = await service.SubmitAsync(
            userId,
            AssessmentKind.ToeicDiagnostic,
            new Dictionary<Guid, int>(),
            120);
        var history = await service.GetHistoryAsync(userId, "Placement");
        var profile = await db.LearnerProfiles
            .AsNoTracking()
            .SingleAsync(x => x.UserId == userId);

        Assert.Equal(placement.Count, placementResult.CorrectAnswers);
        Assert.True(profile.PlacementCompleted);
        Assert.Single(history);
        Assert.Equal(0, history[0].DurationSeconds);
        Assert.Equal(10, diagnosticResult.EstimatedToeicScore);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.SubmitAsync(
                userId,
                AssessmentKind.LessonQuiz,
                correctAnswers,
                1));
    }

    [Fact]
    public async Task Learning_lifecycle_covers_onboarding_dashboard_quiz_and_review()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var learning = scope.ServiceProvider.GetRequiredService<LearningService>();
        var reviews = scope.ServiceProvider.GetRequiredService<ReviewService>();
        var userId = $"learning-{Guid.NewGuid():N}";

        var profile = await learning.GetOrCreateProfileAsync(
            userId,
            "learner@example.test");
        var sameProfile = await learning.GetOrCreateProfileAsync(userId);
        var onboarded = await learning.SaveOnboardingAsync(
            userId,
            "  Learner  ",
            "  TOEIC  ",
            "B2",
            5_000,
            2);
        var modules = await learning.GetModulesAsync(
            userId,
            LearningCategory.Foundation);
        var lesson = await db.Lessons
            .AsNoTracking()
            .Include(x => x.Questions)
            .FirstAsync(x => x.Status == ContentStatus.Published
                && x.Questions.Any(q => q.Kind == AssessmentKind.LessonQuiz));
        var answers = lesson.Questions
            .Where(x => x.Kind == AssessmentKind.LessonQuiz)
            .ToDictionary(x => x.Id, x => x.CorrectOptionIndex);

        var quiz = await learning.SubmitLessonQuizAsync(
            userId,
            lesson.Id,
            answers);
        var due = await reviews.GetDueAsync(userId, 500);
        var update = await reviews.RateAsync(userId, due[0].Id, 5);
        var dashboard = await learning.GetDashboardAsync(userId);
        var loadedLesson = await learning.GetLessonAsync(lesson.Slug);

        Assert.Same(profile, sameProfile);
        Assert.Equal("Learner", onboarded.DisplayName);
        Assert.Equal(990, onboarded.TargetToeicScore);
        Assert.Equal(15, onboarded.DailyMinutes);
        Assert.NotEmpty(modules);
        Assert.True(quiz.Passed);
        Assert.True(update.Repetition >= 1);
        Assert.NotNull(loadedLesson);
        Assert.True(dashboard.CompletedLessons >= 1);
    }

    [Fact]
    public async Task Personal_data_export_returns_each_supported_category()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<PersonalDataService>();

        var export = await service.ExportLearningDataAsync(
            $"export-{Guid.NewGuid():N}");

        Assert.Equal(10, export.Count);
        Assert.Contains("Profile", export.Keys);
        Assert.Contains("LearningProgress", export.Keys);
        Assert.Contains("Assessments", export.Keys);
        Assert.Contains("VocabularyReviews", export.Keys);
        Assert.Contains("WritingSubmissions", export.Keys);
        Assert.Contains("SpeakingSubmissions", export.Keys);
        Assert.Contains("LearningActivities", export.Keys);
        Assert.Contains("Achievements", export.Keys);
        Assert.Contains("AiUsageMetadata", export.Keys);
        Assert.Contains("AudioRetention", export.Keys);
    }

    [Fact]
    public async Task Content_review_and_audit_workflows_cover_state_changes()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reviews = scope.ServiceProvider.GetRequiredService<ContentReviewService>();
        var audit = scope.ServiceProvider.GetRequiredService<AuditService>();
        var assignment = await db.ContentReviewAssignments.FirstAsync();
        var originalStatus = assignment.Status;
        var originalReviewer = assignment.ReviewerUserId;
        var originalNotes = assignment.Notes;
        var originalReviewedAt = assignment.ReviewedAt;

        try
        {
            await reviews.UpdateAsync(
                assignment.Id,
                "reviewer-test",
                ContentReviewStatus.ChangesRequested,
                new string('n', 1_200));
            var coverage = await reviews.GetCoverageAsync();
            var assignments = await reviews.GetAssignmentsAsync();

            Assert.Equal(1_000, assignment.Notes.Length);
            Assert.NotNull(assignment.ReviewedAt);
            Assert.True(coverage.Total >= 48);
            Assert.Contains(assignments, x => x.Id == assignment.Id);

            await audit.AddFindingAsync(
                assignment.LessonId,
                "audit-test",
                "English Reviewer",
                AuditSeverity.Low,
                "Test paragraph",
                "Test issue",
                "Test recommendation");
            var createdFindings = await db.AuditFindings
                .Where(x => x.CreatedByUserId == "audit-test")
                .ToListAsync();
            var finding = createdFindings.MaxBy(x => x.CreatedAt)!;
            await audit.ResolveAsync(finding.Id);
            var summary = await audit.GetSummaryAsync(assignment.LessonId);
            var findings = await audit.GetFindingsAsync(includeResolved: true);
            var lessons = await audit.GetContentLessonsAsync();
            var revisions = await audit.GetRevisionsAsync(assignment.LessonId);

            Assert.True((await db.AuditFindings.FindAsync(finding.Id))!.IsResolved);
            Assert.True(summary.Total >= 1);
            Assert.Contains(findings, x => x.Id == finding.Id);
            Assert.NotEmpty(lessons);
            Assert.NotEmpty(revisions);
        }
        finally
        {
            assignment.Status = originalStatus;
            assignment.ReviewerUserId = originalReviewer;
            assignment.Notes = originalNotes;
            assignment.ReviewedAt = originalReviewedAt;
            var testFindings = await db.AuditFindings
                .Where(x => x.CreatedByUserId == "audit-test")
                .ToListAsync();
            db.AuditFindings.RemoveRange(testFindings);
            await db.SaveChangesAsync();
        }
    }
}
