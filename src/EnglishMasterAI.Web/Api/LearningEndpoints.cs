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
            Results.Ok(ToToeicQuestionDtos(
                await assessment.GetQuestionsAsync(AssessmentKind.ToeicDiagnostic))));

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
            Results.Ok(ToToeicQuestionDtos(
                await assessment.GetQuestionsAsync(AssessmentKind.ToeicMock))));

        authorized.MapGet("/assessment/toeic/questions/{questionId:guid}/audio", async (
            ClaimsPrincipal user,
            Guid questionId,
            AssessmentService assessment,
            ReferenceAudioService referenceAudio,
            CancellationToken cancellationToken) =>
        {
            var question = await assessment.GetToeicListeningQuestionAsync(
                questionId,
                cancellationToken);
            if (question is null)
            {
                return Results.NotFound();
            }

            if (!referenceAudio.IsConfigured)
            {
                return Results.Problem(
                    title: "Reference audio is not configured.",
                    detail: "Configure AI__ApiKey to generate TOEIC Listening audio.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var audio = await referenceAudio.GetOrCreateAsync(
                RequiredUserId(user),
                ToeicListeningPresentation.BuildAudioText(question),
                cancellationToken);
            return Results.File(audio.Bytes, audio.ContentType);
        }).RequireRateLimiting("ai-practice");

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

    private static IReadOnlyList<ToeicQuestionDto> ToToeicQuestionDtos(
        IReadOnlyList<QuestionPrompt> questions) =>
        questions.Select(question =>
        {
            var isListening = ToeicListeningPresentation.IsListening(question);
            var hideOptions = ToeicListeningPresentation.HideOptionText(question);
            return new ToeicQuestionDto(
                question.Id,
                question.Skill,
                isListening
                    ? ToeicListeningPresentation.DisplayPrompt(question)
                    : question.Prompt,
                isListening ? null : question.SupportingText,
                hideOptions
                    ? question.Options
                        .Select((_, index) => $"Choice {(char)('A' + index)}")
                        .ToList()
                    : question.Options,
                question.ToeicPart,
                isListening
                    ? $"/api/v1/assessment/toeic/questions/{question.Id}/audio"
                    : null);
        }).ToList();

    public sealed record AssessmentSubmission(Dictionary<Guid, int> Answers, int DurationSeconds);
    public sealed record LessonQuizSubmission(Dictionary<Guid, int> Answers);
    public sealed record ToeicQuestionDto(
        Guid Id,
        string Skill,
        string Prompt,
        string? SupportingText,
        IReadOnlyList<string> Options,
        int? ToeicPart,
        string? AudioUrl);
}
