// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Moon_WiiVC_Injector.Core.NusPacker.Crypto;
using Moon_WiiVC_Injector.Core.NusPacker.Fst;
using Moon_WiiVC_Injector.Core.NusPacker.Utils;

namespace Moon_WiiVC_Injector.Core.NusPacker.Contents;

public class Content
{
    public const int staticFSTContentHeaderDataSize = 32;
    public const int staticDataSize = 48;
    public const int CONTENT_FILE_PADDING = 32768;

    private const int ALIGNMENT_IN_CONTENT_FILE = 32;
    private static readonly byte[] ZeroPadding = new byte[ALIGNMENT_IN_CONTENT_FILE];

    private const short TYPE_CONTENT = 8192;
    private const short TYPE_ENCRYPTED = 1;
    private const short TYPE_HASHED = 2;

    private readonly short type = TYPE_CONTENT | TYPE_ENCRYPTED;
    private bool IsHashed => (type & TYPE_HASHED) == TYPE_HASHED;
    private long curFileOffset;
    private List<FSTEntry> entries = new List<FSTEntry>();

    public readonly int ID;
    private readonly short index;
    public long encryptedFileSize { get; set; }
    public byte[] SHA1 { get; set; } = new byte[20];
    private readonly int groupID;
    private readonly long parentTitleID;
    public readonly short entriesFlags;
    private readonly bool isFSTContent;

    public Content(int ID, short index, short entriesFlags, int groupID, long parentTitleID, bool isHashed, bool isFSTContent)
    {
        this.ID = ID;
        this.index = index;
        this.entriesFlags = entriesFlags;
        this.groupID = groupID;
        this.parentTitleID = parentTitleID;
        if (isHashed)
            type |= TYPE_HASHED;
        this.isFSTContent = isFSTContent;
    }

    public KeyValuePair<long, byte[]> GetFSTContentHeaderAsData(long oldContentOffset)
    {
        BigEndianMemoryStream buffer = new BigEndianMemoryStream(staticFSTContentHeaderDataSize);

        byte unknown;
        long content_offset = oldContentOffset;
        long fst_content_size = encryptedFileSize / CONTENT_FILE_PADDING;
        long fst_content_size_written = fst_content_size;

        if (IsHashed)
        {
            unknown = 2;
            fst_content_size_written -= ((fst_content_size / 64) + 1) * 2;
            if (fst_content_size_written < 0)
                fst_content_size_written = 0;
        }
        else
        {
            unknown = 1;
        }

        if (isFSTContent)
        {
            unknown = 0;
            if (fst_content_size == 1)
                fst_content_size = 0;

            content_offset += fst_content_size + 2;
        }
        else
        {
            content_offset += fst_content_size;
        }

        buffer.WriteBigEndian((int)oldContentOffset);
        buffer.WriteBigEndian((int)fst_content_size_written);
        buffer.WriteBigEndian(parentTitleID);
        buffer.WriteBigEndian(groupID);
        buffer.WriteByte(unknown);

        return new KeyValuePair<long, byte[]>(content_offset, buffer.GetBuffer());
    }

    public long GetOffsetForFileAndIncrease(FSTEntry fstEntry)
    {
        long old_fileoffset = curFileOffset;
        curFileOffset = old_fileoffset + NusUtils.Align(fstEntry.fileSize, ALIGNMENT_IN_CONTENT_FILE);

        return old_fileoffset;
    }

    public void ResetFileOffset()
    {
        curFileOffset = 0;
    }

    public byte[] GetAsData()
    {
        BigEndianMemoryStream buffer = new BigEndianMemoryStream(staticDataSize);
        buffer.WriteBigEndian(ID);
        buffer.WriteBigEndian(index);
        buffer.WriteBigEndian(type);
        buffer.WriteBigEndian(encryptedFileSize);
        buffer.Write(SHA1, 0, SHA1.Length);

        return buffer.GetBuffer();
    }

    public void PackContentToFile(string outputDir, Encryption encryption, string tempDir, Action<string, double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string decryptedFile = PackDecrypted(tempDir, onProgress, cancellationToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ContentHashes contentHashes = new ContentHashes(decryptedFile, IsHashed, onProgress, cancellationToken);

            string h3Path = Path.Combine(outputDir, $"{ID:X8}.h3");
            contentHashes.SaveH3ToFile(h3Path);
            SHA1 = contentHashes.TMDHash;

            string outputFilePath = Path.Combine(outputDir, $"{ID:X8}.app");
            encryptedFileSize = PackEncrypted(decryptedFile, outputFilePath, contentHashes, encryption, onProgress, cancellationToken);
        }
        finally
        {
            try
            {
                if (File.Exists(decryptedFile))
                    File.Delete(decryptedFile);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    private long PackEncrypted(string decryptedFile, string outputFilePath, ContentHashes hashes, Encryption encryption, Action<string, double>? onProgress, CancellationToken cancellationToken)
    {
        using FileStream input = new FileStream(decryptedFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        using FileStream output = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None);

        if (IsHashed)
        {
            encryption.EncryptFileHashed(input, ID, output, hashes, onProgress, cancellationToken);
        }
        else
        {
            encryption.EncryptFileWithPadding(input, ID, output, CONTENT_FILE_PADDING);
        }

        return output.Length;
    }

    private string PackDecrypted(string tempDir, Action<string, double>? onProgress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(tempDir);
        string tmpPath = Path.Combine(tempDir, $"{ID:X8}.dec");

        using (FileStream fos = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            int totalCount = entries.Count;
            int cnt_file = 1;
            long cur_offset = 0;

            foreach (FSTEntry entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.isFile)
                {
                    using (FileStream input = new FileStream(entry.filepath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        input.CopyTo(fos);
                    }

                    long alignedFileSize = NusUtils.Align(entry.fileSize, ALIGNMENT_IN_CONTENT_FILE);
                    cur_offset += alignedFileSize;

                    long padding = alignedFileSize - entry.fileSize;
                    if (padding > 0)
                    {
                        fos.Write(ZeroPadding, 0, (int)padding);
                    }
                }

                if (cnt_file % 10 == 0 || cnt_file == totalCount)
                {
                    onProgress?.Invoke($"Staging content files ({cnt_file}/{totalCount})...", (double)cnt_file / Math.Max(1, totalCount));
                }
                cnt_file++;
            }
        }

        return tmpPath;
    }

    public void Update(List<FSTEntry> entries)
    {
        if (entries != null)
        {
            this.entries = entries;
        }
    }

    public bool Equals(Content? other)
    {
        if (other == null)
            return false;

        return ID == other.ID;
    }
}
