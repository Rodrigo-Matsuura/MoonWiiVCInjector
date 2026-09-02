using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NKit;

public interface IWiiDiscSection
{
}

public enum PartitionType { Data, Update, Channel, GameData, Other }

internal class WiiPartitionInfo
{
    internal WiiPartitionInfo(PartitionType type, long offset, int table, long tablePos)
    {
        DiscOffset = SrcDiscOffset = offset;
        Table = table;
        Type = type;
        TableOffset = tablePos;
    }

    public PartitionType Type { get; private set; }
    public long DiscOffset { get; internal set; }
    internal long SrcDiscOffset { get; set; }
    internal int Table { get; set; }
    internal long TableOffset { get; set; }
}

internal class WiiPartitionPlaceHolder : WiiPartitionInfo, IDisposable
{
    private readonly NStream _nStream;
    private NStream _ws;

    public WiiPartitionPlaceHolder(NStream nStream, string filename, PartitionType type, long offset, int table) : base(type, offset, table, 0)
    {
        _nStream = nStream;
        Filename = filename;
    }

    public WiiPartitionPlaceHolder(NStream nStream, PartitionType type, long offset, int table) : base(type, offset, table, 0)
    {
        _nStream = nStream;
        Filename = null;
    }

    public string Filename { get; set; }

    public NStream Stream
    {
        get
        {
            if (Filename != null)
            {
                _ws = new NStream(File.OpenRead(Filename));
                _ws.Initialize(false);
            }
            return _ws;
        }
    }

    public override string ToString() => DiscOffset.ToString("X8");

    public void Dispose()
    {
        try
        {
            if (_nStream != _ws)
                _ws?.Close();
        }
        catch { }
    }
}

internal class WiiFillerSectionItem : BaseSection
{
    private readonly JunkStream _junk;
    private readonly byte[] _junkData;
    private readonly bool _useBuff;

    internal WiiFillerSectionItem(NStream stream, long discOffset, byte[] data, long size, bool useBuff, JunkStream junk) : base(stream, discOffset, data, size)
    {
        _useBuff = useBuff;
        _junk = junk;
        if (_junk != null)
        {
            _junk.Position = discOffset;
            _junkData = new byte[Data.Length];
            _junk.Read(_junkData, 0, (int)base.Size);
            Array.Clear(_junkData, 0, 28);
            base.Data = _useBuff ? data : _junkData;
        }
    }

    public void Populate(byte[] data, long discOffset, long size)
    {
        base.DiscOffset = discOffset;
        base.Size = size;
        _junk?.Read(_junkData, 0, (int)base.Size);
        base.Data = _useBuff ? data : _junkData;
    }

    public byte[] Junk => _junkData;
}

internal class WiiFillerSection : IWiiDiscSection
{
    private readonly NStream _stream;
    private readonly byte[] _buff;
    private readonly string _junkId;
    private readonly long _srcSize;
    private readonly bool _generateUpdateFiller;
    private readonly bool _generateOtherFiller;
    private readonly bool _forceFillerJunk;
    private readonly bool _updatePartiton;

    public long DiscOffset { get; private set; }
    public long Size { get; private set; }

    internal WiiFillerSection(NStream stream, bool updatePartition, long discOffset, long size, long updateSkip, string overrideJunkId, bool generateUpdateFiller, bool generateOtherFiller, bool forceFillerJunk)
    {
        _stream = stream;
        _buff = new byte[0x40000];
        _junkId = overrideJunkId ?? stream.Id;
        DiscOffset = discOffset;
        Size = size;
        _srcSize = size - updateSkip;
        _generateUpdateFiller = generateUpdateFiller || size > _srcSize;
        _generateOtherFiller = generateOtherFiller;
        _forceFillerJunk = forceFillerJunk;
        _updatePartiton = updatePartition;
    }

    public IEnumerable<WiiFillerSectionItem> Sections
    {
        get
        {
            WiiDiscHeaderSection hdr = (WiiDiscHeaderSection)_stream.DiscHeader;
            bool readImg = (_updatePartiton && !_generateUpdateFiller) || (!_updatePartiton && !_generateOtherFiller);
            bool createJunk = !_updatePartiton && (_generateOtherFiller || _forceFillerJunk);

            _stream.ChangeJunk(0, _junkId, hdr.DiscNo, _stream.RecoverySize);

            int len = (int)Math.Min(_buff.Length, Size);
            if (readImg)
                _stream.Read(_buff, 0, len);
            else
            {
                if (hdr.Partitions.Any(a => a.DiscOffset > DiscOffset))
                    _stream.Seek(_srcSize, SeekOrigin.Current);
            }

            bool ffScrubbedUpdate = _updatePartiton && _buff.Equals(0, len, 0xFF);
            if (ffScrubbedUpdate)
                Array.Clear(_buff, 0, len);

            WiiFillerSectionItem es = new(_stream, DiscOffset, _buff, len, readImg || _updatePartiton, createJunk ? _stream.JunkStream : null);
            yield return es;
            WiiFillerSectionItem last = es;
            while (last.DiscOffset + last.Size < DiscOffset + Size)
            {
                len = (int)Math.Min(_buff.Length, (DiscOffset + Size) - (last.DiscOffset + last.Size));
                if (readImg)
                    _stream.Read(_buff, 0, len);
                if (ffScrubbedUpdate)
                    Array.Clear(_buff, 0, len);
                es.Populate(_buff, last.DiscOffset + last.Size, len);
                yield return es;
                last = es;
            }
        }
    }
}
