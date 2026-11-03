// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Moon_WiiVC_Injector.Core.NusPacker.Packaging;
using Moon_WiiVC_Injector.Core.NusPacker.Utils;

namespace Moon_WiiVC_Injector.Core.NusPacker.FileSystem;

internal enum FstEntryType
{
    File = 0x00,
    Directory = 0x01,
    WiiVC = 0x02
}

internal sealed class FstEntry
{
    public readonly string Filepath = "";
    public readonly string Filename = "";
    public readonly List<FstEntry> Children = new();
    private FstEntry? _parent;
    private int _nameOffset;
    private int _entryOffset;
    private short _flags;

    private readonly bool _isRoot;
    private int _rootEntryCount;

    public readonly bool IsDir;
    public bool IsFile => !IsDir;
    public int ParentOffset { get; set; }
    public int NextOffset { get; set; }

    public readonly long FileSize;
    public long FileOffset { get; private set; }

    public NusContent? Content { get; private set; }

    public FstEntry()
    {
        _isRoot = true;
        IsDir = true;
    }

    public FstEntry(string filepath)
    {
        Filepath = Path.GetFullPath(filepath);
        if (Directory.Exists(filepath))
        {
            IsDir = true;
        }
        else
        {
            FileSize = new FileInfo(filepath).Length;
        }
        Filename = Path.GetFileName(filepath);
    }

    public void AddChild(FstEntry child)
    {
        Children.Add(child);
        child._parent = this;
    }

    public void SetContent(NusContent content)
    {
        _flags = content.EntriesFlags;
        Content = content;
    }

    private byte GetTypeAsByte()
    {
        byte type = 0;
        if (IsDir)
            type |= (byte)FstEntryType.Directory;
        if (Filename.EndsWith("nfs", StringComparison.OrdinalIgnoreCase))
            type |= (byte)FstEntryType.WiiVC;

        return type;
    }

    public byte[] GetAsData()
    {
        BigEndianMemoryStream buffer = new BigEndianMemoryStream(GetDataSize());
        if (_isRoot)
        {
            buffer.WriteByte(1);
            buffer.Seek(7, SeekOrigin.Current);
            buffer.WriteBigEndian(_rootEntryCount);
            buffer.Seek(4, SeekOrigin.Current);
        }
        else
        {
            buffer.WriteByte(GetTypeAsByte());
            buffer.WriteByte((byte)(_nameOffset >> 16));
            buffer.WriteByte((byte)(_nameOffset >> 8));
            buffer.WriteByte((byte)_nameOffset);

            if (IsDir)
            {
                buffer.WriteBigEndian(ParentOffset);
                buffer.WriteBigEndian(NextOffset);
            }
            else
            {
                buffer.WriteBigEndian((int)(FileOffset >> 5));
                buffer.WriteBigEndian((int)FileSize);
            }

            buffer.WriteBigEndian(_flags);
            buffer.WriteBigEndian((short)(Content?.Id ?? 0));
        }

        foreach (FstEntry entry in Children)
        {
            byte[] childData = entry.GetAsData();
            buffer.Write(childData, 0, childData.Length);
        }

        return buffer.GetBuffer();
    }

    private int GetDataSize()
    {
        return 0x10 + Children.Sum(child => child.GetDataSize());
    }

    private void SetNameOffset(int nameOffset)
    {
        if (nameOffset > 0xFFFFFF)
        {
            throw new InvalidOperationException($"Filename offset is too large. Maximum is 0xFFFFFF, attempted {nameOffset}.");
        }
        _nameOffset = nameOffset;
    }

    public void Update()
    {
        SetNameOffset(FstTable.GetStringPosition());
        FstTable.AddString(Filename);
        _entryOffset = FstTable.CurEntryOffset;
        FstTable.CurEntryOffset++;

        if (IsDir && !_isRoot && _parent != null)
            ParentOffset = _parent._entryOffset;

        if (Content != null && !IsDir)
            FileOffset = Content.GetOffsetForFileAndIncrease(this);

        foreach (FstEntry entry in Children)
        {
            entry.Update();
        }
    }

    public FstEntry? UpdateDirRefs()
    {
        if (!IsDir)
            return null;
        if (_parent != null)
            ParentOffset = _parent._entryOffset;

        FstEntry? result = null;
        List<FstEntry> dirChildren = Children.Where(c => c.IsDir).ToList();

        for (int i = 0; i < dirChildren.Count; i++)
        {
            FstEntry curDir = dirChildren[i];
            if (dirChildren.Count > i + 1)
                curDir.NextOffset = dirChildren[i + 1]._entryOffset;

            FstEntry? curResult = curDir.UpdateDirRefs();

            if (curResult != null)
            {
                FstEntry? curParent = curResult._parent;
                while (curParent != null && curParent.NextOffset == 0)
                {
                    curParent = curParent._parent;
                }
                if (curParent != null)
                {
                    curResult.NextOffset = curParent.NextOffset;
                }
            }

            if (dirChildren.Count > i)
                result = curDir;
        }

        return result;
    }

    public IEnumerable<FstEntry> GetFstEntriesByContent(NusContent targetContent)
    {
        List<FstEntry> result = new();
        if (Content == null)
        {
            if (IsDir)
                throw new InvalidOperationException($"The folder \"{Filename}\" is empty. Please add a dummy file to it.");
            else
                throw new InvalidOperationException($"The file \"{Filename}\" is not assigned to any content (.app).");
        }
        else if (Content.Equals(targetContent))
        {
            result.Add(this);
        }

        result.AddRange(Children.SelectMany(child => child.GetFstEntriesByContent(targetContent)));
        return result;
    }

    public int GetEntryCount() => 1 + Children.Sum(child => child.GetEntryCount());

    public void SetEntryCount(int fstEntryCount) => _rootEntryCount = fstEntryCount;
}

internal sealed class FstEntriesCollection
{
    private readonly List<FstEntry> _entries = new();

    public FstEntriesCollection()
    {
        _entries.Add(new FstEntry());
    }

    public void Update()
    {
        foreach (FstEntry entry in _entries)
        {
            entry.Update();
        }
        UpdateDirRefs();
    }

    private void UpdateDirRefs()
    {
        if (_entries.Count == 0)
            return;

        FstEntry root = _entries[0];
        root.ParentOffset = 0;
        root.NextOffset = FstTable.CurEntryOffset;
        FstEntry? lastDir = root.UpdateDirRefs();
        if (lastDir != null)
        {
            lastDir.NextOffset = FstTable.CurEntryOffset;
        }
    }

    public List<FstEntry> GetFstEntriesByContent(NusContent content)
    {
        return _entries.SelectMany(entry => entry.GetFstEntriesByContent(content)).ToList();
    }

    public int GetFstEntryCount() => _entries.Sum(entry => entry.GetEntryCount());

    public byte[] GetAsData() => _entries.SelectMany(entry => entry.GetAsData()).ToArray();

    public int GetDataSize() => GetFstEntryCount() * 0x10;

    public FstEntry GetRootEntry() => _entries[0];
}

internal sealed class FstTable
{
    private static readonly byte[] MagicBytes = { 0x46, 0x53, 0x54, 0x00 };
    private const int Unknown = 0x20;
    private int _contentCount;

    public readonly NusContentsCollection Contents;
    public readonly FstEntriesCollection FileEntries = new();

    private static readonly MemoryStream StringsStream = new();
    public static int CurEntryOffset { get; set; }

    public FstTable(NusContentsCollection contents)
    {
        Contents = contents;
        Reset();
    }

    public static void Reset()
    {
        StringsStream.SetLength(0);
        CurEntryOffset = 0;
    }

    public void Update()
    {
        Reset();
        Contents.ResetFileOffsets();
        FileEntries.Update();
        Contents.Update(FileEntries);
        FileEntries.GetRootEntry().SetEntryCount(FileEntries.GetFstEntryCount());
        _contentCount = Contents.GetContentCount();
    }

    public static int GetStringPosition() => (int)StringsStream.Position;

    public static void AddString(string filename)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(filename);
        StringsStream.Write(bytes, 0, bytes.Length);
        StringsStream.WriteByte(0x00);
    }

    public byte[] GetAsData()
    {
        BigEndianMemoryStream buffer = new BigEndianMemoryStream(GetDataSize());

        buffer.Write(MagicBytes, 0, MagicBytes.Length);
        buffer.WriteBigEndian(Unknown);
        buffer.WriteBigEndian(_contentCount);
        buffer.Seek(20, SeekOrigin.Current);
        byte[] fstHeader = Contents.GetFstContentHeaderAsData();
        buffer.Write(fstHeader, 0, fstHeader.Length);
        byte[] fstEntriesData = FileEntries.GetAsData();
        buffer.Write(fstEntriesData, 0, fstEntriesData.Length);
        byte[] strData = StringsStream.ToArray();
        buffer.Write(strData, 0, strData.Length);
        buffer.SetLength(GetDataSize());

        return buffer.ToArray();
    }

    public int GetDataSize()
    {
        int size = MagicBytes.Length + 0x04 + 0x04 + 20;
        size += Contents.GetFstContentHeaderDataSize();
        size += FileEntries.GetDataSize();
        size += (int)StringsStream.Position;
        return (int)NusUtils.Align(size, 0x8000);
    }
}

