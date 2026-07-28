using EnglishMasterAI.Web.Data;
using EnglishMasterAI.Web.Domain;
using Microsoft.EntityFrameworkCore;

namespace EnglishMasterAI.Tests;

public sealed class PostgreSqlIntegrationTests
{
    [Fact]
    [Trait("Category", "PostgreSql")]
    public async Task Migrations_and_core_write_path_work_on_postgresql()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "POSTGRES_TEST_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                connectionString,
                postgres => postgres.MigrationsAssembly(
                    "EnglishMasterAI.Migrations.PostgreSql"))
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.MigrateAsync();
        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync();
        var userId = $"postgres-integration-{Guid.NewGuid():N}";

        db.LearnerProfiles.Add(new LearnerProfile
        {
            UserId = userId,
            DisplayName = "PostgreSQL integration",
            TimeZoneId = "Asia/Bangkok"
        });
        await db.SaveChangesAsync();
        var profile = await db.LearnerProfiles
            .AsNoTracking()
            .SingleAsync(x => x.UserId == userId);

        Assert.NotEmpty(appliedMigrations);
        Assert.Equal("PostgreSQL integration", profile.DisplayName);

        db.LearnerProfiles.Remove(
            await db.LearnerProfiles.SingleAsync(x => x.UserId == userId));
        await db.SaveChangesAsync();
    }
}
