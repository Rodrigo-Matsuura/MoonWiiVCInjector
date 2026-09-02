using System;
using System.Buffers.Binary;
using System.IO;

namespace Moon_WiiVC_Injector.Utilities;

/// <summary>
/// Native C# implementation of Wii U .btsnd audio converter and processor.
/// Based on wav2btsnd by Tim Ogus (timogus) - https://bitbucket.org/timogus/wav2btsnd
/// </summary>
public static class BtsndConverter
{
    private const int TargetSampleRate = 48000;
    private const int TargetChannels = 2;
    private const double MaxDurationSeconds = 6.0;
    private const int MaxSamples = (int)(TargetSampleRate * MaxDurationSeconds); // 288,000 samples per channel

    public class WavAudioData
    {
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public int BitsPerSample { get; set; }
        public float[][] ChannelSamples { get; set; } = []; // Normalized [-1.0f, 1.0f]
    }

    /// <summary>
    /// Reads a standard PCM or IEEE Float WAV file.
    /// </summary>
    public static WavAudioData ReadWav(string wavFilePath)
    {
        using var stream = File.OpenRead(wavFilePath);
        using var reader = new BinaryReader(stream);

        // RIFF header
        string riff = new(reader.ReadChars(4));
        if (riff != "RIFF") throw new InvalidDataException("Invalid WAV file: missing RIFF header.");

        reader.ReadInt32(); // ChunkSize

        string wave = new(reader.ReadChars(4));
        if (wave != "WAVE") throw new InvalidDataException("Invalid WAV file: missing WAVE format.");

        int audioFormat = 1; // 1 = PCM, 3 = IEEE float
        int numChannels = 2;
        int sampleRate = 44100;
        int bitsPerSample = 16;
        byte[]? audioBytes = null;

        while (stream.Position < stream.Length)
        {
            string chunkId = new(reader.ReadChars(4));
            int chunkSize = reader.ReadInt32();

            if (chunkId == "fmt ")
            {
                long chunkStart = stream.Position;
                audioFormat = reader.ReadInt16();
                numChannels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32(); // ByteRate
                reader.ReadInt16(); // BlockAlign
                bitsPerSample = reader.ReadInt16();

                // Skip any extra format bytes
                long bytesRead = stream.Position - chunkStart;
                if (chunkSize > bytesRead)
                {
                    stream.Seek(chunkSize - bytesRead, SeekOrigin.Current);
                }
            }
            else if (chunkId == "data")
            {
                audioBytes = reader.ReadBytes(chunkSize);
                break;
            }
            else
            {
                // Skip unknown chunk
                stream.Seek(chunkSize, SeekOrigin.Current);
            }
        }

        if (audioBytes == null)
            throw new InvalidDataException("WAV file does not contain a data chunk.");

        int bytesPerSample = bitsPerSample / 8;
        int frameSize = numChannels * bytesPerSample;
        int totalFrames = audioBytes.Length / frameSize;

        float[][] samples = new float[numChannels][];
        for (int ch = 0; ch < numChannels; ch++)
        {
            samples[ch] = new float[totalFrames];
        }

        ReadOnlySpan<byte> span = audioBytes;
        for (int i = 0; i < totalFrames; i++)
        {
            int frameOffset = i * frameSize;
            for (int ch = 0; ch < numChannels; ch++)
            {
                int sampleOffset = frameOffset + ch * bytesPerSample;
                float sampleValue = 0f;

                if (bitsPerSample == 16)
                {
                    short val = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(sampleOffset, 2));
                    sampleValue = val / 32768f;
                }
                else if (bitsPerSample == 8)
                {
                    byte val = span[sampleOffset];
                    sampleValue = (val - 128) / 128f;
                }
                else if (bitsPerSample == 24)
                {
                    int val = (span[sampleOffset] << 8) | (span[sampleOffset + 1] << 16) | (span[sampleOffset + 2] << 24);
                    sampleValue = (val >> 8) / 8388608f;
                }
                else if (bitsPerSample == 32)
                {
                    if (audioFormat == 3) // IEEE Float
                    {
                        sampleValue = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(sampleOffset, 4));
                    }
                    else
                    {
                        int val = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(sampleOffset, 4));
                        sampleValue = val / 2147483648f;
                    }
                }

                samples[ch][i] = Math.Clamp(sampleValue, -1.0f, 1.0f);
            }
        }

        return new WavAudioData
        {
            SampleRate = sampleRate,
            Channels = numChannels,
            BitsPerSample = bitsPerSample,
            ChannelSamples = samples
        };
    }

    /// <summary>
    /// Resamples audio to 48000 Hz, 2 Channels (Stereo), max 6 seconds duration.
    /// </summary>
    public static (float[] Left, float[] Right) ResampleTo48kStereo(WavAudioData input)
    {
        float[] sourceLeft = input.ChannelSamples[0];
        float[] sourceRight = input.Channels > 1 ? input.ChannelSamples[1] : input.ChannelSamples[0];

        int sourceLength = sourceLeft.Length;
        double ratio = (double)TargetSampleRate / input.SampleRate;
        int targetLength = (int)Math.Min((long)(sourceLength * ratio), MaxSamples);

        float[] targetLeft = new float[targetLength];
        float[] targetRight = new float[targetLength];

        for (int i = 0; i < targetLength; i++)
        {
            double sourcePos = i / ratio;
            int idx0 = (int)sourcePos;
            int idx1 = Math.Min(idx0 + 1, sourceLength - 1);
            double frac = sourcePos - idx0;

            // Linear interpolation
            targetLeft[i] = (float)(sourceLeft[idx0] * (1.0 - frac) + sourceLeft[idx1] * frac);
            targetRight[i] = (float)(sourceRight[idx0] * (1.0 - frac) + sourceRight[idx1] * frac);
        }

        return (targetLeft, targetRight);
    }

    /// <summary>
    /// Converts a WAV file to Wii U .btsnd format with optional looping.
    /// </summary>
    public static void ConvertWavToBtsnd(string inputWavPath, string outputBtsndPath, bool loop = false)
    {
        var wav = ReadWav(inputWavPath);
        var (left, right) = ResampleTo48kStereo(wav);

        int sampleCount = left.Length;
        using var stream = File.Open(outputBtsndPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream);

        // 12-byte BTSND Header (Big-Endian):
        // [0..3]: Loop flag (2 = loop, 0 = no loop)
        // [4..7]: Loop start sample offset (0)
        // [8..11]: Sample rate or sample count (0 or 48000)
        byte[] header = new byte[12];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), loop ? 2 : 0);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), 0);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(8, 4), 0);

        writer.Write(header);

        // Write 16-bit Big-Endian PCM Stereo samples
        byte[] pcmBuffer = new byte[4]; // 2 bytes left + 2 bytes right
        for (int i = 0; i < sampleCount; i++)
        {
            short sampleL = (short)Math.Clamp(Math.Round(left[i] * 32767.0), short.MinValue, short.MaxValue);
            short sampleR = (short)Math.Clamp(Math.Round(right[i] * 32767.0), short.MinValue, short.MaxValue);

            BinaryPrimitives.WriteInt16BigEndian(pcmBuffer.AsSpan(0, 2), sampleL);
            BinaryPrimitives.WriteInt16BigEndian(pcmBuffer.AsSpan(2, 2), sampleR);

            writer.Write(pcmBuffer);
        }
    }
}
