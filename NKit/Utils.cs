using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace NKit;

public class ChecksumsResult
{
    public uint Crc { get; set; }
    public byte[] Md5 { get; set; }
    public byte[] Sha1 { get; set; }
}

[Flags]
public enum LogMessageType
{
    Silent = 0,
    Info = 1,
    Detail = 2,
    Debug = 3
}

public interface ILog
{
    void Log(string message);
    void LogDetail(string message);
    void LogDebug(string message);
    void LogBlank();
    void ProcessingStart(long inputSize, string message);
    void ProcessingProgress(float value);
    void ProcessingComplete(long outputSize, string message, bool success);
}

partial class Math2
{
    public static long Clamp(long val, long min, long max) => Math.Min(Math.Max(min, val), max);
    public static long Align(long val, long boundary) => val / boundary * boundary;
}

public static class ExtensionMethods
{
    public static bool Equals(this byte[] data1, int offset1, byte[] data2, int offset2, int size)
    {
        for (int i = 0; i < size; i++)
        {
            if (data1[i + offset1] != data2[i + offset2])
                return false;
        }
        return true;
    }

    public static void Clear(this byte[] source, int offset, int count, byte value) //, Action<long, long> progress)
    {
        for (int i = 0; i < count; i++)
            source[offset + i] = value;
    }

    public static bool Equals(this byte[] source, int offset, int count, byte value) //, Action<long, long> progress)
    {
        for (int i = 0; i < count; i++)
        {
            if (source[offset + i] != value)
                return false;
        }
        return true;
    }

    public static long Copy(this Stream source, Stream target, long amount) //, Action<long, long> progress)
    {
        int len = 0x200000; //arbitrary
        byte[] buffer = ArrayPool<byte>.Shared.Rent(len);
        byte[] buffer2 = ArrayPool<byte>.Shared.Rent(len); //double buffered
        try
        {
            byte[] tmp;
            int read = 0;
            int read2 = 0;
            long total = amount;
            long prg = 0;
            Task t = null;
            while (prg < total)
            {
                read2 = 0;
                prg += read;
                if (prg < total)
                {
                    t = Task.Run(() => read2 = source.Read(buffer2, 0, (int)Math.Min((long)len, total - prg)));
                    t.ConfigureAwait(false);
                }
                else
                    t = null;
                target.Write(buffer, 0, read);
                if (t != null && !t.IsCompleted)
                    t.Wait();

                tmp = buffer2;
                buffer2 = buffer;
                buffer = tmp;
                if (read == 0 && read2 == 0)
                    throw new Exception("Could not read from stream");
                read = read2;
            }

            return prg;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            ArrayPool<byte>.Shared.Return(buffer2);
        }
    }

}
