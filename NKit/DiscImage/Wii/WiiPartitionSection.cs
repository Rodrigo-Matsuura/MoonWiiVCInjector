using System;
using System.Collections.Generic;
using System.IO;

namespace NKit;

internal class WiiPartitionSection : IWiiDiscSection
{
    public const long GroupSize = 0x8000 * 64;
    public WiiPartitionHeaderSection Header { get; private set; }
    public string Id => Header.Id;
    public int DiscNo => Header.DiscNo;
    public long PartitionLength => Header.PartitionSize;
    public long PartitionDataLength => Header.PartitionDataSize;
    public long DiscOffset => Header.DiscOffset;
    public long Size => Header.Size + Header.PartitionSize;

    private WiiPartitionGroupSection _firstSection;
    private readonly WiiDiscHeaderSection _discHdr;
    private readonly NStream _stream;
    private byte[] _fst;
    private int _partialFst;
    private long _seek;

    public FstFolder FileSystem => Header?.FileSystem?.Root;

    internal WiiPartitionSection(NStream stream, WiiDiscHeaderSection header, NStream readPartitionStream, long discOffset)
    {
        _stream = readPartitionStream;
        _discHdr = header;
        _partialFst = 0;
        _seek = -1;

        // calc the header
        byte[] partHdrTmp = new byte[0x400]; // read enough to get all the details we need
        _stream.ReadExactly(partHdrTmp, 0, partHdrTmp.Length); // need to read this to get header length
        byte[] partHdrLen = new byte[4];
        Array.Copy(partHdrTmp, 0x2b8, partHdrLen, 0, 4); // location of partition header length
        long hdrLen = BigEndian(BitConverter.ToUInt32(partHdrLen, 0)) * 4;
        byte[] partHdr = new byte[hdrLen];
        Array.Copy(partHdrTmp, partHdr, partHdrTmp.Length);
        _stream.ReadExactly(partHdr, partHdrTmp.Length, partHdr.Length - partHdrTmp.Length);

        Header = new WiiPartitionHeaderSection(_discHdr, readPartitionStream, discOffset, partHdr, partHdr.Length);
        if (Header.IsRvtH)
            throw new Exception("RVT-H image detected - this image type is not currently supported.");
        byte[] data = new byte[GroupSize];

        // work around that blocks are scrubbed but the wiistream can't unscrub them because the partition ID is unknown 
        Dictionary<int, int> isoDecUnscub = [];

        int dataLen = (int)Math.Min(data.Length, Header.PartitionSize);
        _stream.Read(data, 0, dataLen, (a, b) => isoDecUnscub.Add(a, b)); // defer the decryption because we don't have the partition id etc

        WiiPartitionGroupSection ps = new(stream, _discHdr, Header, data, Header.DiscOffset + Header.Data.Length, dataLen, true);

        Header.Initialise(ps);

        // deferred unscrubbing
        foreach (var x in isoDecUnscub)
        {
            _stream.JunkStream.Position = _stream.OffsetToData(x.Key);
            _stream.JunkStream.ReadExactly(ps.Decrypted, x.Key, x.Value);
        }
        _firstSection = ps;
    }

    public IEnumerable<WiiPartitionGroupSection> Sections
    {
        get
        {
            int size;
            byte[] data = new byte[GroupSize];
            WiiPartitionGroupSection ps = _firstSection;
            _firstSection = null; // don't hold on to it

            ParseFst(ps);
            yield return ps;

            int sec = 0;
            WiiPartitionGroupSection last = ps;
            while (last.DiscOffset + last.Size < Header.DiscOffset + Header.Size + Header.PartitionSize)
            {
                if (_seek != -1 && _seek != last.Offset + last.Size)
                {
                    long seekDiscOffset = Header.DiscOffset + Header.Size + _seek;
                    _stream.Seek(seekDiscOffset, SeekOrigin.Begin);
                    size = (int)Math.Min((Header.DiscOffset + Header.Size + Header.PartitionSize) - seekDiscOffset, (long)data.Length);
                    _stream.ReadExactly(data, 0, size);
                    sec = (int)(_seek / GroupSize);
                    ps.Populate(sec, data, Header.DiscOffset + Header.Size + _seek, size);
                }
                else
                {
                    size = (int)Math.Min((Header.DiscOffset + Header.Size + Header.PartitionSize) - (last.DiscOffset + last.Size), (long)data.Length);
                    _stream.ReadExactly(data, 0, size);
                    ps.Populate(++sec, data, last.DiscOffset + last.Size, size);
                    ParseFst(ps);
                }
                _seek = -1; // reset
                yield return ps;
                last = ps;
            }
        }
    }

    public void SeekToFile(FstFile file)
    {
        _seek = file.Offset - (file.Offset % GroupSize); // offset within partition of group, set to group boundary
    }

    private void ParseFst(WiiPartitionGroupSection grp)
    {
        if (_partialFst != 0 || (grp.DataOffset <= Header.FstOffset && Header.FstOffset <= grp.DataOffset + (0x7c00 * 64)))
        {
            _fst ??= new byte[Header.FstSize];
            int read = grp.DataCopy(_partialFst == 0 ? (int)(Header.FstOffset - grp.DataOffset) : 0, (int)Header.FstSize - _partialFst, _fst, _partialFst);
            if (read + _partialFst == _fst.Length)
            {
                Header.ParseFst(_fst);
                _fst = null;
                _partialFst = 0;
            }
            else
                _partialFst += read;
        }
    }

    private static uint BigEndian(uint x)
    {
        if (!BitConverter.IsLittleEndian)
            return x;
        x = (x >> 16) | (x << 16);
        return ((x & 0xFF00FF00) >> 8) | ((x & 0x00FF00FF) << 8);
    }
}
