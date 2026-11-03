// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System.IO;
using System.Text;
using Moon_WiiVC_Injector.Core.NusPacker.Contents;
using Moon_WiiVC_Injector.Core.NusPacker.Fst;
using Moon_WiiVC_Injector.Core.NusPacker.Utils;

namespace Moon_WiiVC_Injector.Core.NusPacker.Packaging;

public class FST
{
    private static readonly byte[] magicbytes = { 0x46, 0x53, 0x54, 0x00 };
    private const int unknown = 0x20;
    private int contentCount;

    public readonly Contents.Contents contents;
    public readonly FSTEntries fileEntries = new FSTEntries();

    private static readonly MemoryStream strings = new MemoryStream();

    public static int curEntryOffset { get; set; }

    public FST(Contents.Contents contents)
    {
        this.contents = contents;
        Reset();
    }

    public static void Reset()
    {
        strings.SetLength(0);
        curEntryOffset = 0;
    }

    public void Update()
    {
        Reset();
        contents.ResetFileOffsets();
        fileEntries.Update();
        contents.Update(fileEntries);
        fileEntries.GetRootEntry().SetEntryCount(fileEntries.GetFSTEntryCount());

        contentCount = contents.GetContentCount();
    }

    public static int GetStringPosition()
    {
        return (int)strings.Position;
    }

    public static void AddString(string filename)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(filename);
        strings.Write(bytes, 0, bytes.Length);
        strings.WriteByte(0x00);
    }

    public byte[] GetAsData()
    {
        BigEndianMemoryStream buffer = new BigEndianMemoryStream(GetDataSize());

        buffer.Write(magicbytes, 0, magicbytes.Length);
        buffer.WriteBigEndian(unknown);
        buffer.WriteBigEndian(contentCount);
        buffer.Seek(20, SeekOrigin.Current);
        byte[] fstHeader = contents.GetFSTContentHeaderAsData();
        buffer.Write(fstHeader, 0, fstHeader.Length);
        byte[] fstEntriesData = fileEntries.GetAsData();
        buffer.Write(fstEntriesData, 0, fstEntriesData.Length);
        byte[] strData = strings.ToArray();
        buffer.Write(strData, 0, strData.Length);

        return buffer.GetBuffer();
    }

    public int GetDataSize()
    {
        int size = 0;
        size += magicbytes.Length;
        size += 0x04; // unknown
        size += 0x04; // contentCount
        size += 20; // padding
        size += contents.GetFSTContentHeaderDataSize();
        size += fileEntries.GetDataSize();
        size += (int)strings.Position;
        return (int)NusUtils.Align(size, 0x8000);
    }
}
