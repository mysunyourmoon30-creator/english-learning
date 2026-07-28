using EnglishMasterAI.Web.Data;
using EnglishMasterAI.Web.Domain;
using EnglishMasterAI.Web.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EnglishMasterAI.Web.Application;

public sealed class AiUsageService(
    ApplicationDbContext db,
    IOptions<AiOptions> options,
    ILogger<AiUsageService> logger)
{
    private readonly AiOptions _options = options.Value;

    public async Task RecordAsync(
        string userId,
        string operation,
        string model,
        string status,
        int inputUnits,
        int inputTokens,
        int outputTokens,
        int durationMilliseconds,
        string? requestId = null,
        string? failureType = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            db.AiUsageRecords.Add(new AiUsageRecord
            {
                UserId = userId,
                Operation = Truncate(operation, 40),
                Provider = "OpenAI",
                Model = Truncate(model, 80),
                Status = Truncate(status, 20),
                InputUnits = Math.Max(0, inputUnits),
                InputTokens = Math.Max(0, inputTokens),
                OutputTokens = Math.Max(0, outputTokens),
                DurationMilliseconds = Math.Max(0, durationMilliseconds),
                RequestId = TruncateNullable(requestId, 100),
                FailureType = TruncateNullable(failureType, 120)
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (
            exception is DbUpdateException or InvalidOperationException)
        {
            logger.LogWarning(
                exception,
                "Could not persist privacy-safe AI usage metadata.");
        }
    }

    public Task RecordFallbackAsync(
        string userId,
        string operation,
        string reason,
        CancellationToken cancellationToken = default) =>
        RecordAsync(
            userId,
            operation,
            "local-rules",
            "Fallback",
            0,
            0,
            0,
            0,
            failureType: reason,
            cancellationToken: cancellationToken);

    public async Task<AiOperationsSummary> GetSummaryAsync(
        int days = 30,
        CancellationToken cancellationToken = default)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days, 1, 365));
        var query = db.AiUsageRecords.AsNoTracking();
        var records = db.Database.IsSqlite()
            ? (await query.ToListAsync(cancellationToken))
                .Where(x => x.CreatedAt >= since)
                .ToList()
            : await query
                .Where(x => x.CreatedAt >= since)
                .ToListAsync(cancellationToken);

        return new AiOperationsSummary(
            records.Count,
            records.Count(x => x.Status == "Succeeded"),
            records.Count(x => x.Status == "Failed"),
            records.Count(x => x.Status == "Fallback"),
            records.Sum(x => x.InputTokens),
            records.Sum(x => x.OutputTokens),
            records.Count == 0
                ? 0
                : (int)Math.Round(records.Average(x => x.DurationMilliseconds)),
            Math.Round(records.Sum(EstimateCostUsd), 6),
            _options.HasPricing,
            records
                .GroupBy(x => new { x.Operation, x.Model })
                .Select(group => new AiOperationBreakdown(
                    group.Key.Operation,
                    group.Key.Model,
                    group.Count(),
                    group.Count(x => x.Status == "Failed"),
                    group.Count(x => x.Status == "Fallback"),
                    group.Sum(x => x.InputTokens),
                    group.Sum(x => x.OutputTokens),
                    Math.Round(group.Sum(EstimateCostUsd), 6)))
                .OrderByDescending(x => x.Requests)
                .ToList());
    }

    private static string Truncate(string value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
        return normalized[..Math.Min(normalized.Length, maxLength)];
    }

    private static string? TruncateNullable(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];

    private double EstimateCostUsd(AiUsageRecord record)
    {
        if (record.Operation == "speech-generation")
        {
            return record.InputUnits / 1_000_000d
                * Math.Max(0, _options.SpeechUsdPerMillionCharacters);
        }

        if (record.Operation == "transcription")
        {
            return record.InputUnits / 60d
                * Math.Max(0, _options.TranscriptionUsdPerMinute);
        }

        return record.InputTokens / 1_000_000d
                * Math.Max(0, _options.InputTokenUsdPerMillion)
            + record.OutputTokens / 1_000_000d
                * Math.Max(0, _options.OutputTokenUsdPerMillion);
    }
}

public sealed record AiOperationsSummary(
    int TotalRequests,
    int Succeeded,
    int Failed,
    int Fallbacks,
    int InputTokens,
    int OutputTokens,
    int AverageDurationMilliseconds,
    double EstimatedCostUsd,
    bool PricingConfigured,
    IReadOnlyList<AiOperationBreakdown> Operations);

public sealed record AiOperationBreakdown(
    string Operation,
    string Model,
    int Requests,
    int Failed,
    int Fallbacks,
    int InputTokens,
    int OutputTokens,
    double EstimatedCostUsd);
