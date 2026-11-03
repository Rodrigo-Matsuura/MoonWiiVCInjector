// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System;
using System.Buffers.Binary;
using System.IO;

namespace Moon_WiiVC_Injector.Core.NusPacker.Utils;

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
