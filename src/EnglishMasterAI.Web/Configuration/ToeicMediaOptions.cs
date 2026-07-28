namespace EnglishMasterAI.Web.Configuration;

public sealed class ToeicMediaOptions
{
    public const string SectionName = "ToeicMedia";

    public string ManifestPath { get; set; } =
        "content/toeic-media/manifest.json";
    public bool RequireApprovedHumanAudio { get; set; }
    public bool AllowAiGeneratedFallback { get; set; } = true;
}
