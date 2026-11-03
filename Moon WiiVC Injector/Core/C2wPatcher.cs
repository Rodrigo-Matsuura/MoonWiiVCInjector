using System;
using System.IO;
using System.Security.Cryptography;
using Moon_WiiVC_Injector.Services;

namespace Moon_WiiVC_Injector.Core;

/// <summary>
/// Native C# implementation of FIX94's cafe2wii (c2w) patcher.
/// Based on c2w_patcher by FIX94 - https://github.com/FIX94/c2w_patcher
/// Decrypts, patches, re-encrypts, and updates the SHA1 hash of c2w.img using the Ancast key.
/// </summary>
public static class C2wPatcher
{
    // c2w basic verification magic
    private static ReadOnlySpan<byte> ImgHeader => [0xEF, 0xA2, 0x82, 0xD9];
    private static ReadOnlySpan<byte> ElfHeader => [0x7F, 0x45, 0x4C, 0x46]; // \x7fELF

    // Patch for LT_COMPAT_MEMCTRL_STATE (toggles between 3x and 5x ppc multiplier)
    private static ReadOnlySpan<byte> MemCtrlOri => [0xE3, 0x82, 0x20, 0x20, 0xE5, 0x84, 0x25, 0xB0];
    private static ReadOnlySpan<byte> MemCtrlPatch => [0xE3, 0xC2, 0x20, 0x20, 0xE5, 0x84, 0x25, 0xB0];

    // Patch for LT_SYSPROT (unlocks ppc multiplier)
    private static ReadOnlySpan<byte> SysProtOri => [0xE3, 0x83, 0x30, 0x99, 0xE5, 0x81, 0x35, 0x14];
    private static ReadOnlySpan<byte> SysProtPatch => [0xE3, 0x83, 0x30, 0x9D, 0xE5, 0x81, 0x35, 0x14];

    // Patch for LT_IOP2X (toggles between 1x and 2x arm multiplier)
    private static ReadOnlySpan<byte> Iop2xOri => [0xE1, 0x94, 0x40, 0x00, 0x1A, 0xFF, 0xFF, 0xBD];
    private static ReadOnlySpan<byte> Iop2xPatch => [0xE2, 0x8D, 0xD0, 0x10, 0xE8, 0xBD, 0x8F, 0xF0];

    /// <summary>
    /// Patches c2w.img in-place (or to target path) with the specified Ancast Key.
    /// </summary>
    public static bool PatchC2wImage(string c2wFilePath, string ancastKeyHex, bool doIop2x = false, string? outputPath = null)
    {
        outputPath ??= c2wFilePath;

        if (!File.Exists(c2wFilePath))
        {
            AppLogger.Error($"[C2wPatcher] c2w.img not found: {c2wFilePath}");
            return false;
        }

        byte[] keyBytes = Convert.FromHexString(ancastKeyHex.Trim());
        if (keyBytes.Length != 16)
        {
            AppLogger.Error($"[C2wPatcher] Invalid Ancast key length: expected 16 bytes (32 hex chars), got {keyBytes.Length} bytes.");
            return false;
        }

        byte[] encData = File.ReadAllBytes(c2wFilePath);
        if (encData.Length < 0x808)
        {
            AppLogger.Error("[C2wPatcher] c2w.img is too small.");
            return false;
        }

        // Verify c2w header
        if (!encData.AsSpan(0, 4).SequenceEqual(ImgHeader))
        {
            AppLogger.Error("[C2wPatcher] Invalid c2w.img header magic.");
            return false;
        }

        int payloadLength = encData.Length - 0x200;
        byte[] decData = new byte[encData.Length];
        Array.Copy(encData, 0, decData, 0, 0x200);

        byte[] zeroIv = new byte[16];

        // Decrypt payload from offset 0x200 with hardware-accelerated AES-128-CBC
        using (var aes = Aes.Create())
        {
            aes.Key = keyBytes;
            aes.DecryptCbc(encData.AsSpan(0x200, payloadLength), zeroIv, decData.AsSpan(0x200, payloadLength), PaddingMode.None);
        }

        // Verify decrypted ELF header at offset 0x804
        if (!decData.AsSpan(0x804, 4).SequenceEqual(ElfHeader))
        {
            AppLogger.Error("[C2wPatcher] Failed to decrypt c2w.img! Please verify your Ancast Key.");
            return false;
        }

        int patchCount = 0;
        Span<byte> decPayload = decData.AsSpan(0x200, payloadLength);

        patchCount += ApplyPatchPattern(decPayload, MemCtrlOri, MemCtrlPatch, "LT_COMPAT_MEMCTRL_STATE");
        patchCount += ApplyPatchPattern(decPayload, SysProtOri, SysProtPatch, "LT_SYSPROT");
        if (doIop2x)
        {
            patchCount += ApplyPatchPattern(decPayload, Iop2xOri, Iop2xPatch, "LT_IOP2X");
        }

        AppLogger.Info($"[C2wPatcher] Applied {patchCount} c2w patches.");

        // Re-encrypt payload from offset 0x200 with hardware-accelerated AES-128-CBC
        using (var aes = Aes.Create())
        {
            aes.Key = keyBytes;
            aes.EncryptCbc(decPayload, zeroIv, encData.AsSpan(0x200, payloadLength), PaddingMode.None);
        }

        // Compute SHA-1 hash of re-encrypted payload
        byte[] sha1Hash = SHA1.HashData(encData.AsSpan(0x200, payloadLength));

        // Place SHA-1 into img header at 0x1B0 (20 bytes)
        sha1Hash.CopyTo(encData.AsSpan(0x1B0, 20));

        // Write patched file
        File.WriteAllBytes(outputPath, encData);
        AppLogger.Info($"[C2wPatcher] Successfully wrote patched c2w image to: {outputPath}");
        return true;
    }

    private static int ApplyPatchPattern(Span<byte> buffer, ReadOnlySpan<byte> pattern, ReadOnlySpan<byte> replacement, string patchName)
    {
        int count = 0;
        int searchOffset = 0;
        while (searchOffset <= buffer.Length - pattern.Length)
        {
            int relativeIdx = buffer[searchOffset..].IndexOf(pattern);
            if (relativeIdx < 0)
                break;

            int matchIdx = searchOffset + relativeIdx;
            if ((matchIdx & 3) == 0) // 4-byte instruction aligned
            {
                replacement.CopyTo(buffer.Slice(matchIdx, replacement.Length));
                count++;
                AppLogger.Info($"[C2wPatcher] Patched {patchName} at 0x{0x200 + matchIdx:X8}");
            }
            searchOffset = matchIdx + 4;
        }
        return count;
    }
}
