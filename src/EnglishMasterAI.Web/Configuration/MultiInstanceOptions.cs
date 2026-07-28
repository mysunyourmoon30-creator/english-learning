namespace EnglishMasterAI.Web.Configuration;

public sealed class MultiInstanceOptions
{
    public const string SectionName = "MultiInstance";

    public bool Enabled { get; set; }
    public string RedisConnectionStringName { get; set; } = "Redis";
    public bool UseDistributedRateLimiting { get; set; } = true;
    public bool UseSharedDataProtectionKeys { get; set; } = true;
    public string DataProtectionKeyName { get; set; } =
        "EnglishMasterAI-DataProtection-Keys";
    public string RateLimitKeyPrefix { get; set; } = "englishmaster:ratelimit";
}
