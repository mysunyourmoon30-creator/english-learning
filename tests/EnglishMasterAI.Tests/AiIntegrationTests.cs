using System.Net;
using System.Text.Json.Nodes;
using EnglishMasterAI.Web.Application;
using EnglishMasterAI.Web.Configuration;
using EnglishMasterAI.Web.Data;
using EnglishMasterAI.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnglishMasterAI.Tests;

public sealed class AiIntegrationTests
{
    [Fact]
    public void AiPracticeLimiter_EnforcesPerUserWindow()
    {
        var limiter = new AiPracticeLimiter(Options.Create(new SecurityOptions
        {
            AiRequestsPerMinute = 2
        }));
        var now = new DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

        Assert.True(limiter.TryAcquire("user-a", now));
        Assert.True(limiter.TryAcquire("user-a", now.AddSeconds(10)));
        Assert.False(limiter.TryAcquire("user-a", now.AddSeconds(20)));
        Assert.True(limiter.TryAcquire("user-b", now.AddSeconds(20)));
        Assert.True(limiter.TryAcquire("user-a", now.AddSeconds(61)));
    }

    [Fact]
    public async Task OpenAiGateway_ParsesStructuredOutputAndDisablesStorage()
    {
        var handler = new RecordingHandler(
            """
            {
              "output": [
                {
                  "type": "message",
                  "content": [
                    {
                      "type": "output_text",
                      "text": "{\"score\":88}"
                    }
                  ]
                }
              ]
            }
            """);
        var gateway = CreateGateway(handler);
        var schema = JsonNode.Parse(
            """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": { "score": { "type": "integer" } },
              "required": ["score"]
            }
            """)!.AsObject();

        var result = await gateway.CreateStructuredResponseAsync<ScoreResult>(
            "private-user-id",
            "Evaluate the sample.",
            "Learner sample",
            "score_result",
            schema);

        Assert.NotNull(result);
        Assert.Equal(88, result.Score);
        Assert.NotNull(handler.RequestBody);
        Assert.Contains("\"store\":false", handler.RequestBody);
        Assert.DoesNotContain("private-user-id", handler.RequestBody);
        Assert.Contains("\"safety_identifier\":", handler.RequestBody);
        Assert.Equal("Bearer", handler.AuthorizationScheme);
    }

    [Fact]
    public async Task OpenAiGateway_SendsAudioAsMultipartForm()
    {
        var handler = new RecordingHandler("""{"text":"The model retrieves relevant documents."}""");
        var gateway = CreateGateway(handler);

        var transcript = await gateway.TranscribeAsync(
            new byte[] { 1, 2, 3, 4 },
            "unsafe-name.exe",
            "application/octet-stream",
            CancellationToken.None);

        Assert.Equal("The model retrieves relevant documents.", transcript);
        Assert.Equal("multipart/form-data", handler.ContentType);
        Assert.Contains("name=file", handler.RequestBody);
        Assert.Contains("filename=speaking.webm", handler.RequestBody);
    }

    [Fact]
    public async Task OpenAiGateway_records_and_surfaces_provider_failure()
    {
        var gateway = CreateGateway(new ErrorHandler(HttpStatusCode.ServiceUnavailable));
        var schema = JsonNode.Parse(
            """
            {
              "type": "object",
              "additionalProperties": false,
              "properties": { "score": { "type": "integer" } },
              "required": ["score"]
            }
            """)!.AsObject();

        var exception = await Assert.ThrowsAsync<OpenAiGatewayException>(
            () => gateway.CreateStructuredResponseAsync<ScoreResult>(
                "failure-user",
                "Evaluate.",
                "Sample",
                "failure-test",
                schema));

        Assert.Contains("503", exception.Message);
    }

    private static OpenAiGateway CreateGateway(HttpMessageHandler handler)
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new ApplicationDbContext(dbOptions);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        var usage = new AiUsageService(
            db,
            Options.Create(new AiOptions()),
            NullLogger<AiUsageService>.Instance);
        var telemetry = new LearningTelemetry();
        var alerts = new OperationalAlertService(
            new HttpClient(new RecordingHandler("{}")),
            Options.Create(new AlertingOptions()),
            NullLogger<OperationalAlertService>.Instance);

        return new OpenAiGateway(
            new HttpClient(handler),
            Options.Create(new AiOptions
            {
                Enabled = true,
                Provider = "OpenAI",
                ApiKey = "test-key",
                BaseUrl = "https://api.openai.com/v1/",
                FeedbackModel = "gpt-5.6-luna",
                TranscriptionModel = "gpt-4o-mini-transcribe"
            }),
            usage,
            telemetry,
            alerts,
            NullLogger<OpenAiGateway>.Instance);
    }

    private sealed class ScoreResult
    {
        public int Score { get; set; }
    }

    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? ContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            };
        }
    }

    private sealed class ErrorHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("""{"error":{"message":"simulated"}}""")
            });
    }
}
