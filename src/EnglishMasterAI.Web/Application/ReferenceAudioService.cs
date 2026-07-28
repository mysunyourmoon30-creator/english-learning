using System.Security.Cryptography;
using System.Text;
using EnglishMasterAI.Web.Configuration;
using Microsoft.Extensions.Options;

namespace EnglishMasterAI.Web.Application;

public sealed class ReferenceAudioService(
    OpenAiGateway ai,
    AiPracticeLimiter limiter,
    IWebHostEnvironment environment,
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

        var cacheRoot = Path.GetFullPath(Path.Combine(
            environment.ContentRootPath,
            _options.ReferenceAudioCachePath));
        Directory.CreateDirectory(cacheRoot);
        var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{_options.SpeechModel}|{_options.SpeechVoice}|{safeText}")))
            .ToLowerInvariant();
        var path = Path.Combine(cacheRoot, $"{cacheKey}.mp3");

        byte[] audio;
        var cacheHit = File.Exists(path);
        if (cacheHit)
        {
            audio = await File.ReadAllBytesAsync(path, cancellationToken);
        }
        else
        {
            if (!limiter.TryAcquire(userId))
            {
                throw new OpenAiGatewayException(
                    "Reference audio generation limit reached. Please wait before retrying.");
            }

            audio = await ai.GenerateSpeechAsync(
                userId,
                safeText,
                cancellationToken);
            await File.WriteAllBytesAsync(path, audio, cancellationToken);
        }

        return new ReferenceAudio(
            audio,
            "audio/mpeg",
            cacheHit,
            "เสียงนี้สร้างโดย AI เพื่อใช้เป็น reference สำหรับการฝึกฟังและพูดตาม");
    }
}

public sealed record ReferenceAudio(
    byte[] Bytes,
    string ContentType,
    bool CacheHit,
    string Disclosure);
