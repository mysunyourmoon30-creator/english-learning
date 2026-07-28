namespace EnglishMasterAI.Web.Configuration;

public sealed class PronunciationOptions
{
    public const string SectionName = "Pronunciation";

    public bool Enabled { get; set; }
    public string Provider { get; set; } = "AzureSpeech";
    public string AzureSpeechKey { get; set; } = string.Empty;
    public string AzureSpeechRegion { get; set; } = string.Empty;
    public string Locale { get; set; } = "en-US";
    public int MaxAudioSeconds { get; set; } = 30;

    public bool IsConfigured =>
        Enabled
        && Provider.Equals("AzureSpeech", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(AzureSpeechKey)
        && !string.IsNullOrWhiteSpace(AzureSpeechRegion);
}
