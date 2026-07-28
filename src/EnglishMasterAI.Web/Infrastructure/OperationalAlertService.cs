using System.Collections.Concurrent;
using System.Net.Http.Json;
using EnglishMasterAI.Web.Configuration;
using Microsoft.Extensions.Options;

namespace EnglishMasterAI.Web.Infrastructure;

public sealed class OperationalAlertService(
    HttpClient httpClient,
    IOptions<AlertingOptions> options,
    ILogger<OperationalAlertService> logger)
{
    private readonly AlertingOptions _options = options.Value;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSent = new();

    public async Task TrySendAsync(
        string key,
        string title,
        string detail,
        string severity = "error",
        CancellationToken cancellationToken = default)
    {
        logger.Log(
            severity.Equals("warning", StringComparison.OrdinalIgnoreCase)
                ? LogLevel.Warning
                : LogLevel.Error,
            "Operational alert {AlertKey}: {AlertTitle} - {AlertDetail}",
            key,
            title,
            detail);

        if (!_options.IsConfigured)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var cooldown = TimeSpan.FromMinutes(
            Math.Clamp(_options.CooldownMinutes, 1, 1_440));
        if (_lastSent.TryGetValue(key, out var lastSent)
            && now - lastSent < cooldown)
        {
            return;
        }

        _lastSent[key] = now;
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                _options.WebhookUrl,
                new
                {
                    service = "EnglishMasterAI",
                    key,
                    severity,
                    title,
                    detail,
                    timestamp = now
                },
                cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(
                exception,
                "Could not deliver operational alert {AlertKey}.",
                key);
        }
    }
}
