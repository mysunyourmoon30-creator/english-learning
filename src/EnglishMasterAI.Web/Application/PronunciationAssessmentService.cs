using EnglishMasterAI.Web.Configuration;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.CognitiveServices.Speech.PronunciationAssessment;
using Microsoft.Extensions.Options;

namespace EnglishMasterAI.Web.Application;

public sealed class PronunciationAssessmentService(
    IOptions<PronunciationOptions> options,
    ILogger<PronunciationAssessmentService> logger)
{
    private readonly PronunciationOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task<PronunciationFeedback?> AssessAsync(
        ReadOnlyMemory<byte> wavAudio,
        string referenceText,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || wavAudio.IsEmpty)
        {
            return null;
        }

        var safeReference = WritingFeedbackService.NormalizeText(referenceText, 1_000);
        if (string.IsNullOrWhiteSpace(safeReference))
        {
            return null;
        }

        try
        {
            var pcm = PcmWaveParser.Parse(
                wavAudio.Span,
                _options.MaxAudioSeconds);
            var speechConfig = SpeechConfig.FromSubscription(
                _options.AzureSpeechKey,
                _options.AzureSpeechRegion);
            speechConfig.SpeechRecognitionLanguage = _options.Locale;

            using var format = AudioStreamFormat.GetWaveFormatPCM(
                pcm.SampleRate,
                pcm.BitsPerSample,
                pcm.Channels);
            using var pushStream = AudioInputStream.CreatePushStream(format);
            pushStream.Write(pcm.AudioBytes);
            pushStream.Close();
            using var audioConfig = AudioConfig.FromStreamInput(pushStream);
            using var recognizer = new SpeechRecognizer(speechConfig, audioConfig);
            var assessmentConfig = new PronunciationAssessmentConfig(
                safeReference,
                GradingSystem.HundredMark,
                Granularity.Phoneme,
                enableMiscue: true);
            assessmentConfig.EnableProsodyAssessment();
            assessmentConfig.ApplyTo(recognizer);

            using var registration = cancellationToken.Register(
                () => _ = recognizer.StopContinuousRecognitionAsync());
            var result = await recognizer.RecognizeOnceAsync();
            if (result.Reason != ResultReason.RecognizedSpeech)
            {
                logger.LogWarning(
                    "Pronunciation assessment returned {Reason}.",
                    result.Reason);
                return null;
            }

            var assessment = PronunciationAssessmentResult.FromResult(result);
            return new PronunciationFeedback(
                Math.Round(assessment.PronunciationScore, 1),
                Math.Round(assessment.AccuracyScore, 1),
                Math.Round(assessment.FluencyScore, 1),
                Math.Round(assessment.CompletenessScore, 1),
                Math.Round(assessment.ProsodyScore, 1),
                "Azure Speech",
                "คะแนนนี้ประเมินจาก acoustic speech signal เทียบกับ reference text; ไม่ได้ตัดสินคุณค่าของสำเนียง");
        }
        catch (Exception exception) when (
            exception is InvalidDataException
            or ArgumentException
            or InvalidOperationException)
        {
            logger.LogWarning(
                exception,
                "Pronunciation assessment could not process the submitted audio.");
            return null;
        }
    }

}
