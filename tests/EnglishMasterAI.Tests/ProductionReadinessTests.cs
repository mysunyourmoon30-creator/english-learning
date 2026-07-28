using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EnglishMasterAI.Web.Application;
using EnglishMasterAI.Web.Data;
using EnglishMasterAI.Web.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishMasterAI.Tests;

[Collection(EnglishMasterWebCollection.Name)]
public sealed class ProductionReadinessTests(EnglishMasterWebFactory factory)
{
    [Fact]
    public async Task Healthz_ReportsDatabaseHealthy_AndAddsSecurityHeaders()
    {
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync("/healthz");
        var payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"database\"", payload);
        Assert.Contains("\"healthy\"", payload);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task ProtectedApi_Returns401WithoutAuthentication()
    {
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync("/api/v1/catalog/modules");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ToeicMockApi_Returns200QuestionsWithoutAnswerKey()
    {
        using var client = factory.CreateHttpsClient(authenticated: true);

        using var response = await client.GetAsync("/api/v1/assessment/toeic/mock/questions");
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(200, document.RootElement.GetArrayLength());
        var first = document.RootElement[0];
        Assert.True(first.TryGetProperty("options", out _));
        Assert.False(first.TryGetProperty("correctOptionIndex", out _));
        Assert.False(first.TryGetProperty("explanation", out _));
    }

    [Fact]
    public async Task SeedData_ContainsCompleteCoreAndAiCatalog()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var codes = await db.CourseModules
            .AsNoTracking()
            .Select(x => x.Code)
            .ToListAsync();

        Assert.Equal(41, codes.Count);
        Assert.All(
            Enumerable.Range(1, 17),
            number => Assert.Contains($"E{number:00}", codes));
        Assert.All(
            Enumerable.Range(1, 24),
            number => Assert.Contains($"AI{number:00}", codes));
        Assert.Equal(
            200,
            await db.AssessmentQuestions.CountAsync(x => x.Kind == AssessmentKind.ToeicMock));
    }

    [Fact]
    public async Task WritingFeedback_UsesTransparentFallbackWithoutApiKey()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<WritingFeedbackService>();

        var feedback = await service.EvaluateAsync(
            "writing-test-user",
            "Write a technical update.",
            "The API is complete. However, one integration test is failing because the seed data is incomplete. I will update the data and rerun the tests.");

        Assert.Equal("Transparent local rules", feedback.EvaluationMode);
        Assert.InRange(feedback.Score, 1, 100);
        Assert.Equal(
            feedback.Score,
            feedback.Rubric.Grammar
            + feedback.Rubric.Clarity
            + feedback.Rubric.Organization
            + feedback.Rubric.Vocabulary
            + feedback.Rubric.TaskCompletion);
    }

    [Fact]
    public async Task SpeakingFeedback_DoesNotClaimPronunciationFromTranscript()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<SpeakingAnalysisService>();

        var feedback = await service.EvaluateAsync(
            "speaking-test-user",
            "Explain RAG.",
            null,
            "speaking.webm",
            "audio/webm",
            45,
            "I prefer RAG because it can retrieve relevant company documents. For example, the model can use external context. As a result, the answer is more relevant.");

        Assert.Equal("Transcript-based local rubric", feedback.EvaluationMode);
        Assert.Contains("Pronunciation", feedback.Notice);
        Assert.InRange(feedback.WordsPerMinute, 1, 300);
    }

    [Fact]
    public async Task ContentEditing_CreatesRevisionAndReturnsLessonToDraft()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<AuditService>();
        var lesson = await db.Lessons
            .AsNoTracking()
            .Include(x => x.Module)
            .FirstAsync(x => x.Module.Code == "E11");
        var previousVersion = lesson.Version;

        await audit.UpdateLessonAsync(
            lesson.Id,
            "content-test-user",
            new LessonEditInput(
                lesson.Title,
                lesson.Objective,
                lesson.ThaiExplanation,
                lesson.GrammarFocus,
                $"{lesson.ReadingContent} Review the main idea before details.",
                lesson.ListeningTranscript,
                lesson.SpeakingPrompt,
                lesson.WritingPrompt,
                lesson.EstimatedMinutes,
                "Integration test revision"));

        var updated = await db.Lessons
            .AsNoTracking()
            .SingleAsync(x => x.Id == lesson.Id);
        var revisions = await db.ContentRevisions
            .AsNoTracking()
            .Where(x => x.LessonId == lesson.Id)
            .ToListAsync();
        var revision = revisions.OrderByDescending(x => x.CreatedAt).First();
        Assert.Equal(previousVersion + 1, updated.Version);
        Assert.Equal(ContentStatus.Draft, updated.Status);
        Assert.Equal("Integration test revision", revision.ChangeSummary);
    }

    [Fact]
    public async Task PersonalDataDeletion_RemovesIdentityAndLearningRecords()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var service = scope.ServiceProvider.GetRequiredService<PersonalDataService>();
        var user = new ApplicationUser
        {
            UserName = "privacy-test@example.test",
            Email = "privacy-test@example.test",
            EmailConfirmed = true
        };
        var created = await userManager.CreateAsync(user);
        Assert.True(created.Succeeded);

        db.LearnerProfiles.Add(new LearnerProfile { UserId = user.Id });
        db.WritingSubmissions.Add(new WritingSubmission
        {
            UserId = user.Id,
            Prompt = "Test prompt",
            Content = "Test content.",
            Score = 50,
            FeedbackJson = "{}"
        });
        db.SpeakingSubmissions.Add(new SpeakingSubmission
        {
            UserId = user.Id,
            Prompt = "Test prompt",
            Transcript = "Test transcript.",
            Score = 50,
            EvaluationMode = "Test",
            FeedbackJson = "{}"
        });
        await db.SaveChangesAsync();

        var result = await service.DeleteAccountDataAsync(user);

        Assert.True(result.Succeeded);
        Assert.Null(await userManager.FindByIdAsync(user.Id));
        Assert.False(await db.LearnerProfiles.AnyAsync(x => x.UserId == user.Id));
        Assert.False(await db.WritingSubmissions.AnyAsync(x => x.UserId == user.Id));
        Assert.False(await db.SpeakingSubmissions.AnyAsync(x => x.UserId == user.Id));
    }
}
