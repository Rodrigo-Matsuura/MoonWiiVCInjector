// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Moon_WiiVC_Injector.Core.NusPacker.Crypto;
using Moon_WiiVC_Injector.Core.NusPacker.Fst;
using Moon_WiiVC_Injector.Core.NusPacker.Packaging;

namespace Moon_WiiVC_Injector.Core.NusPacker.Contents;

public class Contents
{
    private readonly List<Content> contents = new List<Content>();
    public readonly Content fstContent;

    public Contents()
    {
        ContentDetails details = new ContentDetails(false, 0, 0, 0);
        fstContent = GetNewContent(details, true);
    }

    public Content GetNewContent(ContentDetails details, bool isFSTContent = false)
    {
        Content content = new Content(contents.Count, (short)contents.Count, details.entriesFlag, details.groupID, details.parentTitleID, details.isHashed, isFSTContent);
        contents.Add(content);

        return content;
    }

    public short GetContentCount()
    {
        return (short)contents.Count;
    }

    public byte[] GetAsData()
    {
        return contents.SelectMany(content => content.GetAsData()).ToArray();
    }

    public int GetDataSize()
    {
        return GetContentCount() * Content.staticDataSize;
    }

    public byte[] GetFSTContentHeaderAsData()
    {
        long content_offset = 0;
        MemoryStream buffer = new MemoryStream(GetFSTContentHeaderDataSize());
        foreach (Content content in contents)
        {
            (long key, byte[] value) = content.GetFSTContentHeaderAsData(content_offset);
            content_offset = key;
            buffer.Write(value, 0, value.Length);
        }

        return buffer.GetBuffer();
    }

    public int GetFSTContentHeaderDataSize()
    {
        return GetContentCount() * Content.staticFSTContentHeaderDataSize;
    }

    public void ResetFileOffsets()
    {
        foreach (Content content in contents)
        {
            content.ResetFileOffset();
        }
    }

    public void Update(FSTEntries fileEntries)
    {
        foreach (Content content in contents)
        {
            content.Update(fileEntries.GetFSTEntriesByContent(content));
        }
    }

    public void PackContents(string outputDir, Encryption encryption, string tempDir, Action<string, double>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var nonFstContents = contents.Where(c => !c.Equals(fstContent)).ToList();
        int total = nonFstContents.Count;
        int current = 0;

        foreach (Content content in nonFstContents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current++;
            onProgress?.Invoke($"Packaging content {content.ID:X8} ({current}/{total})...", (double)(current - 1) / Math.Max(1, total));
            content.PackContentToFile(outputDir, encryption, tempDir, onProgress, cancellationToken);
        }
    }

    public void DeleteContent(Content curContent)
    {
        contents.Remove(curContent);
    }
}

