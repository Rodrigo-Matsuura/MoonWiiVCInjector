// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Moon_WiiVC_Injector.Core.NusPacker.Contents;
using Moon_WiiVC_Injector.Core.NusPacker.Utils;

namespace Moon_WiiVC_Injector.Core.NusPacker.Crypto;

public class ContentHashes
{
    private readonly Dictionary<int, byte[]> h0Hashes = new Dictionary<int, byte[]>();
    private readonly Dictionary<int, byte[]> h1Hashes = new Dictionary<int, byte[]>();
    private readonly Dictionary<int, byte[]> h2Hashes = new Dictionary<int, byte[]>();
    private readonly Dictionary<int, byte[]> h3Hashes = new Dictionary<int, byte[]>();

    public readonly byte[] TMDHash;

    private int blockCount;

    public ContentHashes(string file, bool hashed, Action<string, double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        if (hashed)
        {
            CalculateH0Hashes(file, onProgress, cancellationToken);
            CalculateOtherHashes(1, h0Hashes, h1Hashes, onProgress, cancellationToken);
            CalculateOtherHashes(2, h1Hashes, h2Hashes, onProgress, cancellationToken);
            CalculateOtherHashes(3, h2Hashes, h3Hashes, onProgress, cancellationToken);
            TMDHash = HashUtil.HashSHA1(GetH3Hashes());
        }
        else
        {
            TMDHash = HashUtil.HashSHA1(file, Content.CONTENT_FILE_PADDING);
        }
    }

    private void CalculateOtherHashes(int hashLevel, Dictionary<int, byte[]> inHashes, Dictionary<int, byte[]> outHashes, Action<string, double>? onProgress, CancellationToken cancellationToken)
    {
        int hash_level_pow = 1 << (4 * hashLevel);

        int hashesCount = (blockCount / hash_level_pow) + 1;
        for (int new_block = 0; new_block < hashesCount; new_block++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] cur_hashes = new byte[16 * 20];
            for (int i = new_block * 16; i < (new_block * 16) + 16; i++)
            {
                if (inHashes.TryGetValue(i, out var hashVal))
                    Array.Copy(hashVal, 0, cur_hashes, (i % 16) * 20, 20);
            }
            outHashes.Add(new_block, HashUtil.HashSHA1(cur_hashes));

            if (new_block % 100 == 0 || new_block == hashesCount - 1)
            {
                onProgress?.Invoke($"Calculating h{hashLevel} hashes...", (double)new_block / hashesCount);
            }
        }
    }

    private void CalculateH0Hashes(string file, Action<string, double>? onProgress, CancellationToken cancellationToken)
    {
        using FileStream input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);

        const int bufferSize = 0xFC00;

        byte[] buffer = new byte[bufferSize];
        int total_blocks = (int)(input.Length / bufferSize) + 1;
        for (int block = 0; block < total_blocks; block++)
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

            h0Hashes.Add(block, HashUtil.HashSHA1(buffer));

            if (block % 100 == 0 || block == total_blocks - 1)
            {
                onProgress?.Invoke("Calculating content h0 hashes...", (double)block / total_blocks);
            }
        }
        blockCount = total_blocks;
    }

    public byte[] GetHashForBlock(int block)
    {
        if (block > blockCount)
        {
            throw new InvalidOperationException($"Requested hash block {block} exceeds block count {blockCount}.");
        }

        MemoryStream hashes = new MemoryStream(0x400);
        int h0_hash_start = (block / 16) * 16;
        for (int i = 0; i < 16; i++)
        {
            int index = h0_hash_start + i;
            if (h0Hashes.TryGetValue(index, out var h0))
            {
                hashes.Write(h0, 0, h0.Length);
            }
            else
            {
                hashes.Seek(20, SeekOrigin.Current);
            }
        }

        int h1_hash_start = (block / 256) * 16;
        for (int i = 0; i < 16; i++)
        {
            int index = h1_hash_start + i;
            if (h1Hashes.TryGetValue(index, out var h1))
            {
                hashes.Write(h1, 0, h1.Length);
            }
            else
            {
                hashes.Seek(20, SeekOrigin.Current);
            }
        }

        int h2_hash_start = (block / 4096) * 16;
        for (int i = 0; i < 16; i++)
        {
            int index = h2_hash_start + i;
            if (h2Hashes.TryGetValue(index, out var h2))
            {
                hashes.Write(h2, 0, h2.Length);
            }
            else
            {
                hashes.Seek(20, SeekOrigin.Current);
            }
        }

        return hashes.GetBuffer();
    }

    private byte[] GetH3Hashes()
    {
        MemoryStream buffer = new MemoryStream(h3Hashes.Count * 20);
        for (int i = 0; i < h3Hashes.Count; i++)
        {
            buffer.Write(h3Hashes[i], 0, h3Hashes[i].Length);
        }

        return buffer.GetBuffer();
    }

    public void SaveH3ToFile(string h3Path)
    {
        if (h3Hashes.Count > 0)
        {
            using FileStream fos = new FileStream(h3Path, FileMode.Create, FileAccess.Write, FileShare.None);
            byte[] h3Data = GetH3Hashes();
            fos.Write(h3Data, 0, h3Data.Length);
        }
    }
}

