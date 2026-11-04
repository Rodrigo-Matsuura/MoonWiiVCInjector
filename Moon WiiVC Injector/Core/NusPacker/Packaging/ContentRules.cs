// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Moon_WiiVC_Injector.Core.NusPacker.FileSystem;

namespace Moon_WiiVC_Injector.Core.NusPacker.Packaging;

internal sealed class ContentDetails
{
    public readonly bool IsHashed;
    public readonly short GroupId;
    public readonly long ParentTitleId;
    public readonly short EntriesFlag;

    public ContentDetails(bool isHashed, short groupId, long parentTitleId, short entriesFlag)
    {
        IsHashed = isHashed;
        GroupId = groupId;
        ParentTitleId = parentTitleId;
        EntriesFlag = entriesFlag;
    }
}

internal sealed class ContentRule
{
    public readonly string Pattern;
    public readonly ContentDetails Details;
    public readonly bool ContentPerMatch;

    private ContentRule(string pattern, ContentDetails details, bool contentPerMatch = false)
    {
        Pattern = pattern;
        Details = details;
        ContentPerMatch = contentPerMatch;
    }

    public static List<ContentRule> GetCommonRules(short contentGroup, long titleId)
    {
        List<ContentRule> commonRules = new();
        ContentDetails commonDetailsCode = new(false, NusPackerSettings.GroupIdCode, 0x0L, NusPackerSettings.FstFlagsCode);
        ContentDetails commonDetailsMeta = new(true, NusPackerSettings.GroupIdMeta, 0x0L, NusPackerSettings.FstFlagsMeta);
        ContentDetails commonDetailsPreload = new(true, NusPackerSettings.GroupIdCode, 0x0L, NusPackerSettings.FstFlagsCode);
        ContentDetails commonDetailsContent = new(true, contentGroup, titleId, NusPackerSettings.FstFlagsContent);

        commonRules.Add(new ContentRule(@"^/code/app\.xml$", commonDetailsCode));
        commonRules.Add(new ContentRule(@"^/code/cos\.xml$", commonDetailsCode));

        commonRules.Add(new ContentRule(@"^/meta/meta\.xml$", commonDetailsMeta));
        commonRules.Add(new ContentRule(@"^/meta/((?!\.xml).)*$", commonDetailsMeta));
        commonRules.Add(new ContentRule(@"^/meta/bootMovie\.h264$", commonDetailsMeta));
        commonRules.Add(new ContentRule(@"^/meta/bootLogoTex\.tga$", commonDetailsMeta));
        commonRules.Add(new ContentRule(@"^/meta/Manual\.bfma$", commonDetailsMeta));
        commonRules.Add(new ContentRule(@"^/meta/.*\.jpg$", commonDetailsMeta));

        commonRules.Add(new ContentRule(@"/code/.*(\.rpx|\.rpl)$", commonDetailsCode, true));

        commonRules.Add(new ContentRule(@"^/code/preload\.txt$", commonDetailsPreload));

        commonRules.Add(new ContentRule(@"^/code/fw\.img$", commonDetailsCode));
        commonRules.Add(new ContentRule(@"^/code/fw\.tmd$", commonDetailsCode));
        commonRules.Add(new ContentRule(@"^/code/htk\.bin$", commonDetailsCode));
        commonRules.Add(new ContentRule(@"^/code/rvlt\.tik$", commonDetailsCode));
        commonRules.Add(new ContentRule(@"^/code/rvlt\.tmd$", commonDetailsCode));

        // Cafe2Wii (c2w) clock speed patch and vWii system base files
        commonRules.Add(new ContentRule(@"^/code/c2w\.img$", commonDetailsCode));
        commonRules.Add(new ContentRule(@"^/code/boot\.bin$", commonDetailsCode));
        commonRules.Add(new ContentRule(@"^/code/dmcu\.d\.hex$", commonDetailsCode));
        commonRules.Add(new ContentRule(@"^/code/dmptac\.bin$", commonDetailsCode));
        commonRules.Add(new ContentRule(@"^/code/font\.bin$", commonDetailsCode));
        commonRules.Add(new ContentRule(@"^/code/deint\.txt$", commonDetailsCode));

        commonRules.Add(new ContentRule(@"^/content/.*$", commonDetailsContent));

        return commonRules;
    }
}

internal static class ContentRulesService
{
    private const long MaxContentLength = (long)(0xBFFFFFFFL * 0.975);

    private static long _curContentSize;
    private static NusContent? _curContent;
    private static NusContent? _curContentFirst;

    public static void ApplyRules(FstEntry root, NusContentsCollection targetContents, List<ContentRule> rules)
    {
        foreach (ContentRule rule in rules)
        {
            Regex regex = new(rule.Pattern, RegexOptions.IgnoreCase);
            if (rule.ContentPerMatch)
            {
                SetNewContentRecursiveRule("", root, targetContents, rule.Details, regex);
            }
            else
            {
                _curContent = targetContents.GetNewContent(rule.Details);
                _curContentFirst = _curContent;
                _curContentSize = 0L;
                bool result = SetContentRecursiveRule("", root, targetContents, rule.Details, regex);
                if (!result)
                {
                    targetContents.DeleteContent(_curContent);
                }
            }
        }

        // Defensive fallback pass: ensure no unassigned files or empty directories remain
        AssignUnmatchedFiles("", root, targetContents);
    }

    private static void AssignUnmatchedFiles(string path, FstEntry entry, NusContentsCollection targetContents)
    {
        path += entry.Filename + "/";
        foreach (FstEntry child in entry.Children)
        {
            if (child.IsDir)
            {
                AssignUnmatchedFiles(path, child, targetContents);
                if (child.Content == null)
                {
                    child.SetContent(targetContents.FstContent);
                }
            }
            else if (child.Content == null)
            {
                string childPath = path + child.Filename;
                ContentDetails details;
                if (childPath.StartsWith("/code/", StringComparison.OrdinalIgnoreCase))
                {
                    details = new ContentDetails(false, NusPackerSettings.GroupIdCode, 0x0L, NusPackerSettings.FstFlagsCode);
                }
                else if (childPath.StartsWith("/meta/", StringComparison.OrdinalIgnoreCase))
                {
                    details = new ContentDetails(true, NusPackerSettings.GroupIdMeta, 0x0L, NusPackerSettings.FstFlagsMeta);
                }
                else
                {
                    details = new ContentDetails(true, 0, 0, NusPackerSettings.FstFlagsContent);
                }

                NusContent fallbackContent = targetContents.GetNewContent(details);
                child.SetContent(fallbackContent);
            }
        }
    }

    private static NusContent? SetNewContentRecursiveRule(string path, FstEntry currentEntry, NusContentsCollection targetContents, ContentDetails details, Regex regex)
    {
        path += currentEntry.Filename + "/";
        NusContent? result = null;

        if (currentEntry.Children.Count == 0 && regex.IsMatch(path))
        {
            result = targetContents.GetNewContent(details);
        }

        foreach (FstEntry child in currentEntry.Children)
        {
            if (child.IsDir)
            {
                result = SetNewContentRecursiveRule(path, child, targetContents, details, regex) ?? result;
            }
            else
            {
                string childPath = path + child.Filename;
                if (regex.IsMatch(childPath))
                {
                    NusContent resultContent = targetContents.GetNewContent(details);
                    child.SetContent(resultContent);
                    result = resultContent;
                }
            }
        }

        if (result != null)
            currentEntry.SetContent(result);

        return result;
    }

    private static bool SetContentRecursiveRule(string path, FstEntry currentEntry, NusContentsCollection targetContents, ContentDetails details, Regex regex)
    {
        path += currentEntry.Filename + "/";
        bool matchFound = false;

        if (currentEntry.Children.Count == 0)
        {
            if (regex.IsMatch(path))
            {
                if (_curContent != null)
                    currentEntry.SetContent(_curContent);
                return true;
            }
            return false;
        }

        foreach (FstEntry child in currentEntry.Children)
        {
            if (child.IsDir)
            {
                matchFound |= SetContentRecursiveRule(path, child, targetContents, details, regex);
            }
            else
            {
                string childPath = path + child.Filename;
                if (regex.IsMatch(childPath))
                {
                    if (_curContentSize + child.FileSize > MaxContentLength)
                    {
                        _curContent = targetContents.GetNewContent(details);
                        _curContentSize = 0;
                    }
                    _curContentSize += child.FileSize;

                    if (_curContent != null)
                        child.SetContent(_curContent);

                    matchFound = true;
                }
            }
        }

        if (matchFound && _curContentFirst != null)
            currentEntry.SetContent(_curContentFirst);

        return matchFound;
    }
}

