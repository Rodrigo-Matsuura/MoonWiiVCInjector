using System;
using System.Buffers.Binary;
using System.IO;
using Moon_WiiVC_Injector.Services;

namespace Moon_WiiVC_Injector.Core;

/// <summary>
/// Native C# PowerPC DOL patcher for Wii Virtual Console injections.
/// Based on GetExtTypePatcher by FIX94 - https://github.com/FIX94/GetExtTypePatcher
/// </summary>
public static class DolPatcher
{
    private struct FuncPattern
    {
        public uint Length;
        public uint Loads;
        public uint Stores;
        public uint FCalls;
        public uint Branch;
        public uint Moves;

        public readonly bool Matches(in FuncPattern other)
        {
            return Length == other.Length &&
                   Loads == other.Loads &&
                   Stores == other.Stores &&
                   FCalls == other.FCalls &&
                   Branch == other.Branch &&
                   Moves == other.Moves;
        }
    }

    private static readonly FuncPattern GetExtTypeA = new() { Length = 0xD4, Loads = 18, Stores = 4, FCalls = 3, Branch = 7, Moves = 2 };
    private static readonly FuncPattern GetExtTypeB = new() { Length = 0xFC, Loads = 19, Stores = 4, FCalls = 3, Branch = 9, Moves = 2 };
    private static readonly FuncPattern GetExtTypeC = new() { Length = 0x26C, Loads = 59, Stores = 2, FCalls = 16, Branch = 12, Moves = 10 };

    /// <summary>
    /// Patches Wii main.dol to enable emulated Classic Controller support for Wii U Virtual Console (GetExtType patch).
    /// </summary>
    public static bool PatchClassicController(string dolFilePath)
    {
        if (!File.Exists(dolFilePath))
        {
            AppLogger.Warning($"[DolPatcher] File not found: {dolFilePath}");
            return false;
        }

        byte[] dolData = File.ReadAllBytes(dolFilePath);
        if (dolData.Length < 0x100)
        {
            AppLogger.Warning($"[DolPatcher] Invalid DOL file (too small): {dolFilePath}");
            return false;
        }

        // Verify DOL header signature (offset 0 should be 0x00000100 for standard DOL header)
        if (dolData[0] != 0x00 || dolData[1] != 0x00 || dolData[2] != 0x01 || dolData[3] != 0x00)
        {
            AppLogger.Warning($"[DolPatcher] Header does not match standard DOL format: {dolFilePath}");
        }

        bool patched = false;
        int length = dolData.Length;

        for (int i = 0x100; i + 4 <= length; i += 4)
        {
            uint word = BinaryPrimitives.ReadUInt32BigEndian(dolData.AsSpan(i, 4));
            if (word != 0x4E800020) // blr
                continue;

            int funcStart = i + 4;
            if (funcStart + 0x2000 > length)
                break;

            FuncPattern pattern = AnalyzeFunctionPattern(dolData, funcStart, Math.Min(0x2000, length - funcStart));

            if (pattern.Matches(GetExtTypeA) || pattern.Matches(GetExtTypeB))
            {
                if (funcStart + 0x4C <= length)
                {
                    uint w40 = BinaryPrimitives.ReadUInt32BigEndian(dolData.AsSpan(funcStart + 0x40, 4));
                    uint w48 = BinaryPrimitives.ReadUInt32BigEndian(dolData.AsSpan(funcStart + 0x48, 4));

                    if ((w40 & 0xFFE00000) == 0x88000000 && (w48 & 0xFFE00000) == 0x88000000)
                    {
                        BinaryPrimitives.WriteUInt32BigEndian(dolData.AsSpan(funcStart + 0x40, 4), 0x38000001); // li r0, 1
                        BinaryPrimitives.WriteUInt32BigEndian(dolData.AsSpan(funcStart + 0x48, 4), 0x38000001); // li r0, 1
                        string patName = pattern.Length == 0xD4 ? "GetExtTypeA" : "GetExtTypeB";
                        AppLogger.Info($"[DolPatcher] Successfully patched {patName} at 0x{funcStart:X8}");
                        patched = true;
                        break;
                    }
                }
            }
            else if (pattern.Matches(GetExtTypeC))
            {
                if (funcStart + 0x48 <= length)
                {
                    uint w38 = BinaryPrimitives.ReadUInt32BigEndian(dolData.AsSpan(funcStart + 0x38, 4));
                    uint w44 = BinaryPrimitives.ReadUInt32BigEndian(dolData.AsSpan(funcStart + 0x44, 4));

                    if ((w38 & 0xFFE00000) == 0x88000000 && (w44 & 0xFFE00000) == 0x88000000)
                    {
                        BinaryPrimitives.WriteUInt32BigEndian(dolData.AsSpan(funcStart + 0x38, 4), 0x38000001); // li r0, 1
                        BinaryPrimitives.WriteUInt32BigEndian(dolData.AsSpan(funcStart + 0x44, 4), 0x38000001); // li r0, 1
                        AppLogger.Info($"[DolPatcher] Successfully patched GetExtTypeC at 0x{funcStart:X8}");
                        patched = true;
                        break;
                    }
                }
            }
        }

        if (patched)
        {
            File.WriteAllBytes(dolFilePath, dolData);
            AppLogger.Info("[DolPatcher] Patched DOL written back to disk.");
            return true;
        }

        AppLogger.Info("[DolPatcher] No matching GetExtType function signature found (or already patched).");
        return false;
    }

    private static FuncPattern AnalyzeFunctionPattern(byte[] data, int offset, int maxScanLength)
    {
        FuncPattern pattern = default;
        int i;

        for (i = 0; i + 4 <= maxScanLength; i += 4)
        {
            uint word = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + i, 4));

            if ((word & 0xFC000003) == 0x48000001)
                pattern.FCalls++;

            if ((word & 0xFC000003) == 0x48000000 ||
                (word & 0xFFFF0000) == 0x40800000 ||
                (word & 0xFFFF0000) == 0x41800000 ||
                (word & 0xFFFF0000) == 0x40810000 ||
                (word & 0xFFFF0000) == 0x41820000)
            {
                pattern.Branch++;
            }

            if ((word & 0xFC000000) == 0x80000000 ||
                (word & 0xFF000000) == 0x38000000 ||
                (word & 0xFF000000) == 0x3C000000)
            {
                pattern.Loads++;
            }

            if ((word & 0xFC000000) == 0x90000000 ||
                (word & 0xFC000000) == 0x94000000)
            {
                pattern.Stores++;
            }

            if ((word & 0xFF000000) == 0x7C000000)
                pattern.Moves++;

            if (word == 0x4E800020) // blr
                break;
        }

        pattern.Length = (uint)i;
        return pattern;
    }
}
