using System.Security.Claims;
using EnglishMasterAI.Web.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EnglishMasterAI.Web.Infrastructure;

public interface IDistributedRateLimitGate
{
    Task<bool> TryAcquireAsync(
        string bucket,
        string partition,
        int permitLimit,
        CancellationToken cancellationToken = default);
}

public sealed class RedisDistributedRateLimitGate(
    IConnectionMultiplexer redis,
    IOptions<MultiInstanceOptions> options) : IDistributedRateLimitGate
{
    private const string FixedWindowScript = """
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then
            redis.call('PEXPIRE', KEYS[1], ARGV[1])
        end
        return current
        """;

    private readonly MultiInstanceOptions _options = options.Value;

    public async Task<bool> TryAcquireAsync(
        string bucket,
        string partition,
        int permitLimit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var minute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var safePartition = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(partition)))
            .ToLowerInvariant();
        var key = new RedisKey(
            $"{_options.RateLimitKeyPrefix}:{bucket}:{safePartition}:{minute}");
        var result = await redis.GetDatabase().ScriptEvaluateAsync(
            FixedWindowScript,
            [key],
            [60_000]);
        return (long)result <= permitLimit;
    }
}

public sealed class DistributedRateLimitMiddleware(
    RequestDelegate next,
    IOptions<SecurityOptions> securityOptions)
{
    private readonly SecurityOptions _security = securityOptions.Value;

    public async Task InvokeAsync(
        HttpContext context,
        IDistributedRateLimitGate gate)
    {
        var policy = ResolvePolicy(context.Request.Path);
        if (policy is null)
        {
            await next(context);
            return;
        }

        var partition = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
        if (await gate.TryAcquireAsync(
                policy.Value.Bucket,
                partition,
                policy.Value.PermitLimit,
                context.RequestAborted))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.ContentType = "application/problem+json";
        context.Response.Headers.RetryAfter = "60";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://httpstatuses.com/429",
            title = "Too many requests",
            status = StatusCodes.Status429TooManyRequests,
            detail = "Please wait before trying again."
        }, context.RequestAborted);
    }

    private (string Bucket, int PermitLimit)? ResolvePolicy(PathString path)
    {
        if (path.StartsWithSegments("/Account"))
        {
            return (
                "account",
                Math.Clamp(_security.LoginRequestsPerMinute, 3, 60));
        }

        if (!path.StartsWithSegments("/api"))
        {
            return null;
        }

        if (path.Value?.Contains("/audio", StringComparison.OrdinalIgnoreCase) == true
            || path.Value?.Contains("/writing", StringComparison.OrdinalIgnoreCase) == true
            || path.Value?.Contains("/speaking", StringComparison.OrdinalIgnoreCase) == true)
        {
            return (
                "ai",
                Math.Clamp(_security.AiRequestsPerMinute, 2, 60));
        }

        return (
            "api",
            Math.Clamp(_security.ApiRequestsPerMinute, 10, 600));
    }
}
