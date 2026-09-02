using System;
using System.Text;

namespace NKit;

public abstract class BaseSection
{
    internal BaseSection(NStream stream, long discOffset, byte[] data, long size)
    {
        Stream = stream;
        DiscOffset = discOffset;
        Data = data;
        Size = size;
    }

    protected NStream Stream { get; private set; }
    public long DiscOffset { get; protected set; }
    public long Size { get; protected set; }
    public virtual byte[] Data { get; protected set; }

    public byte Read8(int offset) => Data[offset];
    public ushort ReadUInt16B(int offset) => BigEndian(BitConverter.ToUInt16(Data, offset));
    public uint ReadUInt32B(int offset) => BigEndian(BitConverter.ToUInt32(Data, offset));
    public uint ReadUInt32L(int offset) => LittleEndian(BitConverter.ToUInt32(Data, offset));
    public ulong ReadUInt64L(int offset) => LittleEndian(BitConverter.ToUInt64(Data, offset));
    public string ReadString(int offset, int length) => Encoding.ASCII.GetString(Data, offset, length);
    public string ReadStringToNull(int offset, Encoding encoding) => ReadStringToNullInternal(encoding, offset, -1);
    public string ReadStringToNull(int offset) => ReadStringToNullInternal(Encoding.ASCII, offset, -1);
    public string ReadStringToNull(int offset, int maxLength) => ReadStringToNullInternal(Encoding.ASCII, offset, maxLength);

    public byte[] Read(int offset, int length)
    {
        byte[] buffer = new byte[length];
        Array.Copy(Data, offset, buffer, 0, length);
        return buffer;
    }

    public void Write8(int offset, byte value) => Data[offset] = value;
    public void WriteUInt32B(int offset, uint value) => BitConverter.GetBytes(BigEndian(value)).CopyTo(Data, offset);

    public void Write(int offset, byte[] buffer)
    {
        Array.Copy(buffer, 0, Data, offset, buffer.Length);
    }

    public void Write(int offset, byte[] buffer, int length)
    {
        Array.Copy(buffer, 0, Data, offset, length);
    }

    public void Write(int offset, byte[] buffer, int bufferOffset, int length)
    {
        Array.Copy(buffer, bufferOffset, Data, offset, length);
    }

    private static uint BigEndian(uint x)
    {
        if (!BitConverter.IsLittleEndian)
            return x;
        x = (x >> 16) | (x << 16);
        return ((x & 0xFF00FF00) >> 8) | ((x & 0x00FF00FF) << 8);
    }

    private static ushort BigEndian(ushort x) => !BitConverter.IsLittleEndian ? x : (ushort)((x >> 8) | (x << 8));

    private static uint LittleEndian(uint x)
    {
        if (BitConverter.IsLittleEndian)
            return x;
        x = (x >> 16) | (x << 16);
        return ((x & 0xFF00FF00) >> 8) | ((x & 0x00FF00FF) << 8);
    }

    private static ulong LittleEndian(ulong x)
    {
        if (BitConverter.IsLittleEndian)
            return x;
        return ((ulong)LittleEndian((uint)x) << 32) | LittleEndian((uint)(x >> 32));
    }

    private string ReadStringToNullInternal(Encoding encoding, int offset, int maxLength)
    {
        try
        {
            int i = offset;
            int l = 0;

            while ((maxLength == -1 || l <= maxLength) && Data[i++] != '\0')
                l++;

            return encoding.GetString(Data, offset, l);
        }
        catch (Exception ex)
        {
            throw new HandledException(ex, "NStream.readStringToNull failure");
        }
    }
}
