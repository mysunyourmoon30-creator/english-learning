namespace EnglishMasterAI.Web.Configuration;

public sealed class AiOptions
{
    public const string SectionName = "AI";

    public bool Enabled { get; set; } = true;
    public string Provider { get; set; } = "OpenAI";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string ApiKey { get; set; } = string.Empty;
    public string FeedbackModel { get; set; } = "gpt-5.6-luna";
    public string TranscriptionModel { get; set; } = "gpt-4o-mini-transcribe";
    public int TimeoutSeconds { get; set; } = 45;
    public int MaxWritingCharacters { get; set; } = 4_000;
    public int MaxAudioBytes { get; set; } = 10 * 1024 * 1024;

    public bool IsConfigured =>
        Enabled
        && Provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(ApiKey);
}
