namespace EnglishMasterAI.Web.Configuration;

public sealed class ProxyOptions
{
    public const string SectionName = "Proxy";

    public bool Enabled { get; set; }
    public int ForwardLimit { get; set; } = 1;
    public string[] KnownProxies { get; set; } = [];
    public string[] KnownNetworks { get; set; } = [];

    public bool HasTrustedForwarder =>
        KnownProxies.Any(value => !string.IsNullOrWhiteSpace(value))
        || KnownNetworks.Any(value => !string.IsNullOrWhiteSpace(value));
}
