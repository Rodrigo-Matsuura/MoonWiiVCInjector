// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Moon_WiiVC_Injector.Core.NusPacker.Packaging;
using Moon_WiiVC_Injector.Core.NusPacker.Utils;

namespace Moon_WiiVC_Injector.Core.NusPacker.Crypto;

public class Encryption : IDisposable
{
    private readonly Aes aes = Aes.Create();

    public Encryption(Key key, IV iv)
    {
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        aes.Key = key.key;
        aes.IV = iv.iv;
    }

    public void EncryptFileWithPadding(FST fst, string outputFilename, short contentID, int blockSize)
    {
        using FileStream output = new FileStream(outputFilename, FileMode.Create, FileAccess.Write, FileShare.None);

        byte[] fstData = fst.GetAsData();
        MemoryStream input = new MemoryStream(fstData);
        BigEndianMemoryStream ivStream = new BigEndianMemoryStream(0x10);
        ivStream.WriteBigEndian(contentID);
        IV iv = new IV(ivStream.GetBuffer());

        EncryptSingleFile(input, output, fst.GetDataSize(), iv, blockSize);
    }

    public void EncryptFileWithPadding(FileStream input, int contentID, FileStream output, int blockSize)
    {
        BigEndianMemoryStream ivStream = new BigEndianMemoryStream(0x10);
        ivStream.WriteBigEndian((short)contentID);
        IV iv = new IV(ivStream.GetBuffer());

        EncryptSingleFile(input, output, input.Length, iv, blockSize);
    }

    private void EncryptSingleFile(Stream input, Stream output, long inputLength, IV iv, int blockSize)
    {
        aes.IV = iv.iv;
        long targetSize = NusUtils.Align(inputLength, blockSize);

        long cur_position = 0;
        do
        {
            byte[] blockBuffer = new byte[blockSize];
            int totalRead = 0;
            while (totalRead < blockSize)
            {
                int read = input.Read(blockBuffer, totalRead, blockSize - totalRead);
                if (read <= 0) break;
                totalRead += read;
            }

            blockBuffer = Encrypt(blockBuffer);

            aes.IV = NusUtils.CopyOfRange(blockBuffer, blockSize - 16, blockSize);

            cur_position += blockSize;
            output.Write(blockBuffer, 0, blockBuffer.Length);
        } while (cur_position < targetSize);
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
        BigEndianMemoryStream ivStream = new BigEndianMemoryStream(16);
        ivStream.WriteBigEndian((short)contentID);
        aes.IV = ivStream.GetBuffer();
        byte[] decryptedHashes = hashes.GetHashForBlock(block);
        decryptedHashes[1] ^= (byte)contentID;

        byte[] encryptedhashes = Encrypt(decryptedHashes);
        decryptedHashes[1] ^= (byte)contentID;
        int iv_start = (block % 16) * 20;

        aes.IV = NusUtils.CopyOfRange(decryptedHashes, iv_start, iv_start + 16);

        byte[] encryptedContent = Encrypt(buffer);
        MemoryStream outputStream = new MemoryStream(0x10000);
        outputStream.Write(encryptedhashes, 0, encryptedhashes.Length);
        outputStream.Write(encryptedContent, 0, encryptedContent.Length);

        return outputStream.GetBuffer();
    }

    public byte[] Encrypt(byte[] input)
    {
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(input, 0, input.Length);
    }

    public void Dispose()
    {
        aes.Dispose();
    }
}
