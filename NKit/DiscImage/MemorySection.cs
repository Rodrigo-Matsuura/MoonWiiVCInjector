using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NKit;

internal class MemorySection(byte[] data) : BaseSection(null, 0, data, data.Length)
{

    public static MemorySection Read(Stream stream, long offset, long size)
    {
        if (stream.Position != offset)
            stream.Position = offset;
        byte[] b = new byte[size];
        stream.ReadExactly(b, 0, b.Length);
        return new MemorySection(b);
    }
    public static MemorySection Read(Stream stream, long size)
    {
        byte[] b = new byte[size];
        stream.ReadExactly(b, 0, b.Length);
        return new MemorySection(b);
    }
    public static MemorySection Copy(byte[] data, long size)
    {
        byte[] b = new byte[size];
        Array.Copy(data, b, b.Length);
        return new MemorySection(b);
    }
}
