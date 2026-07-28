using System.Net;
using EnglishMasterAI.Web.Configuration;
using EnglishMasterAI.Web.Data;
using EnglishMasterAI.Web.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnglishMasterAI.Tests;

public sealed class InfrastructureHardeningTests
{
    [Fact]
    public async Task Operational_alert_posts_once_during_cooldown()
    {
        var handler = new RecordingHandler(HttpStatusCode.Accepted);
        var service = new OperationalAlertService(
            new HttpClient(handler),
            Options.Create(new AlertingOptions
            {
                Enabled = true,
                WebhookUrl = "https://alerts.example.test/operations",
                CooldownMinutes = 15
            }),
            NullLogger<OperationalAlertService>.Instance);

        await service.TrySendAsync("database", "Database failed", "timeout");
        await service.TrySendAsync("database", "Database failed", "timeout");

        Assert.Equal(1, handler.RequestCount);
        Assert.Contains("\"key\":\"database\"", handler.LastBody);
    }

    [Fact]
    public async Task Operational_alert_does_not_throw_when_webhook_fails()
    {
        var service = new OperationalAlertService(
            new HttpClient(new ThrowingHandler()),
            Options.Create(new AlertingOptions
            {
                Enabled = true,
                WebhookUrl = "https://alerts.example.test/operations"
            }),
            NullLogger<OperationalAlertService>.Instance);

        await service.TrySendAsync("ai", "AI failed", "provider timeout");
    }

    [Fact]
    public async Task Smtp_sender_alerts_and_fails_when_not_configured()
    {
        var alertHandler = new RecordingHandler(HttpStatusCode.OK);
        var alerts = new OperationalAlertService(
            new HttpClient(alertHandler),
            Options.Create(new AlertingOptions
            {
                Enabled = true,
                WebhookUrl = "https://alerts.example.test/operations"
            }),
            NullLogger<OperationalAlertService>.Instance);
        var sender = new SmtpEmailSender(
            Options.Create(new EmailOptions()),
            alerts,
            NullLogger<SmtpEmailSender>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sender.SendConfirmationLinkAsync(
                new ApplicationUser(),
                "learner@example.test",
                "https://example.test/confirm?a=<unsafe>"));

        Assert.Equal(1, alertHandler.RequestCount);
        Assert.Contains("smtp-not-configured", alertHandler.LastBody);
    }

    [Fact]
    public async Task Distributed_rate_limit_middleware_selects_policy_and_returns_429()
    {
        var nextCalls = 0;
        var middleware = new DistributedRateLimitMiddleware(
            _ =>
            {
                nextCalls++;
                return Task.CompletedTask;
            },
            Options.Create(new SecurityOptions
            {
                ApiRequestsPerMinute = 20,
                AiRequestsPerMinute = 3,
                LoginRequestsPerMinute = 4
            }));
        var gate = new RecordingRateLimitGate(allow: false);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/assessment/toeic/questions/id/audio";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context, gate);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal("ai", gate.Bucket);
        Assert.Equal(3, gate.PermitLimit);
        Assert.Equal(0, nextCalls);

        var passThrough = new DefaultHttpContext();
        passThrough.Request.Path = "/dashboard";
        await middleware.InvokeAsync(passThrough, gate);
        Assert.Equal(1, nextCalls);
    }

    [Fact]
    public async Task Distributed_rate_limit_middleware_allows_account_request()
    {
        var nextCalls = 0;
        var middleware = new DistributedRateLimitMiddleware(
            _ =>
            {
                nextCalls++;
                return Task.CompletedTask;
            },
            Options.Create(new SecurityOptions
            {
                LoginRequestsPerMinute = 7
            }));
        var gate = new RecordingRateLimitGate(allow: true);
        var context = new DefaultHttpContext();
        context.Request.Path = "/Account/Login";

        await middleware.InvokeAsync(context, gate);

        Assert.Equal("account", gate.Bucket);
        Assert.Equal(7, gate.PermitLimit);
        Assert.Equal(1, nextCalls);
    }

    [Fact]
    public void Startup_validator_blocks_automatic_migration_outside_development()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "english.example.test",
            ["Database:Provider"] = "PostgreSql",
            ["Database:ApplyMigrationsOnStartup"] = "true",
            ["Database:SeedOnStartup"] = "false",
            ["SeedAdmin:Enabled"] = "false"
        });
        var validator = Validator(configuration);

        var exception = Assert.Throws<InvalidOperationException>(
            () => validator.Validate());

        Assert.Contains("Automatic migration", exception.Message);
    }

    [Fact]
    public void Startup_validator_allows_explicit_postgresql_migration_job()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:ApplyMigrationsOnStartup"] = "false",
            ["Database:SeedOnStartup"] = "false",
            ["SeedAdmin:Enabled"] = "false"
        });
        var validator = Validator(configuration);

        validator.Validate(migrationJob: true);
    }

    [Fact]
    public void Startup_validator_requires_blob_connection_for_azure_audio()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "english.example.test",
            ["Database:Provider"] = "PostgreSql",
            ["Database:ApplyMigrationsOnStartup"] = "false",
            ["Database:SeedOnStartup"] = "false",
            ["SeedAdmin:Enabled"] = "false"
        });
        var validator = Validator(
            configuration,
            new AudioStorageOptions { Provider = "AzureBlob" });

        var exception = Assert.Throws<InvalidOperationException>(
            () => validator.Validate());

        Assert.Contains("AudioBlobStorage", exception.Message);
    }

    private static StartupSecurityValidator Validator(
        IConfiguration configuration,
        AudioStorageOptions? audioStorage = null)
    {
        var database = configuration
            .GetSection(DatabaseOptions.SectionName)
            .Get<DatabaseOptions>() ?? new DatabaseOptions();
        return new StartupSecurityValidator(
            new TestEnvironment { EnvironmentName = "Production" },
            configuration,
            Options.Create(new EmailOptions()),
            Options.Create(database),
            Options.Create(new MultiInstanceOptions()),
            Options.Create(audioStorage ?? new AudioStorageOptions()),
            Options.Create(new ToeicMediaOptions
            {
                RequireApprovedHumanAudio = true,
                AllowAiGeneratedFallback = false
            }),
            NullLogger<StartupSecurityValidator>.Instance);
    }

    private static IConfiguration Configuration(
        IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private sealed class RecordingRateLimitGate(bool allow)
        : IDistributedRateLimitGate
    {
        public string Bucket { get; private set; } = string.Empty;
        public int PermitLimit { get; private set; }

        public Task<bool> TryAcquireAsync(
            string bucket,
            string partition,
            int permitLimit,
            CancellationToken cancellationToken = default)
        {
            Bucket = bucket;
            PermitLimit = permitLimit;
            return Task.FromResult(allow);
        }
    }

    private sealed class RecordingHandler(HttpStatusCode statusCode)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode);
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new HttpRequestException("simulated failure");
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "EnglishMasterAI.Tests";
        public IFileProvider WebRootFileProvider { get; set; } =
            new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
