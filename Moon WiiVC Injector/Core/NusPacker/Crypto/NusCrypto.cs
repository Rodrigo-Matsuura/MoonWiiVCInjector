// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Moon_WiiVC_Injector.Core.NusPacker.FileSystem;
using Moon_WiiVC_Injector.Core.NusPacker.Packaging;
using Moon_WiiVC_Injector.Core.NusPacker.Utils;

namespace Moon_WiiVC_Injector.Core.NusPacker.Crypto;

public class NusEncryption : IDisposable
{
    private readonly Aes _aes = Aes.Create();

    public NusEncryption(byte[] key, byte[] iv)
    {
        _aes.Mode = CipherMode.CBC;
        _aes.Padding = PaddingMode.None;
        _aes.Key = key;
        _aes.IV = iv;
    }

    public void EncryptFileWithPadding(FstTable fst, string outputFilename, short contentID, int blockSize)
    {
        using FileStream output = new(outputFilename, FileMode.Create, FileAccess.Write, FileShare.None);

        byte[] fstData = fst.GetAsData();
        MemoryStream input = new(fstData);
        BigEndianMemoryStream ivStream = new(0x10);
        ivStream.WriteBigEndian(contentID);

        EncryptSingleFile(input, output, fst.GetDataSize(), ivStream.GetBuffer(), blockSize);
    }

    public void EncryptFileWithPadding(FileStream input, int contentID, FileStream output, int blockSize)
    {
        BigEndianMemoryStream ivStream = new(0x10);
        ivStream.WriteBigEndian((short)contentID);

        EncryptSingleFile(input, output, input.Length, ivStream.GetBuffer(), blockSize);
    }

    private void EncryptSingleFile(Stream input, Stream output, long inputLength, byte[] iv, int blockSize)
    {
        byte[] currentIv = (byte[])iv.Clone();
        long targetSize = NusUtils.Align(inputLength, blockSize);
        byte[] blockBuffer = new byte[blockSize];

        long curPosition = 0;
        do
        {
            int totalRead = 0;
            while (totalRead < blockSize)
            {
                int read = input.Read(blockBuffer, totalRead, blockSize - totalRead);
                if (read <= 0) break;
                totalRead += read;
            }
            if (totalRead < blockSize)
            {
                Array.Clear(blockBuffer, totalRead, blockSize - totalRead);
            }

            _aes.EncryptCbc(blockBuffer.AsSpan(0, blockSize), currentIv, blockBuffer.AsSpan(0, blockSize), PaddingMode.None);
            blockBuffer.AsSpan(blockSize - 16, 16).CopyTo(currentIv);

            curPosition += blockSize;
            output.Write(blockBuffer, 0, blockSize);
        } while (curPosition < targetSize);
    }

    public void EncryptFileHashed(FileStream input, int contentID, FileStream output, ContentHashes hashes, Action<string, double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        const int hashBlockSize = 0xFC00;
        long length = input.Length;
        byte[] buffer = new byte[hashBlockSize];
        int block = 0;
        long totalBlocks = (length / hashBlockSize) + 1;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Array.Clear(buffer, 0, hashBlockSize);
            int totalRead = 0;
            while (totalRead < hashBlockSize)
            {
                int read = input.Read(buffer, totalRead, hashBlockSize - totalRead);
                if (read <= 0) break;
                totalRead += read;
            }

            if (totalRead == 0 && block > 0)
                break;

            byte[] chunk = EncryptChunkHashed(buffer, block, hashes, contentID);
            output.Write(chunk, 0, chunk.Length);
            block++;

            if (block % 100 == 0 || block >= totalBlocks)
            {
                onProgress?.Invoke($"Encrypting content ({contentID:X8})...", Math.Min(1.0, (double)block / totalBlocks));
            }

            if (totalRead < hashBlockSize)
                break;
        }
    }

    private byte[] EncryptChunkHashed(byte[] buffer, int block, ContentHashes hashes, int contentID)
    {
        byte[] iv = new byte[16];
        BinaryPrimitives.WriteInt16BigEndian(iv.AsSpan(0, 2), (short)contentID);

        byte[] decryptedHashes = hashes.GetHashForBlock(block);
        decryptedHashes[1] ^= (byte)contentID;

        byte[] encryptedHashes = new byte[decryptedHashes.Length];
        _aes.EncryptCbc(decryptedHashes, iv, encryptedHashes, PaddingMode.None);
        decryptedHashes[1] ^= (byte)contentID;

        int ivStart = (block % 16) * 20;
        ReadOnlySpan<byte> nextIv = decryptedHashes.AsSpan(ivStart, 16);

        byte[] outputBuffer = new byte[encryptedHashes.Length + buffer.Length];
        encryptedHashes.CopyTo(outputBuffer.AsSpan(0, encryptedHashes.Length));

        _aes.EncryptCbc(buffer, nextIv, outputBuffer.AsSpan(encryptedHashes.Length, buffer.Length), PaddingMode.None);

        return outputBuffer;
    }

    public byte[] Encrypt(byte[] input)
    {
        byte[] output = new byte[input.Length];
        _aes.EncryptCbc(input, _aes.IV, output, PaddingMode.None);
        return output;
    }

    public void Dispose()
    {
        _aes.Dispose();
    }
}

public class ContentHashes
{
    private readonly Dictionary<int, byte[]> _h0Hashes = new();
    private readonly Dictionary<int, byte[]> _h1Hashes = new();
    private readonly Dictionary<int, byte[]> _h2Hashes = new();
    private readonly Dictionary<int, byte[]> _h3Hashes = new();

    public readonly byte[] TmdHash;
    private int _blockCount;

    public ContentHashes(string file, bool hashed, Action<string, double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        if (hashed)
        {
            CalculateH0Hashes(file, onProgress, cancellationToken);
            CalculateOtherHashes(1, _h0Hashes, _h1Hashes, onProgress, cancellationToken);
            CalculateOtherHashes(2, _h1Hashes, _h2Hashes, onProgress, cancellationToken);
            CalculateOtherHashes(3, _h2Hashes, _h3Hashes, onProgress, cancellationToken);
            TmdHash = SHA1.HashData(GetH3Hashes());
        }
        else
        {
            TmdHash = StreamHashSha1(file, NusContent.ContentFilePadding);
        }
    }

    private static byte[] StreamHashSha1(string file, int alignment)
    {
        using var sha1 = SHA1.Create();
        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);

        byte[] buffer = new byte[64 * 1024];
        long remaining = input.Length;
        while (remaining > 0)
        {
            int toRead = (int)Math.Min(buffer.Length, remaining);
            int read = input.Read(buffer, 0, toRead);
            if (read <= 0) break;
            sha1.TransformBlock(buffer, 0, read, null, 0);
            remaining -= read;
        }

        long targetSize = NusUtils.Align(input.Length, alignment);
        long padding = targetSize - input.Length;
        if (padding > 0)
        {
            byte[] zeroPad = new byte[Math.Min(padding, 64 * 1024)];
            while (padding > 0)
            {
                int toPad = (int)Math.Min(zeroPad.Length, padding);
                sha1.TransformBlock(zeroPad, 0, toPad, null, 0);
                padding -= toPad;
            }
        }

        sha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return sha1.Hash!;
    }

    private void CalculateOtherHashes(int hashLevel, Dictionary<int, byte[]> inHashes, Dictionary<int, byte[]> outHashes, Action<string, double>? onProgress, CancellationToken cancellationToken)
    {
        int hashLevelPow = 1 << (4 * hashLevel);
        int hashesCount = (_blockCount / hashLevelPow) + 1;

        for (int newBlock = 0; newBlock < hashesCount; newBlock++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] curHashes = new byte[16 * 20];
            for (int i = newBlock * 16; i < (newBlock * 16) + 16; i++)
            {
                if (inHashes.TryGetValue(i, out var hashVal))
                    Array.Copy(hashVal, 0, curHashes, (i % 16) * 20, 20);
            }
            outHashes.Add(newBlock, SHA1.HashData(curHashes));

            if (newBlock % 100 == 0 || newBlock == hashesCount - 1)
            {
                onProgress?.Invoke($"Calculating h{hashLevel} hashes...", (double)newBlock / hashesCount);
            }
        }
    }

    private void CalculateH0Hashes(string file, Action<string, double>? onProgress, CancellationToken cancellationToken)
    {
        using FileStream input = new(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        const int bufferSize = 0xFC00;
        byte[] buffer = new byte[bufferSize];
        int totalBlocks = (int)(input.Length / bufferSize) + 1;

        for (int block = 0; block < totalBlocks; block++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Array.Clear(buffer, 0, bufferSize);
            int totalRead = 0;
            while (totalRead < bufferSize)
            {
                int read = input.Read(buffer, totalRead, bufferSize - totalRead);
                if (read <= 0) break;
                totalRead += read;
            }

            _h0Hashes.Add(block, SHA1.HashData(buffer));

            if (block % 100 == 0 || block == totalBlocks - 1)
            {
                onProgress?.Invoke("Calculating content h0 hashes...", (double)block / totalBlocks);
            }
        }
        _blockCount = totalBlocks;
    }

    public byte[] GetHashForBlock(int block)
    {
        if (block > _blockCount)
        {
            throw new InvalidOperationException($"Requested hash block {block} exceeds block count {_blockCount}.");
        }

        byte[] hashes = new byte[0x400];
        Span<byte> hashesSpan = hashes.AsSpan();

        int h0HashStart = (block / 16) * 16;
        for (int i = 0; i < 16; i++)
        {
            int index = h0HashStart + i;
            if (_h0Hashes.TryGetValue(index, out var h0))
                h0.CopyTo(hashesSpan.Slice(i * 20, 20));
        }

        int h1HashStart = (block / 256) * 16;
        for (int i = 0; i < 16; i++)
        {
            int index = h1HashStart + i;
            if (_h1Hashes.TryGetValue(index, out var h1))
                h1.CopyTo(hashesSpan.Slice(320 + (i * 20), 20));
        }

        int h2HashStart = (block / 4096) * 16;
        for (int i = 0; i < 16; i++)
        {
            int index = h2HashStart + i;
            if (_h2Hashes.TryGetValue(index, out var h2))
                h2.CopyTo(hashesSpan.Slice(640 + (i * 20), 20));
        }

        return hashes;
    }

    private byte[] GetH3Hashes()
    {
        byte[] buffer = new byte[_h3Hashes.Count * 20];
        for (int i = 0; i < _h3Hashes.Count; i++)
        {
            _h3Hashes[i].CopyTo(buffer.AsSpan(i * 20, 20));
        }

        return buffer;
    }

    public void SaveH3ToFile(string h3Path)
    {
        if (_h3Hashes.Count > 0)
        {
            using FileStream fos = new(h3Path, FileMode.Create, FileAccess.Write, FileShare.None);
            byte[] h3Data = GetH3Hashes();
            fos.Write(h3Data, 0, h3Data.Length);
        }
    }
}

