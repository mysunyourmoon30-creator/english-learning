using System.Security.Cryptography;
using System.Text;
using EnglishMasterAI.Web.Configuration;
using Microsoft.Extensions.Options;

namespace EnglishMasterAI.Web.Application;

public sealed class ReferenceAudioService(
    OpenAiGateway ai,
    AiPracticeLimiter limiter,
    IReferenceAudioStore store,
    IOptions<AiOptions> options)
{
    private readonly AiOptions _options = options.Value;

    public bool IsConfigured => ai.IsConfigured;

    public async Task<ReferenceAudio> GetOrCreateAsync(
        string userId,
        string text,
        CancellationToken cancellationToken = default)
    {
        var safeText = WritingFeedbackService.NormalizeText(text, 1_500);
        if (string.IsNullOrWhiteSpace(safeText))
        {
            throw new ArgumentException("Reference text is required.", nameof(text));
        }

        var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{_options.SpeechModel}|{_options.SpeechVoice}|{safeText}")))
            .ToLowerInvariant();
        var cachedAudio = await store.TryReadAsync(cacheKey, cancellationToken);
        if (cachedAudio is not null)
        {
            return CreateResult(cachedAudio, true);
        }

        if (!limiter.TryAcquire(userId))
        {
            throw new OpenAiGatewayException(
                "Reference audio generation limit reached. Please wait before retrying.");
        }

        var audio = await ai.GenerateSpeechAsync(
            userId,
            safeText,
            cancellationToken);
        await store.WriteAsync(cacheKey, audio, cancellationToken);
        return CreateResult(audio, false);
    }

    private static ReferenceAudio CreateResult(byte[] audio, bool cacheHit) =>
        new(
            audio,
            "audio/mpeg",
            cacheHit,
            "เสียงนี้สร้างโดย AI เพื่อใช้เป็น reference สำหรับฝึกฟังและพูดตาม");
}

public sealed record ReferenceAudio(
    byte[] Bytes,
    string ContentType,
    bool CacheHit,
    string Disclosure);
