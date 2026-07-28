namespace EnglishMasterAI.Web.Configuration;

public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public bool? RequireConfirmedAccount { get; set; }
    public int ApiRequestsPerMinute { get; set; } = 60;
    public int AiRequestsPerMinute { get; set; } = 10;
    public int LoginRequestsPerMinute { get; set; } = 10;
}
