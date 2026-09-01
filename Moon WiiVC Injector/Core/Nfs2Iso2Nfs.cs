using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace Moon_WiiVC_Injector;

public class Nfs2Iso2Nfs(
    string? baseDirectory = null,
    Action<string>? onLog = null,
    IProgress<(string Message, double Progress)>? progress = null,
    CancellationToken cancellationToken = default)
{
    public static int ConvertNfs(
        string[] args,
        string? baseDirectory = null,
        Action<string>? onLog = null,
        IProgress<(string Message, double Progress)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var instance = new Nfs2Iso2Nfs(baseDirectory, onLog, progress, cancellationToken);
        return instance.ConvertNfsInstance(args);
    }

    public const int SECTOR_SIZE = 0x8000;
    public const int HEADER_SIZE = 0x200;
    public byte[] WII_COMMON_KEY = [0xeb, 0xe4, 0x2a, 0x22, 0x5e, 0x85, 0x93, 0xe4, 0x48, 0xd9, 0xc5, 0x45, 0x73, 0x81, 0xaa, 0xf7];
    public const int NFS_SIZE = 0xFA00000;
    public bool dec = false;
    public bool enc = false;
    public bool keepFiles = false;
    public bool keepLegit = false;
    public bool horiz_wiimote = false;
    public bool vert_wiimote = false;
    public bool map_shoulder_to_trigger = false;
    public bool homebrew = false;
    public bool passthrough = false;
    public bool instantcc = false;
    public bool nocc = false;
    public string keyFile = Path.Combine("..", "code", "htk.bin");
    public string isoFile = "game.iso";
    public string wiiKeyFile = "wii_common_key.bin";
    public string nfsDir = "";
    public string fw_file = Path.Combine("..", "code", "fw.img");

    private readonly string _baseDirectory = !string.IsNullOrWhiteSpace(baseDirectory) ? Path.GetFullPath(baseDirectory) : Directory.GetCurrentDirectory();
    private readonly Action<string>? _onLog = onLog;
    private readonly IProgress<(string Message, double Progress)>? _progress = progress;
    private readonly CancellationToken _cancellationToken = cancellationToken;

    private void Log(string message)
    {
        _onLog?.Invoke(message);
        Console.WriteLine(message);
    }

    private void ReportProgress(string message, double percent)
    {
        Log(message);
        _progress?.Report((message, percent));
    }

    private void ResetDefaults()
    {
        dec = false;
        enc = false;
        keepFiles = false;
        keepLegit = false;
        horiz_wiimote = false;
        vert_wiimote = false;
        map_shoulder_to_trigger = false;
        homebrew = false;
        passthrough = false;
        instantcc = false;
        nocc = false;
        keyFile = Path.Combine("..", "code", "htk.bin");
        isoFile = "game.iso";
        wiiKeyFile = "wii_common_key.bin";
        nfsDir = _baseDirectory;
        fw_file = Path.Combine("..", "code", "fw.img");
        WII_COMMON_KEY = [0xeb, 0xe4, 0x2a, 0x22, 0x5e, 0x85, 0x93, 0xe4, 0x48, 0xd9, 0xc5, 0x45, 0x73, 0x81, 0xaa, 0xf7];
    }

    private string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return _baseDirectory;

        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(_baseDirectory, path));
    }

    private bool TryReadOptionValue(string[] args, int index, out string value)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            return false;
        }

        value = args[index + 1];
        return true;
    }

    private void PrintHelp()
    {
        Console.WriteLine("+++++ NFS2ISO2NFS vINTERNAL +++++");
        Console.WriteLine();
        Console.WriteLine("-dec            Decrypt .nfs files to an .iso file.");
        Console.WriteLine("-enc            Encrypt an .iso file to .nfs file(s)");
        Console.WriteLine("-key <file>     Location of AES key file. DEFAULT: code\\htk.bin.");
        Console.WriteLine("-wiikey <file>  Location of Wii Common key file. DEFAULT: wii_common_key.bin.");
        Console.WriteLine("-iso <file>     Location of .iso file. DEFAULT: game.iso.");
        Console.WriteLine("-nfs <file>     Location of .nfs files. DEFAULT: current Directory.");
        Console.WriteLine("-fwimg <file>   Location of fw.img. DEFAULT: code\\fw.img.");
        Console.WriteLine("-keep           Don't delete the files produced in intermediate steps.");
        Console.WriteLine("-legit          Don't patch fw.img to allow fakesigned content");
        Console.WriteLine("-lrpatch        Map emulated Classic Controller's L & R to Gamepad's ZL & ZR");
        Console.WriteLine("-wiimote        Emulate a Wii Remote instead of the Classic Controller");
        Console.WriteLine("-horizontal     Remap Wii Remote d-pad for horizontal usage (implies -wiimote)");
        Console.WriteLine("-homebrew       Various patches to enable proper homebrew functionality");
        Console.WriteLine("-passthrough    Allow homebrew to keep using normal wiimotes with gamepad enabled");
        Console.WriteLine("-instantcc      Report emulated Classic Controller at the very first check");
        Console.WriteLine("-nocc           Report that no Classic Controller is connected");
        Console.WriteLine("-help           Print this text.");
    }

    /// <summary>Reads a signed 32-bit big-endian integer from <paramref name="span"/> at <paramref name="offset"/>.</summary>
    private int ReadBigEndianInt32(ReadOnlySpan<byte> span, int offset)
        => BinaryPrimitives.ReadInt32BigEndian(span.Slice(offset, 4));

    /// <summary>Copies exactly <paramref name="count"/> bytes from <paramref name="source"/> to <paramref name="destination"/> using an ArrayPool buffer.</summary>
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
                int toRead = (int)Math.Min((long)bufferSize, remaining);
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

    private void IncrementIv(byte[] iv, int startIndex = 12)
    {
        for (int i = iv.Length - 1; i >= startIndex; i--)
        {
            iv[i]++;
            if (iv[i] != 0)
                break;
        }
    }

    private byte[] CreateEggsHeader(long[] sizeInfo)
    {
        // Fixed prefix: EGGS magic + version + flags
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

        byte[] header = new byte[HEADER_SIZE];
        header.AsSpan().Fill(0xFF);
        prefix.CopyTo(header);

        // Sector counts for each partition (big-endian 32-bit)
        long part0Sectors = sizeInfo[0] / SECTOR_SIZE;
        long part1Sectors = sizeInfo[1] / SECTOR_SIZE;

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

    public int ConvertNfsInstance(string[] args)
    {
        ResetDefaults();
        Log("");
        if (CheckArgs(args) == -1)
            return -1;
        byte[]? key = CheckKeyFiles();
        if (key == null)
            return -1;

        _cancellationToken.ThrowIfCancellationRequested();

        if (dec)
        {
            byte[] header = GetHeader(Path.Combine(nfsDir, "hif_000000.nfs"));
            string hifNfs = ResolvePath("hif.nfs");
            string hifDecNfs = ResolvePath("hif_dec.nfs");
            string hifUnpackNfs = ResolvePath("hif_unpack.nfs");
            string gameIso = ResolvePath("game.iso");

            CombineNFSFiles(hifNfs);
            _cancellationToken.ThrowIfCancellationRequested();

            EnDecryptNFS(hifNfs, hifDecNfs, key, new byte[key.Length], false, header);
            if (!keepFiles)
                FileUtil.SafeDeleteFile(hifNfs);
            _cancellationToken.ThrowIfCancellationRequested();

            UnpackNFS(hifDecNfs, hifUnpackNfs, header);
            if (!keepFiles)
                FileUtil.SafeDeleteFile(hifDecNfs);
            _cancellationToken.ThrowIfCancellationRequested();

            ManipulateISO(hifUnpackNfs, gameIso, true);
            if (!keepFiles)
                FileUtil.SafeDeleteFile(hifUnpackNfs);
        }
        else if (enc)
        {
            if (!keepLegit || horiz_wiimote || vert_wiimote || map_shoulder_to_trigger)
                DoThePatching(fw_file);

            _cancellationToken.ThrowIfCancellationRequested();
            string hifUnpackNfs = ResolvePath("hif_unpack.nfs");
            string hifDecNfs = ResolvePath("hif_dec.nfs");
            string hifNfs = ResolvePath("hif.nfs");

            long[]? size = ManipulateISO(isoFile, hifUnpackNfs, false);
            if (size == null)
            {
                return -1;
            }
            _cancellationToken.ThrowIfCancellationRequested();

            byte[] header = PackNFS(hifUnpackNfs, hifDecNfs, size);
            if (!keepFiles)
                FileUtil.SafeDeleteFile(hifUnpackNfs);
            _cancellationToken.ThrowIfCancellationRequested();

            EnDecryptNFS(hifDecNfs, hifNfs, key, new byte[key.Length], true, header);
            if (!keepFiles)
                FileUtil.SafeDeleteFile(hifDecNfs);
            _cancellationToken.ThrowIfCancellationRequested();

            SplitNFSFile(hifNfs);
            if (!keepFiles)
                FileUtil.SafeDeleteFile(hifNfs);
        }
        return 0;
    }

    public int CheckArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-dec":
                    dec = true;
                    break;
                case "-enc":
                    enc = true;
                    break;
                case "-keep":
                    keepFiles = true;
                    break;
                case "-legit":
                    keepLegit = true;
                    break;
                case "-key":
                    if (!TryReadOptionValue(args, i, out keyFile))
                        return -1;
                    i++;
                    break;
                case "-wiikey":
                    if (!TryReadOptionValue(args, i, out wiiKeyFile))
                        return -1;
                    i++;
                    break;
                case "-iso":
                    if (!TryReadOptionValue(args, i, out isoFile))
                        return -1;
                    i++;
                    break;
                case "-nfs":
                    if (!TryReadOptionValue(args, i, out nfsDir))
                        return -1;
                    i++;
                    break;
                case "-fwimg":
                    if (!TryReadOptionValue(args, i, out fw_file))
                        return -1;
                    i++;
                    break;
                case "-lrpatch":
                    map_shoulder_to_trigger = true;
                    break;
                case "-wiimote":
                    vert_wiimote = true;
                    break;
                case "-horizontal":
                    horiz_wiimote = true;
                    break;
                case "-homebrew":
                    homebrew = true;
                    break;
                case "-passthrough":
                    passthrough = true;
                    break;
                case "-instantcc":
                    instantcc = true;
                    break;
                case "-nocc":
                    nocc = true;
                    break;
                case "-help":
                    PrintHelp();
                    return -1;
                default:
                    break;
            }
        }

        keyFile = ResolvePath(keyFile);
        isoFile = ResolvePath(isoFile);
        wiiKeyFile = ResolvePath(wiiKeyFile);
        nfsDir = ResolvePath(nfsDir);
        fw_file = ResolvePath(fw_file);

        if (map_shoulder_to_trigger && (horiz_wiimote || vert_wiimote))
        {
            Log("ERROR: Please don't mix patches for Classic Controller and Wii Remote.");
            return -1;
        }

        string nfsFile = Path.Combine(nfsDir, "hif_000000.nfs");
        if (dec || ((!dec && !enc) && File.Exists(nfsFile)))
        {
            Log("+++++ NFS2ISO +++++");
            Log("");
            if (dec && !enc && !File.Exists(nfsFile))
            {
                Log("ERROR: .nfs files not found! Exiting...");
                return -1;
            }
            else if ((!dec && !enc) && File.Exists(nfsFile))
            {
                Log("You haven't specified if you want to use nfs2iso or iso2nfs");
                Log("Found .nfs files! Assuming you want to use nfs2iso...");
                dec = true;
                enc = false;
            }
        }
        else if (enc || ((!dec && !enc) && File.Exists(isoFile)))
        {
            Log("+++++ ISO2NFS +++++");
            Log("");
            if (!dec && enc && !File.Exists(isoFile))
            {
                Log("ERROR: .iso file not found! Exiting...");
                return -1;
            }
            if (!dec && enc && !File.Exists(fw_file))
            {
                Log("ERROR: fw.img not found! Exiting...");
                return -1;
            }
            else if (((dec && enc) || (!dec && !enc)) && File.Exists(isoFile))
            {
                Log("You haven't specified if you want to use nfs2iso or iso2nfs");
                Log("Found .iso file! Assuming you want to use iso2nfs...");
                dec = false;
                enc = true;
            }
        }
        else
        {
            Log("You haven't specified if you want to use nfs2iso or iso2nfs");
            Log("Found neither .iso nor .nfs files! Check -help for usage of this program.");
            return -1;
        }

        return 0;
    }

    public byte[]? CheckKeyFiles()
    {
        Log("Searching for AES key file...");
        if (!File.Exists(keyFile))
        {
            Log($"ERROR: Could not find AES key file at '{keyFile}'! Exiting...");
            return null;
        }
        byte[]? key = GetKey(keyFile);
        if (key == null)
        {
            Log("ERROR: AES key file has wrong file size! Exiting...");
            return null;
        }
        Log("AES key file found!");

        if (WII_COMMON_KEY[0] != 0xeb)
        {
            Log("Wii common key not found in source code. Looking for file...");
            if (!File.Exists(wiiKeyFile))
            {
                Log($"ERROR: Could not find Wii common key file at '{wiiKeyFile}'! Exiting...");
                return null;
            }
            byte[]? wiiKey = GetKey(wiiKeyFile);
            if (wiiKey == null)
            {
                Log("ERROR: Wii common key file has wrong file size! Exiting...");
                return null;
            }
            WII_COMMON_KEY = wiiKey;
            Log("Wii Common Key file found!");
        }
        else Log("Wii common key found in source code!");

        Log("");
        return key;
    }

    public byte[]? GetKey(string keyPath)
    {
        byte[] data = File.ReadAllBytes(keyPath);
        return data.Length == 16 ? data : null;
    }

    public void CombineNFSFiles(string outFile)
    {
        using var nfs = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.SequentialScan);
        Log("Looking for .nfs files...");
        int nfsNo = -1;
        while (File.Exists(Path.Combine(nfsDir, $"hif_{nfsNo + 1:D6}.nfs")))
            nfsNo++;
        Log((nfsNo + 1) + " .nfs files found!");
        Log("Joining .nfs files...");
        Log("");
        for (int i = 0; i <= nfsNo; i++)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            string sourcePath = Path.Combine(nfsDir, $"hif_{i:D6}.nfs");
            Log("Processing hif_" + i.ToString("D6") + ".nfs...");
            using var nfsTemp = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
            if (i == 0)
                nfsTemp.Seek(HEADER_SIZE, SeekOrigin.Begin);
            nfsTemp.CopyTo(nfs);
        }
    }

    public void SplitNFSFile(string inFile)
    {
        using var nfs = new FileStream(inFile, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
        Log("");
        long size = nfs.Length;
        int i = 0;
        byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            while (size > 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                string outputPath = Path.Combine(_baseDirectory, $"hif_{i:D6}.nfs");
                Log("Building hif_" + i.ToString("D6") + ".nfs...");
                using var nfsTemp = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.SequentialScan);
                long bytesToCopy = Math.Min(NFS_SIZE, size);
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
                i++;
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public byte[] GetHeader(string inFile)
    {
        using var file = File.OpenRead(inFile);
        byte[] header = new byte[HEADER_SIZE];
        file.ReadExactly(header);
        return header;
    }

    /// <param name="enc">
    /// When <c>true</c>: encrypt the Wii partition sector data (used when building an ISO from NFS).
    /// When <c>false</c>: decrypt the Wii partition sector data (used when building NFS from an ISO).
    /// </param>
    public long[]? ManipulateISO(string inFile, string outFile, bool enc)
    {
        using var reader = new FileStream(inFile, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.SequentialScan);
        using var writer = new FileStream(outFile, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.SequentialScan);

        long[] sizeInfo = new long[2];

        Log("");
        Log("Read partition table...");
        Log("");

        // Copy leading disc header (0x40000 bytes)
        CopyStream(reader, writer, 0x40000);

        // Read partition info block (4 entries × 8 bytes)
        byte[] partitionTable = new byte[0x20];
        reader.ReadExactly(partitionTable);
        writer.Write(partitionTable);

        int[,] partitionInfo = new int[2, 4]; // [0, i] = count, [1, i] = offset
        for (byte i = 0; i < 4; i++)
        {
            int tableBase = 0x8 * i;
            partitionInfo[0, i] = ReadBigEndianInt32(partitionTable, tableBase);
            Log("Number of " + (i + 1) + ". partitions: " + partitionInfo[0, i]);
            if (partitionInfo[0, i] == 0)
                partitionInfo[1, i] = 0;
            else
                partitionInfo[1, i] = ReadBigEndianInt32(partitionTable, tableBase + 4) * 4;
            Log("Partition info table offset: 0x" + Convert.ToString(partitionInfo[1, i], 16));
        }
        Log("");

        partitionInfo = Sort(partitionInfo, 4);
        byte[][] partitionInfoTable = new byte[4][];
        var partitionOffsetList = new List<int>();
        long curPos = 0x40020;
        int k = 0;

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
                    if (partitionInfoTable[i][0x7 + 0x8 * j] == 0) // check if game partition
                    {
                        int partOffset = ReadBigEndianInt32(partitionInfoTable[i], 0x8 * j) * 4;
                        partitionOffsetList.Add(partOffset);
                        Log("Data partition at offset: 0x" + Convert.ToString(partitionOffsetList[k], 16));
                        k++;
                    }
                }
                writer.Write(partitionInfoTable[i]);
            }
        }

        Log("");
        int[] partitionOffsets = [.. partitionOffsetList];
        if (partitionOffsets.Length == 0)
        {
            Log("ERROR: No data partitions found!");
            return null;
        }
        partitionOffsets = Sort(partitionOffsets, partitionOffsets.Length);
        sizeInfo[0] = partitionOffsets[0];

        byte[] iv = new byte[0x10];
        byte[] ivTemp = new byte[0x10];
        byte[] sector = System.Buffers.ArrayPool<byte>.Shared.Rent(SECTOR_SIZE);
        int timer = 0;
        int mbCounter = 0;

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
                Log("Partition size: 0x" + Convert.ToString(partitionSize, 16));
                writer.Write(partitionHeader);

                curPos += 0x20000;
                curPos += partitionSize;

                // Decrypt title key using Wii Common Key + title IV
                byte[] titleKey = new byte[16];
                using (var aesCommon = Aes.Create())
                {
                    aesCommon.Key = WII_COMMON_KEY;
                    aesCommon.DecryptCbc(encTitleKey, iv, titleKey, PaddingMode.None);
                }
                Log("Write game partition " + i + "...");

                using (var aesTitle = Aes.Create())
                {
                    aesTitle.Key = titleKey;
                    while (partitionSize >= SECTOR_SIZE)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        if (timer == 8000)
                        {
                            timer = 0;
                            mbCounter++;
                            Log((mbCounter * 256) + " MB processed...");
                        }
                        timer++;

                        int read1 = reader.Read(sector, 0, 0x400);
                        if (read1 < 0x400) break;

                        if (enc)
                        {
                            // Encrypt first sub-block (hashes) with zero IV
                            Array.Clear(iv);
                            aesTitle.EncryptCbc(sector.AsSpan(0, 0x400), iv, sector.AsSpan(0, 0x400), PaddingMode.None);
                            writer.Write(sector, 0, 0x400);

                            if (reader.Position >= reader.Length) break;

                            // IV for second sub-block = bytes [0x3D0..0x3DF] of the encrypted first sub-block
                            sector.AsSpan(0x3D0, 0x10).CopyTo(ivTemp);
                            int read2 = reader.Read(sector, 0x400, SECTOR_SIZE - 0x400);
                            if (read2 <= 0) break;

                            aesTitle.EncryptCbc(sector.AsSpan(0x400, read2), ivTemp, sector.AsSpan(0x400, read2), PaddingMode.None);
                            writer.Write(sector, 0x400, read2);
                        }
                        else
                        {
                            // IV for second sub-block = bytes [0x3D0..0x3DF] of the still-encrypted first sub-block
                            sector.AsSpan(0x3D0, 0x10).CopyTo(ivTemp);

                            // Decrypt first sub-block with zero IV
                            Array.Clear(iv);
                            aesTitle.DecryptCbc(sector.AsSpan(0, 0x400), iv, sector.AsSpan(0, 0x400), PaddingMode.None);
                            writer.Write(sector, 0, 0x400);

                            if (reader.Position >= reader.Length) break;

                            int read2 = reader.Read(sector, 0x400, SECTOR_SIZE - 0x400);
                            if (read2 <= 0) break;

                            aesTitle.DecryptCbc(sector.AsSpan(0x400, read2), ivTemp, sector.AsSpan(0x400, read2), PaddingMode.None);
                            writer.Write(sector, 0x400, read2);
                        }

                        partitionSize -= SECTOR_SIZE;
                    }
                }

                sizeInfo[1] = curPos - sizeInfo[0];
                if (partitionSize != 0)
                    Log("WARNING: Last cluster was not complete. This may be a problem.");
            }

            if (enc)
            {
                Log("");
                Log("Writing zeros...");
                long rest = curPos > 0x118240000 ? 0x1FB4E0000 - curPos : 0x118240000 - curPos;
                int zeroTimer = 0;
                int zeroCounter = 0;
                byte[] zeroBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(SECTOR_SIZE);
                Array.Clear(zeroBuffer, 0, SECTOR_SIZE);
                try
                {
                    while (rest > 0)
                    {
                        _cancellationToken.ThrowIfCancellationRequested();
                        if (zeroTimer == 8000)
                        {
                            zeroTimer = 0;
                            zeroCounter++;
                            Log((zeroCounter * 256) + " MB processed...");
                        }
                        zeroTimer++;
                        int toWrite = rest > SECTOR_SIZE ? SECTOR_SIZE : (int)rest;
                        writer.Write(zeroBuffer, 0, toWrite);
                        rest -= SECTOR_SIZE;
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

        Log("");
        Log("Unpacking nfs...");
        Log("");

        int numberOfParts = ReadBigEndianInt32(header, 0x10);
        Log(numberOfParts + " parts found...");

        byte[] sectorBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(SECTOR_SIZE);
        byte[] zeroSector = System.Buffers.ArrayPool<byte>.Shared.Rent(SECTOR_SIZE);
        Array.Clear(zeroSector, 0, SECTOR_SIZE);

        try
        {
            long pos = 0;
            for (int i = 0; i < numberOfParts; i++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                long start = (long)SECTOR_SIZE * ReadBigEndianInt32(header, 0x14 + i * 8);
                long length = (long)SECTOR_SIZE * ReadBigEndianInt32(header, 0x18 + i * 8);

                long zeroCount = start - pos;
                Log("Writing zero segment " + i + " of size 0x" + Convert.ToString(zeroCount, 16));
                for (long j = 0; j < zeroCount; j += SECTOR_SIZE)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    writer.Write(zeroSector, 0, SECTOR_SIZE);
                }

                Log("Writing data segment " + i + " of size 0x" + Convert.ToString(length, 16));
                for (long j = 0; j < length; j += SECTOR_SIZE)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    reader.ReadExactly(sectorBuffer, 0, SECTOR_SIZE);
                    writer.Write(sectorBuffer, 0, SECTOR_SIZE);
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

        Log("");
        Log("Generating EGGS header...");
        byte[] header = CreateEggsHeader(sizeInfo);

        Log("Packing nfs...");

        int numberOfParts = ReadBigEndianInt32(header, 0x10);
        Log("Packing " + numberOfParts + " parts...");

        byte[] sectorBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(SECTOR_SIZE);
        try
        {
            long pos = 0;

            for (int i = 0; i < numberOfParts; i++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                long start = (long)SECTOR_SIZE * ReadBigEndianInt32(header, 0x14 + i * 8);
                long length = (long)SECTOR_SIZE * ReadBigEndianInt32(header, 0x18 + i * 8);

                long skipCount = start - pos;
                Log("Delete zero segment " + i + " of size 0x" + Convert.ToString(skipCount, 16));
                reader.Seek(skipCount, SeekOrigin.Current);

                Log("Writing data segment " + i + " of size 0x" + Convert.ToString(length, 16));
                for (long j = 0; j < length; j += SECTOR_SIZE)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                    reader.ReadExactly(sectorBuffer, 0, SECTOR_SIZE);
                    writer.Write(sectorBuffer, 0, SECTOR_SIZE);
                }

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

        Log("");
        if (encrypt)
        {
            Log("Writing EGGS header...");
            writer.Write(header, 0, header.Length);
            Log("Encrypting hif.nfs...");
        }
        else
            Log("Decrypting hif.nfs...");
        Log("");

        // blockIv is used for sectors at position >= 0x18000 and incremented per sector.
        // The first ~3 sectors use the caller-provided iv.
        byte[] blockIv = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1F, 0x00];
        byte[] sector = System.Buffers.ArrayPool<byte>.Shared.Rent(SECTOR_SIZE);
        try
        {
            int timer = 0;
            int mbCounter = 0;
            long leftSize = reader.Length;
            long processedBytes = 0;

            using var aes = Aes.Create();
            aes.Key = key;

            do
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (timer == 8000)
                {
                    timer = 0;
                    mbCounter++;
                    Log((mbCounter * 256) + " MB processed...");
                }
                timer++;

                int toRead = leftSize > SECTOR_SIZE ? SECTOR_SIZE : (int)leftSize;
                int read = reader.Read(sector, 0, toRead);
                if (read <= 0) break;

                // Use blockIv (incrementing) once position exceeds the initial header region (0x18000 bytes)
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
                leftSize -= SECTOR_SIZE;
            } while (leftSize > 0);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(sector);
        }
    }

    public static int[,] Sort(int[,] list, int size)
    {
        if (list == null)
            throw new ArgumentNullException(nameof(list));
        if (size <= 0)
            return list;

        var items = new List<KeyValuePair<int, int>>(size);
        for (int i = 0; i < size; i++)
            items.Add(new KeyValuePair<int, int>(list[0, i], list[1, i]));

        items.Sort((a, b) => a.Value.CompareTo(b.Value));

        int[,] sorted = new int[2, size];
        for (int i = 0; i < size; i++)
        {
            sorted[0, i] = items[i].Key;
            sorted[1, i] = items[i].Value;
        }

        return sorted;
    }

    public static int[] Sort(int[] list, int size)
    {
        if (list == null)
            throw new ArgumentNullException(nameof(list));
        if (size <= 0 || size > list.Length)
            return list;

        Array.Sort(list, 0, size);
        return list;
    }

    private int PatchBuffer(Span<byte> buffer, ReadOnlySpan<byte> pattern, int writeOffset, ReadOnlySpan<byte> replacement)
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

    private int PatchBufferCustom(Span<byte> buffer, ReadOnlySpan<byte> pattern, Action<Span<byte>, int> patchAction)
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

    public void DoThePatching(string fw_file)
    {
        byte[] fileBytes = File.ReadAllBytes(fw_file);
        Span<byte> fileSpan = fileBytes.AsSpan();

        Log("Checking fw.img's revision number...");

        byte[] revPattern = [0x73, 0x76, 0x6E, 0x2D]; // "svn-"
        string revision = "";

        int revOffset = fileSpan.IndexOf(revPattern);
        if (revOffset >= 0 && revOffset + 8 <= fileSpan.Length)
            revision = System.Text.Encoding.UTF8.GetString(fileSpan.Slice(revOffset + 4, 4));

        if (revision == "r590")
            Log("OK, revision 590 detected.");
        else
            Log($"Warning: {revision} detected. These patches are designed for revision 590 only.");
        Log("");

        Log("Patching fw.img.");
        if (!keepLegit)
        {
            int patchCount = 0;
            byte[] oldHashCheck = [0x20, 0x07, 0x23, 0xA2];
            byte[] newHashCheck = [0x20, 0x07, 0x4B, 0x0B];

            for (int offset = 0; offset <= fileSpan.Length - 4; offset++)
            {
                var slice = fileSpan.Slice(offset, 4);
                if (slice.SequenceEqual(oldHashCheck) || slice.SequenceEqual(newHashCheck))
                {
                    fileSpan[offset + 1] = 0x00;
                    patchCount++;
                }
            }

            if (patchCount == 0)
                Log("Fakesign patching: Nothing to patch.");
            else
                Log($"Fakesigning patching finished... (Patches applied: {patchCount})");

            Log("");
        }

        // Map Classic Controller's L & R to Gamepad's ZL & ZR
        if (map_shoulder_to_trigger)
        {
            int patchCount = 0;

            byte[] pattern1 = [0x40, 0x05, 0x46, 0xA9];
            byte[] patch1 = [0x26, 0x80, 0x40, 0x06];
            byte[] pattern2 = [0x1C, 0x05, 0x40, 0x35];
            byte[] patch2 = [0x25, 0x40, 0x40, 0x05];
            byte[] pattern3 = [0x23, 0x7F, 0x1C, 0x02];
            byte[] patch3 = [0x46, 0xB1, 0x23, 0x20, 0x40, 0x03];
            byte[] pattern4 = [0x46, 0x53, 0x42, 0x18];
            byte[] patch4 = [0x23, 0x10, 0x40, 0x03];
            byte[] pattern5 = [0x1C, 0x05, 0x80, 0x22];
            byte[] patch5 = [0x25, 0x40, 0x80, 0x22, 0x40, 0x05];

            patchCount += PatchBuffer(fileSpan, pattern1, 0, patch1);
            patchCount += PatchBuffer(fileSpan, pattern2, 0, patch2);
            patchCount += PatchBuffer(fileSpan, pattern3, 0, patch3);
            patchCount += PatchBuffer(fileSpan, pattern4, 0, patch4);
            patchCount += PatchBuffer(fileSpan, pattern5, 0, patch5);

            if (patchCount == 0)
                Log("LR to ZLZR patching: Nothing to patch.");
            else
                Log($"LR to ZLZR patching finished. (Patches applied: {patchCount})");

            Log("");
        }

        // Enable Wii Remote emulation
        if (horiz_wiimote || vert_wiimote)
        {
            int patchCount = 0;
            byte[] pattern = [0x16, 0x13, 0x1C, 0x02, 0x40, 0x9A, 0x1C, 0x13];
            byte[] patch = [0x23, 0x00];

            patchCount += PatchBuffer(fileSpan, pattern, 0, patch);

            if (patchCount == 0)
                Log("Wii Remote emulation patching: Nothing to patch.");
            else
                Log($"Wii Remote emulation enabled... (Patches applied: {patchCount})");

            Log("");
        }

        // Enable horizontal Wii Remote emulation (remap d-pad and A/B/1/2 buttons)
        if (horiz_wiimote)
        {
            int patchCount = 0;
            byte[] pattern = [0x4A, 0x71, 0x42, 0x13, 0xD0, 0xD2, 0x9B, 0x00];

            patchCount += PatchBufferCustom(fileSpan, pattern, (buf, offset) =>
            {
                buf[offset + 0x07] = 0x02; // dpad left  -> down
                buf[offset + 0x0F] = 0x03; // dpad right -> up
                buf[offset + 0x1D] = 0x01; // dpad down  -> right
                buf[offset + 0x2B] = 0x00; // dpad up    -> left
                buf[offset + 0x65] = 0x07; // B -> 2
                buf[offset + 0x75] = 0x06; // A -> 1
                buf[offset + 0x85] = 0x04; // 1 -> B
                buf[offset + 0x95] = 0x05; // 2 -> A
            });

            if (patchCount == 0)
                Log("Horizontal Wii Remote patching: Nothing to patch.");
            else
                Log($"Horizontal Wii Remote emulation enabled... (Patches applied: {patchCount})");

            Log("");
        }

        // Enable proper input support in homebrew
        if (homebrew)
        {
            Log("Homebrew-related patches:");
            int patchCount = 0;

            // Disable AHBPROT
            byte[] patternAhbprot = [0xD0, 0x0B, 0x23, 0x08, 0x43, 0x13, 0x60, 0x0B];
            byte[] patchAhbprot = [0x46, 0xC0];
            patchCount += PatchBufferCustom(fileSpan, patternAhbprot, (buf, offset) =>
            {
                Log("* Disabling AHBPROT...");
                patchAhbprot.CopyTo(buf.Slice(offset, 2));
            });

            // Disable MEMPROT
            byte[] patternMemprot = [0x01, 0x94, 0xB5, 0x00, 0x4B, 0x08, 0x22, 0x01];
            byte[] patchMemprot = [0x22, 0x00];
            patchCount += PatchBufferCustom(fileSpan, patternMemprot, (buf, offset) =>
            {
                Log("* Disabling MEMPROT...");
                patchMemprot.CopyTo(buf.Slice(offset + 6, 2));
            });

            // Nintendont patch 1
            byte[] patternNintendont1 = [0xB0, 0xBA, 0x1C, 0x0F];
            byte[] patchNintendont1 = [0xE5, 0x9F, 0x10, 0x04, 0xE5, 0x91, 0x00, 0x00, 0xE1, 0x2F, 0xFF, 0x10, 0x12, 0xFF, 0xFF, 0xE0];
            patchCount += PatchBufferCustom(fileSpan, patternNintendont1, (buf, offset) =>
            {
                Log("* Nintendont patch 1...");
                patchNintendont1.CopyTo(buf.Slice(offset - 12, 16));
            });

            // Nintendont patch 2
            byte[] patternNintendont2 = [0x68, 0x4B, 0x2B, 0x06];
            byte[] patchNintendont2 = [0x49, 0x01, 0x47, 0x88, 0x46, 0xC0, 0xE0, 0x01, 0x12, 0xFF, 0xFE, 0x00, 0x22, 0x00, 0x23, 0x01, 0x46, 0xC0, 0x46, 0xC0];
            patchCount += PatchBufferCustom(fileSpan, patternNintendont2, (buf, offset) =>
            {
                Log("* Nintendont patch 2...");
                patchNintendont2.CopyTo(buf.Slice(offset, 20));
            });

            // Nintendont patch 3 (two-stage search)
            byte[] patternNintendont3a = [0x0D, 0x80, 0x00, 0x00, 0x0D, 0x80, 0x00, 0x00];
            byte[] patternNintendont3b = [0x00, 0x00, 0x00, 0x02];
            byte[] patchNintendont3 = [0x00, 0x00, 0x00, 0x03];
            for (int offset = 0; offset <= fileSpan.Length - 8; offset++)
            {
                if (fileSpan.Slice(offset, 8).SequenceEqual(patternNintendont3a))
                {
                    if (offset + 0x10 + 4 <= fileSpan.Length)
                    {
                        var target = fileSpan.Slice(offset + 0x10, 4);
                        if (target.SequenceEqual(patternNintendont3b))
                        {
                            Log("* Nintendont patch 3...");
                            patchNintendont3.CopyTo(fileSpan.Slice(offset + 0x10, 4));
                            patchCount++;
                        }
                    }
                }
            }

            if (patchCount == 0)
                Log("Homebrew patching: Nothing to patch.");
            else
                Log($"Homebrew patching finished... (Patches applied: {patchCount})");

            Log("");
        }

        // Allow homebrew to keep using normal Wiimotes with gamepad enabled
        if (passthrough)
        {
            Log("Wiimote Passthrough patching:");
            int patchCount = 0;

            byte[] patternPassthrough = [0x20, 0x4B, 0x01, 0x68, 0x18, 0x47, 0x70, 0x00];
            byte[] patchPassthrough = [0x20, 0x00];
            patchCount += PatchBuffer(fileSpan, patternPassthrough, 3, patchPassthrough);

            byte[] patternCustomFunc = [0x28, 0x00, 0xD0, 0x03, 0x49, 0x02, 0x22, 0x09];
            byte[] patchCustomFunc = [0xF0, 0x04, 0xFF, 0x21, 0x48, 0x02, 0x21, 0x09, 0xF0, 0x04, 0xFE, 0xF9];
            patchCount += PatchBuffer(fileSpan, patternCustomFunc, 0, patchCustomFunc);

            byte[] patternCustomCall = [0xF0, 0x01, 0xFA, 0xB9];
            byte[] patchCustomCall = [0xF7, 0xFC, 0xFB, 0x95];
            patchCount += PatchBuffer(fileSpan, patternCustomCall, 0, patchCustomCall);

            if (patchCount == 0)
                Log("Wiimote Passthrough patching: Nothing to patch.");
            else
                Log($"Wiimote Passthrough patching finished... (Patches applied: {patchCount})");

            Log("");
        }

        // Report Classic Controller at first check
        if (instantcc)
        {
            int patchCount = 0;
            byte[] pattern = [0x78, 0x93, 0x21, 0x10, 0x2B, 0x02, 0xD1, 0xB7];
            byte[] patch = [0x78, 0x93, 0x21, 0x10, 0x2B, 0x02, 0x46, 0xC0];

            patchCount += PatchBuffer(fileSpan, pattern, 0, patch);

            if (patchCount == 0)
                Log("Instant Classic Controller report patching: Nothing to patch.");
            else
                Log($"Instant Classic Controller report patched... (Patches applied: {patchCount})");

            Log("");
        }

        // Report no Classic Controller connected
        if (nocc)
        {
            int patchCount = 0;
            byte[] pattern = [0x78, 0x93, 0x21, 0x10, 0x2B, 0x02, 0xD1, 0xB7];
            byte[] patch = [0x78, 0x93, 0x21, 0x10, 0x2B, 0x02, 0xE0, 0xB7];

            patchCount += PatchBuffer(fileSpan, pattern, 0, patch);

            if (patchCount == 0)
                Log("No Classic Controller report patching: Nothing to patch.");
            else
                Log($"No Classic Controller report patched... (Patches applied: {patchCount})");

            Log("");
        }

        File.WriteAllBytes(fw_file, fileBytes);
    }
}
