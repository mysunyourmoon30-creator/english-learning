using EnglishMasterAI.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EnglishMasterAI.Web.Infrastructure;

public sealed class OperationalMonitorService(
    IServiceScopeFactory scopeFactory,
    OperationalAlertService alerts,
    ILogger<OperationalMonitorService> logger) : BackgroundService
{
    private bool _databaseWasHealthy = true;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckDatabaseAsync(stoppingToken);
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task CheckDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var healthy = await db.Database.CanConnectAsync(cancellationToken);

            if (!healthy)
            {
                await alerts.TrySendAsync(
                    "database-unavailable",
                    "Database health check failed",
                    "The application could not connect to its configured database.",
                    cancellationToken: cancellationToken);
            }
            else if (!_databaseWasHealthy)
            {
                await alerts.TrySendAsync(
                    "database-recovered",
                    "Database connection recovered",
                    "The application can connect to its database again.",
                    "warning",
                    cancellationToken);
            }

            _databaseWasHealthy = healthy;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            _databaseWasHealthy = false;
            logger.LogError(exception, "Background database monitoring failed.");
            await alerts.TrySendAsync(
                "database-monitor-error",
                "Database monitor failed",
                exception.GetType().Name,
                cancellationToken: cancellationToken);
        }
    }
}
