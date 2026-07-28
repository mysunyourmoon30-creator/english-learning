using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace EnglishMasterAI.Web.Infrastructure;

public sealed class RedisHealthCheck(
    IConnectionMultiplexer redis) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var latency = await redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy(
                $"Redis connection is available ({latency.TotalMilliseconds:F1} ms).");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy(
                "Redis connection is unavailable.",
                exception);
        }
    }
}
