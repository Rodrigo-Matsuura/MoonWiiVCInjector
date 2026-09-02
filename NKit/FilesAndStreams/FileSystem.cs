using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NKit;

public class FstFolder(FstFolder parent)
{
    public FstFolder Parent { get; private set; } = parent;
    public List<FstFolder> Folders { get; private set; } = [];
    public List<FstFile> Files { get; } = [];
    public string Name { get; set; }

    public override string ToString() => Name ?? "";
}

internal class ConvertFile
{
    private readonly bool _isGc;

    public ConvertFile(bool isGc)
    {
        _isGc = isGc;
        Alignment = -1;
    }

    public ConvertFile(long gapLength, bool isGc) : this(isGc)
    {
        GapLength = gapLength;
        Gap = new Gap(gapLength, _isGc);
    }

    public FstFile FstFile { get; set; }
    public Gap Gap { get; internal set; }
    public long GapLength { get; internal set; }
    public long Alignment { get; set; }

    public override string ToString() => $"{FstFile} : {GapLength:X8} : {Alignment}";
}

public class FstFile(FstFolder parent)
{
    internal FstFolder Parent { get; private set; } = parent;
    public string Name { get; internal set; }
    public long DataOffset { get; internal set; }
    internal long Offset { get; set; }
    public long Length { get; internal set; }
    public bool IsNonFstFile { get; internal set; }
    internal int OffsetInFstFile { get; set; }

    public override string ToString() => $"{OffsetInFstFile:X8} : {DataOffset:X8} : {Length:X8} : {Name}";
}

public class FileSystem
{
    private FileSystem(FstFolder root)
    {
        Root = root;
    }

    public FstFile[] Files => RecurseFolders(Root, []).OrderBy(a => a.Offset).ToArray();

    private static List<FstFile> RecurseFolders(FstFolder folder, List<FstFile> files)
    {
        files.AddRange(folder.Files);

        foreach (FstFolder fl in folder.Folders)
            RecurseFolders(fl, files);
        return files;
    }

    public FstFolder Root { get; private set; }

    public static FileSystem Parse(byte[] fstData, long fstOffset, string id, bool isGc)
    {
        MemorySection ms = new(fstData);
        FstFile ff = new(null) { Name = "fst.bin", DataOffset = fstOffset, Offset = NStream.DataToOffset(fstOffset, !isGc), IsNonFstFile = true, Length = fstData.Length };
        return Parse(ms, ff, id, isGc);
    }

    public static FileSystem Parse(Stream fstData, long fstOffset, long length, string id, bool isGc)
    {
        MemorySection ms = MemorySection.Read(fstData, length);
        FstFile ff = new(null) { Name = "fst.bin", DataOffset = fstOffset, Offset = NStream.DataToOffset(fstOffset, !isGc), IsNonFstFile = true, Length = (int)fstData.Length };
        return Parse(ms, ff, id, isGc);
    }

    internal static FileSystem Parse(MemorySection ms, FstFile fst, string id)
    {
        return Parse(ms, fst, id, false);
    }

    internal static FileSystem Parse(MemorySection ms, FstFile fst, string id, bool isGc)
    {
        FstFolder fld = new(null);

        long nFiles = ms.ReadUInt32B(0x8);
        if (12 * nFiles > ms.Size)
            return null;

        if (fst != null)
            fld.Files.Add(fst);
        RecurseFst(ms, fld, 12 * nFiles, 0, id, isGc);
        return new FileSystem(fld);
    }

    private static uint RecurseFst(MemorySection ms, FstFolder folder, long names, uint i, string id, bool isGc)
    {
        uint j;
        uint hdr = ms.ReadUInt32B((int)(12 * i));
        long name = names + hdr & 0x00ffffffL;
        int type = (int)(hdr >> 24);
        string nm = ms.ReadStringToNull((int)name, Encoding.GetEncoding("shift-jis"));
        uint size = ms.ReadUInt32B((int)(12 * i + 8));

        if (type == 1)
        {
            FstFolder f = i == 0 ? folder : new FstFolder(folder) { Name = nm };
            if (i != 0)
                folder.Folders.Add(f);
            for (j = i + 1; j < size;)
                j = RecurseFst(ms, f, names, j, id, isGc);
            return size;
        }
        else
        {
            int pos = (int)(12 * i + 4);
            long doff = ms.ReadUInt32B(pos) * (isGc ? 1L : 4L); //offset in data
            size = ms.ReadUInt32B((int)(12 * i + 8));
            long off = NStream.DataToOffset(doff, !isGc); //offset in raw partition
            folder.Files.Add(new FstFile(folder) { DataOffset = doff, Offset = off, Length = size, Name = nm, OffsetInFstFile = pos });
            return i + 1;
        }
    }
}
