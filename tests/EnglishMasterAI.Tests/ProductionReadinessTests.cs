using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using EnglishMasterAI.Web.Application;
using EnglishMasterAI.Web.Data;
using EnglishMasterAI.Web.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace EnglishMasterAI.Tests;

[Collection(EnglishMasterWebCollection.Name)]
public sealed class ProductionReadinessTests(EnglishMasterWebFactory factory)
{
    [Fact]
    public async Task Forwarded_scheme_and_client_ip_are_used_only_from_trusted_proxy()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Add(IPAddress.Loopback);
        });
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (context.Request.Headers.TryGetValue(
                    "X-Test-Remote-IP",
                    out var value)
                && IPAddress.TryParse(value, out var address))
            {
                context.Connection.RemoteIpAddress = address;
            }
            await next();
        });
        app.UseForwardedHeaders();
        app.MapGet(
            "/observed-request",
            (HttpContext context) =>
                $"{context.Request.Scheme}|{context.Connection.RemoteIpAddress}");
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var trusted = new HttpRequestMessage(HttpMethod.Get, "/observed-request");
        trusted.Headers.Add("X-Test-Remote-IP", "127.0.0.1");
        trusted.Headers.Add("X-Forwarded-Proto", "https");
        trusted.Headers.Add("X-Forwarded-For", "203.0.113.25");

        using var trustedResponse = await client.SendAsync(trusted);

        Assert.Equal(HttpStatusCode.OK, trustedResponse.StatusCode);
        Assert.Equal(
            "https|203.0.113.25",
            await trustedResponse.Content.ReadAsStringAsync());

        using var untrusted = new HttpRequestMessage(HttpMethod.Get, "/observed-request");
        untrusted.Headers.Add("X-Test-Remote-IP", "127.0.0.2");
        untrusted.Headers.Add("X-Forwarded-Proto", "https");
        untrusted.Headers.Add("X-Forwarded-For", "198.51.100.4");

        using var untrustedResponse = await client.SendAsync(untrusted);

        Assert.Equal(HttpStatusCode.OK, untrustedResponse.StatusCode);
        Assert.Equal(
            "http|127.0.0.2",
            await untrustedResponse.Content.ReadAsStringAsync());
    }

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
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self' 'nonce-", csp);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", csp);
    }

    [Fact]
    public async Task Public_legal_pages_share_the_csp_nonce_and_template_routes_are_gone()
    {
        using var client = factory.CreateHttpsClient();

        using var privacy = await client.GetAsync("/privacy");
        var privacyHtml = await privacy.Content.ReadAsStringAsync();
        var csp = Assert.Single(
            privacy.Headers.GetValues("Content-Security-Policy"),
            value => value.Contains("script-src", StringComparison.Ordinal));
        var nonce = Regex.Match(csp, @"'nonce-([^']+)'").Groups[1].Value;

        Assert.Equal(HttpStatusCode.OK, privacy.StatusCode);
        Assert.Contains("นโยบายความเป็นส่วนตัว", privacyHtml);
        Assert.False(string.IsNullOrWhiteSpace(nonce));

        using var terms = await client.GetAsync("/terms");
        Assert.Equal(HttpStatusCode.OK, terms.StatusCode);
        Assert.Contains(
            "ข้อกำหนดการใช้งาน",
            await terms.Content.ReadAsStringAsync());

        foreach (var path in new[] { "/weather", "/counter", "/auth" })
        {
            using var removed = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        }

        using var css = await client.GetAsync("/app.css");
        var cssBody = await css.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.DoesNotContain("fonts.googleapis.com", cssBody);
        Assert.Contains("/fonts/IBMPlexSansThai-Regular.ttf", cssBody);
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
        Assert.Equal(JsonValueKind.Null, first.GetProperty("supportingText").ValueKind);
        Assert.StartsWith(
            "/api/v1/assessment/toeic/questions/",
            first.GetProperty("audioUrl").GetString());
        Assert.All(
            document.RootElement.EnumerateArray()
                .Where(x => x.GetProperty("toeicPart").GetInt32() <= 4),
            question => Assert.Equal(
                JsonValueKind.Null,
                question.GetProperty("supportingText").ValueKind));
    }

    [Fact]
    public async Task ToeicAudioEndpoint_FailsClosedWhenSpeechProviderIsNotConfigured()
    {
        using var client = factory.CreateHttpsClient(authenticated: true);
        using var questions = await client.GetAsync(
            "/api/v1/assessment/toeic/mock/questions");
        using var document = JsonDocument.Parse(
            await questions.Content.ReadAsStringAsync());
        var audioUrl = document.RootElement[0]
            .GetProperty("audioUrl")
            .GetString();

        using var response = await client.GetAsync(audioUrl);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
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
    public async Task LearningActivity_IsIdempotent_AndUpdatesWeeklyProgressAndAchievements()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var engagement = scope.ServiceProvider.GetRequiredService<LearningEngagementService>();
        var userId = $"engagement-{Guid.NewGuid():N}";
        db.LearnerProfiles.Add(new LearnerProfile
        {
            UserId = userId,
            TimeZoneId = "Asia/Bangkok"
        });
        await db.SaveChangesAsync();

        await engagement.RecordAsync(userId, "lesson", "lesson:AI01:attempt:1", 20, 80);
        await engagement.RecordAsync(userId, "lesson", "lesson:AI01:attempt:1", 20, 80);

        var profile = await db.LearnerProfiles
            .AsNoTracking()
            .SingleAsync(x => x.UserId == userId);
        var activities = await db.LearningActivities
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .ToListAsync();
        var achievements = await engagement.GetAchievementsAsync(userId);
        var weekly = await engagement.GetWeeklyProgressAsync(userId);

        Assert.Single(activities);
        Assert.Equal(1, profile.CurrentStreak);
        Assert.Equal(20, weekly.Minutes);
        Assert.Equal(80, weekly.Points);
        Assert.Equal(1, weekly.LessonsCompleted);
        Assert.Contains(achievements, x => x.Code == "first-step");
        Assert.Contains(achievements, x => x.Code == "lesson-hero");
    }

    [Fact]
    public async Task AiUsage_StoresOnlyOperationalMetadata_AndBuildsSummary()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var usage = scope.ServiceProvider.GetRequiredService<AiUsageService>();
        var userId = $"usage-{Guid.NewGuid():N}";

        await usage.RecordAsync(
            userId,
            "writing",
            "test-model",
            "Succeeded",
            inputUnits: 100,
            inputTokens: 30,
            outputTokens: 20,
            durationMilliseconds: 125,
            requestId: "request-test");

        var record = await db.AiUsageRecords
            .AsNoTracking()
            .SingleAsync(x => x.UserId == userId);
        var summary = await usage.GetSummaryAsync();

        Assert.Equal("writing", record.Operation);
        Assert.Equal(30, record.InputTokens);
        Assert.Equal(20, record.OutputTokens);
        Assert.True(summary.TotalRequests >= 1);
        Assert.True(summary.InputTokens >= 30);
        Assert.True(summary.OutputTokens >= 20);
        Assert.True(summary.PricingConfigured);
        Assert.True(summary.EstimatedCostUsd >= 0.00014);
    }

    [Fact]
    public async Task AiEnglishPublishing_RequiresBothHumanReviewRoles()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<AuditService>();
        var lesson = await db.Lessons
            .Include(x => x.Module)
            .FirstAsync(x => x.Module.Code == "AI24");
        var assignments = await db.ContentReviewAssignments
            .Where(x => x.LessonId == lesson.Id)
            .ToListAsync();
        var originalLessonStatus = lesson.Status;
        var originalReviewStates = assignments
            .Select(x => (x.Id, x.Status, x.ReviewerUserId, x.ReviewedAt))
            .ToDictionary(x => x.Id);

        try
        {
            Assert.Equal(2, assignments.Count);
            lesson.Status = ContentStatus.Approved;
            foreach (var assignment in assignments)
            {
                assignment.Status = ContentReviewStatus.Pending;
                assignment.ReviewerUserId = null;
                assignment.ReviewedAt = null;
            }
            await db.SaveChangesAsync();

            var rejection = await Assert.ThrowsAsync<InvalidOperationException>(
                () => audit.ChangeContentStatusAsync(
                    lesson.Id,
                    ContentStatus.Published,
                    "quality-gate-test"));
            Assert.Contains("require approved", rejection.Message);

            foreach (var assignment in assignments)
            {
                assignment.Status = ContentReviewStatus.Approved;
                assignment.ReviewerUserId = "human-reviewer";
                assignment.ReviewedAt = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync();

            await audit.ChangeContentStatusAsync(
                lesson.Id,
                ContentStatus.Published,
                "quality-gate-test");

            Assert.Equal(
                ContentStatus.Published,
                (await db.Lessons.FindAsync(lesson.Id))!.Status);
        }
        finally
        {
            lesson.Status = originalLessonStatus;
            foreach (var assignment in assignments)
            {
                var original = originalReviewStates[assignment.Id];
                assignment.Status = original.Status;
                assignment.ReviewerUserId = original.ReviewerUserId;
                assignment.ReviewedAt = original.ReviewedAt;
            }
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task AiLessons_HaveLanguageAndSubjectMatterReviewAssignments()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var assignments = await db.ContentReviewAssignments
            .AsNoTracking()
            .Include(x => x.Lesson)
            .ThenInclude(x => x.Module)
            .Where(x => x.Lesson.Module.Category == LearningCategory.AiEnglish)
            .ToListAsync();

        Assert.Equal(48, assignments.Count);
        Assert.Equal(
            24,
            assignments.Select(x => x.LessonId).Distinct().Count());
        Assert.All(
            assignments.GroupBy(x => x.LessonId),
            group =>
            {
                Assert.Equal(2, group.Count());
                Assert.Contains(group, x => x.ReviewerRole == "English Reviewer");
                Assert.Contains(group, x => x.ReviewerRole == "AI Subject Matter Expert");
            });
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
