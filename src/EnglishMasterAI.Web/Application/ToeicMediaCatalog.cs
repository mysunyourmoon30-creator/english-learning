using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EnglishMasterAI.Web.Configuration;
using Microsoft.Extensions.Options;

namespace EnglishMasterAI.Web.Application;

public sealed class ToeicMediaCatalog
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly IReadOnlyDictionary<string, ToeicMediaAsset> _assets;

    public ToeicMediaCatalog(
        IWebHostEnvironment environment,
        IOptions<ToeicMediaOptions> options,
        ILogger<ToeicMediaCatalog> logger)
    {
        var path = Path.GetFullPath(Path.Combine(
            environment.ContentRootPath,
            options.Value.ManifestPath));
        if (!File.Exists(path))
        {
            path = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                options.Value.ManifestPath));
        }
        if (!File.Exists(path))
        {
            logger.LogWarning("TOEIC media manifest was not found at {Path}.", path);
            _assets = new Dictionary<string, ToeicMediaAsset>();
            return;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<ToeicMediaManifest>(
                File.ReadAllText(path),
                JsonOptions) ?? new ToeicMediaManifest();
            _assets = manifest.Assets
                .Where(IsRuntimeApproved)
                .GroupBy(asset => asset.ContentKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"TOEIC media manifest is invalid JSON: {path}",
                exception);
        }
    }

    public int ApprovedAssetCount => _assets.Count;

    public ToeicMediaAsset? Find(QuestionPrompt question) =>
        _assets.GetValueOrDefault(CreateContentKey(question));

    public string? GetApprovedPartOneImageUrl(QuestionPrompt question)
    {
        if (question.ToeicPart != 1)
        {
            return null;
        }

        var asset = Find(question);
        return Uri.TryCreate(asset?.ImageUrl, UriKind.Absolute, out var imageUri)
            && imageUri.Scheme == Uri.UriSchemeHttps
                ? imageUri.AbsoluteUri
                : null;
    }

    public static string CreateContentKey(QuestionPrompt question)
    {
        var canonical = string.Join(
            '\n',
            question.ToeicPart?.ToString() ?? string.Empty,
            ToeicListeningPresentation.BuildAudioText(question));
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static bool IsRuntimeApproved(ToeicMediaAsset asset) =>
        asset.Approved
        && asset.SourceType.Equals(
            "LicensedHumanRecording",
            StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(asset.LicenseId)
        && !string.IsNullOrWhiteSpace(asset.ApprovedBy)
        && asset.ApprovedAtUtc is not null
        && asset.ExpertClarityApproved
        && asset.ContentKey.Length == 64
        && asset.AudioObjectKey.Length == 64
        && asset.Sha256.Length == 64;
}

public sealed class ToeicMediaManifest
{
    public int Version { get; set; } = 1;
    public List<ToeicMediaAsset> Assets { get; set; } = [];
}

public sealed class ToeicMediaAsset
{
    public string ContentKey { get; set; } = string.Empty;
    public int Part { get; set; }
    public string AudioObjectKey { get; set; } = string.Empty;
    public string Accent { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string LicenseId { get; set; } = string.Empty;
    public string LicenseEvidencePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public double IntegratedLufs { get; set; }
    public double TruePeakDb { get; set; }
    public bool ExpertClarityApproved { get; set; }
    public bool Approved { get; set; }
    public string ApprovedBy { get; set; } = string.Empty;
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public string? ImageUrl { get; set; }
    public string? ImageLicenseId { get; set; }
}
