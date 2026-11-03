// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System;
using System.IO;

namespace Moon_WiiVC_Injector.Core.NusPacker.Utils;

public static class NusUtils
{
    public static void DeleteDir(string dir)
    {
        if (!Directory.Exists(dir))
            return;

        try
        {
            Directory.Delete(dir, true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    public static long Align(long input, int alignment)
    {
        long remainder = input % alignment;
        return remainder == 0 ? input : input + (alignment - remainder);
    }

    public static byte[] HexStringToByteArray(string s) => Convert.FromHexString(s);

    public static string ByteArrayToHexString(byte[] bytes) => Convert.ToHexString(bytes);

    public static byte[] CopyOfRange(byte[] src, int start, int end)
    {
        int len = end - start;
        byte[] dest = new byte[len];
        Array.Copy(src, start, dest, 0, len);
        return dest;
    }
}
