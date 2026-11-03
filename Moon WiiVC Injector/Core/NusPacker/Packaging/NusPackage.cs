// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using Moon_WiiVC_Injector.Core.NusPacker.Crypto;
using Moon_WiiVC_Injector.Core.NusPacker.FileSystem;
using Moon_WiiVC_Injector.Core.NusPacker.Metadata;
using Moon_WiiVC_Injector.Core.NusPacker.Utils;

namespace Moon_WiiVC_Injector.Core.NusPacker.Packaging;

internal sealed class NusContentInfo
{
    public const int StaticDataSize = 0x24;
    private readonly short _indexOffset;
    private readonly short _contentCount;
    public byte[] Sha2Hash { get; set; } = new byte[32];

    public NusContentInfo(short contentCount) : this(0, contentCount)
    {
    }

    private NusContentInfo(short indexOffset, short contentCount)
    {
        _indexOffset = indexOffset;
        _contentCount = contentCount;
    }

    public byte[] GetAsData()
    {
        BigEndianMemoryStream buffer = new BigEndianMemoryStream(2304);
        buffer.WriteBigEndian(_indexOffset);
        buffer.WriteBigEndian(_contentCount);
        buffer.Write(Sha2Hash, 0, Sha2Hash.Length);
        buffer.SetLength(2304);
        return buffer.ToArray();
    }
}

internal sealed class NusContent
{
    public const int StaticFstContentHeaderDataSize = 32;
    public const int StaticDataSize = 48;
    public const int ContentFilePadding = 32768;

    private const int AlignmentInContentFile = 32;
    private static readonly byte[] ZeroPadding = new byte[AlignmentInContentFile];

    private const short TypeContent = 8192;
    private const short TypeEncrypted = 1;
    private const short TypeHashed = 2;

    private readonly short _type = TypeContent | TypeEncrypted;
    private bool IsHashed => (_type & TypeHashed) == TypeHashed;
    private long _curFileOffset;
    private List<FstEntry> _entries = new();

    public readonly int Id;
    private readonly short _index;
    public long EncryptedFileSize { get; set; }
    public byte[] Sha1 { get; set; } = new byte[20];
    private readonly int _groupId;
    private readonly long _parentTitleId;
    public readonly short EntriesFlags;
    private readonly bool _isFstContent;

    public NusContent(int id, short index, short entriesFlags, int groupId, long parentTitleId, bool isHashed, bool isFstContent)
    {
        Id = id;
        _index = index;
        EntriesFlags = entriesFlags;
        _groupId = groupId;
        _parentTitleId = parentTitleId;
        if (isHashed)
            _type |= TypeHashed;
        _isFstContent = isFstContent;
    }

    public KeyValuePair<long, byte[]> GetFstContentHeaderAsData(long oldContentOffset)
    {
        BigEndianMemoryStream buffer = new BigEndianMemoryStream(StaticFstContentHeaderDataSize);

        byte unknown;
        long contentOffset = oldContentOffset;
        long fstContentSize = EncryptedFileSize / ContentFilePadding;
        long fstContentSizeWritten = fstContentSize;

        if (IsHashed)
        {
            unknown = 2;
            fstContentSizeWritten -= ((fstContentSize / 64) + 1) * 2;
            if (fstContentSizeWritten < 0)
                fstContentSizeWritten = 0;
        }
        else
        {
            unknown = 1;
        }

        if (_isFstContent)
        {
            unknown = 0;
            if (fstContentSize == 1)
                fstContentSize = 0;

            contentOffset += fstContentSize + 2;
        }
        else
        {
            contentOffset += fstContentSize;
        }

        buffer.WriteBigEndian((int)oldContentOffset);
        buffer.WriteBigEndian((int)fstContentSizeWritten);
        buffer.WriteBigEndian(_parentTitleId);
        buffer.WriteBigEndian(_groupId);
        buffer.WriteByte(unknown);
        buffer.SetLength(StaticFstContentHeaderDataSize);

        return new KeyValuePair<long, byte[]>(contentOffset, buffer.ToArray());
    }

    public long GetOffsetForFileAndIncrease(FstEntry fstEntry)
    {
        long oldOffset = _curFileOffset;
        _curFileOffset = oldOffset + NusUtils.Align(fstEntry.FileSize, AlignmentInContentFile);
        return oldOffset;
    }

    public void ResetFileOffset() => _curFileOffset = 0;

    public byte[] GetAsData()
    {
        BigEndianMemoryStream buffer = new BigEndianMemoryStream(StaticDataSize);
        buffer.WriteBigEndian(Id);
        buffer.WriteBigEndian(_index);
        buffer.WriteBigEndian(_type);
        buffer.WriteBigEndian(EncryptedFileSize);
        buffer.Write(Sha1, 0, Sha1.Length);
        buffer.SetLength(StaticDataSize);
        return buffer.ToArray();
    }

    public void PackContentToFile(string outputDir, NusEncryption encryption, string tempDir, Action<string, double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string decryptedFile = PackDecrypted(tempDir, onProgress, cancellationToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ContentHashes contentHashes = new(decryptedFile, IsHashed, onProgress, cancellationToken);

            string h3Path = Path.Combine(outputDir, $"{Id:X8}.h3");
            contentHashes.SaveH3ToFile(h3Path);
            Sha1 = contentHashes.TmdHash;

            string outputFilePath = Path.Combine(outputDir, $"{Id:X8}.app");
            EncryptedFileSize = PackEncrypted(decryptedFile, outputFilePath, contentHashes, encryption, onProgress, cancellationToken);
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

    private long PackEncrypted(string decryptedFile, string outputFilePath, ContentHashes hashes, NusEncryption encryption, Action<string, double>? onProgress, CancellationToken cancellationToken)
    {
        using FileStream input = new(decryptedFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        using FileStream output = new(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None);

        if (IsHashed)
        {
            encryption.EncryptFileHashed(input, Id, output, hashes, onProgress, cancellationToken);
        }
        else
        {
            encryption.EncryptFileWithPadding(input, Id, output, ContentFilePadding);
        }

        return output.Length;
    }

    private string PackDecrypted(string tempDir, Action<string, double>? onProgress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(tempDir);
        string tmpPath = Path.Combine(tempDir, $"{Id:X8}.dec");

        using (FileStream fos = new(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            int totalCount = _entries.Count;
            int cntFile = 1;
            long curOffset = 0;

            foreach (FstEntry entry in _entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.IsFile)
                {
                    using (FileStream input = new(entry.Filepath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        input.CopyTo(fos);
                    }

                    long alignedFileSize = NusUtils.Align(entry.FileSize, AlignmentInContentFile);
                    curOffset += alignedFileSize;

                    long padding = alignedFileSize - entry.FileSize;
                    if (padding > 0)
                    {
                        fos.Write(ZeroPadding, 0, (int)padding);
                    }
                }

                if (cntFile % 10 == 0 || cntFile == totalCount)
                {
                    onProgress?.Invoke($"Staging content files ({cntFile}/{totalCount})...", (double)cntFile / Math.Max(1, totalCount));
                }
                cntFile++;
            }
        }

        return tmpPath;
    }

    public void Update(List<FstEntry> entries)
    {
        if (entries != null)
            _entries = entries;
    }

    public bool Equals(NusContent? other) => other != null && Id == other.Id;
}

internal sealed class NusContentsCollection
{
    private readonly List<NusContent> _contents = new();
    public readonly NusContent FstContent;

    public NusContentsCollection()
    {
        ContentDetails details = new(false, 0, 0, 0);
        FstContent = GetNewContent(details, true);
    }

    public NusContent GetNewContent(ContentDetails details, bool isFstContent = false)
    {
        NusContent content = new(_contents.Count, (short)_contents.Count, details.EntriesFlag, details.GroupId, details.ParentTitleId, details.IsHashed, isFstContent);
        _contents.Add(content);
        return content;
    }

    public short GetContentCount() => (short)_contents.Count;

    public byte[] GetAsData() => _contents.SelectMany(content => content.GetAsData()).ToArray();

    public int GetDataSize() => GetContentCount() * NusContent.StaticDataSize;

    public byte[] GetFstContentHeaderAsData()
    {
        long contentOffset = 0;
        MemoryStream buffer = new(GetFstContentHeaderDataSize());
        foreach (NusContent content in _contents)
        {
            (long key, byte[] value) = content.GetFstContentHeaderAsData(contentOffset);
            contentOffset = key;
            buffer.Write(value, 0, value.Length);
        }
        return buffer.GetBuffer();
    }

    public int GetFstContentHeaderDataSize() => GetContentCount() * NusContent.StaticFstContentHeaderDataSize;

    public void ResetFileOffsets()
    {
        foreach (NusContent content in _contents)
            content.ResetFileOffset();
    }

    public void Update(FstEntriesCollection fileEntries)
    {
        foreach (NusContent content in _contents)
            content.Update(fileEntries.GetFstEntriesByContent(content));
    }

    public void PackContents(string outputDir, NusEncryption encryption, string tempDir, Action<string, double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var nonFstContents = _contents.Where(c => !c.Equals(FstContent)).ToList();
        int total = nonFstContents.Count;
        int current = 0;

        foreach (NusContent content in nonFstContents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current++;
            onProgress?.Invoke($"Packaging content {content.Id:X8} ({current}/{total})...", (double)(current - 1) / Math.Max(1, total));
            content.PackContentToFile(outputDir, encryption, tempDir, onProgress, cancellationToken);
        }
    }

    public void DeleteContent(NusContent curContent) => _contents.Remove(curContent);
}

internal sealed class NusPackage
{
    private readonly FstTable _fst;
    private readonly TitleTicket _ticket;
    private readonly TitleTmd _tmd;
    private readonly string _tempDir;

    public NusPackage(FstTable fst, TitleTicket ticket, TitleTmd tmd, string tempDir)
    {
        _fst = fst;
        _ticket = ticket;
        _tmd = tmd;
        _tempDir = tempDir;
    }

    public void Pack(string outputDir, Action<string, double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDir);
        cancellationToken.ThrowIfCancellationRequested();

        using NusEncryption encryption = _tmd.GetEncryption();
        _fst.Contents.PackContents(outputDir, encryption, _tempDir, onProgress, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        onProgress?.Invoke("Packing FST into 00000000.app...", 0.9);

        string fstPath = Path.Combine(outputDir, "00000000.app");
        encryption.EncryptFileWithPadding(_fst, fstPath, 0, NusContent.ContentFilePadding);

        NusContent fstContent = _fst.Contents.FstContent;
        fstContent.Sha1 = SHA1.HashData(_fst.GetAsData());
        fstContent.EncryptedFileSize = _fst.GetDataSize();

        _tmd.ContentInfo.Sha2Hash = SHA256.HashData(_fst.Contents.GetAsData());
        _tmd.UpdateContentInfoHash();

        cancellationToken.ThrowIfCancellationRequested();
        onProgress?.Invoke("Writing title metadata (title.tmd)...", 0.95);
        using (var fos = new FileStream(Path.Combine(outputDir, "title.tmd"), FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] tmdData = _tmd.GetAsData();
            fos.Write(tmdData, 0, tmdData.Length);
        }

        onProgress?.Invoke("Writing certificate (title.cert)...", 0.97);
        using (var fos = new FileStream(Path.Combine(outputDir, "title.cert"), FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] certData = TitleCert.GetCertAsData();
            fos.Write(certData, 0, certData.Length);
        }

        onProgress?.Invoke("Writing ticket (title.tik)...", 0.99);
        using (var fos = new FileStream(Path.Combine(outputDir, "title.tik"), FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] tikData = _ticket.GetAsData();
            fos.Write(tikData, 0, tikData.Length);
        }
    }
}

