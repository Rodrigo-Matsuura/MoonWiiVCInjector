using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace Moon_WiiVC_Injector;

/// <summary>
/// Configuration options for NFS conversion operations.
/// </summary>
public class NfsConversionOptions
{
    public bool Encrypt { get; set; } = true;
    public bool Decrypt { get; set; } = false;
    public string IsoFile { get; set; } = "game.iso";
    public string KeyFile { get; set; } = Path.Combine("..", "code", "htk.bin");
    public string WiiKeyFile { get; set; } = "wii_common_key.bin";
    public string FwFile { get; set; } = Path.Combine("..", "code", "fw.img");
    public string NfsDirectory { get; set; } = string.Empty;
    public bool KeepIntermediateFiles { get; set; } = false;
    public bool KeepLegit { get; set; } = false;
    public bool MapShoulderToTrigger { get; set; } = false;
    public bool VerticalWiimote { get; set; } = false;
    public bool HorizontalWiimote { get; set; } = false;
    public bool Homebrew { get; set; } = false;
    public bool Passthrough { get; set; } = false;
    public bool InstantCc { get; set; } = false;
    public bool NoCc { get; set; } = false;
}

/// <summary>
/// Native C# implementation of NFS2ISO2NFS (Wii U Virtual Console NFS partition unpacker/packer).
/// Based on NFS2ISO2NFS by FIX94 - https://github.com/FIX94/NFS2ISO2NFS
/// </summary>
public class Nfs2Iso2Nfs(
    string? baseDirectory = null,
    Action<string>? onLog = null,
    IProgress<(string Message, double Progress)>? progress = null,
    CancellationToken cancellationToken = default)
{
    public const int SectorSize = 0x8000;
    public const int HeaderSize = 0x200;
    public const int NfsChunkSize = 0xFA00000;

    private static readonly byte[] DefaultWiiCommonKey = [0xeb, 0xe4, 0x2a, 0x22, 0x5e, 0x85, 0x93, 0xe4, 0x48, 0xd9, 0xc5, 0x45, 0x73, 0x81, 0xaa, 0xf7];

    private readonly string _baseDirectory = !string.IsNullOrWhiteSpace(baseDirectory) ? Path.GetFullPath(baseDirectory) : Directory.GetCurrentDirectory();
    private readonly Action<string>? _onLog = onLog;
    private readonly IProgress<(string Message, double Progress)>? _progress = progress;
    private readonly CancellationToken _cancellationToken = cancellationToken;

    /// <summary>
    /// Converts between ISO and Wii U NFS format using strongly-typed options.
    /// </summary>
    public static int ConvertNfs(
        NfsConversionOptions options,
        string? baseDirectory = null,
        Action<string>? onLog = null,
        IProgress<(string Message, double Progress)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var instance = new Nfs2Iso2Nfs(baseDirectory, onLog, progress, cancellationToken);
        return instance.Execute(options);
    }

    /// <summary>
    /// Legacy entrypoint supporting array-based arguments.
    /// </summary>
    public static int ConvertNfs(
        string[] args,
        string? baseDirectory = null,
        Action<string>? onLog = null,
        IProgress<(string Message, double Progress)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var options = ParseArguments(args, baseDirectory);
        var instance = new Nfs2Iso2Nfs(baseDirectory, onLog, progress, cancellationToken);
        return instance.Execute(options);
    }

    private static NfsConversionOptions ParseArguments(string[] args, string? baseDir)
    {
        var opt = new NfsConversionOptions
        {
            NfsDirectory = baseDir ?? string.Empty
        };

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-dec": opt.Decrypt = true; opt.Encrypt = false; break;
                case "-enc": opt.Encrypt = true; opt.Decrypt = false; break;
                case "-keep": opt.KeepIntermediateFiles = true; break;
                case "-legit": opt.KeepLegit = true; break;
                case "-key" when i + 1 < args.Length: opt.KeyFile = args[++i]; break;
                case "-wiikey" when i + 1 < args.Length: opt.WiiKeyFile = args[++i]; break;
                case "-iso" when i + 1 < args.Length: opt.IsoFile = args[++i]; break;
                case "-nfs" when i + 1 < args.Length: opt.NfsDirectory = args[++i]; break;
                case "-fwimg" when i + 1 < args.Length: opt.FwFile = args[++i]; break;
                case "-lrpatch": opt.MapShoulderToTrigger = true; break;
                case "-wiimote": opt.VerticalWiimote = true; break;
                case "-horizontal": opt.HorizontalWiimote = true; break;
                case "-homebrew": opt.Homebrew = true; break;
                case "-passthrough": opt.Passthrough = true; break;
                case "-instantcc": opt.InstantCc = true; break;
                case "-nocc": opt.NoCc = true; break;
            }
        }

        return opt;
    }

    public int Execute(NfsConversionOptions options)
    {
        string keyPath = ResolvePath(options.KeyFile);
        string wiiKeyPath = ResolvePath(options.WiiKeyFile);
        string isoPath = ResolvePath(options.IsoFile);
        string nfsDirPath = ResolvePath(string.IsNullOrEmpty(options.NfsDirectory) ? _baseDirectory : options.NfsDirectory);
        string fwPath = ResolvePath(options.FwFile);

        byte[]? key = LoadAesKey(keyPath);
        if (key == null) return -1;

        byte[] wiiCommonKey = LoadWiiCommonKey(wiiKeyPath);

        _cancellationToken.ThrowIfCancellationRequested();

        if (options.Decrypt)
        {
            return ExecuteDecrypt(nfsDirPath, key, wiiCommonKey, options.KeepIntermediateFiles);
        }
        else
        {
            return ExecuteEncrypt(options, isoPath, fwPath, key, wiiCommonKey);
        }
    }

    private int ExecuteDecrypt(string nfsDirPath, byte[] key, byte[] wiiCommonKey, bool keepFiles)
    {
        string firstNfs = Path.Combine(nfsDirPath, "hif_000000.nfs");
        if (!File.Exists(firstNfs))
        {
            Log("ERROR: .nfs files not found! Exiting...");
            return -1;
        }

        byte[] header = GetHeader(firstNfs);
        string hifNfs = ResolvePath("hif.nfs");
        string hifDecNfs = ResolvePath("hif_dec.nfs");
        string hifUnpackNfs = ResolvePath("hif_unpack.nfs");
        string gameIso = ResolvePath("game.iso");

        CombineNFSFiles(hifNfs, nfsDirPath);
        _cancellationToken.ThrowIfCancellationRequested();

        EnDecryptNFS(hifNfs, hifDecNfs, key, new byte[key.Length], false, header);
        if (!keepFiles) FileUtil.SafeDeleteFile(hifNfs);
        _cancellationToken.ThrowIfCancellationRequested();

        UnpackNFS(hifDecNfs, hifUnpackNfs, header);
        if (!keepFiles) FileUtil.SafeDeleteFile(hifDecNfs);
        _cancellationToken.ThrowIfCancellationRequested();

        ManipulateISO(hifUnpackNfs, gameIso, true, wiiCommonKey);
        if (!keepFiles) FileUtil.SafeDeleteFile(hifUnpackNfs);

        return 0;
    }

    private int ExecuteEncrypt(NfsConversionOptions options, string isoPath, string fwPath, byte[] key, byte[] wiiCommonKey)
    {
        if (!File.Exists(isoPath))
        {
            Log($"ERROR: ISO file not found at '{isoPath}'! Exiting...");
            return -1;
        }

        if (File.Exists(fwPath) && (!options.KeepLegit || options.HorizontalWiimote || options.VerticalWiimote || options.MapShoulderToTrigger || options.Homebrew || options.Passthrough || options.InstantCc || options.NoCc))
        {
            PatchFirmware(fwPath, options);
        }

        _cancellationToken.ThrowIfCancellationRequested();
        string hifUnpackNfs = ResolvePath("hif_unpack.nfs");
        string hifDecNfs = ResolvePath("hif_dec.nfs");
        string hifNfs = ResolvePath("hif.nfs");

        long[]? size = ManipulateISO(isoPath, hifUnpackNfs, false, wiiCommonKey);
        if (size == null)
        {
            return -1;
        }
        _cancellationToken.ThrowIfCancellationRequested();

        byte[] header = PackNFS(hifUnpackNfs, hifDecNfs, size);
        if (!options.KeepIntermediateFiles) FileUtil.SafeDeleteFile(hifUnpackNfs);
        _cancellationToken.ThrowIfCancellationRequested();

        EnDecryptNFS(hifDecNfs, hifNfs, key, new byte[key.Length], true, header);
        if (!options.KeepIntermediateFiles) FileUtil.SafeDeleteFile(hifDecNfs);
        _cancellationToken.ThrowIfCancellationRequested();

        SplitNFSFile(hifNfs);
        if (!options.KeepIntermediateFiles) FileUtil.SafeDeleteFile(hifNfs);

        return 0;
    }

    private byte[]? LoadAesKey(string keyPath)
    {
        Log("Searching for AES key file...");
        if (!File.Exists(keyPath))
        {
            Log($"ERROR: Could not find AES key file at '{keyPath}'! Exiting...");
            return null;
        }
        byte[] data = File.ReadAllBytes(keyPath);
        if (data.Length != 16)
        {
            Log("ERROR: AES key file has invalid size (expected 16 bytes)! Exiting...");
            return null;
        }
        Log("AES key file found!");
        return data;
    }

    private byte[] LoadWiiCommonKey(string wiiKeyPath)
    {
        if (File.Exists(wiiKeyPath))
        {
            byte[] data = File.ReadAllBytes(wiiKeyPath);
            if (data.Length == 16)
            {
                Log("Wii Common Key loaded from file!");
                return data;
            }
        }

        Log("Wii common key found in source code!");
        return DefaultWiiCommonKey;
    }

    private void Log(string message)
    {
        _onLog?.Invoke(message);
    }

    private void ReportProgress(string message, double percent)
    {
        Log(message);
        _progress?.Report((message, percent));
    }

    private string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return _baseDirectory;

        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(_baseDirectory, path));
    }

    private static int ReadBigEndianInt32(ReadOnlySpan<byte> span, int offset)
        => BinaryPrimitives.ReadInt32BigEndian(span.Slice(offset, 4));

    private void CopyStream(Stream source, Stream destination, long count)
    {
        if (count <= 0) return;
        const int bufferSize = 128 * 1024;
        byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            long remaining = count;
            while (remaining > 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                int toRead = (int)Math.Min(bufferSize, remaining);
                source.ReadExactly(buffer, 0, toRead);
                destination.Write(buffer, 0, toRead);
                remaining -= toRead;
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void IncrementIv(byte[] iv, int startIndex = 12)
    {
        for (int i = iv.Length - 1; i >= startIndex; i--)
        {
            iv[i]++;
            if (iv[i] != 0)
                break;
        }
    }

    private static byte[] CreateEggsHeader(long[] sizeInfo)
    {
        ReadOnlySpan<byte> prefix =
        [
            0x45, 0x47, 0x47, 0x53,  // "EGGS"
            0x00, 0x01, 0x10, 0x11,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x03,
            0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x08,
            0x00, 0x00, 0x00, 0x02
        ];

        byte[] header = new byte[HeaderSize];
        header.AsSpan().Fill(0xFF);
        prefix.CopyTo(header);

        long part0Sectors = sizeInfo[0] / SectorSize;
        long part1Sectors = sizeInfo[1] / SectorSize;

        header[0x24] = (byte)(part0Sectors >> 24);
        header[0x25] = (byte)(part0Sectors >> 16);
        header[0x26] = (byte)(part0Sectors >> 8);
        header[0x27] = (byte)(part0Sectors);
        header[0x28] = (byte)(part1Sectors >> 24);
        header[0x29] = (byte)(part1Sectors >> 16);
        header[0x2A] = (byte)(part1Sectors >> 8);
        header[0x2B] = (byte)(part1Sectors);

        // Footer "SGGE" magic
        header[0x1FC] = 0x53;
        header[0x1FD] = 0x47;
        header[0x1FE] = 0x47;
        header[0x1FF] = 0x45;

        return header;
    }

    public void CombineNFSFiles(string outFile, string nfsDir)
    {
        using var nfs = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.SequentialScan);
        int nfsNo = -1;
        while (File.Exists(Path.Combine(nfsDir, $"hif_{nfsNo + 1:D6}.nfs")))
            nfsNo++;

        Log($"Joining {nfsNo + 1} .nfs chunks...");
        for (int i = 0; i <= nfsNo; i++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            string sourcePath = Path.Combine(nfsDir, $"hif_{i:D6}.nfs");
            using var nfsTemp = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
            if (i == 0)
                nfsTemp.Seek(HeaderSize, SeekOrigin.Begin);
            nfsTemp.CopyTo(nfs);
        }
    }

    public void SplitNFSFile(string inFile)
    {
        using var nfs = new FileStream(inFile, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
        long totalSize = nfs.Length;
        long size = totalSize;
        int i = 0;
        byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            while (size > 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                string outputPath = Path.Combine(_baseDirectory, $"hif_{i:D6}.nfs");
                using var nfsTemp = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.SequentialScan);
                long bytesToCopy = Math.Min(NfsChunkSize, size);
                long copied = 0;
                while (copied < bytesToCopy)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    int toRead = (int)Math.Min(128 * 1024, bytesToCopy - copied);
                    int read = nfs.Read(buffer, 0, toRead);
                    if (read <= 0) break;
                    nfsTemp.Write(buffer, 0, read);
                    copied += read;
                }
                size -= bytesToCopy;

                double splitFraction = totalSize > 0 ? (double)(totalSize - size) / totalSize : 1.0;
                double subProgress = 0.85 + (splitFraction * 0.15);
                ReportProgress($"Splitting NFS chunks: hif_{i:D6}.nfs ({(int)(splitFraction * 100)}%)...", subProgress);
                i++;
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static byte[] GetHeader(string inFile)
    {
        using var file = File.OpenRead(inFile);
        byte[] header = new byte[HeaderSize];
        file.ReadExactly(header);
        return header;
    }

    public long[]? ManipulateISO(string inFile, string outFile, bool enc, byte[] wiiCommonKey)
    {
        using var reader = new FileStream(inFile, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
        using var writer = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.SequentialScan);

        long[] sizeInfo = new long[2];

        // Copy leading disc header (0x40000 bytes)
        CopyStream(reader, writer, 0x40000);

        // Read partition info block (4 entries x 8 bytes)
        byte[] partitionTable = new byte[0x20];
        reader.ReadExactly(partitionTable);
        writer.Write(partitionTable);

        int[,] partitionInfo = new int[2, 4];
        for (byte i = 0; i < 4; i++)
        {
            int tableBase = 0x8 * i;
            partitionInfo[0, i] = ReadBigEndianInt32(partitionTable, tableBase);
            if (partitionInfo[0, i] == 0)
                partitionInfo[1, i] = 0;
            else
                partitionInfo[1, i] = ReadBigEndianInt32(partitionTable, tableBase + 4) * 4;
        }

        partitionInfo = Sort2D(partitionInfo, 4);
        byte[][] partitionInfoTable = new byte[4][];
        var partitionOffsetList = new System.Collections.Generic.List<int>();
        long curPos = 0x40020;

        for (int i = 0; i < 4; i++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (partitionInfo[0, i] != 0)
            {
                long skipBytes = partitionInfo[1, i] - curPos;
                CopyStream(reader, writer, skipBytes);
                curPos += skipBytes;

                int tableSize = 0x8 * partitionInfo[0, i];
                partitionInfoTable[i] = new byte[tableSize];
                reader.ReadExactly(partitionInfoTable[i]);
                curPos += tableSize;

                for (int j = 0; j < partitionInfo[0, i]; j++)
                {
                    if (partitionInfoTable[i][0x7 + 0x8 * j] == 0) // game partition
                    {
                        int partOffset = ReadBigEndianInt32(partitionInfoTable[i], 0x8 * j) * 4;
                        partitionOffsetList.Add(partOffset);
                    }
                }
                writer.Write(partitionInfoTable[i]);
            }
        }

        int[] partitionOffsets = [.. partitionOffsetList];
        if (partitionOffsets.Length == 0)
        {
            Log("ERROR: No data partitions found in ISO!");
            return null;
        }
        Array.Sort(partitionOffsets);
        sizeInfo[0] = partitionOffsets[0];

        byte[] iv = new byte[0x10];
        byte[] ivTemp = new byte[0x10];
        byte[] sector = System.Buffers.ArrayPool<byte>.Shared.Rent(SectorSize);
        int timer = 0;

        try
        {
            for (int i = 0; i < partitionOffsets.Length; i++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                long skipBytes = partitionOffsets[i] - curPos;
                CopyStream(reader, writer, skipBytes);
                curPos += skipBytes;

                // Ticket header
                CopyStream(reader, writer, 0x1BF);

                byte[] encTitleKey = new byte[0x10];
                reader.ReadExactly(encTitleKey);
                writer.Write(encTitleKey);

                CopyStream(reader, writer, 0xD); // bytes until titleID

                byte[] titleID = new byte[0x8];
                reader.ReadExactly(titleID);
                writer.Write(titleID);

                // IV = titleID padded with zeros to 16 bytes
                Array.Clear(iv);
                titleID.AsSpan(0, 8).CopyTo(iv);

                CopyStream(reader, writer, 0xC0); // bytes until end of ticket

                byte[] partitionHeader = new byte[0x1FD5C];
                reader.ReadExactly(partitionHeader);
                long partitionSize = (long)4 * ReadBigEndianInt32(partitionHeader, 0x18);
                writer.Write(partitionHeader);

                curPos += 0x20000;
                curPos += partitionSize;

                // Decrypt title key using Wii Common Key + title IV
                byte[] titleKey = new byte[16];
                using (var aesCommon = Aes.Create())
                {
                    aesCommon.Key = wiiCommonKey;
                    aesCommon.DecryptCbc(encTitleKey, iv, titleKey, PaddingMode.None);
                }

                using (var aesTitle = Aes.Create())
                {
                    aesTitle.Key = titleKey;
                    while (partitionSize >= SectorSize)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        timer++;
                        if (timer >= 500)
                        {
                            timer = 0;
                            double isoFraction = reader.Length > 0 ? (double)reader.Position / reader.Length : 0;
                            double subProgress = Math.Min(0.20, isoFraction * 0.20);
                            ReportProgress($"Processing ISO partitions: {reader.Position / (1024 * 1024)} MB / {reader.Length / (1024 * 1024)} MB ({(int)(isoFraction * 100)}%)...", subProgress);
                        }

                        int read1 = reader.Read(sector, 0, 0x400);
                        if (read1 < 0x400) break;

                        if (enc)
                        {
                            Array.Clear(iv);
                            aesTitle.EncryptCbc(sector.AsSpan(0, 0x400), iv, sector.AsSpan(0, 0x400), PaddingMode.None);
                            writer.Write(sector, 0, 0x400);

                            if (reader.Position >= reader.Length) break;

                            sector.AsSpan(0x3D0, 0x10).CopyTo(ivTemp);
                            int read2 = reader.Read(sector, 0x400, SectorSize - 0x400);
                            if (read2 <= 0) break;

                            aesTitle.EncryptCbc(sector.AsSpan(0x400, read2), ivTemp, sector.AsSpan(0x400, read2), PaddingMode.None);
                            writer.Write(sector, 0x400, read2);
                        }
                        else
                        {
                            sector.AsSpan(0x3D0, 0x10).CopyTo(ivTemp);
                            Array.Clear(iv);
                            aesTitle.DecryptCbc(sector.AsSpan(0, 0x400), iv, sector.AsSpan(0, 0x400), PaddingMode.None);
                            writer.Write(sector, 0, 0x400);

                            if (reader.Position >= reader.Length) break;

                            int read2 = reader.Read(sector, 0x400, SectorSize - 0x400);
                            if (read2 <= 0) break;

                            aesTitle.DecryptCbc(sector.AsSpan(0x400, read2), ivTemp, sector.AsSpan(0x400, read2), PaddingMode.None);
                            writer.Write(sector, 0x400, read2);
                        }

                        partitionSize -= SectorSize;
                    }
                }

                sizeInfo[1] = curPos - sizeInfo[0];
            }

            if (enc)
            {
                long rest = curPos > 0x118240000 ? 0x1FB4E0000 - curPos : 0x118240000 - curPos;
                byte[] zeroBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(SectorSize);
                Array.Clear(zeroBuffer, 0, SectorSize);
                try
                {
                    while (rest > 0)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        int toWrite = rest > SectorSize ? SectorSize : (int)rest;
                        writer.Write(zeroBuffer, 0, toWrite);
                        rest -= SectorSize;
                    }
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(zeroBuffer);
                }
                return null;
            }
            else
            {
                return sizeInfo;
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(sector);
        }
    }

    public void UnpackNFS(string inFile, string outFile, byte[] header)
    {
        using var reader = new FileStream(inFile, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
        using var writer = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.SequentialScan);

        int numberOfParts = ReadBigEndianInt32(header, 0x10);
        byte[] sectorBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(SectorSize);
        byte[] zeroSector = System.Buffers.ArrayPool<byte>.Shared.Rent(SectorSize);
        Array.Clear(zeroSector, 0, SectorSize);

        try
        {
            long pos = 0;
            for (int i = 0; i < numberOfParts; i++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                long start = (long)SectorSize * ReadBigEndianInt32(header, 0x14 + i * 8);
                long length = (long)SectorSize * ReadBigEndianInt32(header, 0x18 + i * 8);

                long zeroCount = start - pos;
                for (long j = 0; j < zeroCount; j += SectorSize)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    writer.Write(zeroSector, 0, SectorSize);
                }

                for (long j = 0; j < length; j += SectorSize)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    reader.ReadExactly(sectorBuffer, 0, SectorSize);
                    writer.Write(sectorBuffer, 0, SectorSize);
                }

                pos = start + length;
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(sectorBuffer);
            System.Buffers.ArrayPool<byte>.Shared.Return(zeroSector);
        }
    }

    public byte[] PackNFS(string inFile, string outFile, long[] sizeInfo)
    {
        using var reader = new FileStream(inFile, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
        using var writer = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.SequentialScan);

        byte[] header = CreateEggsHeader(sizeInfo);
        int numberOfParts = ReadBigEndianInt32(header, 0x10);

        long totalPartsBytes = 0;
        for (int p = 0; p < numberOfParts; p++) totalPartsBytes += (long)SectorSize * ReadBigEndianInt32(header, 0x18 + p * 8);
        long totalPackedBytes = 0;

        byte[] sectorBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(SectorSize);
        try
        {
            long pos = 0;
            for (int i = 0; i < numberOfParts; i++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                long start = (long)SectorSize * ReadBigEndianInt32(header, 0x14 + i * 8);
                long length = (long)SectorSize * ReadBigEndianInt32(header, 0x18 + i * 8);

                long skipCount = start - pos;
                reader.Seek(skipCount, SeekOrigin.Current);

                for (long j = 0; j < length; j += SectorSize)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    reader.ReadExactly(sectorBuffer, 0, SectorSize);
                    writer.Write(sectorBuffer, 0, SectorSize);
                    totalPackedBytes += SectorSize;
                }

                double packFraction = totalPartsBytes > 0 ? (double)totalPackedBytes / totalPartsBytes : (double)(i + 1) / numberOfParts;
                double subProgress = 0.20 + (packFraction * 0.20);
                ReportProgress($"Packing NFS: {totalPackedBytes / (1024 * 1024)} MB ({(int)(packFraction * 100)}%)...", subProgress);

                pos = start + length;
            }
            return header;
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(sectorBuffer);
        }
    }

    public void EnDecryptNFS(string inFile, string outFile, byte[] key, byte[] iv, bool encrypt, byte[] header)
    {
        using var reader = new FileStream(inFile, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
        using var writer = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.SequentialScan);

        if (encrypt)
        {
            writer.Write(header, 0, header.Length);
        }

        byte[] blockIv = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1F, 0x00];
        byte[] sector = System.Buffers.ArrayPool<byte>.Shared.Rent(SectorSize);
        try
        {
            int timer = 0;
            long leftSize = reader.Length;
            long processedBytes = 0;

            using var aes = Aes.Create();
            aes.Key = key;

            do
            {
                _cancellationToken.ThrowIfCancellationRequested();
                timer++;
                if (timer >= 500)
                {
                    timer = 0;
                    double encFraction = reader.Length > 0 ? (double)processedBytes / reader.Length : 0;
                    double subProgress = 0.40 + (encFraction * 0.45);
                    string act = encrypt ? "Encrypting" : "Decrypting";
                    ReportProgress($"{act} NFS content: {processedBytes / (1024 * 1024)} MB / {reader.Length / (1024 * 1024)} MB ({(int)(encFraction * 100)}%)...", subProgress);
                }

                int toRead = leftSize > SectorSize ? SectorSize : (int)leftSize;
                int read = reader.Read(sector, 0, toRead);
                if (read <= 0) break;

                bool useBlockIv = processedBytes >= 0x18000;
                byte[] currentIv = useBlockIv ? blockIv : iv;

                if (encrypt)
                    aes.EncryptCbc(sector.AsSpan(0, read), currentIv, sector.AsSpan(0, read), PaddingMode.None);
                else
                    aes.DecryptCbc(sector.AsSpan(0, read), currentIv, sector.AsSpan(0, read), PaddingMode.None);

                if (useBlockIv)
                    IncrementIv(blockIv);

                writer.Write(sector, 0, read);
                processedBytes += read;
                leftSize -= SectorSize;
            } while (leftSize > 0);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(sector);
        }
    }

    private static int[,] Sort2D(int[,] list, int size)
    {
        if (list == null || size <= 0) return list!;
        var items = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, int>>(size);
        for (int i = 0; i < size; i++) items.Add(new(list[0, i], list[1, i]));
        items.Sort((a, b) => a.Value.CompareTo(b.Value));
        int[,] sorted = new int[2, size];
        for (int i = 0; i < size; i++)
        {
            sorted[0, i] = items[i].Key;
            sorted[1, i] = items[i].Value;
        }
        return sorted;
    }

    private static int PatchBuffer(Span<byte> buffer, ReadOnlySpan<byte> pattern, int writeOffset, ReadOnlySpan<byte> replacement)
    {
        int patchCount = 0;
        int index = 0;
        while (index <= buffer.Length - pattern.Length)
        {
            if (buffer.Slice(index, pattern.Length).SequenceEqual(pattern))
            {
                replacement.CopyTo(buffer.Slice(index + writeOffset, replacement.Length));
                patchCount++;
                index += pattern.Length;
            }
            else
            {
                index++;
            }
        }
        return patchCount;
    }

    private static int PatchBufferCustom(Span<byte> buffer, ReadOnlySpan<byte> pattern, Action<Span<byte>, int> patchAction)
    {
        int patchCount = 0;
        int index = 0;
        while (index <= buffer.Length - pattern.Length)
        {
            if (buffer.Slice(index, pattern.Length).SequenceEqual(pattern))
            {
                patchAction(buffer, index);
                patchCount++;
                index += pattern.Length;
            }
            else
            {
                index++;
            }
        }
        return patchCount;
    }

    public void PatchFirmware(string fwFilePath, NfsConversionOptions options)
    {
        byte[] fileBytes = File.ReadAllBytes(fwFilePath);
        Span<byte> fileSpan = fileBytes.AsSpan();

        if (!options.KeepLegit)
        {
            byte[] oldHashCheck = [0x20, 0x07, 0x23, 0xA2];
            byte[] newHashCheck = [0x20, 0x07, 0x4B, 0x0B];

            for (int offset = 0; offset <= fileSpan.Length - 4; offset++)
            {
                var slice = fileSpan.Slice(offset, 4);
                if (slice.SequenceEqual(oldHashCheck) || slice.SequenceEqual(newHashCheck))
                {
                    fileSpan[offset + 1] = 0x00;
                }
            }
        }

        if (options.MapShoulderToTrigger)
        {
            PatchBuffer(fileSpan, [0x40, 0x05, 0x46, 0xA9], 0, [0x26, 0x80, 0x40, 0x06]);
            PatchBuffer(fileSpan, [0x1C, 0x05, 0x40, 0x35], 0, [0x25, 0x40, 0x40, 0x05]);
            PatchBuffer(fileSpan, [0x23, 0x7F, 0x1C, 0x02], 0, [0x46, 0xB1, 0x23, 0x20, 0x40, 0x03]);
            PatchBuffer(fileSpan, [0x46, 0x53, 0x42, 0x18], 0, [0x23, 0x10, 0x40, 0x03]);
            PatchBuffer(fileSpan, [0x1C, 0x05, 0x80, 0x22], 0, [0x25, 0x40, 0x80, 0x22, 0x40, 0x05]);
        }

        if (options.HorizontalWiimote || options.VerticalWiimote)
        {
            PatchBuffer(fileSpan, [0x16, 0x13, 0x1C, 0x02, 0x40, 0x9A, 0x1C, 0x13], 0, [0x23, 0x00]);
        }

        if (options.HorizontalWiimote)
        {
            PatchBufferCustom(fileSpan, [0x4A, 0x71, 0x42, 0x13, 0xD0, 0xD2, 0x9B, 0x00], (buf, offset) =>
            {
                buf[offset + 0x07] = 0x02;
                buf[offset + 0x0F] = 0x03;
                buf[offset + 0x1D] = 0x01;
                buf[offset + 0x2B] = 0x00;
                buf[offset + 0x65] = 0x07;
                buf[offset + 0x75] = 0x06;
                buf[offset + 0x85] = 0x04;
                buf[offset + 0x95] = 0x05;
            });
        }

        if (options.Homebrew)
        {
            byte[] patchAhbprot = [0x46, 0xC0];
            PatchBufferCustom(fileSpan, [0xD0, 0x0B, 0x23, 0x08, 0x43, 0x13, 0x60, 0x0B], (buf, offset) => patchAhbprot.CopyTo(buf.Slice(offset, 2)));

            byte[] patchMemprot = [0x22, 0x00];
            PatchBufferCustom(fileSpan, [0x01, 0x94, 0xB5, 0x00, 0x4B, 0x08, 0x22, 0x01], (buf, offset) => patchMemprot.CopyTo(buf.Slice(offset + 6, 2)));

            byte[] patchNintendont1 = [0xE5, 0x9F, 0x10, 0x04, 0xE5, 0x91, 0x00, 0x00, 0xE1, 0x2F, 0xFF, 0x10, 0x12, 0xFF, 0xFF, 0xE0];
            PatchBufferCustom(fileSpan, [0xB0, 0xBA, 0x1C, 0x0F], (buf, offset) => patchNintendont1.CopyTo(buf.Slice(offset - 12, 16)));

            byte[] patchNintendont2 = [0x49, 0x01, 0x47, 0x88, 0x46, 0xC0, 0xE0, 0x01, 0x12, 0xFF, 0xFE, 0x00, 0x22, 0x00, 0x23, 0x01, 0x46, 0xC0, 0x46, 0xC0];
            PatchBufferCustom(fileSpan, [0x68, 0x4B, 0x2B, 0x06], (buf, offset) => patchNintendont2.CopyTo(buf.Slice(offset, 20)));

            byte[] patternN3a = [0x0D, 0x80, 0x00, 0x00, 0x0D, 0x80, 0x00, 0x00];
            byte[] patternN3b = [0x00, 0x00, 0x00, 0x02];
            byte[] patchN3 = [0x00, 0x00, 0x00, 0x03];
            for (int offset = 0; offset <= fileSpan.Length - 8; offset++)
            {
                if (fileSpan.Slice(offset, 8).SequenceEqual(patternN3a) && offset + 0x10 + 4 <= fileSpan.Length)
                {
                    if (fileSpan.Slice(offset + 0x10, 4).SequenceEqual(patternN3b))
                    {
                        patchN3.CopyTo(fileSpan.Slice(offset + 0x10, 4));
                    }
                }
            }
        }

        if (options.Passthrough)
        {
            PatchBuffer(fileSpan, [0x20, 0x4B, 0x01, 0x68, 0x18, 0x47, 0x70, 0x00], 3, [0x20, 0x00]);
            PatchBuffer(fileSpan, [0x28, 0x00, 0xD0, 0x03, 0x49, 0x02, 0x22, 0x09], 0, [0xF0, 0x04, 0xFF, 0x21, 0x48, 0x02, 0x21, 0x09, 0xF0, 0x04, 0xFE, 0xF9]);
            PatchBuffer(fileSpan, [0xF0, 0x01, 0xFA, 0xB9], 0, [0xF7, 0xFC, 0xFB, 0x95]);
        }

        if (options.InstantCc)
        {
            PatchBuffer(fileSpan, [0x78, 0x93, 0x21, 0x10, 0x2B, 0x02, 0xD1, 0xB7], 0, [0x78, 0x93, 0x21, 0x10, 0x2B, 0x02, 0x46, 0xC0]);
        }

        if (options.NoCc)
        {
            PatchBuffer(fileSpan, [0x78, 0x93, 0x21, 0x10, 0x2B, 0x02, 0xD1, 0xB7], 0, [0x78, 0x93, 0x21, 0x10, 0x2B, 0x02, 0xE0, 0xB7]);
        }

        File.WriteAllBytes(fwFilePath, fileBytes);
    }
}
