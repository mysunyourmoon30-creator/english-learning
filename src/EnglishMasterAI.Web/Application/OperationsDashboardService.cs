using EnglishMasterAI.Web.Configuration;
using EnglishMasterAI.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EnglishMasterAI.Web.Application;

public sealed class OperationsDashboardService(
    ApplicationDbContext db,
    AiUsageService usage,
    IOptions<AiOptions> ai,
    IOptions<PronunciationOptions> pronunciation,
    IOptions<ObservabilityOptions> observability,
    IOptions<AlertingOptions> alerting)
{
    public async Task<OperationsDashboard> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var canConnect = await db.Database.CanConnectAsync(cancellationToken);
        var aiSummary = await usage.GetSummaryAsync(30, cancellationToken);
        return new OperationsDashboard(
            canConnect,
            db.Database.ProviderName ?? "unknown",
            ai.Value.IsConfigured,
            pronunciation.Value.IsConfigured,
            !string.IsNullOrWhiteSpace(observability.Value.OtlpEndpoint),
            alerting.Value.IsConfigured,
            aiSummary);
    }
}

public sealed record OperationsDashboard(
    bool DatabaseHealthy,
    string DatabaseProvider,
    bool AiConfigured,
    bool PronunciationConfigured,
    bool OtlpConfigured,
    bool AlertingConfigured,
    AiOperationsSummary Ai);
