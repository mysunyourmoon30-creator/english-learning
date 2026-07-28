using EnglishMasterAI.Web.Configuration;
using Microsoft.Extensions.Options;

namespace EnglishMasterAI.Web.Application;

public sealed class ToeicAudioService(
    ToeicMediaCatalog catalog,
    IReferenceAudioStore store,
    ReferenceAudioService generatedAudio,
    IOptions<ToeicMediaOptions> options)
{
    private readonly ToeicMediaOptions _options = options.Value;

    public bool CanServe(QuestionPrompt question) =>
        catalog.Find(question) is not null
        || (!_options.RequireApprovedHumanAudio
            && _options.AllowAiGeneratedFallback
            && generatedAudio.IsConfigured);

    public async Task<ReferenceAudio> GetAsync(
        string userId,
        QuestionPrompt question,
        CancellationToken cancellationToken = default)
    {
        var asset = catalog.Find(question);
        if (asset is not null)
        {
            var audio = await store.TryReadAsync(
                asset.AudioObjectKey,
                cancellationToken);
            if (audio is null)
            {
                throw new ToeicMediaUnavailableException(
                    "Approved TOEIC audio metadata exists, but its object is unavailable.");
            }

            return new ReferenceAudio(
                audio,
                "audio/mpeg",
                true,
                $"Licensed human recording · {asset.Accent} accent · {asset.LicenseId}");
        }

        if (!_options.RequireApprovedHumanAudio
            && _options.AllowAiGeneratedFallback)
        {
            return await generatedAudio.GetOrCreateAsync(
                userId,
                ToeicListeningPresentation.BuildAudioText(question),
                cancellationToken);
        }

        throw new ToeicMediaUnavailableException(
            "This TOEIC item does not have approved licensed human audio.");
    }
}

public sealed class ToeicMediaUnavailableException(string message)
    : Exception(message);
