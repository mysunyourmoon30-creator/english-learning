using System.Buffers.Binary;
using System.Text;

namespace EnglishMasterAI.Web.Application;

public static class PcmWaveParser
{
    public static PcmWave Parse(
        ReadOnlySpan<byte> wave,
        int maxAudioSeconds = 30)
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
            var chunkEnd = checked(dataOffset + (long)chunkSize);
            if (chunkEnd > wave.Length)
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

            offset = checked((int)chunkEnd + (int)(chunkSize % 2));
        }

        if (audioFormat != 1
            || channels is < 1 or > 2
            || bitsPerSample != 16
            || sampleRate is < 8_000 or > 96_000
            || pcm is null
            || pcm.Length == 0)
        {
            throw new InvalidDataException(
                "WAV must contain 16-bit PCM audio with a supported sample rate.");
        }

        var bytesPerSecond = checked((long)sampleRate * channels * (bitsPerSample / 8));
        var duration = TimeSpan.FromSeconds((double)pcm.Length / bytesPerSecond);
        if (duration > TimeSpan.FromSeconds(Math.Clamp(maxAudioSeconds, 1, 300)))
        {
            throw new InvalidDataException(
                $"WAV duration exceeds the {maxAudioSeconds}-second limit.");
        }

        return new PcmWave(
            sampleRate,
            checked((byte)bitsPerSample),
            checked((byte)channels),
            pcm,
            duration);
    }
}

public sealed record PcmWave(
    uint SampleRate,
    byte BitsPerSample,
    byte Channels,
    byte[] AudioBytes,
    TimeSpan Duration);
