// Ported from CNUS_Packer (https://github.com/NicoAICP/CNUS_Packer)
// Original Authors: NicoAICP, Morilli (based on NUSPacker by timogus)

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Moon_WiiVC_Injector.Core.NusPacker.Contents;
using Moon_WiiVC_Injector.Core.NusPacker.Fst;

namespace Moon_WiiVC_Injector.Core.NusPacker.Packaging;

public static class ContentRulesService
{
    private const long MAX_CONTENT_LENGTH = (long)(0xBFFFFFFFL * 0.975);

    private static long cur_content_size;
    private static Content? cur_content;
    private static Content? cur_content_first;

    public static void ApplyRules(FSTEntry root, Contents.Contents targetContents, List<ContentRule> rules)
    {
        foreach (ContentRule rule in rules)
        {
            Regex regex = new Regex(rule.pattern, RegexOptions.IgnoreCase);
            if (rule.contentPerMatch)
            {
                SetNewContentRecursiveRule("", root, targetContents, rule.details, regex);
            }
            else
            {
                cur_content = targetContents.GetNewContent(rule.details);
                cur_content_first = cur_content;
                cur_content_size = 0L;
                bool result = SetContentRecursiveRule("", root, targetContents, rule.details, regex);
                if (!result)
                {
                    targetContents.DeleteContent(cur_content);
                }
            }
        }
    }

    private static Content? SetNewContentRecursiveRule(string path, FSTEntry currentEntry, Contents.Contents targetContents, ContentDetails details, Regex regex)
    {
        path += currentEntry.filename + "/";
        Content? result = null;

        if (currentEntry.children.Count == 0)
        {
            if (regex.IsMatch(path))
            {
                result = targetContents.GetNewContent(details);
            }
        }

        foreach (FSTEntry child in currentEntry.children)
        {
            if (child.isDir)
            {
                result = SetNewContentRecursiveRule(path, child, targetContents, details, regex) ?? result;
            }
            else
            {
                string childPath = path + child.filename;
                if (regex.IsMatch(childPath))
                {
                    Content result_content = targetContents.GetNewContent(details);
                    child.SetContent(result_content);
                    result = result_content;
                }
            }
        }

        if (result != null)
            currentEntry.SetContent(result);

        return result;
    }

    private static bool SetContentRecursiveRule(string path, FSTEntry currentEntry, Contents.Contents targetContents, ContentDetails details, Regex regex)
    {
        path += currentEntry.filename + "/";
        bool matchFound = false;
        if (currentEntry.children.Count == 0)
        {
            if (regex.IsMatch(path))
            {
                if (cur_content != null)
                {
                    currentEntry.SetContent(cur_content);
                }
                return true;
            }

            return false;
        }

        foreach (FSTEntry child in currentEntry.children)
        {
            if (child.isDir)
            {
                matchFound |= SetContentRecursiveRule(path, child, targetContents, details, regex);
            }
            else
            {
                string childPath = path + child.filename;
                if (regex.IsMatch(childPath))
                {
                    if (cur_content_size + child.fileSize > MAX_CONTENT_LENGTH)
                    {
                        cur_content = targetContents.GetNewContent(details);
                        cur_content_size = 0;
                    }
                    cur_content_size += child.fileSize;

                    if (cur_content != null)
                    {
                        child.SetContent(cur_content);
                    }
                    matchFound = true;
                }
            }
        }

        if (matchFound && cur_content_first != null)
            currentEntry.SetContent(cur_content_first);

        return matchFound;
    }
}

