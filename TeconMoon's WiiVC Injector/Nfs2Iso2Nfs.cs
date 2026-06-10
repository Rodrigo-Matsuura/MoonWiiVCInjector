using System.Security.Cryptography;

namespace TeconMoon_s_WiiVC_Injector
{
    public class Nfs2Iso2Nfs
    {
        public const int SECTOR_SIZE = 0x8000;
        public const int HEADER_SIZE = 0x200;
        public static byte[] WII_COMMON_KEY = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        public const int NFS_SIZE = 0xFA00000;
        public static bool dec = false;
        public static bool enc = false;
        public static bool keepFiles = false;
        public static bool keepLegit = false;
        public static bool horiz_wiimote = false;
        public static bool vert_wiimote = false;
        public static bool map_shoulder_to_trigger = false;
        public static bool homebrew = false;
        public static bool passthrough = false;
        public static bool instantcc = false;
        public static bool nocc = false;
        public static string keyFile = Path.Combine("..", "code", "htk.bin");
        public static string isoFile = "game.iso";
        public static string wiiKeyFile = "wii_common_key.bin";
        public static string nfsDir = "";
        public static string fw_file = Path.Combine("..", "code", "fw.img");

        private static void ResetDefaults()
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
            nfsDir = "";
            fw_file = Path.Combine("..", "code", "fw.img");
            WII_COMMON_KEY = new byte[16];
        }

        private static string ResolvePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return Directory.GetCurrentDirectory();

            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), path));
        }

        private static bool TryReadOptionValue(string[] args, int index, out string value)
        {
            if (index + 1 >= args.Length)
            {
                value = string.Empty;
                return false;
            }

            value = args[index + 1];
            return true;
        }

        private static void PrintHelp()
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

        private static int PatchStream(Stream stream, byte[] pattern, Action<Stream, long> patchAction)
        {
            if (stream == null || pattern == null || patchAction == null)
                return 0;

            byte[] buffer = new byte[pattern.Length];
            int patchCount = 0;
            long lastOffset = stream.Length - pattern.Length;

            for (long offset = 0; offset <= lastOffset; offset++)
            {
                stream.Position = offset;
                stream.ReadExactly(buffer, 0, buffer.Length);

                if (ByteArrayCompare(buffer, pattern))
                {
                    patchAction(stream, offset);
                    patchCount++;
                }
            }

            return patchCount;
        }

        private static int PatchStreamWithPattern(Stream stream, byte[] pattern, long writeOffset, byte[] replacement)
        {
            return PatchStream(stream, pattern, (s, offset) =>
            {
                s.Seek(offset + writeOffset, SeekOrigin.Begin);
                s.Write(replacement, 0, replacement.Length);
            });
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
            var header = Enumerable.Repeat((byte)0xFF, HEADER_SIZE).ToArray();

            header[0x0] = 0x45;
            header[0x1] = 0x47;
            header[0x2] = 0x47;
            header[0x3] = 0x53;
            header[0x4] = 0x00;
            header[0x5] = 0x01;
            header[0x6] = 0x10;
            header[0x7] = 0x11;
            header[0x8] = 0x00;
            header[0x9] = 0x00;
            header[0xA] = 0x00;
            header[0xB] = 0x00;
            header[0xC] = 0x00;
            header[0xD] = 0x00;
            header[0xE] = 0x00;
            header[0xF] = 0x00;
            header[0x10] = 0x00;
            header[0x11] = 0x00;
            header[0x12] = 0x00;
            header[0x13] = 0x03;
            header[0x14] = 0x00;
            header[0x15] = 0x00;
            header[0x16] = 0x00;
            header[0x17] = 0x00;
            header[0x18] = 0x00;
            header[0x19] = 0x00;
            header[0x1A] = 0x00;
            header[0x1B] = 0x01;
            header[0x1C] = 0x00;
            header[0x1D] = 0x00;
            header[0x1E] = 0x00;
            header[0x1F] = 0x08;
            header[0x20] = 0x00;
            header[0x21] = 0x00;
            header[0x22] = 0x00;
            header[0x23] = 0x02;
            header[0x24] = (byte)((sizeInfo[0] / 0x8000) / 0x1000000);
            header[0x25] = (byte)(((sizeInfo[0] / 0x8000) / 0x10000) % 0x100);
            header[0x26] = (byte)(((sizeInfo[0] / 0x8000) / 0x100) % 0x10000);
            header[0x27] = (byte)((sizeInfo[0] / 0x8000) % 0x1000000);
            header[0x28] = (byte)((sizeInfo[1] / 0x8000) / 0x1000000);
            header[0x29] = (byte)(((sizeInfo[1] / 0x8000) / 0x10000) % 0x100);
            header[0x2A] = (byte)(((sizeInfo[1] / 0x8000) / 0x100) % 0x10000);
            header[0x2B] = (byte)((sizeInfo[1] / 0x8000) % 0x1000000);
            header[0x1FC] = 0x53;
            header[0x1FD] = 0x47;
            header[0x1FE] = 0x47;
            header[0x1FF] = 0x45;

            return header;
        }

        public static int ConvertNfs(string[] args)
        {
            ResetDefaults();
            Console.WriteLine();
            if (checkArgs(args) == -1)
                return -1;
            byte[]? key = checkKeyFiles();
            if (key == null)
                return -1;
            if (dec)
            {
                byte[] header = getHeader(Path.Combine(nfsDir, "hif_000000.nfs"));
                combineNFSFiles("hif.nfs");
                EnDecryptNFS("hif.nfs", "hif_dec.nfs", key, buildZero(key.Length), false, header);
                if (!keepFiles)
                    File.Delete("hif.nfs");
                unpackNFS("hif_dec.nfs", "hif_unpack.nfs", header);
                if (!keepFiles)
                    File.Delete("hif_dec.nfs");
                manipulateISO("hif_unpack.nfs", "game.iso", true);
                if (!keepFiles)
                    File.Delete("hif_unpack.nfs");
            }
            else if (enc)
            {
                if (!keepLegit || horiz_wiimote || vert_wiimote || map_shoulder_to_trigger)
                    DoThePatching(fw_file);
                long[]? size = manipulateISO(isoFile, "hif_unpack.nfs", false);
                if (size == null)
                {
                    return -1;
                }
                byte[] header = packNFS("hif_unpack.nfs", "hif_dec.nfs", size);
                if (!keepFiles)
                    File.Delete("hif_unpack.nfs");
                EnDecryptNFS("hif_dec.nfs", "hif.nfs", key, buildZero(key.Length), true, header);
                if (!keepFiles)
                    File.Delete("hif_dec.nfs");
                splitNFSFile("hif.nfs");
                if (!keepFiles)
                    File.Delete("hif.nfs");
            }
            return 0;
        }

        public static int checkArgs(string[] args)
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
                Console.WriteLine("ERROR: Please don't mix patches for Classic Controller and  Wii Remote.");
                return -1;
            }

            string nfsFile = Path.Combine(nfsDir, "hif_000000.nfs");
            if (dec || ((!dec && !enc) && File.Exists(nfsFile)))
            {
                Console.WriteLine("+++++ NFS2ISO +++++");
                Console.WriteLine();
                if (dec && !enc && !File.Exists(nfsFile))
                {
                    Console.WriteLine("ERROR: .nfs files not found! Exiting...");
                    return -1;
                }
                else if ((!dec && !enc) && File.Exists(nfsFile))
                {
                    Console.WriteLine("You haven't specified if you want to use nfs2iso or iso2nfs");
                    Console.WriteLine("Found .nfs files! Assuming you want to use nfs2iso...");
                    dec = true;
                    enc = false;
                }
            }
            else if (enc || ((!dec && !enc) && File.Exists(isoFile)))
            {
                Console.WriteLine("+++++ ISO2NFS +++++");
                Console.WriteLine();
                if (!dec && enc && !File.Exists(isoFile))
                {
                    Console.WriteLine("ERROR: .iso file not found! Exiting...");
                    return -1;
                }
                if (!dec && enc && !File.Exists(fw_file))
                {
                    Console.WriteLine("ERROR: fw.img not found! Exiting...");
                    return -1;
                }
                else if (((dec && enc) || (!dec && !enc)) && File.Exists(isoFile))
                {
                    Console.WriteLine("You haven't specified if you want to use nfs2iso or iso2nfs");
                    Console.WriteLine("Found .iso file!  Assuming you want to use iso2nfs...");
                    dec = false;
                    enc = true;
                }
            }
            else
            {
                Console.WriteLine("You haven't specified if you want to use nfs2iso or iso2nfs");
                Console.WriteLine("Found neither .iso nor .nfs files! Check -help for usage of this program.");
                return -1;
            }

            return 0;
        }

        public static byte[]? checkKeyFiles()
        {
            Console.WriteLine("Searching for AES key file...");
            if (!File.Exists(keyFile))
            {
                Console.WriteLine("ERROR: Could not find AES key file! Exiting...");
                return null;
            }
            byte[]? key = getKey(keyFile);
            if (key == null)
            {
                Console.WriteLine("ERROR: AES key file has wrong file size! Exiting...");
                return null;
            }
            Console.WriteLine("AES key file found!");

            if (WII_COMMON_KEY[0] != 0xeb)
            {
                Console.WriteLine("Wii common key not found in source code. Looking for file...");
                if (!File.Exists(wiiKeyFile))
                {
                    Console.WriteLine("ERROR: Could not find Wii common key file! Exiting...");
                    return null;
                }
                byte[]? wiiKey = getKey(wiiKeyFile);
                if (wiiKey == null)
                {
                    Console.WriteLine("ERROR: Wii common key file has wrong file size! Exiting...");
                    return null;
                }
                WII_COMMON_KEY = wiiKey;
                Console.WriteLine("Wii Common Key file found!");
            }
            else Console.WriteLine("Wii common key found in source code!");

            Console.WriteLine();
            return key;
        }

        public static byte[]? getKey(string keyDir)
        {
            using (var keyFile = new BinaryReader(File.OpenRead(keyDir)))
            {
                long keySize = keyFile.BaseStream.Length;
                if (keySize != 16)
                    return null;
                return keyFile.ReadBytes(0x10);
            }
        }

        public static byte[] buildZero(int size)
        {
            return new byte[size];
        }

        public static void combineNFSFiles(string outFile)
        {
            using (var nfs = File.Create(outFile))
            {
                Console.WriteLine("Looking for .nfs files...");
                int nfsNo = -1;
                while (File.Exists(Path.Combine(nfsDir, $"hif_{nfsNo + 1:D6}.nfs")))
                    nfsNo++;
                Console.WriteLine((nfsNo + 1) + " .nfs files found!");
                Console.WriteLine("Joining .nfs files...");
                Console.WriteLine();
                for (int i = 0; i <= nfsNo; i++)
                {
                    string sourcePath = Path.Combine(nfsDir, $"hif_{i:D6}.nfs");
                    Console.WriteLine("Processing hif_" + i.ToString("D6") + ".nfs...");
                    using (var nfsTemp = File.OpenRead(sourcePath))
                    {
                        if (i == 0)
                        {
                            nfsTemp.Seek(HEADER_SIZE, SeekOrigin.Begin);
                        }
                        nfsTemp.CopyTo(nfs);
                    }
                }
            }
        }

        public static void splitNFSFile(string inFile)
        {
            using (var nfs = File.OpenRead(inFile))
            {
                Console.WriteLine();
                long size = nfs.Length;
                int i = 0;
                byte[] buffer = new byte[81920]; // 80KB buffer
                while (size > 0)
                {
                    string outputPath = Path.Combine(Directory.GetCurrentDirectory(), $"hif_{i:D6}.nfs");
                    Console.WriteLine("Building hif_" + i.ToString("D6") + ".nfs...");
                    using (var nfsTemp = File.Create(outputPath))
                    {
                        long bytesToCopy = Math.Min(NFS_SIZE, size);
                        long copied = 0;
                        while (copied < bytesToCopy)
                        {
                            int toRead = (int)Math.Min(buffer.Length, bytesToCopy - copied);
                            int read = nfs.Read(buffer, 0, toRead);
                            if (read <= 0) break;
                            nfsTemp.Write(buffer, 0, read);
                            copied += read;
                        }
                    }
                    size -= NFS_SIZE;
                    i++;
                }
            }
        }

        public static byte[] getHeader(string inFile)
        {
            using (var file = new BinaryReader(File.OpenRead(inFile)))
            {
                return file.ReadBytes(0x200);
            }
        }

        public static long[]? manipulateISO(string InFile, string OutFile, bool enc)
        {
            using (var er = new BinaryReader(File.OpenRead(InFile)))
            using (var ew = new BinaryWriter(File.Create(OutFile)))
            {
                long[] sizeInfo = new long[2];

                Console.WriteLine();
                Console.WriteLine("Read partition table...");
                Console.WriteLine();
                ew.Write(er.ReadBytes(0x40000));

                byte[] partitionTable = er.ReadBytes(0x20);
                ew.Write(partitionTable);
                int[,] partitionInfo = new int[2, 4];            //first coorfinate number of partitions, second offset of partition table
                for (byte i = 0; i < 4; i++)
                {
                    partitionInfo[0, i] = partitionTable[0x0 + 0x8 * i] * 0x1000000 + partitionTable[0x1 + 0x8 * i] * 0x10000 + partitionTable[0x2 + 0x8 * i] * 0x100 + partitionTable[0x3 + 0x8 * i];
                    Console.WriteLine("Number of " + (i + 1) + ". partitions: " + partitionInfo[0, i]);
                    if (partitionInfo[0, i] == 0)
                        partitionInfo[1, i] = 0;
                    else partitionInfo[1, i] = (partitionTable[0x4 + 0x8 * i] * 0x1000000 + partitionTable[0x5 + 0x8 * i] * 0x10000 + partitionTable[0x6 + 0x8 * i] * 0x100 + partitionTable[0x7 + 0x8 * i]) * 0x4;
                    Console.WriteLine("Partition info table offset: 0x" + Convert.ToString(partitionInfo[1, i], 16));
                }
                Console.WriteLine();
                partitionInfo = sort(partitionInfo, 4);
                byte[][] partitionInfoTable = new byte[4][];
                List<int> partitionOffsetList = new List<int>();
                long curPos = 0x40020;
                int k = 0;
                for (int i = 0; i < 4; i++)
                {
                    if (partitionInfo[0, i] != 0)
                    {
                        ew.Write(er.ReadBytes((int)(partitionInfo[1, i] - curPos)));
                        curPos += (partitionInfo[1, i] - curPos);
                        partitionInfoTable[i] = er.ReadBytes(0x8 * partitionInfo[0, i]);
                        curPos += (0x8 * partitionInfo[0, i]);
                        for (int j = 0; j < partitionInfo[0, i]; j++)
                            if (partitionInfoTable[i][0x7 + 0x8 * j] == 0) //check if game partition
                            {
                                partitionOffsetList.Add((partitionInfoTable[i][0x0 + 0x8 * j] * 0x1000000 + partitionInfoTable[i][0x1 + 0x8 * j] * 0x10000 + partitionInfoTable[i][0x2 + 0x8 * j] * 0x100 + partitionInfoTable[i][0x3 + 0x8 * j]) * 0x4);
                                Console.WriteLine("Data partition at offset: 0x" + Convert.ToString(partitionOffsetList[k], 16));
                                k++;
                            }
                        ew.Write(partitionInfoTable[i]);
                    }
                }
                Console.WriteLine();
                int[] partitionOffsets = partitionOffsetList.ToArray();
                partitionOffsets = sort(partitionOffsets, partitionOffsets.Length);
                sizeInfo[0] = partitionOffsets[0];
                byte[] IV = new byte[0x10];
                int timer = 0;
                int l = 0;
                for (int i = 0; i < partitionOffsets.Length; i++)
                {
                    ew.Write(er.ReadBytes((int)(partitionOffsets[i] - curPos)));
                    curPos += (partitionOffsets[i] - curPos);
                    ew.Write(er.ReadBytes(0x1BF));                              //Write start of partiton
                    byte[] enc_titlekey = er.ReadBytes(0x10);                   //read encrypted titlekey
                    ew.Write(enc_titlekey);                                     //Write encrypted titlekey
                    ew.Write(er.ReadBytes(0xD));                                //Write bytes till titleID
                    byte[] titleID = er.ReadBytes(0x8);                         //read titleID
                    ew.Write(titleID);
                    for (int j = 0; j < 0x10; j++)                              //build IV
                        if (j < 8)
                            IV[j] = titleID[j];
                        else IV[j] = 0x0;
                    ew.Write(er.ReadBytes(0xC0));                               //Write bytes till end of ticket
                    byte[] partitionHeader = er.ReadBytes(0x1FD5C);
                    long partitionSize = (long)0x4 * (partitionHeader[0x18] * 0x1000000 + partitionHeader[0x19] * 0x10000 + partitionHeader[0x1A] * 0x100 + partitionHeader[0x1B]);
                    Console.WriteLine("Partition size: 0x" + Convert.ToString(partitionSize, 16));
                    ew.Write(partitionHeader);                                  //Write bytes till start of partition data
                    curPos += 0x20000;
                    curPos += partitionSize;

                    byte[] titlekey = new byte[16];
                    using (var aesCommon = Aes.Create())
                    {
                        aesCommon.Key = WII_COMMON_KEY;
                        aesCommon.DecryptCbc(enc_titlekey, IV, titlekey, PaddingMode.None);
                    }
                    Console.WriteLine("Write game partition " + i + "...");
                    
                    byte[] Sector = new byte[SECTOR_SIZE];
                    byte[] IV_temp = new byte[16];
                    using (var aesTitle = Aes.Create())
                    {
                        aesTitle.Key = titlekey;
                        while (partitionSize >= SECTOR_SIZE)
                        {
                            if (timer == 8000)
                            {
                                timer = 0;
                                l++;
                                Console.WriteLine((l * 256) + " MB processed...");
                            }
                            timer++;

                            if (enc)
                            {
                                int read1 = er.BaseStream.Read(Sector, 0, 0x400);
                                if (read1 < 0x400) break;

                                Array.Clear(IV, 0, 0x10);
                                aesTitle.EncryptCbc(Sector.AsSpan(0, 0x400), IV, Sector.AsSpan(0, 0x400), PaddingMode.None);
                                ew.Write(Sector, 0, 0x400);

                                if (er.BaseStream.Position >= er.BaseStream.Length)
                                {
                                    break;
                                }
                                Array.Copy(Sector, 0x3D0, IV, 0, 0x10);
                                int read2 = er.BaseStream.Read(Sector, 0x400, SECTOR_SIZE - 0x400);
                                if (read2 <= 0) break;

                                aesTitle.EncryptCbc(Sector.AsSpan(0x400, read2), IV, Sector.AsSpan(0x400, read2), PaddingMode.None);
                                ew.Write(Sector, 0x400, read2);
                            }
                            else
                            {
                                int read1 = er.BaseStream.Read(Sector, 0, 0x400);
                                if (read1 < 0x400) break;

                                Array.Copy(Sector, 0x3D0, IV, 0, 0x10);
                                Array.Clear(IV_temp, 0, 0x10);
                                aesTitle.DecryptCbc(Sector.AsSpan(0, 0x400), IV_temp, Sector.AsSpan(0, 0x400), PaddingMode.None);
                                ew.Write(Sector, 0, 0x400);

                                if (er.BaseStream.Position >= er.BaseStream.Length)
                                {
                                    break;
                                }
                                int read2 = er.BaseStream.Read(Sector, 0x400, SECTOR_SIZE - 0x400);
                                if (read2 <= 0) break;

                                aesTitle.DecryptCbc(Sector.AsSpan(0x400, read2), IV, Sector.AsSpan(0x400, read2), PaddingMode.None);
                                ew.Write(Sector, 0x400, read2);
                            }

                            partitionSize -= SECTOR_SIZE;
                        }
                    }

                    sizeInfo[1] = curPos - sizeInfo[0];
                    if (partitionSize != 0)
                        Console.WriteLine("WARNING: Last cluster was not complete. This may be a problem.");
                }
                if (enc)
                {
                    Console.WriteLine();
                    Console.WriteLine("Writing zeros...");
                    long rest;
                    if (curPos > 0x118240000)
                        rest = 0x1FB4E0000 - curPos;
                    else rest = 0x118240000 - curPos;
                    l = 0;
                    timer = 0;
                    byte[] zeroBuffer = new byte[SECTOR_SIZE];
                    while (rest > 0)
                    {
                        if (timer == 8000)
                        {
                            timer = 0;
                            l++;
                            Console.WriteLine((l * 256) + " MB processed...");
                        }
                        timer++;
                        int toWrite = rest > SECTOR_SIZE ? SECTOR_SIZE : (int)rest;
                        ew.Write(zeroBuffer, 0, toWrite);
                        rest -= SECTOR_SIZE;
                    }
                    return null;
                }
                else return sizeInfo;
            }
        }

        public static void unpackNFS(string InFile, string OutFile, byte[] header)
        {
            using (var er = new BinaryReader(File.OpenRead(InFile)))
            using (var ew = new BinaryWriter(File.Create(OutFile)))
            {
                Console.WriteLine();
                Console.WriteLine("Unpacking nfs...");
                Console.WriteLine();
                int numberOfParts = 0x1000000 * header[0x10] + 0x10000 * header[0x11] + 0x100 * header[0x12] + header[0x13];
                Console.WriteLine(numberOfParts + " parts found...");
                long start, length;
                long pos = 0x0;
                long j = 0;
                for (int i = 0; i < numberOfParts; i++)
                {
                    start = (long)SECTOR_SIZE * ((long)0x1000000 * (long)header[0x14 + i * 0x8] + (long)0x10000 * (long)header[0x15 + i * 0x8] + (long)0x100 * (long)header[0x16 + i * 0x8] + (long)header[0x17 + i * 0x8]);
                    length = (long)SECTOR_SIZE * ((long)0x1000000 * (long)header[0x18 + i * 0x8] + (long)0x10000 * (long)header[0x19 + i * 0x8] + (long)0x100 * (long)header[0x1A + i * 0x8] + (long)header[0x1B + i * 0x8]);
                    j = start - pos;
                    Console.WriteLine("Writing zero segment " + i + " of size 0x" + Convert.ToString(j, 16));
                    while (j > 0)
                    {
                        ew.Write(buildZero(SECTOR_SIZE));
                        j -= SECTOR_SIZE;
                    }
                    Console.WriteLine("Writing data segment " + i + " of size 0x" + Convert.ToString(length, 16));
                    j = length;
                    while (j > 0)
                    {
                        ew.Write(er.ReadBytes(SECTOR_SIZE));
                        j -= SECTOR_SIZE;
                    }
                    pos = start + length;
                }
            }
        }

        public static byte[] packNFS(string InFile, string OutFile, long[] sizeInfo)
        {
            using (var er = new BinaryReader(File.OpenRead(InFile)))
            using (var ew = new BinaryWriter(File.Create(OutFile)))
            {
                Console.WriteLine();
                Console.WriteLine("Generating EGGS header...");
                byte[] header = CreateEggsHeader(sizeInfo);

                Console.WriteLine("Packing nfs...");

                int numberOfParts = 0x1000000 * header[0x10] + 0x10000 * header[0x11] + 0x100 * header[0x12] + header[0x13];
                Console.WriteLine("Packing " + numberOfParts + " parts...");
                long start, length;
                long pos = 0x0;
                long j = 0;
                for (int i = 0; i < numberOfParts; i++)
                {
                    start = (long)SECTOR_SIZE * ((long)0x1000000 * (long)header[0x14 + i * 0x8] + (long)0x10000 * (long)header[0x15 + i * 0x8] + (long)0x100 * (long)header[0x16 + i * 0x8] + (long)header[0x17 + i * 0x8]);
                    length = (long)SECTOR_SIZE * ((long)0x1000000 * (long)header[0x18 + i * 0x8] + (long)0x10000 * (long)header[0x19 + i * 0x8] + (long)0x100 * (long)header[0x1A + i * 0x8] + (long)header[0x1B + i * 0x8]);
                    j = start - pos;
                    Console.WriteLine("Delete zero segment " + i + " of size 0x" + Convert.ToString(j, 16));
                    while (j > 0)
                    {
                        er.ReadBytes(SECTOR_SIZE);
                        j -= SECTOR_SIZE;
                    }
                    Console.WriteLine("Writing data segment " + i + " of size 0x" + Convert.ToString(length, 16));
                    j = length;
                    while (j > 0)
                    {
                        ew.Write(er.ReadBytes(SECTOR_SIZE));
                        j -= SECTOR_SIZE;
                    }
                    pos = start + length;
                }
                return header;
            }
        }

        public static void EnDecryptNFS(string InFile, string OutFile, byte[] key, byte[] iv, bool enc, byte[] header)
        {
            using (var er = File.OpenRead(InFile))
            using (var ew = File.Create(OutFile))
            {
                Console.WriteLine();
                if (enc)
                {
                    Console.WriteLine("Writing EGGS header...");
                    ew.Write(header, 0, header.Length);
                    Console.WriteLine("Encrypting hif.nfs...");
                }
                else
                    Console.WriteLine("Decrypting hif.nfs...");
                Console.WriteLine();
                byte[] block_iv = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1F, 0x00 };
                byte[] Sector = new byte[SECTOR_SIZE];
                int timer = 0;
                int i = 0;
                long leftSize = er.Length;
                using (var aes = Aes.Create())
                {
                    aes.Key = key;
                    do
                    {
                        if (timer == 8000)
                        {
                            timer = 0;
                            i++;
                            Console.WriteLine((i * 256) + " MB processed...");
                        }
                        timer++;
                        
                        int toRead = leftSize > SECTOR_SIZE ? SECTOR_SIZE : (int)leftSize;
                        int read = er.Read(Sector, 0, toRead);
                        if (read <= 0) break;

                        if (ew.Position >= 0x18000)
                        {
                            iv = block_iv;
                        }

                        // ENCRYPTION
                        if (enc && ew.Position < 0x18000)
                        {
                            aes.EncryptCbc(Sector.AsSpan(0, read), iv, Sector.AsSpan(0, read), PaddingMode.None);
                        }
                        else if (enc && ew.Position >= 0x18000)
                        {
                            aes.EncryptCbc(Sector.AsSpan(0, read), block_iv, Sector.AsSpan(0, read), PaddingMode.None);
                            IncrementIv(block_iv);
                        }

                        // DECRYPTION
                        else if (!enc && ew.Position < 0x18000)
                        {
                            aes.DecryptCbc(Sector.AsSpan(0, read), iv, Sector.AsSpan(0, read), PaddingMode.None);
                        }
                        else if (!enc && ew.Position >= 0x18000)
                        {
                            aes.DecryptCbc(Sector.AsSpan(0, read), iv, Sector.AsSpan(0, read), PaddingMode.None);
                            IncrementIv(block_iv);
                        }

                        ew.Write(Sector, 0, read);
                        leftSize -= SECTOR_SIZE;
                    } while (leftSize > 0);
                }
            }
        }

        public static byte[] aes_128_cbc(byte[] key, byte[] iv, byte[] data, bool enc)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (iv == null) throw new ArgumentNullException(nameof(iv));
            if (data == null) throw new ArgumentNullException(nameof(data));

            byte[] result = new byte[data.Length];
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                if (enc)
                    aes.EncryptCbc(data, iv, result, PaddingMode.None);
                else
                    aes.DecryptCbc(data, iv, result, PaddingMode.None);
            }
            return result;
        }

        public static int[,] sort(int[,] list, int size)
        {
            if (list == null)
                throw new ArgumentNullException(nameof(list));
            if (size <= 0)
                return list;

            var items = new List<KeyValuePair<int, int>>(size);
            for (int i = 0; i < size; i++)
            {
                items.Add(new KeyValuePair<int, int>(list[0, i], list[1, i]));
            }

            items.Sort((a, b) => a.Value.CompareTo(b.Value));

            int[,] sorted = new int[2, size];
            for (int i = 0; i < size; i++)
            {
                sorted[0, i] = items[i].Key;
                sorted[1, i] = items[i].Value;
            }

            return sorted;
        }

        public static int[] sort(int[] list, int size)
        {
            if (list == null)
                throw new ArgumentNullException(nameof(list));
            if (size <= 0 || size > list.Length)
                return list;

            Array.Sort(list, 0, size);
            return list;
        }

        static bool ByteArrayCompare(ReadOnlySpan<byte> b1, ReadOnlySpan<byte> b2)
        {
            return b1.SequenceEqual(b2);
        }

        private static int PatchBuffer(Span<byte> buffer, ReadOnlySpan<byte> pattern, int writeOffset, ReadOnlySpan<byte> replacement)
        {
            int patchCount = 0;
            int index = 0;
            while (index <= buffer.Length - pattern.Length)
            {
                var slice = buffer.Slice(index, pattern.Length);
                if (slice.SequenceEqual(pattern))
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
                var slice = buffer.Slice(index, pattern.Length);
                if (slice.SequenceEqual(pattern))
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

        public static void DoThePatching(string fw_file)
        {
            byte[] fileBytes = File.ReadAllBytes(fw_file);
            Span<byte> fileSpan = fileBytes.AsSpan();

            Console.WriteLine("Checking fw.img's revision number...");

            byte[] rev_pattern = { 0x73, 0x76, 0x6E, 0x2D };                                  // search for "svn-"
            string revision = "";

            int revOffset = fileSpan.IndexOf(rev_pattern);
            if (revOffset >= 0 && revOffset + 8 <= fileSpan.Length)
            {
                revision = System.Text.Encoding.UTF8.GetString(fileSpan.Slice(revOffset + 4, 4));
            }

            if (revision == "r590")
            {
                Console.WriteLine("OK, revision 590 detected.");
            }
            else
            {
                Console.WriteLine("Warning: {0} detected. These patches are designed for revision 590 only.", revision);
            }
            Console.WriteLine();

            Console.WriteLine("Patching fw.img.");
            if (!keepLegit)
            {
                int patchCount = 0;
                byte[] oldHashCheck = { 0x20, 0x07, 0x23, 0xA2 };
                byte[] newHashCheck = { 0x20, 0x07, 0x4B, 0x0B };

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
                    Console.WriteLine("Fakesign patching: Nothing to patch.");
                else
                    Console.WriteLine("Fakesigning patching finished... (Patches applied: {0})", patchCount);

                Console.WriteLine();
            }

            //map classic controller's L & R to the gamepad's ZL & ZR
            if (map_shoulder_to_trigger)
            {
                int patchCount = 0;

                byte[] pattern1 = { 0x40, 0x05, 0x46, 0xA9 };
                byte[] patch1 = { 0x26, 0x80, 0x40, 0x06 };

                byte[] pattern2 = { 0x1C, 0x05, 0x40, 0x35 };
                byte[] patch2 = { 0x25, 0x40, 0x40, 0x05 };

                byte[] pattern3 = { 0x23, 0x7F, 0x1C, 0x02 };
                byte[] patch3 = { 0x46, 0xB1, 0x23, 0x20, 0x40, 0x03 };

                byte[] pattern4 = { 0x46, 0x53, 0x42, 0x18 };
                byte[] patch4 = { 0x23, 0x10, 0x40, 0x03 };

                byte[] pattern5 = { 0x1C, 0x05, 0x80, 0x22 };
                byte[] patch5 = { 0x25, 0x40, 0x80, 0x22, 0x40, 0x05 };

                patchCount += PatchBuffer(fileSpan, pattern1, 0, patch1);
                patchCount += PatchBuffer(fileSpan, pattern2, 0, patch2);
                patchCount += PatchBuffer(fileSpan, pattern3, 0, patch3);
                patchCount += PatchBuffer(fileSpan, pattern4, 0, patch4);
                patchCount += PatchBuffer(fileSpan, pattern5, 0, patch5);

                if (patchCount == 0)
                    Console.WriteLine("LR to ZLZR patching: Nothing to patch.");
                else
                    Console.WriteLine("LR to ZLZR patching finished. (Patches applied: {0})", patchCount);

                Console.WriteLine();
            }

            //enable wii remote emulation
            if (horiz_wiimote || vert_wiimote)
            {
                int patchCount = 0;
                byte[] pattern = { 0x16, 0x13, 0x1C, 0x02, 0x40, 0x9A, 0x1C, 0x13 };
                byte[] patch = { 0x23, 0x00 };

                patchCount += PatchBuffer(fileSpan, pattern, 0, patch);

                if (patchCount == 0)
                    Console.WriteLine("Wii Remote emulation patching: Nothing to patch.");
                else
                    Console.WriteLine("Wii Remote emulation enabled... (Patches applied: {0})", patchCount);

                Console.WriteLine();
            }

            //enable horizontal wii remote emulation (remap dpad and ab12)
            if (horiz_wiimote)
            {
                int patchCount = 0;
                byte[] pattern = { 0x4A, 0x71, 0x42, 0x13, 0xD0, 0xD2, 0x9B, 0x00 };

                patchCount += PatchBufferCustom(fileSpan, pattern, (buf, offset) =>
                {
                    buf[offset + 0x07] = 0x02;                                            // dpad left -> down
                    buf[offset + 0x0F] = 0x03;                                            // dpad right -> up
                    buf[offset + 0x1D] = 0x01;                                            // dpad down -> right
                    buf[offset + 0x2B] = 0x00;                                            // dpad up -> left
                    buf[offset + 0x65] = 0x07;                                            // B -> 2
                    buf[offset + 0x75] = 0x06;                                            // A -> 1
                    buf[offset + 0x85] = 0x04;                                            // 1 -> B
                    buf[offset + 0x95] = 0x05;                                            // 2 -> A
                });

                if (patchCount == 0)
                    Console.WriteLine("Horizontal Wii Remote patching: Nothing to patch.");
                else
                    Console.WriteLine("Horizontal Wii Remote emulation enabled... (Patches applied: {0})", patchCount);

                Console.WriteLine();
            }

            // enable proper input support in homebrew
            if (homebrew)
            {
                Console.WriteLine("Homebrew-related patches:");
                int patchCount = 0;

                // disable AHBPROT
                byte[] pattern_ahbprot = { 0xD0, 0x0B, 0x23, 0x08, 0x43, 0x13, 0x60, 0x0B };
                byte[] patch_ahbprot = { 0x46, 0xC0 };
                patchCount += PatchBufferCustom(fileSpan, pattern_ahbprot, (buf, offset) =>
                {
                    Console.WriteLine("* Disabling AHBPROT...");
                    patch_ahbprot.CopyTo(buf.Slice(offset, 2));
                });

                //disable MEMPROT
                byte[] pattern_memprot = { 0x01, 0x94, 0xB5, 0x00, 0x4B, 0x08, 0x22, 0x01 };
                byte[] patch_memprot = { 0x22, 0x00 };
                patchCount += PatchBufferCustom(fileSpan, pattern_memprot, (buf, offset) =>
                {
                    Console.WriteLine("* Disabling MEMPROT...");
                    patch_memprot.CopyTo(buf.Slice(offset + 6, 2));
                });

                // nintendont 1
                byte[] pattern_nintendont_1 = { 0xB0, 0xBA, 0x1C, 0x0F };
                byte[] patch_nintendont_1 = { 0xE5, 0x9F, 0x10, 0x04, 0xE5, 0x91, 0x00, 0x00, 0xE1, 0x2F, 0xFF, 0x10, 0x12, 0xFF, 0xFF, 0xE0 };
                patchCount += PatchBufferCustom(fileSpan, pattern_nintendont_1, (buf, offset) =>
                {
                    Console.WriteLine("* Nintendont patch 1...");
                    patch_nintendont_1.CopyTo(buf.Slice(offset - 12, 16));
                });

                //nintendont 2
                byte[] pattern_nintendont_2 = { 0x68, 0x4B, 0x2B, 0x06 };
                byte[] patch_nintendont_2 = { 0x49, 0x01, 0x47, 0x88, 0x46, 0xC0, 0xE0, 0x01, 0x12, 0xFF, 0xFE, 0x00, 0x22, 0x00, 0x23, 0x01, 0x46, 0xC0, 0x46, 0xC0 };
                patchCount += PatchBufferCustom(fileSpan, pattern_nintendont_2, (buf, offset) =>
                {
                    Console.WriteLine("* Nintendont patch 2...");
                    patch_nintendont_2.CopyTo(buf.Slice(offset, 20));
                });

                //nintendont 3
                byte[] pattern1_nintendont_3 = { 0x0D, 0x80, 0x00, 0x00, 0x0D, 0x80, 0x00, 0x00 };
                byte[] pattern2_nintendont_3 = { 0x00, 0x00, 0x00, 0x02 };
                byte[] patch_nintendont_3 = { 0x00, 0x00, 0x00, 0x03 };
                for (int offset = 0; offset <= fileSpan.Length - 8; offset++)
                {
                    if (fileSpan.Slice(offset, 8).SequenceEqual(pattern1_nintendont_3))
                    {
                        if (offset + 0x10 + 4 <= fileSpan.Length)
                        {
                            var target = fileSpan.Slice(offset + 0x10, 4);
                            if (target.SequenceEqual(pattern2_nintendont_3))
                            {
                                Console.WriteLine("* Nintendont patch 3...");
                                patch_nintendont_3.CopyTo(fileSpan.Slice(offset + 0x10, 4));
                                patchCount++;
                            }
                        }
                    }
                }

                if (patchCount == 0)
                    Console.WriteLine("Homebrew patching: Nothing to patch.");
                else
                    Console.WriteLine("Homebrew patching finished... (Patches applied: {0})", patchCount);

                Console.WriteLine();
            }

            // for homebrew: allow wiimote passthrough
            if (passthrough)
            {
                Console.WriteLine("Wiimote Passthrough patching:");
                int patchCount = 0;

                //wiimote passthrough
                byte[] pattern_passthrough = { 0x20, 0x4B, 0x01, 0x68, 0x18, 0x47, 0x70, 0x00 };
                byte[] patch_passthrough = { 0x20, 0x00 };
                patchCount += PatchBuffer(fileSpan, pattern_passthrough, 3, patch_passthrough);

                // the custom function
                byte[] pattern_custom_func = { 0x28, 0x00, 0xD0, 0x03, 0x49, 0x02, 0x22, 0x09 };
                byte[] patch_custom_func = { 0xF0, 0x04, 0xFF, 0x21, 0x48, 0x02, 0x21, 0x09, 0xF0, 0x04, 0xFE, 0xF9 };
                patchCount += PatchBuffer(fileSpan, pattern_custom_func, 0, patch_custom_func);

                // call custom function
                byte[] pattern_custom_call = { 0xF0, 0x01, 0xFA, 0xB9 };
                byte[] patch_custom_call = { 0xF7, 0xFC, 0xFB, 0x95 };
                patchCount += PatchBuffer(fileSpan, pattern_custom_call, 0, patch_custom_call);

                if (patchCount == 0)
                    Console.WriteLine("Wiimote Passthrough patching: Nothing to patch.");
                else
                    Console.WriteLine("Wiimote Passthrough patching finished... (Patches applied: {0})", patchCount);

                Console.WriteLine();
            }

            //for titles that dont immediately detect CC
            if (instantcc)
            {
                int patchCount = 0;
                byte[] pattern = { 0x78, 0x93, 0x21, 0x10, 0x2B, 0x02, 0xD1, 0xB7 };
                byte[] patch = { 0x78, 0x93, 0x21, 0x10, 0x2B, 0x02, 0x46, 0xC0 };

                patchCount += PatchBuffer(fileSpan, pattern, 0, patch);

                if (patchCount == 0)
                    Console.WriteLine("Instant Classic Controller report patching: Nothing to patch.");
                else
                    Console.WriteLine("Instant Classic Controller report patched... (Patches applied: {0})", patchCount);

                Console.WriteLine();
            }

            //for titles that dont want CC connected
            if (nocc)
            {
                int patchCount = 0;
                byte[] pattern = { 0x78, 0x93, 0x21, 0x10, 0x2B, 0x02, 0xD1, 0xB7 };
                byte[] patch = { 0x78, 0x93, 0x21, 0x10, 0x2B, 0x02, 0xE0, 0xB7 };

                patchCount += PatchBuffer(fileSpan, pattern, 0, patch);

                if (patchCount == 0)
                    Console.WriteLine("No Classic Controller report patching: Nothing to patch.");
                else
                    Console.WriteLine("No Classic Controller report patched... (Patches applied: {0})", patchCount);

                Console.WriteLine();
            }

            File.WriteAllBytes(fw_file, fileBytes);
        }
    }
}
