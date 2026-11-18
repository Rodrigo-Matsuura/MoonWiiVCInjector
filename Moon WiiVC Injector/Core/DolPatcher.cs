using System;
using System.Buffers.Binary;
using System.IO;
using Moon_WiiVC_Injector.Services;

namespace Moon_WiiVC_Injector.Core;

internal enum VideoPatchMode
{
    None = 0,
    NtscToPal = 1,
    PalToNtsc = 2
}

/// <summary>
/// Native C# PowerPC DOL patcher for Wii Virtual Console injections.
/// Includes GetExtTypePatcher (FIX94) and Smart Video Mode Changer (Waninkoko wii-vmc).
/// </summary>
internal static class DolPatcher
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
            if (funcStart + 0xD4 > length)
                break;

            FuncPattern pattern = AnalyzeFunctionPattern(dolData.AsSpan(funcStart, Math.Min(0x2000, length - funcStart)));

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

    /// <summary>
    /// Native C# port of Waninkoko's Wii Video Mode Changer (wii-vmc) Smart Patcher.
    /// Scans main.dol for Nintendo SDK GXRModeObj structures and patches TV mode &amp; framebuffer dimensions.
    /// </summary>
    public static int PatchVideoMode(string dolFilePath, VideoPatchMode patchMode)
    {
        if (patchMode == VideoPatchMode.None)
            return 0;

        if (!File.Exists(dolFilePath))
        {
            AppLogger.Warning($"[DolPatcher] File not found for video mode patch: {dolFilePath}");
            return 0;
        }

        byte[] dolData = File.ReadAllBytes(dolFilePath);
        if (dolData.Length < 0x120)
        {
            AppLogger.Warning($"[DolPatcher] Invalid DOL file (too small): {dolFilePath}");
            return 0;
        }

        Span<byte> span = dolData.AsSpan();
        int patchedCount = 0;

        // Scan for GXRModeObj signature starting at fbWidth (offset + 4 of GXRModeObj, so mode byte is at i - 1):
        // ptr[0..1] == 0x0280 (fbWidth = 640)
        // ptr[6..7] == 0x0028 (viXOrigin = 40)
        // ptr[8]    == 0x00   (viYOrigin high byte = 0)
        // ptr[10..11] == 0x0280 (viWidth = 640)
        for (int i = 0x104; i + 12 <= span.Length; i++)
        {
            if (span[i] != 0x02 || span[i + 1] != 0x80 ||
                span[i + 6] != 0x00 || span[i + 7] != 0x28 ||
                span[i + 8] != 0x00 ||
                span[i + 10] != 0x02 || span[i + 11] != 0x80)
            {
                continue;
            }

            int modeOffset = i - 1;
            byte currentMode = span[modeOffset];
            (byte newMode, ushort width, ushort height)? replacement = (patchMode, currentMode) switch
            {
                // NTSC -> PAL Smart Patcher
                (VideoPatchMode.NtscToPal, 0x00) => ((byte)0x14, (ushort)640, (ushort)480), // NTSC 480i -> EURGB60 / PAL60 480i
                (VideoPatchMode.NtscToPal, 0x02) => ((byte)0x16, (ushort)640, (ushort)480), // NTSC 480p -> EURGB60 / PAL60 480p
                (VideoPatchMode.NtscToPal, 0x08) => ((byte)0x04, (ushort)640, (ushort)528), // MPAL 480i -> PAL 576i/528i

                // PAL -> NTSC Smart Patcher
                (VideoPatchMode.PalToNtsc, 0x04) => ((byte)0x08, (ushort)640, (ushort)480), // PAL 576i/528i -> MPAL 480i
                (VideoPatchMode.PalToNtsc, 0x14) => ((byte)0x00, (ushort)640, (ushort)480), // EURGB60 480i -> NTSC 480i
                (VideoPatchMode.PalToNtsc, 0x16) => ((byte)0x02, (ushort)640, (ushort)480), // EURGB60 480p -> NTSC 480p
                _ => null
            };

            if (replacement is var (targetMode, fbWidth, fbHeight))
            {
                span[modeOffset] = targetMode;
                BinaryPrimitives.WriteUInt16BigEndian(span.Slice(modeOffset + 1, 2), fbWidth);
                BinaryPrimitives.WriteUInt16BigEndian(span.Slice(modeOffset + 3, 2), fbHeight);
                BinaryPrimitives.WriteUInt16BigEndian(span.Slice(modeOffset + 5, 2), fbHeight);
                patchedCount++;
                AppLogger.Info($"[DolPatcher] Patched GXRModeObj at 0x{modeOffset:X8}: mode 0x{currentMode:X2} -> 0x{targetMode:X2} ({fbWidth}x{fbHeight})");
            }
        }

        if (patchedCount > 0)
        {
            File.WriteAllBytes(dolFilePath, dolData);
            AppLogger.Info($"[DolPatcher] Video mode Smart Patcher ({patchMode}) updated {patchedCount} mode table entries.");
        }
        else
        {
            AppLogger.Info($"[DolPatcher] No matching GXRModeObj entries required patching for {patchMode}.");
        }

        return patchedCount;
    }

    private static FuncPattern AnalyzeFunctionPattern(ReadOnlySpan<byte> data)
    {
        FuncPattern pattern = default;
        int i;

        for (i = 0; i + 4 <= data.Length; i += 4)
        {
            uint word = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(i, 4));

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
