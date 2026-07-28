namespace EnglishMasterAI.Web.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string Provider { get; set; } = "Sqlite";
    public string ConnectionStringName { get; set; } = "DefaultConnection";
    public bool ApplyMigrationsOnStartup { get; set; } = true;
    public bool SeedOnStartup { get; set; } = true;
    public bool AllowSqliteInProduction { get; set; }

    public bool IsPostgreSql =>
        Provider.Equals("PostgreSql", StringComparison.OrdinalIgnoreCase)
        || Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase);
}
