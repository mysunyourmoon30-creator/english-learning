using System.Security.Claims;
using EnglishMasterAI.Web.Application;
using EnglishMasterAI.Web.Domain;

namespace EnglishMasterAI.Web.Api;

public static class LearningEndpoints
{
    public static IEndpointRouteBuilder MapLearningApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/v1");
        api.MapGet("/health", () => Results.Ok(new
        {
            status = "healthy",
            service = "EnglishMaster AI",
            timestamp = DateTimeOffset.UtcNow
        })).AllowAnonymous();

        var authorized = api.MapGroup(string.Empty)
            .RequireAuthorization()
            .RequireRateLimiting("learning-api");

        authorized.MapGet("/catalog/modules", async (ClaimsPrincipal user, LearningService learning) =>
        {
            var userId = RequiredUserId(user);
            return Results.Ok(await learning.GetModulesAsync(userId));
        });

        authorized.MapGet("/assessment/placement/questions", async (AssessmentService assessment) =>
            Results.Ok(await assessment.GetQuestionsAsync(AssessmentKind.Placement)));

        authorized.MapPost("/assessment/placement/submit", async (
            ClaimsPrincipal user,
            AssessmentSubmission submission,
            AssessmentService assessment) =>
        {
            var result = await assessment.SubmitAsync(
                RequiredUserId(user),
                AssessmentKind.Placement,
                submission.Answers,
                submission.DurationSeconds);
            return Results.Ok(result);
        });

        authorized.MapGet("/assessment/toeic/questions", async (AssessmentService assessment) =>
            Results.Ok(await assessment.GetQuestionsAsync(AssessmentKind.ToeicDiagnostic)));

        authorized.MapPost("/assessment/toeic/submit", async (
            ClaimsPrincipal user,
            AssessmentSubmission submission,
            AssessmentService assessment) =>
        {
            var result = await assessment.SubmitAsync(
                RequiredUserId(user),
                AssessmentKind.ToeicDiagnostic,
                submission.Answers,
                submission.DurationSeconds);
            return Results.Ok(result);
        });

        authorized.MapGet("/assessment/toeic/mock/questions", async (AssessmentService assessment) =>
            Results.Ok(await assessment.GetQuestionsAsync(AssessmentKind.ToeicMock)));

        authorized.MapPost("/assessment/toeic/mock/submit", async (
            ClaimsPrincipal user,
            AssessmentSubmission submission,
            AssessmentService assessment) =>
        {
            var result = await assessment.SubmitAsync(
                RequiredUserId(user),
                AssessmentKind.ToeicMock,
                submission.Answers,
                submission.DurationSeconds);
            return Results.Ok(result);
        });

        authorized.MapPost("/lessons/{lessonId:guid}/complete", async (
            ClaimsPrincipal user,
            Guid lessonId,
            LessonQuizSubmission submission,
            LearningService learning) =>
        {
            var result = await learning.SubmitLessonQuizAsync(RequiredUserId(user), lessonId, submission.Answers);
            return Results.Ok(result);
        });

        return endpoints;
    }

    private static string RequiredUserId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Authenticated user id is missing.");

    public sealed record AssessmentSubmission(Dictionary<Guid, int> Answers, int DurationSeconds);
    public sealed record LessonQuizSubmission(Dictionary<Guid, int> Answers);
}
