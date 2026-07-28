using System.Buffers.Binary;
using System.Text;
using EnglishMasterAI.Web.Application;

namespace EnglishMasterAI.Tests;

public sealed class PcmWaveParserTests
{
    [Fact]
    public void Parse_reads_supported_pcm_wave()
    {
        var wave = CreateWave(sampleRate: 16_000, channels: 1, seconds: 1);

        var result = PcmWaveParser.Parse(wave, maxAudioSeconds: 2);

        Assert.Equal((uint)16_000, result.SampleRate);
        Assert.Equal((byte)1, result.Channels);
        Assert.Equal((byte)16, result.BitsPerSample);
        Assert.Equal(TimeSpan.FromSeconds(1), result.Duration);
        Assert.Equal(32_000, result.AudioBytes.Length);
    }

    [Fact]
    public void Parse_rejects_non_wave_payload()
    {
        var exception = Assert.Throws<InvalidDataException>(
            () => PcmWaveParser.Parse("not a wave"u8));

        Assert.Contains("PCM WAV", exception.Message);
    }

    [Fact]
    public void Parse_rejects_truncated_chunk()
    {
        var wave = CreateWave(sampleRate: 8_000, channels: 1, seconds: 1);
        BinaryPrimitives.WriteUInt32LittleEndian(
            wave.AsSpan(40, 4),
            uint.MaxValue);

        Assert.Throws<InvalidDataException>(() => PcmWaveParser.Parse(wave));
    }

    [Fact]
    public void Parse_enforces_duration_limit()
    {
        var wave = CreateWave(sampleRate: 8_000, channels: 1, seconds: 2);

        var exception = Assert.Throws<InvalidDataException>(
            () => PcmWaveParser.Parse(wave, maxAudioSeconds: 1));

        Assert.Contains("duration", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] CreateWave(
        int sampleRate,
        short channels,
        int seconds)
    {
        const short bitsPerSample = 16;
        var pcmLength = sampleRate * channels * (bitsPerSample / 8) * seconds;
        var wave = new byte[44 + pcmLength];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(wave, 0);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(4, 4), wave.Length - 8);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(wave, 8);
        Encoding.ASCII.GetBytes("fmt ").CopyTo(wave, 12);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(16, 4), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(20, 2), 1);
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(22, 2), channels);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(24, 4), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(
            wave.AsSpan(28, 4),
            sampleRate * channels * (bitsPerSample / 8));
        BinaryPrimitives.WriteInt16LittleEndian(
            wave.AsSpan(32, 2),
            (short)(channels * (bitsPerSample / 8)));
        BinaryPrimitives.WriteInt16LittleEndian(wave.AsSpan(34, 2), bitsPerSample);
        Encoding.ASCII.GetBytes("data").CopyTo(wave, 36);
        BinaryPrimitives.WriteInt32LittleEndian(wave.AsSpan(40, 4), pcmLength);
        return wave;
    }
}
