using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NKit;

internal class NkitFormat
{
    internal static List<ConvertFile> GetConvertFstFiles(long size, MemorySection hdr, MemorySection fst, bool isGc, long fstFileAlignment, out string error)
    {
        string[] align = [".tgc"];
        error = null;
        List<ConvertFile> conFiles = [];
        try
        {
            List<FstFile> srcFiles = FileSystem.Parse(fst, null, hdr.ReadString(0, 4), isGc)?.Files?.OrderBy(a => a.Offset)?.ThenBy(a => a.Length)?.ToList();

            //get list of files and gaps
            long end;
            long gap;
            long fstLen = (long)hdr.ReadUInt32B(0x424) * (isGc ? 1L : 4L);
            FstFile ff = new(null) { Name = "fst.bin", DataOffset = fstLen, Offset = fstLen, Length = (int)fst.Size, IsNonFstFile = true };
            for (int i = 0; i < srcFiles.Count; i++)
            {
                ff = i == 0 ? ff : srcFiles[i - 1];
                end = ff.DataOffset + ff.Length;
                end += end % 4 == 0 ? 0 : 4 - (end % 4);

                gap = srcFiles[i].DataOffset - end;

                if (gap < 0)
                {
                    error = $"The gap between '{ff.Name}' and '{srcFiles[i].Name}' is {gap} - Converting as bad image";
                    return null;
                }
                conFiles.Add(new ConvertFile(gap, isGc) { FstFile = ff });
            }
            ff = srcFiles.Last();
            end = ff.DataOffset + ff.Length;
            end += end % 4 == 0 ? 0 : 4 - (end % 4);
            gap = size - end;
            if (gap >= -3 && gap < 0)
                gap = 0;
            if (gap < 0)
            {
                error = $"The gap between '{ff.Name}' and the end of the image is {gap} - Converting as bad image/partition";
                return null;
            }

            conFiles.Add(new ConvertFile(gap, isGc) { FstFile = ff });

            //set alignment
            foreach (ConvertFile cf in conFiles)
            {
                ff = cf.FstFile;
                if (fstFileAlignment == 0)
                    cf.Alignment = 0;
                else if (fstFileAlignment == -1 && ff.DataOffset % 0x8000 == 0 && (ff.Length % 0x8000 == 0 || align.Contains(Path.GetExtension(ff.Name).ToLower())))
                    cf.Alignment = 0x8000;
                else if (fstFileAlignment != 0 && ff.DataOffset % fstFileAlignment == 0)
                    cf.Alignment = fstFileAlignment;
                else
                    cf.Alignment = -1;
            }
        }
        catch
        {
            error = "Fst parsing error - Converting as bad image";
            return null;
        }
        return conFiles;
    }

    internal static long ProcessGap(ref long nullsPos, ConvertFile file, ref long srcPos, Stream s, JunkStream junk, bool firstOrLastFile, ScrubManager scrub, Stream output, ILog log)
    {
        long nulls = 0;

        if (file.GapLength != 0)
        {
            if (srcPos % 4 != 0)
                throw new Exception("Src Position should be on a 4 byte boundary");

            long size = file.GapLength;
            long maxNulls = Math.Max(0, nullsPos - srcPos);
            if (size < maxNulls)
                nulls = size;
            else
                nulls = size >= 0x40000 && !firstOrLastFile ? 0 : maxNulls;
        }
        return file.Gap.Encode(s, ref srcPos, nulls, file.GapLength, junk, scrub, output, log);
    }

    internal static void LogNkitInfo(NkitInfo imageInfo, ILog log, string id, bool isDisc)
    {
        string discOrPrtn = isDisc ? "Disc" : "Prtn";
        string pfx = $"NKit {discOrPrtn} [{SourceFiles.CleanseFileName(id),-4}]";

        log?.LogDetail($"{pfx}: In [{imageInfo.BytesReadSize / (double)(1024 * 1024),2:#.0} MiB] ({imageInfo.BytesReadSize} bytes), Out [{imageInfo.BytesWriteSize / (double)(1024 * 1024),2:#.0} MiB] (bytes {imageInfo.BytesWriteSize})");
        if (imageInfo.BytesGcz != 0)
            log?.LogDetail($"{pfx}: GCZ Out [{imageInfo.BytesGcz / (double)(1024 * 1024),2:#.0} MiB] ({imageInfo.BytesGcz} bytes)");
        if (imageInfo.BytesJunkFiles != 0)
            log?.LogDetail($"{pfx}: Junk Files Removed [{imageInfo.BytesJunkFiles / (double)(1024 * 1024),2:#.0} MiB] ({imageInfo.BytesJunkFiles} bytes)");
        if (imageInfo.BytesHashesData != 0 || imageInfo.BytesHashesPreservation != 0)
            log?.LogDetail($"{pfx}: Hashes In [{imageInfo.BytesHashesData / (double)(1024 * 1024),2:#.0} MiB] ({imageInfo.BytesHashesData} bytes), Preserved [{imageInfo.BytesHashesPreservation / (double)(1024 * 1024),2:#.0} MiB] (bytes {imageInfo.BytesHashesPreservation})");
        if (imageInfo.BytesPreservationData != 0)
            log?.LogDetail($"{pfx}: Preservation Data [{imageInfo.BytesPreservationData / (double)(1024 * 1024),2:#.0} MiB] {imageInfo.BytesPreservationData} bytes");
        if (imageInfo.BytesPreservationDiscPadding != 0)
            log?.LogDetail($"{pfx}: Preservation Padding [{imageInfo.BytesPreservationDiscPadding / (double)(1024 * 1024),2:#.0} MiB] {imageInfo.BytesPreservationDiscPadding} bytes");
        if (imageInfo.FilesTotal != 0 || imageInfo.FilesAligned != 0)
            log?.LogDetail($"{pfx}: {imageInfo.FilesTotal} Total Files, {imageInfo.FilesAligned} aligning boundary preserved");
    }
}
