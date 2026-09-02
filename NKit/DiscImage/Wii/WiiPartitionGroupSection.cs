using System;
using System.IO;
using System.Linq;

namespace NKit;

internal class WiiPartitionGroupSection : BaseSection
{
    public bool IsEncrypted { get; private set; }

    private readonly WiiPartitionGroupEncryptionState _data;
    private int _idx;
    private readonly int _maxLength;
    private readonly byte[] _h3Table;
    private readonly WiiPartitionHeaderSection _partHdr;
    private readonly bool _isIsoDec;

    public WiiPartitionHeaderSection Header => _partHdr;
    public int H3Errors { get; private set; }
    public byte[] Key => _partHdr.Key;
    internal long DataOffset { get; private set; }
    internal long Offset { get; private set; }
    public byte[] Junk { get; private set; }
    public byte[] Encrypted => _data.Encrypted;
    public byte[] Decrypted => _data.Decrypted;
    public PartitionType Type => _partHdr.Type;

    internal WiiPartitionGroupSection(WiiDiscHeaderSection hdr, WiiPartitionHeaderSection partHdr, byte[] data, long discOffset, long size, bool encrypted)
        : this(null, hdr, partHdr, data, discOffset, size, encrypted)
    {
    }

    internal WiiPartitionGroupSection(NStream stream, WiiDiscHeaderSection hdr, WiiPartitionHeaderSection partHdr, byte[] data, long discOffset, long size, bool encrypted)
        : base(stream, discOffset, new byte[(0x400 * 32) * 64], size)
    {
        _isIsoDec = hdr.IsIsoDecPartition(partHdr.DiscOffset);
        _partHdr = partHdr;
        _h3Table = _partHdr.H3Table;
        _idx = 0;
        _maxLength = base.Data.Length; // 0x8000 per block * 64 = 0x200000

        _data = new WiiPartitionGroupEncryptionState((int)WiiPartitionSection.GroupSize, this.Key, _h3Table);

        this.IsEncrypted = encrypted || !data.Equals(0x26c, new byte[20], 0, 20);
        this.Junk = new byte[WiiPartitionSection.GroupSize];

        _data.Populate(data, (int)size, this.IsEncrypted && !_isIsoDec, _isIsoDec, _idx);

        Initialise();
    }

    public void Populate(int groupIdx, byte[] data, long discOffset, long size)
    {
        base.DiscOffset = discOffset;
        base.Size = size;
        _idx = groupIdx;

        _data.Populate(data, (int)size, this.IsEncrypted && !_isIsoDec, _isIsoDec, _idx);

        Initialise();
    }

    private void Initialise()
    {
        this.Offset = _idx * (long)_maxLength;
        this.DataOffset = (long)_idx * 64L * 0x7c00L;
        this.H3Errors = 0;
    }

    public bool PreserveHashes()
    {
        int scrubbedBlocks = _data.ScrubbedBlocks;
        if (scrubbedBlocks != 0 && scrubbedBlocks < _data.UsedBlocks)
            return true;

        long end = this.DataOffset + (64 * 0x7c00L);
        bool usedScrubbed = scrubbedBlocks == _data.UsedBlocks && (this.Header.FileSystem != null && this.Header.FileSystem.Files.Any(a =>
                                                                   (this.DataOffset <= a.DataOffset && end > a.DataOffset)
                                                                || (this.DataOffset <= a.DataOffset + a.Length && end > a.DataOffset + a.Length)
                                                                || (this.DataOffset >= a.DataOffset && end <= a.DataOffset + a.Length)));
        if (usedScrubbed)
            return true;
        if (scrubbedBlocks == _data.UsedBlocks)
        {
            return !_data.AllScrubbedSameByte();
        }

        return !_data.FastHashIsValid();
    }

    public int DataCopy(int position, int length, byte[] buffer, int bufferOffset)
    {
        int c = 0;
        int b = position / 0x7c00;
        int p = position % 0x7c00;
        while (b < _data.UsedBlocks && c != length)
        {
            int l = Math.Min(0x7c00 - p, length - c);
            if (l == 0)
                break;
            Array.Copy(_data.Decrypted, ((b++ * 0x8000) + 0x400) + p, buffer, bufferOffset + c, l);
            c += l;
            p = 0;
        }
        return c;
    }

    internal void MarkBlockDirty(int blockIndex)
    {
        _data.MarkBlockDirty(blockIndex);
    }

    internal void SetScrubbed(int blockIndex, byte scrubByte)
    {
        _data.MarkBlockScrubbed(blockIndex, scrubByte);
    }

    internal bool IsValid(bool calculateHashes)
    {
        return _data.IsValid(calculateHashes);
    }

    internal void ForceHashes(byte[] hashes)
    {
        _data.ForceHashes(hashes);
    }
}
