using System.Buffers.Binary;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EnglishMasterAI.Web.Configuration;
using EnglishMasterAI.Web.Infrastructure;
using Microsoft.Extensions.Options;

namespace EnglishMasterAI.Web.Application;

public sealed class OpenAiGateway(
    HttpClient httpClient,
    IOptions<AiOptions> options,
    AiUsageService usage,
    LearningTelemetry telemetry,
    OperationalAlertService alerts,
    ILogger<OpenAiGateway> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AiOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;
    public int MaxAudioBytes => Math.Clamp(_options.MaxAudioBytes, 256_000, 25 * 1024 * 1024);

    public async Task<T?> CreateStructuredResponseAsync<T>(
        string userId,
        string instructions,
        string input,
        string schemaName,
        JsonObject schema,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var stopwatch = Stopwatch.StartNew();
        using var activity = telemetry.StartAiActivity(
            schemaName,
            _options.FeedbackModel);

        var payload = new
        {
            model = _options.FeedbackModel,
            instructions,
            input,
            reasoning = new { effort = "low" },
            text = new
            {
                verbosity = "low",
                format = new
                {
                    type = "json_schema",
                    name = schemaName,
                    strict = true,
                    schema
                }
            },
            max_output_tokens = 1_500,
            store = false,
            safety_identifier = CreateSafetyIdentifier(userId)
        };

        using var request = CreateRequest(HttpMethod.Post, "responses");
        request.Content = JsonContent.Create(payload, options: JsonOptions);
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var requestId = GetRequestId(response);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "OpenAI response request failed with status {StatusCode}. Request id: {RequestId}",
                (int)response.StatusCode,
                requestId);
            telemetry.RecordAiFailure(schemaName, _options.FeedbackModel);
            await usage.RecordAsync(
                userId,
                schemaName,
                _options.FeedbackModel,
                "Failed",
                input.Length,
                0,
                0,
                (int)stopwatch.ElapsedMilliseconds,
                requestId,
                $"HTTP {(int)response.StatusCode}",
                cancellationToken);
            await alerts.TrySendAsync(
                $"ai-{schemaName}-http",
                "OpenAI request failed",
                $"{schemaName} returned HTTP {(int)response.StatusCode}.",
                cancellationToken: cancellationToken);
            throw new OpenAiGatewayException(
                $"AI feedback service returned HTTP {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(responseJson);
        var tokenUsage = ReadTokenUsage(document.RootElement);
        telemetry.RecordAiRequest(
            schemaName,
            _options.FeedbackModel,
            stopwatch.Elapsed.TotalMilliseconds);
        await usage.RecordAsync(
            userId,
            schemaName,
            _options.FeedbackModel,
            "Succeeded",
            input.Length,
            tokenUsage.InputTokens,
            tokenUsage.OutputTokens,
            (int)stopwatch.ElapsedMilliseconds,
            requestId,
            cancellationToken: cancellationToken);
        var outputText = FindOutputText(document.RootElement);
        if (string.IsNullOrWhiteSpace(outputText))
        {
            throw new OpenAiGatewayException("AI feedback service returned no structured output.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(outputText, JsonOptions);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Could not parse structured AI response.");
            throw new OpenAiGatewayException("AI feedback response was not valid JSON.", exception);
        }
    }

    public async Task<string> TranscribeAsync(
        string userId,
        ReadOnlyMemory<byte> audio,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var stopwatch = Stopwatch.StartNew();
        const string operation = "transcription";
        using var activity = telemetry.StartAiActivity(
            operation,
            _options.TranscriptionModel);
        if (audio.IsEmpty || audio.Length > MaxAudioBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(audio),
                $"Audio must be between 1 and {MaxAudioBytes:N0} bytes.");
        }

        var durationSeconds = EstimateAudioDurationSeconds(audio.Span);
        using var request = CreateRequest(HttpMethod.Post, "audio/transcriptions");
        using var form = new MultipartFormDataContent();
        var audioContent = new ByteArrayContent(audio.ToArray());
        audioContent.Headers.ContentType = MediaTypeHeaderValue.Parse(
            NormalizeAudioContentType(contentType));
        form.Add(audioContent, "file", SanitizeFileName(fileName));
        form.Add(new StringContent(_options.TranscriptionModel), "model");
        form.Add(new StringContent("en"), "language");
        form.Add(new StringContent("json"), "response_format");
        request.Content = form;

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var requestId = GetRequestId(response);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "OpenAI transcription failed with status {StatusCode}. Request id: {RequestId}",
                (int)response.StatusCode,
                requestId);
            telemetry.RecordAiFailure(operation, _options.TranscriptionModel);
            await usage.RecordAsync(
                userId,
                operation,
                _options.TranscriptionModel,
                "Failed",
                durationSeconds,
                0,
                0,
                (int)stopwatch.ElapsedMilliseconds,
                requestId,
                $"HTTP {(int)response.StatusCode}",
                cancellationToken);
            await alerts.TrySendAsync(
                "ai-transcription-http",
                "OpenAI transcription failed",
                $"Transcription returned HTTP {(int)response.StatusCode}.",
                cancellationToken: cancellationToken);
            throw new OpenAiGatewayException(
                $"Speech transcription service returned HTTP {(int)response.StatusCode}.");
        }

        using var document = JsonDocument.Parse(responseJson);
        var tokenUsage = ReadTokenUsage(document.RootElement);
        telemetry.RecordAiRequest(
            operation,
            _options.TranscriptionModel,
            stopwatch.Elapsed.TotalMilliseconds);
        await usage.RecordAsync(
            userId,
            operation,
            _options.TranscriptionModel,
            "Succeeded",
            durationSeconds,
            tokenUsage.InputTokens,
            tokenUsage.OutputTokens,
            (int)stopwatch.ElapsedMilliseconds,
            requestId,
            cancellationToken: cancellationToken);
        return document.RootElement.TryGetProperty("text", out var text)
            ? text.GetString()?.Trim() ?? string.Empty
            : string.Empty;
    }

    public Task<string> TranscribeAsync(
        ReadOnlyMemory<byte> audio,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default) =>
        TranscribeAsync(
            "anonymous-audio",
            audio,
            fileName,
            contentType,
            cancellationToken);

    public async Task<byte[]> GenerateSpeechAsync(
        string userId,
        string text,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var safeText = WritingFeedbackService.NormalizeText(text, 1_500);
        if (string.IsNullOrWhiteSpace(safeText))
        {
            throw new ArgumentException("Speech text is required.", nameof(text));
        }

        const string operation = "speech-generation";
        var stopwatch = Stopwatch.StartNew();
        using var activity = telemetry.StartAiActivity(operation, _options.SpeechModel);
        using var request = CreateRequest(HttpMethod.Post, "audio/speech");
        request.Content = JsonContent.Create(new
        {
            model = _options.SpeechModel,
            voice = _options.SpeechVoice,
            input = safeText,
            response_format = "mp3"
        }, options: JsonOptions);

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var requestId = GetRequestId(response);
        if (!response.IsSuccessStatusCode)
        {
            telemetry.RecordAiFailure(operation, _options.SpeechModel);
            await usage.RecordAsync(
                userId,
                operation,
                _options.SpeechModel,
                "Failed",
                safeText.Length,
                0,
                0,
                (int)stopwatch.ElapsedMilliseconds,
                requestId,
                $"HTTP {(int)response.StatusCode}",
                cancellationToken);
            await alerts.TrySendAsync(
                "ai-speech-http",
                "OpenAI speech generation failed",
                $"Speech generation returned HTTP {(int)response.StatusCode}.",
                cancellationToken: cancellationToken);
            throw new OpenAiGatewayException(
                $"Speech generation service returned HTTP {(int)response.StatusCode}.");
        }

        var audio = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        telemetry.RecordAiRequest(
            operation,
            _options.SpeechModel,
            stopwatch.Elapsed.TotalMilliseconds);
        await usage.RecordAsync(
            userId,
            operation,
            _options.SpeechModel,
            "Succeeded",
            safeText.Length,
            0,
            0,
            (int)stopwatch.ElapsedMilliseconds,
            requestId,
            cancellationToken: cancellationToken);
        return audio;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var baseUri = Uri.TryCreate(_options.BaseUrl, UriKind.Absolute, out var configured)
            ? configured
            : new Uri("https://api.openai.com/v1/");
        var request = new HttpRequestMessage(method, new Uri(baseUri, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new OpenAiGatewayException("AI provider is not configured.");
        }
    }

    private static int EstimateAudioDurationSeconds(ReadOnlySpan<byte> audio)
    {
        if (audio.Length >= 44
            && audio[..4].SequenceEqual("RIFF"u8)
            && audio.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            var channels = BinaryPrimitives.ReadUInt16LittleEndian(
                audio.Slice(22, 2));
            var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(
                audio.Slice(24, 4));
            var bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(
                audio.Slice(34, 2));
            var bytesPerSecond = sampleRate * channels * bitsPerSample / 8d;
            if (bytesPerSecond > 0)
            {
                return Math.Max(
                    1,
                    (int)Math.Ceiling((audio.Length - 44) / bytesPerSecond));
            }
        }

        return Math.Max(1, (int)Math.Ceiling(audio.Length / 32_000d));
    }

    private static string FindOutputText(JsonElement root)
    {
        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("type", out var type)
                    && type.GetString() == "output_text"
                    && contentItem.TryGetProperty("text", out var text))
                {
                    return text.GetString() ?? string.Empty;
                }
            }
        }

        return string.Empty;
    }

    private static TokenUsage ReadTokenUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usageElement))
        {
            return new TokenUsage(0, 0);
        }

        var input = usageElement.TryGetProperty("input_tokens", out var inputTokens)
            && inputTokens.TryGetInt32(out var parsedInput)
            ? parsedInput
            : 0;
        var output = usageElement.TryGetProperty("output_tokens", out var outputTokens)
            && outputTokens.TryGetInt32(out var parsedOutput)
            ? parsedOutput
            : 0;
        return new TokenUsage(input, output);
    }

    private static string CreateSafetyIdentifier(string userId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(userId));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
    }

    private static string GetRequestId(HttpResponseMessage response) =>
        response.Headers.TryGetValues("x-request-id", out var values)
            ? values.FirstOrDefault() ?? "not-provided"
            : "not-provided";

    private static string SanitizeFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension is ".webm" or ".wav" or ".mp3" or ".m4a" or ".ogg"
            ? $"speaking{extension}"
            : "speaking.webm";
    }

    private static string NormalizeAudioContentType(string contentType)
    {
        var normalized = contentType.Split(';', 2)[0].Trim().ToLowerInvariant();
        return normalized is "audio/webm" or "audio/wav" or "audio/mpeg"
            or "audio/mp4" or "audio/x-m4a" or "audio/ogg"
            ? normalized
            : "audio/webm";
    }

    private readonly record struct TokenUsage(int InputTokens, int OutputTokens);
}

public sealed class OpenAiGatewayException : Exception
{
    public OpenAiGatewayException(string message) : base(message)
    {
    }

    public OpenAiGatewayException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
