using EnglishMasterAI.Web.Configuration;
using Microsoft.Extensions.Options;

namespace EnglishMasterAI.Web.Infrastructure;

public sealed class StartupSecurityValidator(
    IWebHostEnvironment environment,
    IConfiguration configuration,
    IOptions<EmailOptions> emailOptions,
    ILogger<StartupSecurityValidator> logger)
{
    public void Validate()
    {
        if (environment.IsDevelopment())
        {
            return;
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

        if (configuration.GetValue("SeedAdmin:Enabled", false))
        {
            throw new InvalidOperationException(
                "SeedAdmin must be disabled outside Development.");
        }

        logger.LogInformation("Production security configuration passed startup validation.");
    }
}
