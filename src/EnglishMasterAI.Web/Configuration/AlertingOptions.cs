namespace EnglishMasterAI.Web.Configuration;

public sealed class AlertingOptions
{
    public const string SectionName = "Alerting";

    public bool Enabled { get; set; }
    public string WebhookUrl { get; set; } = string.Empty;
    public int CooldownMinutes { get; set; } = 15;

    public bool IsConfigured =>
        Enabled
        && Uri.TryCreate(WebhookUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;
}
