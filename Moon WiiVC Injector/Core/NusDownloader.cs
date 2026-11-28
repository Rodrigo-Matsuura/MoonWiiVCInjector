using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Moon_WiiVC_Injector.Core;

internal sealed record NusFileRequest(
    string TitleIdHex,
    string? EncryptedTitleKeyHex,
    string RemoteFilePath,
    string LocalDestinationPath,
    string ExpectedMd5Hex);

/// <summary>
/// Native C# client for downloading and decrypting specific files from Nintendo NUS (Wii U CDN),
/// completely replacing external Java and JNUSTool.jar dependencies.
/// </summary>
internal sealed class NusDownloader(
    HttpClient httpClient,
    string commonKeyHex,
    string nusBaseUrl = NusDownloader.DefaultNusBaseUrl,
    Action<string>? onLog = null)
{
    public const string DefaultNusBaseUrl = "http://ccs.cdn.wup.shop.nintendo.net/ccs/download";

    private const int HashedRawBlockSize = 0x10000;     // 65,536 bytes (0x400 hash tree + 0xFC00 payload)
    private const int HashedHeaderSize = 0x0400;        // 1,024 bytes
    private const int HashedPayloadSize = 0xFC00;       // 64,512 bytes
    private const ushort ContentTypeHashed = 0x0002;
    private const ushort FstFlagByteOffset = 0x0004;

    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly byte[] _commonKey = ParseHexKey(commonKeyHex, "Wii U Common Key");
    private readonly string _nusBaseUrl = nusBaseUrl.TrimEnd('/');
    private readonly Action<string>? _onLog = onLog;

    private sealed record TmdContentRecord(uint Id, ushort Index, ushort Type, long Size, byte[] Hash)
    {
        public bool IsHashed => (Type & ContentTypeHashed) != 0;
    }

    private sealed record TmdMetadata(ulong TitleId, IReadOnlyList<TmdContentRecord> Contents);

    private sealed record FstFileEntry(
        string FullPath,
        ushort ContentIndex,
        long FileOffset,
        long FileLength,
        bool IsDirectory,
        bool InNusTitle);

    public async Task DownloadFilesAsync(
        IReadOnlyList<NusFileRequest> requests,
        IProgress<(int Completed, int Total, string CurrentFile)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var pendingByTitle = requests
            .Where(r => !IsLocalFileValid(r.LocalDestinationPath, r.ExpectedMd5Hex))
            .GroupBy(r => r.TitleIdHex.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (pendingByTitle.Count == 0)
        {
            Log("[NUS] All required base files are already cached and verified.");
            return;
        }

        int totalPending = pendingByTitle.Sum(g => g.Count());
        int completed = 0;

        foreach (var titleGroup in pendingByTitle)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string titleIdHex = titleGroup.Key;
            if (titleIdHex.Length != 16 || !ulong.TryParse(titleIdHex, System.Globalization.NumberStyles.HexNumber, null, out ulong titleId))
            {
                throw new InvalidOperationException($"Invalid NUS Title ID '{titleIdHex}'. Expected 16 hexadecimal characters.");
            }

            string? encryptedTitleKeyHex = titleGroup.Select(r => r.EncryptedTitleKeyHex).FirstOrDefault(k => !string.IsNullOrWhiteSpace(k));

            Log($"[NUS] Fetching metadata (TMD/FST) for title {titleIdHex}...");
            byte[] tmdBytes = await DownloadBytesWithRetryAsync($"{_nusBaseUrl}/{titleIdHex}/tmd", cancellationToken);
            TmdMetadata tmd = ParseTmd(tmdBytes);

            byte[] encryptedTitleKey;
            if (!string.IsNullOrWhiteSpace(encryptedTitleKeyHex))
            {
                encryptedTitleKey = ParseHexKey(encryptedTitleKeyHex, $"Title Key for {titleIdHex}");
            }
            else
            {
                byte[] cetkBytes = await DownloadBytesWithRetryAsync($"{_nusBaseUrl}/{titleIdHex}/cetk", cancellationToken);
                if (cetkBytes.Length < 0x1BF + 16)
                {
                    throw new InvalidDataException($"Downloaded ticket (cetk) for title {titleIdHex} is truncated ({cetkBytes.Length} bytes).");
                }
                encryptedTitleKey = cetkBytes.AsSpan(0x1BF, 16).ToArray();
            }

            byte[] decryptedTitleKey = DecryptTitleKey(encryptedTitleKey, tmd.TitleId);

            if (tmd.Contents.Count == 0)
            {
                throw new InvalidDataException($"TMD for title {titleIdHex} contains zero content records.");
            }

            TmdContentRecord fstContent = tmd.Contents[0];
            byte[] encryptedFst = await DownloadBytesWithRetryAsync($"{_nusBaseUrl}/{titleIdHex}/{fstContent.Id:X8}", cancellationToken);
            byte[] decryptedFst = DecryptFst(encryptedFst, decryptedTitleKey, fstContent.Index);
            List<FstFileEntry> fstEntries = ParseFst(decryptedFst);

            foreach (NusFileRequest req in titleGroup)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report((completed, totalPending, req.RemoteFilePath));

                FstFileEntry entry = FindMatchingEntry(fstEntries, req.RemoteFilePath, titleIdHex);
                if (entry.ContentIndex >= tmd.Contents.Count)
                {
                    throw new InvalidDataException(
                        $"FST entry '{entry.FullPath}' references content index {entry.ContentIndex}, which is outside TMD contents ({tmd.Contents.Count}).");
                }

                TmdContentRecord content = tmd.Contents[entry.ContentIndex];
                string contentUrl = $"{_nusBaseUrl}/{titleIdHex}/{content.Id:X8}";

                Log($"[NUS] Downloading '{entry.FullPath}' ({entry.FileLength:N0} bytes) from {titleIdHex}/{content.Id:X8}...");
                await DownloadAndDecryptEntryAsync(contentUrl, content, entry, decryptedTitleKey, req.LocalDestinationPath, cancellationToken);

                if (!string.IsNullOrWhiteSpace(req.ExpectedMd5Hex) && !IsLocalFileValid(req.LocalDestinationPath, req.ExpectedMd5Hex))
                {
                    FileUtil.SafeDeleteFile(req.LocalDestinationPath);
                    throw new InvalidOperationException(
                        $"MD5 checksum mismatch after downloading '{entry.FullPath}' from title {titleIdHex}. Please verify your keys and internet connection.");
                }

                completed++;
                progress?.Report((completed, totalPending, req.RemoteFilePath));
            }
        }
    }

    private static bool IsLocalFileValid(string filePath, string expectedMd5Hex)
    {
        if (!File.Exists(filePath))
            return false;

        if (string.IsNullOrWhiteSpace(expectedMd5Hex))
            return true;

        using var stream = File.OpenRead(filePath);
        string actualMd5 = Convert.ToHexString(MD5.HashData(stream));
        return string.Equals(actualMd5, expectedMd5Hex.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] ParseHexKey(string hex, string keyName)
    {
        if (string.IsNullOrWhiteSpace(hex))
            throw new ArgumentException($"{keyName} is missing.", nameof(hex));

        string trimmed = hex.Trim();
        if (trimmed.Length != 32)
            throw new ArgumentException($"{keyName} must be 32 hexadecimal characters (16 bytes).", nameof(hex));

        try
        {
            return Convert.FromHexString(trimmed);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException($"{keyName} contains non-hexadecimal characters.", nameof(hex), ex);
        }
    }

    private byte[] DecryptTitleKey(ReadOnlySpan<byte> encryptedTitleKey, ulong titleId)
    {
        Span<byte> iv = stackalloc byte[16];
        iv.Clear();
        BinaryPrimitives.WriteUInt64BigEndian(iv[..8], titleId);

        byte[] decrypted = new byte[16];
        using var aes = Aes.Create();
        aes.Key = _commonKey;
        aes.DecryptCbc(encryptedTitleKey, iv, decrypted, PaddingMode.None);
        return decrypted;
    }

    private static byte[] DecryptFst(ReadOnlySpan<byte> encryptedFst, byte[] decryptedTitleKey, ushort contentIndex)
    {
        if (encryptedFst.Length < 32 || (encryptedFst.Length % 16) != 0)
        {
            throw new InvalidDataException($"Invalid encrypted FST payload length ({encryptedFst.Length} bytes).");
        }

        Span<byte> iv = stackalloc byte[16];
        iv.Clear();
        BinaryPrimitives.WriteUInt16BigEndian(iv[..2], contentIndex);

        byte[] decrypted = new byte[encryptedFst.Length];
        using (var aes = Aes.Create())
        {
            aes.Key = decryptedTitleKey;
            aes.DecryptCbc(encryptedFst, iv, decrypted, PaddingMode.None);
        }

        if (decrypted[0] != (byte)'F' || decrypted[1] != (byte)'S' || decrypted[2] != (byte)'T' || decrypted[3] != 0x00)
        {
            throw new InvalidOperationException(
                "Failed to decrypt NUS File System Table (FST). Please verify that your Wii U Common Key and Base Game Title Key are valid.");
        }

        return decrypted;
    }

    private static TmdMetadata ParseTmd(ReadOnlySpan<byte> data)
    {
        // Standard Wii U TMD header:
        // 0x18C (396): Title ID (u64 BE)
        // 0x1DE (478): Content Count (u16 BE)
        // 0xB04 (2820): Content Records (contentCount * 48 bytes)
        const int titleIdOffset = 0x18C;
        const int contentCountOffset = 0x1DE;
        const int contentRecordsOffset = 0xB04;
        const int contentRecordSize = 48;

        if (data.Length < contentRecordsOffset)
        {
            throw new InvalidDataException($"Invalid TMD size ({data.Length} bytes).");
        }

        ulong titleId = BinaryPrimitives.ReadUInt64BigEndian(data.Slice(titleIdOffset, 8));
        ushort contentCount = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(contentCountOffset, 2));

        int requiredLength = contentRecordsOffset + (contentCount * contentRecordSize);
        if (data.Length < requiredLength)
        {
            throw new InvalidDataException($"TMD truncated: expected at least {requiredLength} bytes for {contentCount} contents, got {data.Length}.");
        }

        var contents = new List<TmdContentRecord>(contentCount);
        for (int i = 0; i < contentCount; i++)
        {
            ReadOnlySpan<byte> rec = data.Slice(contentRecordsOffset + (i * contentRecordSize), contentRecordSize);
            uint id = BinaryPrimitives.ReadUInt32BigEndian(rec.Slice(0, 4));
            ushort index = BinaryPrimitives.ReadUInt16BigEndian(rec.Slice(4, 2));
            ushort type = BinaryPrimitives.ReadUInt16BigEndian(rec.Slice(6, 2));
            long size = BinaryPrimitives.ReadInt64BigEndian(rec.Slice(8, 8));
            byte[] hash = rec.Slice(16, 32).ToArray();
            contents.Add(new TmdContentRecord(id, index, type, size, hash));
        }

        return new TmdMetadata(titleId, contents);
    }

    private static List<FstFileEntry> ParseFst(ReadOnlySpan<byte> fst)
    {
        int totalContentCount = BinaryPrimitives.ReadInt32BigEndian(fst.Slice(8, 4));
        int entriesOffset = 32 + (totalContentCount * 32);
        if (fst.Length < entriesOffset + 16)
        {
            throw new InvalidDataException("FST is too small to contain root entry.");
        }

        int totalEntries = BinaryPrimitives.ReadInt32BigEndian(fst.Slice(entriesOffset + 8, 4));
        int stringsOffset = entriesOffset + (totalEntries * 16);
        if (totalEntries <= 0 || fst.Length < stringsOffset)
        {
            throw new InvalidDataException($"FST entry table out of bounds (entries={totalEntries}, size={fst.Length}).");
        }

        var result = new List<FstFileEntry>(totalEntries);
        var dirStack = new List<(int EndIndex, string Name)>();

        for (int i = 0; i < totalEntries; i++)
        {
            while (dirStack.Count > 0 && i >= dirStack[^1].EndIndex)
            {
                dirStack.RemoveAt(dirStack.Count - 1);
            }

            ReadOnlySpan<byte> entrySpan = fst.Slice(entriesOffset + (i * 16), 16);
            byte typeFlags = entrySpan[0];
            bool isDir = (typeFlags & 0x01) != 0;
            bool inNus = (typeFlags & 0x80) == 0;

            int nameRelOffset = (entrySpan[1] << 16) | (entrySpan[2] << 8) | entrySpan[3];
            string name = i == 0 ? string.Empty : ReadNullTerminatedAscii(fst, stringsOffset + nameRelOffset);

            uint offsetOrParent = BinaryPrimitives.ReadUInt32BigEndian(entrySpan.Slice(4, 4));
            uint lengthOrNext = BinaryPrimitives.ReadUInt32BigEndian(entrySpan.Slice(8, 4));
            ushort flags = BinaryPrimitives.ReadUInt16BigEndian(entrySpan.Slice(12, 2));
            ushort contentIndex = BinaryPrimitives.ReadUInt16BigEndian(entrySpan.Slice(14, 2));

            if (i == 0)
            {
                continue;
            }

            var pathBuilder = new StringBuilder("/");
            foreach (var dir in dirStack)
            {
                if (!string.IsNullOrEmpty(dir.Name))
                {
                    pathBuilder.Append(dir.Name).Append('/');
                }
            }
            pathBuilder.Append(name);
            string fullPath = pathBuilder.ToString();

            if (isDir)
            {
                dirStack.Add(((int)lengthOrNext, name));
                result.Add(new FstFileEntry(fullPath, contentIndex, 0, 0, IsDirectory: true, InNusTitle: inNus));
            }
            else
            {
                long fileOffset = (flags & FstFlagByteOffset) != 0 ? offsetOrParent : ((long)offsetOrParent << 5);
                long fileLength = lengthOrNext;
                result.Add(new FstFileEntry(fullPath, contentIndex, fileOffset, fileLength, IsDirectory: false, InNusTitle: inNus));
            }
        }

        return result;
    }

    private static string ReadNullTerminatedAscii(ReadOnlySpan<byte> buffer, int startOffset)
    {
        if (startOffset < 0 || startOffset >= buffer.Length)
            return string.Empty;

        ReadOnlySpan<byte> slice = buffer[startOffset..];
        int nullIdx = slice.IndexOf((byte)0);
        if (nullIdx >= 0)
            slice = slice[..nullIdx];

        return Encoding.UTF8.GetString(slice);
    }

    private static FstFileEntry FindMatchingEntry(List<FstFileEntry> entries, string requestedPath, string titleIdHex)
    {
        string normalized = "/" + requestedPath.Trim().TrimStart('/').TrimEnd('*');

        FstFileEntry? exact = entries.FirstOrDefault(e =>
            !e.IsDirectory &&
            e.InNusTitle &&
            string.Equals(e.FullPath, normalized, StringComparison.OrdinalIgnoreCase));

        if (exact != null)
            return exact;

        if (requestedPath.EndsWith('*'))
        {
            FstFileEntry? prefixMatch = entries.FirstOrDefault(e =>
                !e.IsDirectory &&
                e.InNusTitle &&
                e.FullPath.StartsWith(normalized, StringComparison.OrdinalIgnoreCase));

            if (prefixMatch != null)
                return prefixMatch;
        }

        throw new FileNotFoundException($"File '{requestedPath}' was not found in NUS FST for title {titleIdHex}.");
    }

    private async Task DownloadAndDecryptEntryAsync(
        string contentUrl,
        TmdContentRecord content,
        FstFileEntry entry,
        byte[] decryptedTitleKey,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        string? parentDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        string tempOutPath = destinationPath + ".part";
        FileUtil.SafeDeleteFile(tempOutPath);

        try
        {
            if (entry.FileLength == 0)
            {
                await File.WriteAllBytesAsync(tempOutPath, [], cancellationToken);
            }
            else if (content.IsHashed)
            {
                await DownloadHashedEntryAsync(contentUrl, entry, decryptedTitleKey, tempOutPath, cancellationToken);
            }
            else
            {
                await DownloadRawEntryAsync(contentUrl, content, entry, decryptedTitleKey, tempOutPath, cancellationToken);
            }

            File.Move(tempOutPath, destinationPath, overwrite: true);
        }
        catch
        {
            FileUtil.SafeDeleteFile(tempOutPath);
            throw;
        }
    }

    private async Task DownloadRawEntryAsync(
        string contentUrl,
        TmdContentRecord content,
        FstFileEntry entry,
        byte[] decryptedTitleKey,
        string outputFilePath,
        CancellationToken cancellationToken)
    {
        if ((entry.FileOffset % 16) != 0)
        {
            throw new InvalidDataException($"Unaligned non-hashed FST offset 0x{entry.FileOffset:X} for '{entry.FullPath}'.");
        }

        long alignedLength = Align(entry.FileLength, 16);
        bool needPreviousCipherBlock = entry.FileOffset >= 16;
        long rangeStart = needPreviousCipherBlock ? (entry.FileOffset - 16) : 0;
        long rangeEnd = entry.FileOffset + alignedLength - 1;
        long alignedContentSize = Align(content.Size, 16);
        if (content.Size > 0 && rangeEnd >= alignedContentSize)
        {
            rangeEnd = alignedContentSize - 1;
        }

        const int maxAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, contentUrl);
                request.Headers.Range = new RangeHeaderValue(rangeStart, rangeEnd);

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                await using Stream netStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                if (response.StatusCode == HttpStatusCode.OK && rangeStart > 0)
                {
                    await SkipExactBytesAsync(netStream, rangeStart, cancellationToken);
                }

                byte[] iv = new byte[16];
                if (needPreviousCipherBlock)
                {
                    await ReadExactBytesAsync(netStream, iv, cancellationToken);
                }
                else
                {
                    BinaryPrimitives.WriteUInt16BigEndian(iv.AsSpan(0, 2), entry.ContentIndex);
                }

                using var aes = Aes.Create();
                aes.Key = decryptedTitleKey;

                const int chunkSize = 64 * 1024;
                byte[] cipherBuf = ArrayPool<byte>.Shared.Rent(chunkSize);
                byte[] plainBuf = ArrayPool<byte>.Shared.Rent(chunkSize);
                byte[] nextIv = new byte[16];

                try
                {
                    await using var outFile = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536, useAsync: true);
                    long remainingAligned = alignedLength;
                    long remainingPlain = entry.FileLength;

                    while (remainingAligned > 0)
                    {
                        int toRead = (int)Math.Min(chunkSize, remainingAligned);
                        await ReadExactBytesAsync(netStream, cipherBuf.AsMemory(0, toRead), cancellationToken);

                        cipherBuf.AsSpan(toRead - 16, 16).CopyTo(nextIv);
                        aes.DecryptCbc(cipherBuf.AsSpan(0, toRead), iv, plainBuf.AsSpan(0, toRead), PaddingMode.None);
                        nextIv.CopyTo(iv.AsSpan());

                        int toWrite = (int)Math.Min(toRead, remainingPlain);
                        if (toWrite > 0)
                        {
                            await outFile.WriteAsync(plainBuf.AsMemory(0, toWrite), cancellationToken);
                            remainingPlain -= toWrite;
                        }

                        remainingAligned -= toRead;
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(cipherBuf);
                    ArrayPool<byte>.Shared.Return(plainBuf);
                }

                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && !cancellationToken.IsCancellationRequested && ex is HttpRequestException or IOException)
            {
                Log($"[NUS] Transient error downloading '{entry.FullPath}' (attempt {attempt}/{maxAttempts}): {ex.Message}. Retrying...");
                await Task.Delay(500 * attempt, cancellationToken);
            }
        }
    }

    private async Task DownloadHashedEntryAsync(
        string contentUrl,
        FstFileEntry entry,
        byte[] decryptedTitleKey,
        string outputFilePath,
        CancellationToken cancellationToken)
    {
        long startBlock = entry.FileOffset / HashedPayloadSize;
        int firstBlockSliceOffset = (int)(entry.FileOffset % HashedPayloadSize);
        long endBlock = (entry.FileOffset + entry.FileLength - 1) / HashedPayloadSize;

        long rangeStart = startBlock * HashedRawBlockSize;
        long rangeEnd = ((endBlock + 1) * HashedRawBlockSize) - 1;

        const int maxAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, contentUrl);
                request.Headers.Range = new RangeHeaderValue(rangeStart, rangeEnd);

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                await using Stream netStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                if (response.StatusCode == HttpStatusCode.OK && rangeStart > 0)
                {
                    await SkipExactBytesAsync(netStream, rangeStart, cancellationToken);
                }

                using var aes = Aes.Create();
                aes.Key = decryptedTitleKey;

                byte[] encBlock = ArrayPool<byte>.Shared.Rent(HashedRawBlockSize);
                byte[] decHashes = new byte[HashedHeaderSize];
                byte[] decPayload = ArrayPool<byte>.Shared.Rent(HashedPayloadSize);
                byte[] actualH0 = new byte[20];
                byte[] hashIv = new byte[16];
                BinaryPrimitives.WriteUInt16BigEndian(hashIv.AsSpan(0, 2), entry.ContentIndex);

                try
                {
                    await using var outFile = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536, useAsync: true);
                    long remainingBytes = entry.FileLength;

                    for (long block = startBlock; block <= endBlock; block++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await ReadExactBytesAsync(netStream, encBlock.AsMemory(0, HashedRawBlockSize), cancellationToken);

                        aes.DecryptCbc(encBlock.AsSpan(0, HashedHeaderSize), hashIv, decHashes, PaddingMode.None);
                        decHashes[1] ^= (byte)entry.ContentIndex;

                        int h0Offset = (int)(block % 16) * 20;
                        ReadOnlySpan<byte> payloadIv = decHashes.AsSpan(h0Offset, 16);
                        ReadOnlySpan<byte> expectedH0 = decHashes.AsSpan(h0Offset, 20);

                        aes.DecryptCbc(
                            encBlock.AsSpan(HashedHeaderSize, HashedPayloadSize),
                            payloadIv,
                            decPayload.AsSpan(0, HashedPayloadSize),
                            PaddingMode.None);

                        SHA1.HashData(decPayload.AsSpan(0, HashedPayloadSize), actualH0);
                        if (!actualH0.AsSpan().SequenceEqual(expectedH0))
                        {
                            throw new InvalidDataException(
                                $"NUS H0 SHA-1 integrity check failed on block {block} while extracting '{entry.FullPath}'.");
                        }

                        int sliceOffset = block == startBlock ? firstBlockSliceOffset : 0;
                        int availableInBlock = HashedPayloadSize - sliceOffset;
                        int toWrite = (int)Math.Min(availableInBlock, remainingBytes);

                        await outFile.WriteAsync(decPayload.AsMemory(sliceOffset, toWrite), cancellationToken);
                        remainingBytes -= toWrite;
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(encBlock);
                    ArrayPool<byte>.Shared.Return(decPayload);
                }

                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && !cancellationToken.IsCancellationRequested && ex is HttpRequestException or IOException)
            {
                Log($"[NUS] Transient error downloading hashed '{entry.FullPath}' (attempt {attempt}/{maxAttempts}): {ex.Message}. Retrying...");
                await Task.Delay(500 * attempt, cancellationToken);
            }
        }
    }

    private async Task<byte[]> DownloadBytesWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync(cancellationToken);
            }
            catch (Exception ex) when (attempt < maxAttempts && !cancellationToken.IsCancellationRequested && ex is HttpRequestException or IOException)
            {
                Log($"[NUS] Transient error fetching '{url}' (attempt {attempt}/{maxAttempts}): {ex.Message}. Retrying...");
                await Task.Delay(500 * attempt, cancellationToken);
            }
        }
    }

    private static async Task ReadExactBytesAsync(Stream stream, Memory<byte> destination, CancellationToken cancellationToken)
    {
        int totalRead = 0;
        while (totalRead < destination.Length)
        {
            int read = await stream.ReadAsync(destination[totalRead..], cancellationToken);
            if (read <= 0)
            {
                throw new EndOfStreamException($"Unexpected end of NUS stream after reading {totalRead} of {destination.Length} bytes.");
            }
            totalRead += read;
        }
    }

    private static async Task SkipExactBytesAsync(Stream stream, long bytesToSkip, CancellationToken cancellationToken)
    {
        byte[] scratch = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            long remaining = bytesToSkip;
            while (remaining > 0)
            {
                int toRead = (int)Math.Min(scratch.Length, remaining);
                int read = await stream.ReadAsync(scratch.AsMemory(0, toRead), cancellationToken);
                if (read <= 0)
                {
                    throw new EndOfStreamException($"Unexpected end of NUS stream while skipping to offset {bytesToSkip}.");
                }
                remaining -= read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    private static long Align(long value, int alignment)
    {
        long rem = value % alignment;
        return rem == 0 ? value : value + (alignment - rem);
    }

    private void Log(string message) => _onLog?.Invoke(message);
}
