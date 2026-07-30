using EnglishMasterAI.Web.Configuration;
using Microsoft.Extensions.Options;

namespace EnglishMasterAI.Web.Infrastructure;

public sealed class StartupSecurityValidator(
    IWebHostEnvironment environment,
    IConfiguration configuration,
    IOptions<EmailOptions> emailOptions,
    IOptions<DatabaseOptions> databaseOptions,
    IOptions<MultiInstanceOptions> multiInstanceOptions,
    IOptions<AudioStorageOptions> audioStorageOptions,
    IOptions<ToeicMediaOptions> toeicMediaOptions,
    IOptions<ProxyOptions> proxyOptions,
    IOptions<AiOptions> aiOptions,
    IOptions<PronunciationOptions> pronunciationOptions,
    IOptions<ObservabilityOptions> observabilityOptions,
    IOptions<AlertingOptions> alertingOptions,
    IOptions<LegalOptions> legalOptions,
    ILogger<StartupSecurityValidator> logger)
{
    public void Validate(bool migrationJob = false)
    {
        if (environment.IsDevelopment())
        {
            return;
        }

        if (!migrationJob
            && (databaseOptions.Value.ApplyMigrationsOnStartup
                || databaseOptions.Value.SeedOnStartup))
        {
            throw new InvalidOperationException(
                "Automatic migration and seed are disabled outside Development. "
                + "Run the one-off '--migrate-and-seed' job before starting application instances.");
        }

        if (!databaseOptions.Value.IsPostgreSql
            && !databaseOptions.Value.AllowSqliteInProduction)
        {
            throw new InvalidOperationException(
                "Production must use PostgreSQL unless Database:AllowSqliteInProduction is explicitly enabled.");
        }

        if (multiInstanceOptions.Value.Enabled)
        {
            var redisConnection = configuration.GetConnectionString(
                multiInstanceOptions.Value.RedisConnectionStringName);
            if (string.IsNullOrWhiteSpace(redisConnection))
            {
                throw new InvalidOperationException(
                    "Multi-instance mode requires a shared Redis connection.");
            }
        }

        if (configuration.GetValue("SeedAdmin:Enabled", false))
        {
            throw new InvalidOperationException(
                "SeedAdmin must be disabled outside Development.");
        }

        if (migrationJob)
        {
            logger.LogInformation(
                "One-off database migration job configuration passed startup validation.");
            return;
        }

        if (environment.IsProduction()
            && (!proxyOptions.Value.Enabled
                || !proxyOptions.Value.HasTrustedForwarder))
        {
            throw new InvalidOperationException(
                "Production requires Proxy:Enabled=true and at least one trusted "
                + "Proxy:KnownProxies or Proxy:KnownNetworks entry.");
        }

        if (environment.IsProduction()
            && multiInstanceOptions.Value.Enabled
            && !audioStorageOptions.Value.IsAzureBlob)
        {
            throw new InvalidOperationException(
                "Production multi-instance mode requires AudioStorage:Provider=AzureBlob.");
        }

        if (environment.IsProduction()
            && audioStorageOptions.Value.IsAzureBlob
            && string.IsNullOrWhiteSpace(configuration.GetConnectionString(
                audioStorageOptions.Value.ConnectionStringName)))
        {
            throw new InvalidOperationException(
                $"AudioStorage:Provider=AzureBlob requires ConnectionStrings:"
                + audioStorageOptions.Value.ConnectionStringName + ".");
        }

        if (environment.IsProduction()
            && (!toeicMediaOptions.Value.RequireApprovedHumanAudio
                || toeicMediaOptions.Value.AllowAiGeneratedFallback))
        {
            throw new InvalidOperationException(
                "Production must require approved human TOEIC audio and disable AI-generated fallback.");
        }

        if (environment.IsProduction() && !aiOptions.Value.IsConfigured)
        {
            throw new InvalidOperationException(
                "Production requires AI:Enabled=true and a non-empty AI:ApiKey "
                + "from the deployment secret manager.");
        }

        if (environment.IsProduction() && !aiOptions.Value.HasPricing)
        {
            throw new InvalidOperationException(
                "Production requires non-zero AI pricing configuration so usage "
                + "and cost ceilings are meaningful.");
        }

        if (environment.IsProduction()
            && pronunciationOptions.Value.Enabled
            && !pronunciationOptions.Value.IsConfigured)
        {
            throw new InvalidOperationException(
                "Enabled pronunciation assessment requires Azure Speech key and region.");
        }

        if (environment.IsProduction()
            && (!Uri.TryCreate(
                    observabilityOptions.Value.OtlpEndpoint,
                    UriKind.Absolute,
                    out var otlpEndpoint)
                || (otlpEndpoint.Scheme != Uri.UriSchemeHttp
                    && otlpEndpoint.Scheme != Uri.UriSchemeHttps)))
        {
            throw new InvalidOperationException(
                "Production requires an absolute HTTP(S) Observability:OtlpEndpoint.");
        }

        if (environment.IsProduction() && !alertingOptions.Value.IsConfigured)
        {
            throw new InvalidOperationException(
                "Production requires Alerting:Enabled=true and an HTTPS webhook URL.");
        }

        if (environment.IsProduction() && !legalOptions.Value.IsConfigured)
        {
            throw new InvalidOperationException(
                "Production requires the legal operator, privacy contact, backup "
                + "retention, and telemetry retention under the Legal section.");
        }

        var allowedHosts = configuration["AllowedHosts"];
        if (string.IsNullOrWhiteSpace(allowedHosts) || allowedHosts.Trim() == "*")
        {
            throw new InvalidOperationException(
                "AllowedHosts must be explicitly configured outside Development.");
        }

        var requireConfirmed = configuration.GetValue<bool?>(
            $"{SecurityOptions.SectionName}:RequireConfirmedAccount") ?? true;
        if (requireConfirmed && !emailOptions.Value.IsConfigured)
        {
            throw new InvalidOperationException(
                "Email SMTP settings are required when confirmed accounts are enabled.");
        }

        logger.LogInformation("Production security configuration passed startup validation.");
    }
}
