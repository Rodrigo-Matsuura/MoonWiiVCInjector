using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NKit;

public class NDisc : IDisposable
{
    private readonly Settings _settings;
    public ILog Log { get; set; }

    public Settings Settings => _settings;
    public string SourceFileName { get; private set; }

    internal NStream NStream { get; private set; }
    public BaseSection Header => NStream?.DiscHeader;
    public bool IsGameCube => NStream?.IsGameCube ?? true;

    public NDisc(Converter cvt, string sourceFileName)
    {
        SourceFileName = sourceFileName;
        NStream = cvt.NStream;
        NStream.Initialize(true);
        _settings = new Settings(NStream.IsGameCube ? DiscType.GameCube : DiscType.Wii);
        Log = cvt;
    }

    public NDisc(ILog log, Stream stream)
    {
        if (stream is NStream ns)
        {
            NStream = ns;
        }
        else
        {
            NStream = new NStream(stream);
            NStream.Initialize(true);
        }

        _settings = new Settings(NStream.IsGameCube ? DiscType.GameCube : DiscType.Wii);
        Log = log;
    }

    internal NDisc(ILog log, NStream nStream, string sourceFileName)
    {
        NStream = nStream;
        SourceFileName = sourceFileName;
        _settings = new Settings(NStream.IsGameCube ? DiscType.GameCube : DiscType.Wii);
        Log = log;
    }

    private static void EnsurePosition(NStream stream, long discOffset)
    {
        if (stream.Position != discOffset)
            stream.Seek(discOffset, SeekOrigin.Begin);
    }

    public IEnumerable<IWiiDiscSection> EnumerateSections(long imageSize)
    {
        long discOffset = 0;
        WiiDiscHeaderSection hdr = (WiiDiscHeaderSection)NStream.DiscHeader;
        yield return hdr;
        discOffset += hdr.Size;
        string lastId = null;
        long updateGapPadding = 0;

        foreach (WiiPartitionInfo part in hdr.Partitions)
        {
            if (part is WiiPartitionPlaceHolder placeholder)
            {
                if (placeholder.Filename != null)
                {
                    var partSec = new WiiPartitionSection(NStream, (WiiDiscHeaderSection)NStream.DiscHeader, placeholder.Stream, discOffset);
                    EnsurePosition(NStream, discOffset + partSec.Size - updateGapPadding);
                    yield return partSec;
                    placeholder.Dispose();
                    lastId = partSec.Id;
                    discOffset += partSec.Size;
                }
                continue;
            }

            if (lastId != null || part.DiscOffset - discOffset != 0)
            {
                if (part.DiscOffset < discOffset)
                    throw new HandledException("Partition alignment error, this could be a bug when adding missing partitions");

                WiiFillerSection gap = new(NStream, part.Type == PartitionType.Update, discOffset, part.DiscOffset - discOffset, updateGapPadding, null, false, false, false);
                yield return gap;
                discOffset += gap.Size;
                EnsurePosition(NStream, discOffset - updateGapPadding);
            }

            var partitionSec = new WiiPartitionSection(NStream, (WiiDiscHeaderSection)NStream.DiscHeader, NStream, discOffset);
            yield return partitionSec;
            EnsurePosition(NStream, discOffset + partitionSec.Size - updateGapPadding);

            lastId = partitionSec.Id;
            discOffset += partitionSec.Size;
        }

        if (lastId != null)
        {
            yield return new WiiFillerSection(NStream, false, discOffset, (imageSize == 0 ? NStream.Length : imageSize) - discOffset, 0, null, false, false, false);
        }
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}
