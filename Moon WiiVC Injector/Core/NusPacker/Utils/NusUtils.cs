// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System;
using System.Buffers.Binary;
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

    public static byte[] CopyOfRange(byte[] src, int start, int end)
    {
        int len = end - start;
        byte[] dest = new byte[len];
        Array.Copy(src, start, dest, 0, len);
        return dest;
    }
}

public class BigEndianMemoryStream : MemoryStream
{
    private readonly byte[] _spanBuffer = new byte[8];

    public BigEndianMemoryStream(int capacity) : base(capacity)
    {
    }

    public void WriteBigEndian(short value)
    {
        BinaryPrimitives.WriteInt16BigEndian(_spanBuffer, value);
        base.Write(_spanBuffer, 0, 2);
    }

    public void WriteBigEndian(int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(_spanBuffer, value);
        base.Write(_spanBuffer, 0, 4);
    }

    public void WriteBigEndian(uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(_spanBuffer, value);
        base.Write(_spanBuffer, 0, 4);
    }

    public void WriteBigEndian(long value)
    {
        BinaryPrimitives.WriteInt64BigEndian(_spanBuffer, value);
        base.Write(_spanBuffer, 0, 8);
    }
}

