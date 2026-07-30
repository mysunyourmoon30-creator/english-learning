using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EnglishMasterAI.Tests;

public sealed class EnglishMasterWebFactory : WebApplicationFactory<Program>
{
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        "EnglishMasterAI.Tests",
        Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_testRoot);
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    $"DataSource={Path.Combine(_testRoot, "integration.db")}",
                ["DataProtection:KeysPath"] = Path.Combine(_testRoot, "keys"),
                ["SeedAdmin:Enabled"] = "false",
                ["AI:Enabled"] = "false",
                ["AI:InputTokenUsdPerMillion"] = "2",
                ["AI:OutputTokenUsdPerMillion"] = "4",
                ["AI:SpeechUsdPerMillionCharacters"] = "10",
                ["AI:TranscriptionUsdPerMinute"] = "0.01",
                ["Security:RequireConfirmedAccount"] = "false"
            });
        });
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });
        });
    }

    public HttpClient CreateHttpsClient(bool authenticated = false)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
        if (authenticated)
        {
            client.DefaultRequestHeaders.Add("X-Test-User", "integration-user");
        }
        return client;
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "IntegrationTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var userId)
                || string.IsNullOrWhiteSpace(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Name, "Integration User"),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim(ClaimTypes.Role, "ContentAdmin")
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            var principal = new ClaimsPrincipal(identity);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(principal, SchemeName)));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return Task.CompletedTask;
        }

        protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = (int)HttpStatusCode.Forbidden;
            return Task.CompletedTask;
        }
    }

}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EnglishMasterWebCollection : ICollectionFixture<EnglishMasterWebFactory>
{
    public const string Name = "EnglishMaster Web";
}
