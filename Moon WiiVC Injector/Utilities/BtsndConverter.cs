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
    private const double MaxDurationSeconds = 6.0;
    private const int MaxSamples = (int)(TargetSampleRate * MaxDurationSeconds); // 288,000 stereo frames
    private const int AudioOutputTvAndGamepad = 2; // 0 = TV only, 1 = GamePad only, 2 = TV + GamePad
    private const int NonLoopingTrailingSilenceFrames = 64;

    public class WavAudioData
    {
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public int BitsPerSample { get; set; }
        public float[][] ChannelSamples { get; set; } = []; // Normalized [-1.0f, 1.0f]
    }

    public static bool IsWavBytes(ReadOnlySpan<byte> data)
    {
        return data.Length >= 12 &&
               data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F' &&
               data[8] == (byte)'W' && data[9] == (byte)'A' && data[10] == (byte)'V' && data[11] == (byte)'E';
    }

    public static bool IsWavFile(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        using var fs = File.OpenRead(filePath);
        if (fs.Length < 12)
            return false;

        Span<byte> header = stackalloc byte[12];
        fs.ReadExactly(header);
        return IsWavBytes(header);
    }

    public static bool IsValidBtsndBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12 || ((data.Length - 8) % 4) != 0)
            return false;

        uint mode = BinaryPrimitives.ReadUInt32BigEndian(data[..4]);
        return mode <= 2;
    }

    public static bool IsValidBtsndFile(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        using var fs = File.OpenRead(filePath);
        if (fs.Length < 12 || ((fs.Length - 8) % 4) != 0)
            return false;

        Span<byte> header = stackalloc byte[8];
        fs.ReadExactly(header);
        uint mode = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
        return mode <= 2;
    }

    /// <summary>
    /// Reads a standard PCM or IEEE Float WAV file.
    /// </summary>
    public static WavAudioData ReadWav(string wavFilePath)
    {
        using var stream = File.OpenRead(wavFilePath);
        using var reader = new BinaryReader(stream);

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

        while (stream.Position + 8 <= stream.Length)
        {
            string chunkId = new(reader.ReadChars(4));
            int chunkSize = reader.ReadInt32();
            if (chunkSize < 0)
                throw new InvalidDataException("Invalid negative WAV chunk size.");

            if (chunkId == "fmt ")
            {
                long chunkStart = stream.Position;
                audioFormat = reader.ReadInt16();
                numChannels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32(); // ByteRate
                reader.ReadInt16(); // BlockAlign
                bitsPerSample = reader.ReadInt16();

                long bytesRead = stream.Position - chunkStart;
                if (chunkSize > bytesRead)
                {
                    stream.Seek(chunkSize - bytesRead, SeekOrigin.Current);
                }
            }
            else if (chunkId == "data")
            {
                int safeRead = (int)Math.Min(chunkSize, stream.Length - stream.Position);
                audioBytes = reader.ReadBytes(safeRead);
                break;
            }
            else
            {
                long paddedSkip = chunkSize + (chunkSize & 1);
                if (stream.Position + paddedSkip > stream.Length)
                    break;
                stream.Seek(paddedSkip, SeekOrigin.Current);
            }
        }

        if (audioBytes == null)
            throw new InvalidDataException("WAV file does not contain a data chunk.");

        int bytesPerSample = bitsPerSample / 8;
        if (bytesPerSample <= 0 || numChannels <= 0)
            throw new InvalidDataException($"Unsupported WAV audio layout ({numChannels}ch, {bitsPerSample}-bit).");

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
                int sampleOffset = frameOffset + (ch * bytesPerSample);
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
        if (sourceLength == 0)
            return ([], []);

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

            targetLeft[i] = (float)((sourceLeft[idx0] * (1.0 - frac)) + (sourceLeft[idx1] * frac));
            targetRight[i] = (float)((sourceRight[idx0] * (1.0 - frac)) + (sourceRight[idx1] * frac));
        }

        return (targetLeft, targetRight);
    }

    /// <summary>
    /// Converts a WAV or BTSND source file to Wii U .btsnd format, respecting the loop option.
    /// </summary>
    public static void ConvertSoundToBtsnd(string inputSoundPath, string outputBtsndPath, bool loop = false)
    {
        if (IsWavFile(inputSoundPath))
        {
            ConvertWavToBtsnd(inputSoundPath, outputBtsndPath, loop);
            return;
        }

        byte[] raw = File.ReadAllBytes(inputSoundPath);
        if (raw.Length < 8)
        {
            throw new InvalidDataException($"Boot sound file '{inputSoundPath}' is invalid or truncated.");
        }

        if (loop)
        {
            // Force loop start to sample 0
            BinaryPrimitives.WriteUInt32BigEndian(raw.AsSpan(4, 4), 0u);
        }
        File.WriteAllBytes(outputBtsndPath, raw);
    }

    /// <summary>
    /// Converts a WAV file to Wii U .btsnd format with accurate 8-byte header and optional looping.
    /// </summary>
    public static void ConvertWavToBtsnd(string inputWavPath, string outputBtsndPath, bool loop = false)
    {
        var wav = ReadWav(inputWavPath);
        var (left, right) = ResampleTo48kStereo(wav);

        int sampleCount = left.Length;
        using var stream = File.Open(outputBtsndPath, FileMode.Create, FileAccess.Write, FileShare.None);

        // Authentic 8-byte Wii U BTSND Header (Big-Endian):
        // [0..3]: Audio output mode (2 = both TV and GamePad)
        // [4..7]: Loop start frame offset (0 = loop from start; sampleCount = loop on trailing silence)
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(header[..4], AudioOutputTvAndGamepad);
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), loop ? 0u : (uint)sampleCount);
        stream.Write(header);

        // Write 16-bit Big-Endian PCM Stereo samples
        Span<byte> pcmFrame = stackalloc byte[4];
        for (int i = 0; i < sampleCount; i++)
        {
            short sampleL = (short)Math.Clamp(Math.Round(left[i] * 32767.0), short.MinValue, short.MaxValue);
            short sampleR = (short)Math.Clamp(Math.Round(right[i] * 32767.0), short.MinValue, short.MaxValue);

            BinaryPrimitives.WriteInt16BigEndian(pcmFrame[..2], sampleL);
            BinaryPrimitives.WriteInt16BigEndian(pcmFrame.Slice(2, 2), sampleR);
            stream.Write(pcmFrame);
        }

        if (!loop)
        {
            // Append silent stereo frames at sampleCount so Wii U DSP loops silently after one playback
            Span<byte> silence = stackalloc byte[NonLoopingTrailingSilenceFrames * 4];
            silence.Clear();
            stream.Write(silence);
        }
    }
}
