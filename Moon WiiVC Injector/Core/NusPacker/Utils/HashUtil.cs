// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System;
using System.IO;
using System.Security.Cryptography;

namespace Moon_WiiVC_Injector.Core.NusPacker.Utils;

public static class HashUtil
{
    public static byte[] HashSHA2(byte[] data)
    {
        return SHA256.HashData(data);
    }

    public static byte[] HashSHA1(byte[] data)
    {
        return SHA1.HashData(data);
    }

    public static byte[] HashSHA1(string file, int alignment)
    {
        using var sha1 = SHA1.Create();
        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);

        byte[] buffer = new byte[64 * 1024];
        long remaining = input.Length;
        while (remaining > 0)
        {
            int toRead = (int)Math.Min(buffer.Length, remaining);
            int read = input.Read(buffer, 0, toRead);
            if (read <= 0) break;
            sha1.TransformBlock(buffer, 0, read, null, 0);
            remaining -= read;
        }

        long targetSize = NusUtils.Align(input.Length, alignment);
        long padding = targetSize - input.Length;
        if (padding > 0)
        {
            byte[] zeroPad = new byte[Math.Min(padding, 64 * 1024)];
            while (padding > 0)
            {
                int toPad = (int)Math.Min(zeroPad.Length, padding);
                sha1.TransformBlock(zeroPad, 0, toPad, null, 0);
                padding -= toPad;
            }
        }

        sha1.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return sha1.Hash!;
    }
}
