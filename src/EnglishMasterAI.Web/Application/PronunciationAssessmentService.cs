using System.Buffers.Binary;
using System.Text;
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
            var pcm = ParsePcmWave(wavAudio.Span);
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

    private static PcmWave ParsePcmWave(ReadOnlySpan<byte> wave)
    {
        if (wave.Length < 44
            || !wave[..4].SequenceEqual("RIFF"u8)
            || !wave.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            throw new InvalidDataException("Only PCM WAV audio is supported.");
        }

        ushort audioFormat = 0;
        ushort channels = 0;
        ushort bitsPerSample = 0;
        uint sampleRate = 0;
        byte[]? pcm = null;

        var offset = 12;
        while (offset + 8 <= wave.Length)
        {
            var chunkId = Encoding.ASCII.GetString(wave.Slice(offset, 4));
            var chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(
                wave.Slice(offset + 4, 4));
            var dataOffset = offset + 8;
            if (dataOffset + chunkSize > wave.Length)
            {
                throw new InvalidDataException("WAV chunk is truncated.");
            }

            if (chunkId == "fmt " && chunkSize >= 16)
            {
                audioFormat = BinaryPrimitives.ReadUInt16LittleEndian(
                    wave.Slice(dataOffset, 2));
                channels = BinaryPrimitives.ReadUInt16LittleEndian(
                    wave.Slice(dataOffset + 2, 2));
                sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(
                    wave.Slice(dataOffset + 4, 4));
                bitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(
                    wave.Slice(dataOffset + 14, 2));
            }
            else if (chunkId == "data")
            {
                pcm = wave.Slice(dataOffset, checked((int)chunkSize)).ToArray();
            }

            offset = dataOffset + checked((int)chunkSize) + (int)(chunkSize % 2);
        }

        if (audioFormat != 1
            || channels is < 1 or > 2
            || bitsPerSample != 16
            || sampleRate is < 8_000 or > 96_000
            || pcm is null)
        {
            throw new InvalidDataException(
                "WAV must contain 16-bit PCM audio with a supported sample rate.");
        }

        return new PcmWave(
            sampleRate,
            checked((byte)bitsPerSample),
            checked((byte)channels),
            pcm);
    }

    private sealed record PcmWave(
        uint SampleRate,
        byte BitsPerSample,
        byte Channels,
        byte[] AudioBytes);
}
